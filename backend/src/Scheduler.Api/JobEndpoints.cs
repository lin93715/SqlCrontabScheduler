using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;

namespace Scheduler.Api;

internal static class JobEndpoints
{
    public static RouteGroupBuilder MapJobs(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").WithTags("Jobs");
        jobs.MapGet("", List).Produces<PageResult<JobListItem>>();
        jobs.MapGet("/{id:guid}", Get).Produces<JobDetail>().ProducesProblem(404);
        jobs.MapPost("", (JobWriteRequest request, ClaimsPrincipal actor, SchedulerDbContext db,
            ScheduleCalculator calculator, TimeProvider time, CancellationToken ct) =>
            Write(null, request, actor, db, calculator, time, ct))
            .Produces<JobDetail>(201).ProducesValidationProblem();
        jobs.MapPut("/{id:guid}", (Guid id, JobWriteRequest request, ClaimsPrincipal actor, SchedulerDbContext db,
            ScheduleCalculator calculator, TimeProvider time, CancellationToken ct) =>
            Write(id, request, actor, db, calculator, time, ct))
            .Produces<JobDetail>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);
        jobs.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal actor, SchedulerDbContext db, TimeProvider time, CancellationToken ct) =>
            Change(id, "delete", actor, db, time, ct)).Produces(204).ProducesProblem(404);

        foreach (var action in new[] { "enable", "disable", "pause", "resume" })
        {
            jobs.MapPost($"/{{id:guid}}/{action}", (Guid id, ClaimsPrincipal actor, SchedulerDbContext db,
                TimeProvider time, CancellationToken ct) => Change(id, action, actor, db, time, ct))
                .Produces<JobDetail>().ProducesProblem(404).ProducesProblem(409);
        }

        jobs.MapPost("/{id:guid}/execute", ManualExecute)
            .Produces<ManualAccepted>(202).ProducesProblem(404).ProducesProblem(409);

        api.MapPost("/schedules/preview", (PreviewRequest request, ScheduleCalculator calculator) =>
        {
            if (request.Schedule is null)
                return Invalid("schedule", "請提供排程設定。");
            try { return Results.Ok(calculator.Preview(request.Schedule, request.TimeZone)); }
            catch (ScheduleValidationException ex) { return Invalid(ex.Field, ex.Message); }
        }).WithTags("Schedules").Produces<SchedulePreview>().ProducesValidationProblem();

        api.MapGet("/connections", async (ClaimsPrincipal actor, SchedulerDbContext db, CancellationToken ct) =>
            await db.AvailableConnections(actor).OrderBy(x => x.Name)
                .Select(x => new ConnectionItem(x.Id, x.Name)).ToListAsync(ct)).WithTags("Connections");
        return api;
    }

    private static async Task<IResult> List(string? search, int? page, int? pageSize, ClaimsPrincipal actor,
        SchedulerDbContext db, CancellationToken ct)
    {
        if (!ValidPage(page, pageSize))
            return Invalid("page", "頁碼或每頁筆數不在允許範圍。");
        var number = page ?? 1;
        var size = pageSize ?? 20;
        var query = db.VisibleJobs(actor).AsNoTracking().Where(x => x.DeletedUtc == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (search.Trim().Length > 150)
                return Invalid("search", "搜尋文字不可超過 150 字元。");
            var pattern = SearchPattern(search);
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "!"));
        }

        var total = await query.CountAsync(ct);
        var items = await query.Include(x => x.DatabaseConnection).OrderByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PageResult<JobListItem>(items.Select(JobListItem.From).ToArray(), total, number, size));
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal actor, SchedulerDbContext db, CancellationToken ct)
    {
        var job = await Find(id, actor, db, ct);
        return job is null ? Missing() : Results.Ok(JobDetail.From(job));
    }

    private static async Task<IResult> Write(Guid? id, JobWriteRequest request, ClaimsPrincipal actor,
        SchedulerDbContext db, ScheduleCalculator calculator, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 150 || name.Contains('\0'))
            errors["name"] = ["請輸入 1–150 字元的排程名稱。"];
        if (description?.Length > 2000 || description?.Contains('\0') == true)
            errors["description"] = ["說明不可超過 2,000 字元。"];
        if (string.IsNullOrWhiteSpace(request.SqlText) || request.SqlText.Length > 100_000 || request.SqlText.Contains('\0'))
            errors["sqlText"] = ["請輸入有效的 SQL，最多 100,000 字元。"];
        if (request.TimeoutSeconds is < 1 or > 300)
            errors["timeoutSeconds"] = ["逾時秒數須介於 1 至 300。"];
        if (request.Schedule is null)
            errors["schedule"] = ["請提供排程設定。"];
        if (id.HasValue && !request.Revision.HasValue)
            errors["revision"] = ["更新需要目前的排程版本。"];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var existing = id is { } known ? await Find(known, actor, db, ct) : null;
        if (id.HasValue && existing is null)
            return Missing();
        if (existing is not null && existing.Revision != request.Revision)
            return Conflict("排程已更新，請重新載入後再試。");

        var connection = await db.AvailableConnections(actor).SingleOrDefaultAsync(x => x.Id == request.DatabaseConnectionId, ct);
        if (connection is null || (connection.AdminOnly && existing is not null && existing.CreatedBy != actor.Identity!.Name))
            return Invalid("databaseConnectionId", "請選擇你可使用的資料庫連線。");

        SchedulePreview preview;
        try { preview = calculator.Preview(request.Schedule!, request.TimeZone); }
        catch (ScheduleValidationException ex) { return Invalid(ex.Field, ex.Message); }

        var now = time.GetUtcNow();
        var job = existing ?? new SchedulerJob
        {
            Id = Guid.NewGuid(), Name = name!, SqlText = request.SqlText!, TimeZone = preview.TimeZone,
            CronExpression = preview.CronExpression, Schedule = request.Schedule!, CreatedUtc = now, CreatedBy = actor.Identity!.Name!
        };
        job.Name = name!;
        job.Description = description;
        job.DatabaseConnection = connection;
        job.AdminTargetAuthorized = connection.AdminOnly;
        job.SqlText = request.SqlText!;
        job.Schedule = request.Schedule!;
        job.CronExpression = preview.CronExpression;
        job.TimeZone = preview.TimeZone;
        job.TimeoutSeconds = request.TimeoutSeconds;
        job.Enabled = request.Enabled;
        job.Paused = request.Enabled && job.Paused;
        job.UpdatedUtc = now;
        job.Revision = Guid.NewGuid();
        if (existing is null)
            db.Jobs.Add(job);
        AddAudit(db, job.Id, actor, existing is null ? "create" : "update", now);
        await db.SaveChangesAsync(ct);
        var response = JobDetail.From(job);
        return existing is null ? Results.Created($"/api/jobs/{job.Id}", response) : Results.Ok(response);
    }

    private static async Task<IResult> Change(Guid id, string action, ClaimsPrincipal actor,
        SchedulerDbContext db, TimeProvider time, CancellationToken ct)
    {
        var job = await Find(id, actor, db, ct);
        if (job is null)
            return Missing();
        if ((action is "pause" or "resume") && !job.Enabled)
            return Conflict("請先啟用排程。");

        var now = time.GetUtcNow();
        switch (action)
        {
            case "delete":
                job.DeletedUtc = now;
                job.Enabled = false;
                job.Paused = false;
                break;
            case "enable":
                if (!CanUseConnection(job, actor))
                    return Conflict("此資料庫連線目前無法使用。");
                if (job.Enabled)
                    return Results.Ok(JobDetail.From(job));
                job.Enabled = true;
                break;
            case "disable":
                if (!job.Enabled)
                    return Results.Ok(JobDetail.From(job));
                job.Enabled = false;
                job.Paused = false;
                break;
            case "pause":
                if (job.Paused)
                    return Results.Ok(JobDetail.From(job));
                job.Paused = true;
                break;
            case "resume":
                if (!CanUseConnection(job, actor))
                    return Conflict("此資料庫連線目前無法使用。");
                if (!job.Paused)
                    return Results.Ok(JobDetail.From(job));
                job.Paused = false;
                break;
        }
        job.UpdatedUtc = now;
        job.Revision = Guid.NewGuid();
        AddAudit(db, job.Id, actor, action, now);
        await db.SaveChangesAsync(ct);
        return action == "delete" ? Results.NoContent() : Results.Ok(JobDetail.From(job));
    }

    private static async Task<IResult> ManualExecute(Guid id, ClaimsPrincipal actor, SchedulerDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var job = await Find(id, actor, db, ct);
        if (job is null)
            return Missing();
        if (!job.Enabled || !CanUseConnection(job, actor))
            return Conflict("排程須啟用，且資料庫連線須可使用。");
        var now = time.GetUtcNow();
        var request = new ManualRequest { Id = Guid.NewGuid(), JobId = id, JobRevision = job.Revision, RequestedUtc = now };
        db.ManualRequests.Add(request);
        AddAudit(db, id, actor, "execute", now, request.Id);
        await db.SaveChangesAsync(ct);
        return Results.Accepted(value: new ManualAccepted(request.Id, now));
    }

    private static Task<SchedulerJob?> Find(Guid id, ClaimsPrincipal actor, SchedulerDbContext db, CancellationToken ct) =>
        db.VisibleJobs(actor).Include(x => x.DatabaseConnection)
            .SingleOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null, ct);

    private static bool CanUseConnection(SchedulerJob job, ClaimsPrincipal actor) =>
        job.DatabaseConnection.Enabled && (!job.DatabaseConnection.AdminOnly || (job.AdminTargetAuthorized && actor.IsInRole(LocalAccounts.Admin)));

    private static void AddAudit(SchedulerDbContext db, Guid id, ClaimsPrincipal actor, string action,
        DateTimeOffset now, Guid? requestId = null) => db.Audit.Add(new AuditEntry
        {
            Id = Guid.NewGuid(), JobId = id, Actor = actor.Identity!.Name!, Action = action,
            OccurredUtc = now, ManualRequestId = requestId
        });

    internal static bool ValidPage(int? page, int? size) =>
        page is null or (>= 1 and <= 100_000) && size is null or (>= 1 and <= 100);
    internal static string SearchPattern(string search) =>
        $"%{search.Trim().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_")}%";
    internal static IResult Missing() => Results.Problem(statusCode: 404, title: "找不到該筆資料。");
    internal static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);
}
