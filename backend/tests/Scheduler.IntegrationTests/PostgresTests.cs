using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SCHEDULER_TEST_METADATA")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SCHEDULER_TEST_TARGET")))
            Skip = "Requires the isolated scheduler and report PostgreSQL test databases.";
    }
}

public sealed class PostgresTests
{
    private static string Metadata => Environment.GetEnvironmentVariable("SCHEDULER_TEST_METADATA")!;
    private static string Target => Environment.GetEnvironmentVariable("SCHEDULER_TEST_TARGET")!;

    [PostgresFact]
    public async Task Executor_enforces_single_read_only_query_timeout_and_cancellation()
    {
        IConfiguration options = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TargetConnections:Reports"] = Target, ["Worker:MaxResultRows"] = "2" }).Build();
        var executor = new PostgresSqlExecutor(options, NullLogger<PostgresSqlExecutor>.Instance);
        Task<SqlExecutionResult> Run(string sql, int timeout = 8, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Reports", sql, timeout), cancellationToken);

        var success = await Run("WITH input AS (SELECT generate_series(1, 3)) SELECT * FROM input");
        Assert.Equal(ExecutionStatus.Success, success.Status);
        Assert.Equal(2, success.RowsRead);
        Assert.True(success.RowLimitReached);

        Assert.Equal(ExecutionStatus.Failed, (await Run("DELETE FROM report.daily_totals")).Status);
        Assert.Equal(ExecutionStatus.Failed, (await Run("SELECT 1; SELECT 2")).Status);
        Assert.Equal(ExecutionStatus.Failed, (await Run("SELECT pg_notify('test_channel', 'test')")).Status);
        Assert.Equal(ExecutionStatus.Failed, (await Run("SELECT * FROM report.table_that_is_not_present")).Status);
        Assert.Equal(ExecutionStatus.Timeout, (await Run("SELECT pg_sleep(4)", timeout: 1)).Status);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        Assert.Equal(ExecutionStatus.Cancelled, (await Run("SELECT pg_sleep(4)", cancellationToken: cancel.Token)).Status);
    }

    [PostgresFact]
    public async Task Api_worker_and_quartz_support_job_lifecycle_and_restart()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var hash = MakePasswordHash(password);
        var unique = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        var member = "member-" + unique;
        var admin = "admin-" + unique;
        var catalog = await SeedConnection();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Scheduler"] = Metadata,
                ["Auth:Users:0:Username"] = member, ["Auth:Users:0:Role"] = "User", ["Auth:Users:0:PasswordHash"] = hash,
                ["Auth:Users:1:Username"] = admin, ["Auth:Users:1:Role"] = "Admin", ["Auth:Users:1:PasswordHash"] = hash
            })));
        using var client = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        using (var anonymous = await client.GetAsync("/api/jobs"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var missingHeader = await client.PostAsJsonAsync("/api/auth/login", new { username = member, password }))
            Assert.Equal(HttpStatusCode.Forbidden, missingHeader.StatusCode);
        client.DefaultRequestHeaders.Add("X-Scheduler-Request", "1");
        adminClient.DefaultRequestHeaders.Add("X-Scheduler-Request", "1");
        await Read(await client.PostAsJsonAsync("/api/auth/login", new { username = member, password }));
        await Read(await adminClient.PostAsJsonAsync("/api/auth/login", new { username = admin, password }));
        using (var ready = await client.GetAsync("/health")) ready.EnsureSuccessStatusCode();

        var timing = new { type = "everyMinutes", intervalMinutes = 1 };
        var preview = await Read(await client.PostAsJsonAsync("/api/schedules/preview", new { schedule = timing }));
        Assert.Equal("0 0/1 * * * ?", preview.GetProperty("cronExpression").GetString());

        var body = new
        {
            name = "Lifecycle " + unique, description = "Integration", databaseConnectionId = catalog,
            sqlText = "SELECT * FROM report.daily_totals;", schedule = timing, timeoutSeconds = 8,
            timeZone = "Asia/Taipei", enabled = true
        };
        var created = await Read(await client.PostAsJsonAsync("/api/jobs", body), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var path = $"/api/jobs/{id}";
        try
        {
            var adminView = await Read(await adminClient.GetAsync(path));
            Assert.Equal(member, adminView.GetProperty("createdBy").GetString());
            var request = new
            {
                body.name, description = "Edited", body.databaseConnectionId, body.sqlText,
                body.schedule, body.timeoutSeconds, body.timeZone, body.enabled, revision = created.GetProperty("revision").GetGuid()
            };
            await Read(await client.PutAsJsonAsync(path, request));
            using (var stale = await client.PutAsJsonAsync(path, request))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var queued = await Read(await client.PostAsync(path + "/execute", null), HttpStatusCode.Accepted);
            using (var duplicate = await client.PostAsync(path + "/execute", null))
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            var requestId = queued.GetProperty("requestId").GetGuid();
            DateTimeOffset? expected;
            await using (var worker = await StartWorker())
            {
                try
                {
                    var first = await Eventually(async () =>
                    {
                        using var response = await client.GetAsync($"/api/executions/{requestId}");
                        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
                    }, item => item.ValueKind != JsonValueKind.Undefined && item.GetProperty("status").GetString() == "success");
                    Assert.Equal(2, first.GetProperty("rowsRead").GetInt32());

                    await Read(await client.PostAsync(path + "/pause", null));
                    await Eventually(() => Get(client, path), item =>
                        !item.GetProperty("syncPending").GetBoolean() && item.GetProperty("nextRunUtc").ValueKind == JsonValueKind.Null);
                    var pausedRun = await Read(await client.PostAsync(path + "/execute", null), HttpStatusCode.Accepted);
                    var pausedPath = "/api/executions/" + pausedRun.GetProperty("requestId").GetGuid();
                    await Eventually(async () =>
                    {
                        using var response = await client.GetAsync(pausedPath);
                        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
                    }, item => item.ValueKind != JsonValueKind.Undefined && item.GetProperty("status").GetString() == "success");

                    await Read(await client.PostAsync(path + "/resume", null));
                    var resumed = await Eventually(() => Get(client, path), item =>
                        !item.GetProperty("syncPending").GetBoolean() && item.GetProperty("nextRunUtc").ValueKind == JsonValueKind.String);
                    expected = resumed.GetProperty("nextRunUtc").GetDateTimeOffset();
                }
                finally { await worker.Stop(); }
            }

            var offlineRequest = await Read(await client.PostAsync(path + "/execute", null), HttpStatusCode.Accepted);
            var offlineId = offlineRequest.GetProperty("requestId").GetGuid();
            await using (var recoveredWorker = await StartWorker())
            {
                try
                {
                    await Eventually(async () =>
                    {
                        var histories = await Get(client, path + "/executions?page=1&pageSize=20");
                        return histories.GetProperty("items");
                    }, items => items.EnumerateArray().Any(run => run.GetProperty("id").GetGuid() == offlineId &&
                        run.GetProperty("status").GetString() == "success"), TimeSpan.FromSeconds(18));

                    var scheduled = await Eventually(async () =>
                    {
                        var histories = await Get(client, path + "/executions?status=success");
                        return histories.GetProperty("items");
                    }, items => items.EnumerateArray().Any(run => run.GetProperty("triggerType").GetString() == "scheduled" &&
                        run.GetProperty("scheduledUtc").GetDateTimeOffset() >= expected), TimeSpan.FromSeconds(75));
                    Assert.NotEmpty(scheduled.EnumerateArray());
                    var dashboard = await Get(client, "/api/dashboard/summary");
                    Assert.True(dashboard.GetProperty("totalJobs").GetInt32() >= 1);
                    Assert.True(dashboard.GetProperty("recentSuccess").GetInt32() >= 3);
                    await Read(await client.PostAsync(path + "/disable", null));
                    using var blocked = await client.PostAsync(path + "/execute", null);
                    Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                    await Read(await client.PostAsync(path + "/enable", null));
                }
                finally { await recoveredWorker.Stop(); }
            }

            await using var db = new NpgsqlConnection(Metadata);
            await db.OpenAsync();
            await using var countAudit = new NpgsqlCommand(
                "SELECT count(*) FROM scheduler_audit_log WHERE job_id = $1", db);
            countAudit.Parameters.AddWithValue(id);
            Assert.True((long)(await countAudit.ExecuteScalarAsync())! >= 8);
        }
        finally
        {
            using var delete = await client.DeleteAsync(path);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            using var deleted = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        }
        var retained = await Get(client, path + "/executions");
        Assert.True(retained.GetProperty("total").GetInt32() > 0);
    }

    private static async Task<WorkerProcess> StartWorker()
    {
        var cli = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutput.Parent!.Name;
        var project = Path.GetFullPath(Path.Combine(testOutput.FullName, "../../../../../src/Scheduler.Worker"));
        var workerOutput = Path.Combine(project, "bin", configuration, testOutput.Name);
        var start = new ProcessStartInfo(cli)
        {
            WorkingDirectory = workerOutput, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(workerOutput, "Scheduler.Worker.dll"));
        start.Environment["ConnectionStrings__Scheduler"] = Metadata;
        start.Environment["TargetConnections__Reports"] = Target;
        start.Environment["Logging__LogLevel__Quartz"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "None";
        start.Environment["Logging__LogLevel__Npgsql"] = "None";
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data?.Contains("Scheduler active as the single Worker", StringComparison.Ordinal) == true)
                ready.TrySetResult();
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException($"Worker exited before startup completed (code {process.ExitCode})."));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var worker = new WorkerProcess(process);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return worker;
        }
        catch
        {
            await worker.DisposeAsync();
            throw;
        }
    }

    private sealed class WorkerProcess(Process process) : IAsyncDisposable
    {
        public async Task Stop()
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            try { await Stop(); }
            finally { process.Dispose(); }
        }
    }

    private static async Task<Guid> SeedConnection()
    {
        await using var connection = new NpgsqlConnection(Metadata);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO scheduler_db_connection (id, name, secret_key, enabled, admin_only)
            VALUES ($1, 'Integration reports', 'Reports', true, false)
            ON CONFLICT (secret_key) DO UPDATE SET enabled = true RETURNING id
            """, connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static string MakePasswordHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static async Task<JsonElement> Eventually(Func<Task<JsonElement>> fetch, Func<JsonElement, bool> matches,
        TimeSpan? timeout = null)
    {
        using var stop = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(12));
        while (!stop.IsCancellationRequested)
        {
            var value = await fetch();
            if (matches(value)) return value;
            try { await Task.Delay(300, stop.Token); }
            catch (OperationCanceledException) { break; }
        }
        throw new TimeoutException("Expected API state did not appear before the test deadline.");
    }

    private static async Task<JsonElement> Get(HttpClient client, string path) => await Read(await client.GetAsync(path));

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}. {body}");
            return JsonDocument.Parse(body).RootElement.Clone();
        }
    }
}
