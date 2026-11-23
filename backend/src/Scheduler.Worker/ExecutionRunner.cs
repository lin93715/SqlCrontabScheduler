using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;

namespace Scheduler.Worker;

internal sealed class ExecutionRunner(
    IDbContextFactory<SchedulerDbContext> factory, ISqlExecutor executor,
    TimeProvider time, IHostApplicationLifetime lifetime, ILogger<ExecutionRunner> logger)
{
    public async Task<SqlExecutionRequest?> ClaimAsync(Guid jobId, Guid? manualId, DateTimeOffset scheduled,
        Guid? revision, CancellationToken ct, bool onlyIfUnavailable = false)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.Jobs.FromSqlInterpolated($"SELECT * FROM scheduler_job WHERE id = {jobId} FOR SHARE")
            .AsNoTracking().Include(x => x.DatabaseConnection).SingleOrDefaultAsync(ct);
        if (job is null)
            return null;

        var now = time.GetUtcNow();
        if (manualId is { } requestId)
        {
            var manual = await db.ManualRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.JobId == jobId, ct);
            if (manual is null || manual.ProcessedUtc.HasValue)
                return null;
            revision = manual.JobRevision;
            scheduled = manual.RequestedUtc;
            manual.ProcessedUtc = now;
        }

        var id = manualId ?? ScheduledExecutionId(jobId, scheduled);
        var unavailable = job.DeletedUtc.HasValue || !job.Enabled || job.Revision != revision
            || (!manualId.HasValue && job.Paused) || !job.DatabaseConnection.Enabled
            || (job.DatabaseConnection.AdminOnly && !job.AdminTargetAuthorized);
        if (onlyIfUnavailable && !unavailable)
            return null;
        db.Executions.Add(new JobExecution
        {
            Id = id, JobId = jobId, JobName = job.Name, ScheduledUtc = scheduled.ToUniversalTime(),
            TriggerType = manualId.HasValue ? TriggerType.Manual : TriggerType.Scheduled,
            StartUtc = unavailable ? null : now, EndUtc = unavailable ? now : null,
            Status = unavailable ? ExecutionStatus.Skipped : ExecutionStatus.Running,
            ErrorMessage = unavailable ? "排程已變更、停用，或目標連線已撤銷。" : null,
            ErrorDetail = unavailable ? "job_unavailable" : null
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_scheduler_execution" })
        {
            return null;
        }

        return unavailable ? null :
            new SqlExecutionRequest(id, jobId, job.DatabaseConnection.SecretKey, job.SqlText, job.TimeoutSeconds);
    }

    public async Task RunAsync(SqlExecutionRequest request, CancellationToken ct)
    {
        SqlExecutionResult result;
        try { result = await executor.ExecuteAsync(request, ct); }
        catch (Exception ex)
        {
            logger.LogError("Executor failed for execution {ExecutionId} ({FailureType})", request.ExecutionId, ex.GetType().Name);
            result = new(ExecutionStatus.Failed, 0, ErrorMessage: "SQL 執行器發生錯誤。", ErrorDetail: "executor_error");
        }

        using var writeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var writeToken = writeDeadline.Token;
        var finished = time.GetUtcNow();
        // Retry the metadata write only. The target SQL has already completed.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(writeToken);
                await db.Executions.Where(x => x.Id == request.ExecutionId && x.Status == ExecutionStatus.Running)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.EndUtc, finished)
                        .SetProperty(x => x.DurationMs, result.DurationMs)
                        .SetProperty(x => x.Status, result.Status)
                        .SetProperty(x => x.RowsAffected, result.RowsAffected)
                        .SetProperty(x => x.RowsRead, result.RowsRead)
                        .SetProperty(x => x.RowLimitReached, result.RowLimitReached)
                        .SetProperty(x => x.ErrorMessage, result.ErrorMessage)
                        .SetProperty(x => x.ErrorDetail, result.ErrorDetail), writeToken);
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < 2 && !writeToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
            }
            catch (Exception ex)
            {
                logger.LogError("Cannot finalize execution {ExecutionId} ({FailureType})", request.ExecutionId, ex.GetType().Name);
                Environment.ExitCode = 1;
                lifetime.StopApplication();
                return;
            }
        }
    }

    private static Guid ScheduledExecutionId(Guid jobId, DateTimeOffset scheduled)
    {
        Span<byte> key = stackalloc byte[24];
        jobId.TryWriteBytes(key[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(key[16..], scheduled.UtcTicks);
        return new Guid(SHA256.HashData(key).AsSpan(0, 16));
    }
}
