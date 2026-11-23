namespace Scheduler.Domain;

public enum ScheduleType { EveryMinutes, Hourly, Daily, Weekly, Monthly }
public enum TriggerType { Scheduled, Manual }
public enum ExecutionStatus { Running, Success, Failed, Timeout, Cancelled, Skipped }

public sealed record ScheduleSettings(
    ScheduleType Type, int? IntervalMinutes = null, int? Minute = null,
    int? Hour = null, DayOfWeek? DayOfWeek = null, int? DayOfMonth = null);

public sealed class SchedulerJob
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid DatabaseConnectionId { get; set; }
    public DatabaseConnection DatabaseConnection { get; set; } = null!;
    public required string SqlText { get; set; }
    public required ScheduleSettings Schedule { get; set; }
    public required string CronExpression { get; set; }
    public required string TimeZone { get; set; }
    public int TimeoutSeconds { get; set; }
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public bool AdminTargetAuthorized { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid Revision { get; set; }
    public Guid? AppliedRevision { get; set; }
    public DateTimeOffset? NextRunUtc { get; set; }
}

public sealed class DatabaseConnection
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string SecretKey { get; set; }
    public bool Enabled { get; set; }
    public bool AdminOnly { get; set; }
}

public sealed class JobExecution
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string JobName { get; set; }
    public TriggerType TriggerType { get; set; }
    public DateTimeOffset ScheduledUtc { get; set; }
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public long? DurationMs { get; set; }
    public ExecutionStatus Status { get; set; }
    public int? RowsAffected { get; set; }
    public int? RowsRead { get; set; }
    public bool RowLimitReached { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetail { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public Guid? ManualRequestId { get; set; }
}

public sealed class ManualRequest
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid JobRevision { get; set; }
    public DateTimeOffset RequestedUtc { get; set; }
    public DateTimeOffset? ProcessedUtc { get; set; }
}
