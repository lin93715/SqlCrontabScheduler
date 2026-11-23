using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quartz;
using Scheduler.Domain;
using Scheduler.Infrastructure;

namespace Scheduler.Worker;

internal sealed class SchedulerCoordinator(
    ISchedulerFactory schedulerFactory,
    IDbContextFactory<SchedulerDbContext> factory,
    IConfiguration configuration,
    TimeProvider time,
    ExecutionRunner runs,
    IHostApplicationLifetime lifetime,
    ILogger<SchedulerCoordinator> logger) : BackgroundService
{
    private const long InstanceLock = 78345160215193;
    private const string JobGroup = "sql-jobs";
    private const string CronGroup = "sql-cron";
    private const string ManualGroup = "sql-manual";
    private DateTimeOffset nextRetentionCheck;

    public static JobKey JobKeyFor(Guid id) => new(id.ToString("N"), JobGroup);
    private static TriggerKey CronKey(Guid id) => new(id.ToString("N"), CronGroup);
    private static TriggerKey ManualKey(Guid id) => new(id.ToString("N"), ManualGroup);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseSettings = new NpgsqlConnectionStringBuilder(DatabaseServices.GetConnectionString(configuration, "Scheduler"))
        {
            Pooling = false, KeepAlive = 10, CommandTimeout = 4
        };
        await using var lease = new NpgsqlConnection(leaseSettings.ConnectionString);
        IScheduler? scheduler = null;
        try
        {
            await lease.OpenAsync(stoppingToken);
            await using (var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", lease))
            {
                claim.Parameters.AddWithValue(InstanceLock);
                if (await claim.ExecuteScalarAsync(stoppingToken) is not true)
                {
                    logger.LogError("Another Worker owns the scheduler lease");
                    Environment.ExitCode = 1;
                    lifetime.StopApplication();
                    return;
                }
            }

            await RecoverInterruptedRuns(stoppingToken);
            scheduler = await schedulerFactory.GetScheduler(stoppingToken);
            await Reconcile(scheduler, stoppingToken);
            await scheduler.Start(stoppingToken);
            logger.LogInformation("Scheduler active as the single Worker");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await using (var ping = new NpgsqlCommand("SELECT 1", lease))
                    await ping.ExecuteScalarAsync(stoppingToken);
                try
                {
                    await Reconcile(scheduler, stoppingToken);
                    if (time.GetUtcNow() >= nextRetentionCheck)
                    {
                        await Prune(stoppingToken);
                        nextRetentionCheck = time.GetUtcNow().AddHours(1);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Schedule synchronization will be retried ({FailureType})", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Scheduler stopped during {FailureType}", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogError("Scheduler initialization or lease failed ({FailureType})", ex.GetType().Name);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
        finally
        {
            if (scheduler is not null)
            {
                try { await scheduler.Shutdown(waitForJobsToComplete: true); }
                catch (Exception ex) { logger.LogError("Scheduler shutdown failed ({FailureType})", ex.GetType().Name); }
            }
        }
    }

    private async Task Reconcile(IScheduler scheduler, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var jobs = await db.Jobs.AsNoTracking()
            .Where(x => x.DeletedUtc == null || x.AppliedRevision != x.Revision)
            .Select(x => new DesiredJob(x.Id, x.CronExpression, x.TimeZone, x.Enabled, x.Paused,
                x.DeletedUtc.HasValue, x.DatabaseConnection.Enabled && (!x.DatabaseConnection.AdminOnly || x.AdminTargetAuthorized),
                x.Revision, x.AppliedRevision, x.NextRunUtc))
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            var next = await ApplyJob(scheduler, job, ct);
            if (job.AppliedRevision != job.Revision || job.NextRunUtc != next)
            {
                await db.Jobs.Where(x => x.Id == job.Id && x.Revision == job.Revision)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.AppliedRevision, job.Revision)
                        .SetProperty(x => x.NextRunUtc, next), ct);
            }
        }

        var pending = await db.ManualRequests.AsNoTracking().Where(x => x.ProcessedUtc == null)
            .OrderBy(x => x.RequestedUtc).ToListAsync(ct);
        var jobStates = jobs.ToDictionary(x => x.Id);
        foreach (var request in pending)
        {
            if (!jobStates.TryGetValue(request.JobId, out var job) || job.Deleted || !job.Enabled ||
                !job.ConnectionAllowed || job.Revision != request.JobRevision)
            {
                await runs.ClaimAsync(request.JobId, request.Id, request.RequestedUtc, request.JobRevision, ct, onlyIfUnavailable: true);
                continue;
            }

            var key = ManualKey(request.Id);
            if (await scheduler.CheckExists(key, ct))
                continue;
            var trigger = TriggerBuilder.Create().WithIdentity(key).ForJob(JobKeyFor(request.JobId))
                .UsingJobData("manualRequestId", request.Id.ToString("N"))
                .StartAt(request.RequestedUtc)
                .WithSimpleSchedule(schedule => schedule.WithRepeatCount(0).WithMisfireHandlingInstructionFireNow())
                .Build();
            try { await scheduler.ScheduleJob(trigger, ct); }
            catch (ObjectAlreadyExistsException) { }
        }
    }

    private static async Task<DateTimeOffset?> ApplyJob(IScheduler scheduler, DesiredJob job, CancellationToken ct)
    {
        var jobKey = JobKeyFor(job.Id);
        var triggerKey = CronKey(job.Id);
        if (job.Deleted)
        {
            await scheduler.DeleteJob(jobKey, ct);
            return null;
        }
        if (!job.Enabled || !job.ConnectionAllowed)
        {
            if (await scheduler.CheckExists(triggerKey, ct))
                await scheduler.UnscheduleJob(triggerKey, ct);
            return null;
        }

        if (!await scheduler.CheckExists(jobKey, ct))
        {
            await scheduler.AddJob(JobBuilder.Create<SqlExecutionJob>().WithIdentity(jobKey)
                .StoreDurably().Build(), replace: false, ct);
        }

        var trigger = await scheduler.GetTrigger(triggerKey, ct);
        var revision = job.Revision.ToString("N");
        if (trigger is null || !trigger.JobDataMap.TryGetValue("revision", out var storedRevision) ||
            !revision.Equals(storedRevision))
        {
            var replacement = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
                .UsingJobData("revision", revision)
                .WithCronSchedule(job.Cron, schedule => schedule
                    .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(job.TimeZone))
                    .WithMisfireHandlingInstructionDoNothing())
                .Build();
            if (trigger is null)
                await scheduler.ScheduleJob(replacement, ct);
            else
                await scheduler.RescheduleJob(triggerKey, replacement, ct);
            trigger = await scheduler.GetTrigger(triggerKey, ct);
        }

        var state = await scheduler.GetTriggerState(triggerKey, ct);
        if (job.Paused && state != TriggerState.Paused)
            await scheduler.PauseTrigger(triggerKey, ct);
        if (!job.Paused && state == TriggerState.Paused)
        {
            await scheduler.ResumeTrigger(triggerKey, ct);
            trigger = await scheduler.GetTrigger(triggerKey, ct);
        }
        return job.Paused ? null : trigger?.GetNextFireTimeUtc()?.ToUniversalTime();
    }

    private async Task RecoverInterruptedRuns(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var interrupted = await db.Executions.Where(x => x.Status == ExecutionStatus.Running)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ExecutionStatus.Cancelled)
                .SetProperty(x => x.ErrorMessage, "Worker 上次執行時中斷。")
                .SetProperty(x => x.ErrorDetail, "worker_interrupted")
                .SetProperty(x => x.EndUtc, now), ct);
        if (interrupted > 0)
            logger.LogWarning("Closed {InterruptedCount} interrupted executions", interrupted);
    }

    private async Task Prune(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var executionCutoff = now.AddDays(-Math.Clamp(configuration.GetValue("Retention:ExecutionDays", 90), 1, 3650));
        var auditCutoff = now.AddDays(-Math.Clamp(configuration.GetValue("Retention:AuditDays", 365), 1, 3650));
        await db.Executions.Where(x => x.EndUtc < executionCutoff).ExecuteDeleteAsync(ct);
        await db.ManualRequests.Where(x => x.ProcessedUtc < executionCutoff).ExecuteDeleteAsync(ct);
        await db.Audit.Where(x => x.OccurredUtc < auditCutoff).ExecuteDeleteAsync(ct);
        await db.Jobs.Where(x => x.DeletedUtc < executionCutoff && x.Revision == x.AppliedRevision &&
                !db.Executions.Any(run => run.JobId == x.Id) && !db.Audit.Any(item => item.JobId == x.Id) &&
                !db.ManualRequests.Any(request => request.JobId == x.Id))
            .ExecuteDeleteAsync(ct);
    }

    private sealed record DesiredJob(Guid Id, string Cron, string TimeZone, bool Enabled, bool Paused, bool Deleted,
        bool ConnectionAllowed, Guid Revision, Guid? AppliedRevision, DateTimeOffset? NextRunUtc);
}
