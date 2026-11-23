using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using static Scheduler.Api.JobEndpoints;

namespace Scheduler.Api;

internal static class MonitoringEndpoints
{
    public static RouteGroupBuilder MapMonitoring(this RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{id:guid}/executions", (Guid id, [AsParameters] ExecutionFilter filters,
            ClaimsPrincipal actor, SchedulerDbContext db, CancellationToken ct) =>
            List(filters with { JobId = id }, actor, db, ct)).WithTags("Executions").Produces<PageResult<ExecutionItem>>();
        api.MapGet("/executions", List).WithTags("Executions").Produces<PageResult<ExecutionItem>>();
        api.MapGet("/executions/{id:guid}", async (Guid id, ClaimsPrincipal actor, SchedulerDbContext db, CancellationToken ct) =>
        {
            var execution = await VisibleExecutions(actor, db).SingleOrDefaultAsync(x => x.Id == id, ct);
            return execution is null ? Missing() : Results.Ok(ExecutionItem.From(execution, showDetail: true));
        }).WithTags("Executions").Produces<ExecutionItem>().ProducesProblem(404);
        api.MapGet("/dashboard/summary", Dashboard).WithTags("Dashboard");
        return api;
    }

    private static IQueryable<JobExecution> VisibleExecutions(ClaimsPrincipal actor, SchedulerDbContext db) =>
        VisibleExecutions(db.VisibleJobs(actor), db);

    private static IQueryable<JobExecution> VisibleExecutions(IQueryable<SchedulerJob> jobs, SchedulerDbContext db) =>
        db.Executions.AsNoTracking().Where(x => jobs.Any(job => job.Id == x.JobId));

    private static async Task<IResult> List([AsParameters] ExecutionFilter filter, ClaimsPrincipal actor,
        SchedulerDbContext db, CancellationToken ct)
    {
        if (!ValidPage(filter.Page, filter.PageSize))
            return Invalid("page", "頁碼或每頁筆數不在允許範圍。");
        if (filter.FromUtc > filter.ToUtc)
            return Invalid("fromUtc", "開始時間必須早於或等於結束時間。");
        ExecutionStatus? requestedStatus = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<ExecutionStatus>(filter.Status, ignoreCase: true, out var status) ||
                !string.Equals(status.ToString(), filter.Status, StringComparison.OrdinalIgnoreCase))
                return Invalid("status", "執行狀態無效。");
            requestedStatus = status;
        }

        var query = VisibleExecutions(actor, db);
        if (filter.JobId is { } jobId)
        {
            if (!await db.VisibleJobs(actor).AnyAsync(x => x.Id == jobId, ct))
                return Missing();
            query = query.Where(x => x.JobId == jobId);
        }
        if (!string.IsNullOrWhiteSpace(filter.JobName))
        {
            if (filter.JobName.Trim().Length > 150)
                return Invalid("jobName", "搜尋文字不可超過 150 字元。");
            var pattern = SearchPattern(filter.JobName);
            query = query.Where(x => EF.Functions.ILike(x.JobName, pattern, "!"));
        }
        if (requestedStatus is { } validStatus)
            query = query.Where(x => x.Status == validStatus);
        if (filter.FromUtc is { } from)
            query = query.Where(x => (x.StartUtc ?? x.ScheduledUtc) >= from.ToUniversalTime());
        if (filter.ToUtc is { } to)
            query = query.Where(x => (x.StartUtc ?? x.ScheduledUtc) <= to.ToUniversalTime());

        var number = filter.Page ?? 1;
        var size = filter.PageSize ?? 20;
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ScheduledUtc).ThenByDescending(x => x.Id)
            .Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PageResult<ExecutionItem>(items.Select(x => ExecutionItem.From(x)).ToArray(), total, number, size));
    }

    private static async Task<DashboardSummary> Dashboard(ClaimsPrincipal actor, SchedulerDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var since = time.GetUtcNow().AddDays(-1);
        var jobs = db.VisibleJobs(actor).Where(x => x.DeletedUtc == null);
        var runs = VisibleExecutions(actor, db);
        var jobCounts = await jobs.GroupBy(_ => 1).Select(items => new
        {
            Total = items.Count(),
            Enabled = items.Count(x => x.Enabled),
            Pending = items.Count(x => x.Revision != x.AppliedRevision)
        }).SingleOrDefaultAsync(ct);
        var runCounts = await runs.Where(x => x.EndUtc >= since || x.Status == ExecutionStatus.Running)
            .GroupBy(x => x.Status).Select(items => new { Status = items.Key, Count = items.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var recent = await runs.OrderByDescending(x => x.ScheduledUtc).ThenByDescending(x => x.Id).Take(8).ToListAsync(ct);
        return new(jobCounts?.Total ?? 0, jobCounts?.Enabled ?? 0,
            runCounts.GetValueOrDefault(ExecutionStatus.Running), runCounts.GetValueOrDefault(ExecutionStatus.Success),
            runCounts.GetValueOrDefault(ExecutionStatus.Failed) + runCounts.GetValueOrDefault(ExecutionStatus.Timeout),
            jobCounts?.Pending ?? 0, recent.Select(x => ExecutionItem.From(x)).ToArray());
    }

    internal sealed record ExecutionFilter(Guid? JobId, string? JobName, string? Status,
        DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int? Page, int? PageSize);
}
