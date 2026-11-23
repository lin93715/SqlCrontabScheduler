using Quartz;
using Scheduler.Application;
using Scheduler.Infrastructure;
using Scheduler.Worker;

internal static class WorkerProgram
{
    private static Task Main(string[] args) => WorkerHost.CreateBuilder(args).Build().RunAsync();
}

namespace Scheduler.Worker
{
    public static class WorkerHost
    {
        public static HostApplicationBuilder CreateBuilder(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            var config = builder.Configuration;
            var parallelism = config.GetValue("Worker:MaxConcurrentJobs", 4);
            if (parallelism is < 1 or > 32)
                throw new InvalidOperationException("Worker:MaxConcurrentJobs must be 1–32.");

            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSchedulerDatabase(config);
            builder.Services.AddSingleton<ISqlExecutor, PostgresSqlExecutor>();
            builder.Services.AddSingleton<ExecutionRunner>();
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(45));
            builder.Services.AddQuartz(quartz =>
            {
                quartz.SchedulerName = "SqlCrontabScheduler";
                quartz.SchedulerId = "NON_CLUSTERED";
                quartz.MisfireThreshold = TimeSpan.FromSeconds(30);
                quartz.UseDefaultThreadPool(pool => pool.MaxConcurrency = parallelism);
                quartz.UsePersistentStore(store =>
                {
                    store.UseProperties = true;
                    store.PerformSchemaValidation = true;
                    store.RetryInterval = TimeSpan.FromSeconds(5);
                    store.UsePostgres(postgres =>
                    {
                        postgres.ConnectionString = DatabaseServices.GetConnectionString(config, "Scheduler");
                        postgres.TablePrefix = "qrtz_";
                    });
                    store.UseSystemTextJsonSerializer();
                });
            });
            builder.Services.AddHostedService<SchedulerCoordinator>();
            return builder;
        }
    }
}
