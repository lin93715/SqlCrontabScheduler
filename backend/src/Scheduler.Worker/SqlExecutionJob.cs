using Quartz;

namespace Scheduler.Worker;

[DisallowConcurrentExecution]
internal sealed class SqlExecutionJob(ExecutionRunner runs, IHostApplicationLifetime lifetime, ILogger<SqlExecutionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken, lifetime.ApplicationStopping);
        var jobId = Guid.ParseExact(context.JobDetail.Key.Name, "N");
        try
        {
            var data = context.MergedJobDataMap;
            var manualId = ReadGuid(data, "manualRequestId");
            var revision = ReadGuid(data, "revision");
            var scheduled = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
            var request = await runs.ClaimAsync(jobId, manualId, scheduled, revision, linked.Token);
            if (request is not null)
                await runs.RunAsync(request, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError("Quartz job {JobId} failed to record execution ({FailureType})", jobId, ex.GetType().Name);
        }
    }

    private static Guid? ReadGuid(JobDataMap data, string key) =>
        data.TryGetValue(key, out var raw) && raw is string value && Guid.TryParseExact(value, "N", out var id)
            ? id : null;
}
