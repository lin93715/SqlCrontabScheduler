using Scheduler.Domain;

namespace Scheduler.Api;

public sealed record JobWriteRequest(string? Name, string? Description, Guid DatabaseConnectionId,
    string? SqlText, ScheduleSettings? Schedule, string? TimeZone, int TimeoutSeconds, bool Enabled,
    Guid? Revision = null);
public sealed record PreviewRequest(ScheduleSettings? Schedule, string? TimeZone);

public sealed record JobListItem(Guid Id, string Name, string? Description, Guid DatabaseConnectionId,
    string ConnectionName, ScheduleType ScheduleType, string CronExpression, string TimeZone, bool Enabled,
    bool Paused, string CreatedBy, DateTimeOffset UpdatedUtc, DateTimeOffset? NextRunUtc, bool SyncPending)
{
    public static JobListItem From(SchedulerJob job) => new(
        job.Id, job.Name, job.Description, job.DatabaseConnectionId, job.DatabaseConnection.Name,
        job.Schedule.Type, job.CronExpression, job.TimeZone, job.Enabled, job.Paused, job.CreatedBy,
        job.UpdatedUtc, JobDetail.NextRun(job), job.AppliedRevision != job.Revision);
}

public sealed record JobDetail(Guid Id, string Name, string? Description, Guid DatabaseConnectionId,
    string ConnectionName, string SqlText, ScheduleSettings Schedule, string CronExpression, string TimeZone,
    int TimeoutSeconds, bool Enabled, bool Paused, string CreatedBy, DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc, DateTimeOffset? NextRunUtc, bool SyncPending, Guid Revision)
{
    public static JobDetail From(SchedulerJob job) => new(
        job.Id, job.Name, job.Description, job.DatabaseConnectionId, job.DatabaseConnection.Name, job.SqlText,
        job.Schedule, job.CronExpression, job.TimeZone, job.TimeoutSeconds, job.Enabled, job.Paused, job.CreatedBy,
        job.CreatedUtc, job.UpdatedUtc, NextRun(job), job.AppliedRevision != job.Revision, job.Revision);

    internal static DateTimeOffset? NextRun(SchedulerJob job) =>
        job.AppliedRevision == job.Revision && job.Enabled && !job.Paused ? job.NextRunUtc : null;
}

public sealed record ExecutionItem(Guid Id, Guid JobId, string JobName, TriggerType TriggerType,
    DateTimeOffset ScheduledUtc, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, long? DurationMs,
    ExecutionStatus Status, int? RowsAffected, int? RowsRead, bool RowLimitReached,
    string? ErrorMessage, string? ErrorDetail)
{
    public static ExecutionItem From(JobExecution run, bool showDetail = false) =>
        new(run.Id, run.JobId, run.JobName, run.TriggerType, run.ScheduledUtc, run.StartUtc, run.EndUtc,
            run.DurationMs, run.Status, run.RowsAffected, run.RowsRead, run.RowLimitReached,
            run.ErrorMessage, showDetail ? run.ErrorDetail : null);
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record ConnectionItem(Guid Id, string Name);
public sealed record DashboardSummary(int TotalJobs, int EnabledJobs, int RunningJobs,
    int RecentSuccess, int RecentFailure, int PendingSync, IReadOnlyList<ExecutionItem> RecentExecutions);
public sealed record ManualAccepted(Guid RequestId, DateTimeOffset RequestedUtc);
