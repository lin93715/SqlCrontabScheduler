using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Scheduler.Infrastructure;

namespace Scheduler.Api;

internal sealed record LoginRequest(string? Username, string? Password);
internal sealed record Session(string Username, string Role);

internal sealed class LocalAccounts
{
    public const string Admin = "Admin";
    public const string Member = "User";
    private const int PasswordIterations = 600_000;
    private readonly Dictionary<string, Account> accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Account dummy = new("", Member, RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(32));

    public LocalAccounts(IConfiguration config)
    {
        foreach (var item in config.GetSection("Auth:Users").GetChildren())
        {
            var name = item["Username"]?.Trim();
            var role = item["Role"];
            var encoded = item["PasswordHash"]?.Split('.');
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || role is not (Admin or Member)
                || encoded is not [_, _])
                throw new InvalidOperationException("Invalid Auth:Users entry.");

            var salt = Convert.FromBase64String(encoded[0]);
            var hash = Convert.FromBase64String(encoded[1]);
            if (salt.Length != 16 || hash.Length != 32 || !accounts.TryAdd(name, new(name, role, salt, hash)))
                throw new InvalidOperationException("Invalid or duplicate Auth:Users entry.");
        }
    }

    public Session? Authenticate(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 100 || password is null || password.Length > 1024)
            return null;
        var found = accounts.TryGetValue(username.Trim(), out var account);
        account ??= dummy;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, account.Salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, account.Hash) && found
            ? new Session(account.Name, account.Role) : null;
    }

    private sealed record Account(string Name, string Role, byte[] Salt, byte[] Hash);
}

internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");
        auth.MapPost("/login", async (LoginRequest request, HttpContext context, LocalAccounts accounts) =>
        {
            if (accounts.Authenticate(request.Username, request.Password) is not { } session)
                return Results.Problem(statusCode: 401, title: "帳號或密碼驗證失敗。");

            var identity = new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, session.Username), new(ClaimTypes.Name, session.Username),
                 new(ClaimTypes.Role, session.Role)], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(new ClaimsPrincipal(identity));
            return Results.Ok(session);
        }).AllowAnonymous().RequireRateLimiting("login").Produces<Session>().ProducesProblem(401);

        auth.MapGet("/me", (ClaimsPrincipal actor) =>
            new Session(actor.Identity!.Name!, actor.IsInRole(LocalAccounts.Admin) ? LocalAccounts.Admin : LocalAccounts.Member));
        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        });
        return api;
    }

    public static IQueryable<Scheduler.Domain.SchedulerJob> VisibleJobs(this SchedulerDbContext db, ClaimsPrincipal actor)
    {
        var user = actor.Identity!.Name!;
        return actor.IsInRole(LocalAccounts.Admin) ? db.Jobs : db.Jobs.Where(job => job.CreatedBy == user);
    }

    public static IQueryable<Scheduler.Domain.DatabaseConnection> AvailableConnections(
        this SchedulerDbContext db, ClaimsPrincipal actor) =>
        db.Connections.Where(x => x.Enabled && (!x.AdminOnly || actor.IsInRole(LocalAccounts.Admin)));
}
