using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Scheduler.Api;
using Scheduler.Application;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 400_000);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
var protection = builder.Services.AddDataProtection().SetApplicationName("SqlCrontabScheduler");
if (builder.Configuration["Auth:DataProtectionKeyPath"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
else
    protection.UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ScheduleCalculator>();
builder.Services.AddSingleton<LocalAccounts>();
builder.Services.AddSchedulerDatabase(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealth>("metadata", tags: ["ready"]);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "scheduler.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
    new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
_ = app.Services.GetRequiredService<LocalAccounts>();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        DbUpdateConcurrencyException => (409, "排程已更新，請重新載入後再試。"),
        DbUpdateException { InnerException: PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_scheduler_manual_request_pending" } }
            => (409, "這個排程已有待執行的手動請求。"),
        BadHttpRequestException bad => (bad.StatusCode, "請求內容或格式無效。"),
        _ => (500, "處理請求時發生錯誤。")
    };
    await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
}));
app.UseStatusCodePages(async status =>
    await Results.Problem(statusCode: status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext));
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && context.Request.Headers["X-Scheduler-Request"] != "1")
        {
            await Results.Problem(statusCode: 403, title: "缺少請求來源驗證標頭。").ExecuteAsync(context);
            return;
        }
    }

    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = x => x.Tags.Contains("ready") }).AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();
else
    app.MapOpenApi().RequireAuthorization(policy => policy.RequireRole(LocalAccounts.Admin));
app.MapGroup("/api").MapAuth().MapJobs().MapMonitoring();
app.Run();

public partial class Program;

internal sealed class DatabaseHealth(IDbContextFactory<SchedulerDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            _ = await db.Jobs.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Metadata database unavailable or not migrated.");
        }
    }
}
