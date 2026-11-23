using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Scheduler.Application;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public sealed class PostgresSqlExecutor(
    IConfiguration configuration, ILogger<PostgresSqlExecutor> logger) : ISqlExecutor
{
    public async Task<SqlExecutionResult> ExecuteAsync(SqlExecutionRequest request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var timeout = request.TimeoutSeconds;
        if (timeout is < 1 or > 300)
            return Failure(ExecutionStatus.Failed, "SQL 逾時設定無效。", "invalid_timeout");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var ct = linked.Token;
        try
        {
            ct.ThrowIfCancellationRequested();
            var configured = configuration.GetSection("TargetConnections")[request.ConnectionKey];
            if (string.IsNullOrWhiteSpace(configured))
                return Failure(ExecutionStatus.Failed, "目標資料庫尚未設定。", "connection_not_configured");

            var settings = new NpgsqlConnectionStringBuilder(configured)
            {
                Pooling = false, Enlist = false, Multiplexing = false,
                IncludeErrorDetail = false, LogParameters = false, Timeout = Math.Min(timeout, 10),
                CommandTimeout = timeout, CancellationTimeout = 1000
            };
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var prepare = new NpgsqlCommand(
                $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = {timeout * 1000}; " +
                "SET LOCAL idle_in_transaction_session_timeout = 15000", connection, tx))
                await prepare.ExecuteNonQueryAsync(ct);

            const string checkRole = """
                SELECT NOT (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls)
                    AND NOT pg_catalog.has_database_privilege(current_database(), 'TEMP')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m WHERE m.member = r.oid)
                    AND current_setting('transaction_read_only') = 'on'
                FROM pg_catalog.pg_roles r
                WHERE rolname = current_user
                """;
            await using (var check = new NpgsqlCommand(checkRole, connection, tx))
                if (await check.ExecuteScalarAsync(ct) is not true)
                    return Failure(ExecutionStatus.Failed, "目標資料庫帳號未符合唯讀執行條件。", "unsafe_reader_role");

            var limit = Math.Clamp(configuration.GetValue("Worker:MaxResultRows", 10_000), 1, 100_000);
            await using var countQuery = new NpgsqlCommand(
                "SELECT scheduler_guard.count_read_rows($1, $2)", connection, tx);
            countQuery.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = request.SqlText });
            countQuery.Parameters.AddWithValue(NpgsqlDbType.Integer, limit + 1);
            var count = (int)(await countQuery.ExecuteScalarAsync(ct))!;

            await tx.RollbackAsync(ct);
            var result = new SqlExecutionResult(ExecutionStatus.Success, watch.ElapsedMilliseconds,
                RowsRead: Math.Min(count, limit), RowLimitReached: count > limit);
            Log(result);
            return result;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(ExecutionStatus.Cancelled, "SQL 執行已取消。", "cancelled");
        }
        catch (Exception ex) when (deadline.IsCancellationRequested || ex is TimeoutException
            or NpgsqlException { InnerException: TimeoutException }
            or PostgresException { SqlState: PostgresErrorCodes.QueryCanceled })
        {
            return Failure(ExecutionStatus.Timeout, "SQL 執行已逾時。", "query_timeout");
        }
        catch (PostgresException ex)
        {
            var message = ex.SqlState switch
            {
                PostgresErrorCodes.SyntaxError => "SQL 語法有誤。",
                PostgresErrorCodes.InsufficientPrivilege => "唯讀帳號沒有查詢所需的權限。",
                PostgresErrorCodes.UndefinedTable => "查詢的資料表或檢視不存在。",
                PostgresErrorCodes.UndefinedColumn => "查詢的欄位不存在。",
                PostgresErrorCodes.ReadOnlySqlTransaction => "唯讀交易禁止這項操作。",
                _ => "PostgreSQL 拒絕或無法執行查詢。"
            };
            return Failure(ExecutionStatus.Failed, message, "SQLSTATE " + ex.SqlState);
        }
        catch (NpgsqlException)
        {
            return Failure(ExecutionStatus.Failed, "無法連線至目標資料庫，或連線已中斷。", "database_communication");
        }
        catch (Exception)
        {
            return Failure(ExecutionStatus.Failed, "SQL 執行器無法處理查詢。", "executor_error");
        }

        SqlExecutionResult Failure(ExecutionStatus status, string message, string detail)
        {
            var result = new SqlExecutionResult(status, watch.ElapsedMilliseconds, ErrorMessage: message, ErrorDetail: detail);
            Log(result);
            return result;
        }

        void Log(SqlExecutionResult result)
        {
            logger.Log(result.Status == ExecutionStatus.Failed ? LogLevel.Warning : LogLevel.Information,
                "SQL execution {ExecutionId} for job {JobId} completed with {Status} in {DurationMs} ms ({Diagnostic})",
                request.ExecutionId, request.JobId, result.Status, result.DurationMs, result.ErrorDetail);
        }
    }
}
