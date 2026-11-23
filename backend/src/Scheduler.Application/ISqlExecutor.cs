using Scheduler.Domain;

namespace Scheduler.Application;

public interface ISqlExecutor
{
    Task<SqlExecutionResult> ExecuteAsync(SqlExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record SqlExecutionRequest(Guid ExecutionId, Guid JobId, string ConnectionKey, string SqlText, int TimeoutSeconds);

public sealed record SqlExecutionResult(
    ExecutionStatus Status,
    long DurationMs,
    int? RowsAffected = null,
    int? RowsRead = null,
    bool RowLimitReached = false,
    string? ErrorMessage = null,
    string? ErrorDetail = null);
