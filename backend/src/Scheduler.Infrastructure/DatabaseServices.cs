using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Scheduler.Infrastructure;

public static class DatabaseServices
{
    public static IServiceCollection AddSchedulerDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<SchedulerDbContext>(options => options
            .UseNpgsql(GetConnectionString(configuration, "Scheduler"))
            .LogTo(_ => { }));
        return services;
    }

    public static string GetConnectionString(IConfiguration configuration, string key)
    {
        var configured = configuration.GetConnectionString(key);
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException($"Missing configuration: ConnectionStrings:{key}");
        return new NpgsqlConnectionStringBuilder(configured)
        {
            IncludeErrorDetail = false,
            LogParameters = false
        }.ConnectionString;
    }
}
