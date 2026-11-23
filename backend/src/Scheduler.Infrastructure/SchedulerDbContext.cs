using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<SchedulerJob> Jobs => Set<SchedulerJob>();
    public DbSet<DatabaseConnection> Connections => Set<DatabaseConnection>();
    public DbSet<JobExecution> Executions => Set<JobExecution>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ManualRequest> ManualRequests => Set<ManualRequest>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var connection = model.Entity<DatabaseConnection>();
        connection.ToTable("scheduler_db_connection");
        connection.Property(x => x.Name).HasMaxLength(150);
        connection.Property(x => x.SecretKey).HasMaxLength(100);
        connection.HasIndex(x => x.Name).IsUnique();
        connection.HasIndex(x => x.SecretKey).IsUnique();

        var job = model.Entity<SchedulerJob>();
        job.ToTable("scheduler_job", t => t.HasCheckConstraint(
            "ck_scheduler_job_timeout", "timeout_seconds BETWEEN 1 AND 300"));
        job.Property(x => x.Name).HasMaxLength(150);
        job.Property(x => x.Description).HasMaxLength(2000);
        job.Property(x => x.TimeZone).HasMaxLength(128);
        job.Property(x => x.CronExpression).HasMaxLength(120);
        job.Property(x => x.CreatedBy).HasMaxLength(100);
        job.Property(x => x.Revision).IsConcurrencyToken();
        job.Property(x => x.Schedule).HasColumnType("jsonb")
            .HasConversion(x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
                x => JsonSerializer.Deserialize<ScheduleSettings>(x, (JsonSerializerOptions?)null)!);
        job.HasOne(x => x.DatabaseConnection).WithMany().HasForeignKey(x => x.DatabaseConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => new { x.CreatedBy, x.DeletedUtc, x.UpdatedUtc });
        job.HasIndex(x => x.DeletedUtc);

        var execution = model.Entity<JobExecution>();
        execution.ToTable("scheduler_execution");
        execution.Property(x => x.JobName).HasMaxLength(150);
        execution.Property(x => x.TriggerType).HasConversion<string>().HasMaxLength(20);
        execution.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        execution.Property(x => x.ErrorMessage).HasMaxLength(500);
        execution.Property(x => x.ErrorDetail).HasMaxLength(500);
        execution.HasOne<SchedulerJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        execution.HasIndex(x => x.ScheduledUtc);
        execution.HasIndex(x => new { x.JobId, x.ScheduledUtc });
        execution.HasIndex(x => new { x.Status, x.ScheduledUtc });
        execution.HasIndex(x => x.StartUtc);
        execution.HasIndex(x => x.EndUtc);

        var audit = model.Entity<AuditEntry>();
        audit.ToTable("scheduler_audit_log");
        audit.Property(x => x.Actor).HasMaxLength(100);
        audit.Property(x => x.Action).HasMaxLength(30);
        audit.HasOne<SchedulerJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        audit.HasIndex(x => new { x.JobId, x.OccurredUtc });
        audit.HasIndex(x => x.OccurredUtc);

        var manual = model.Entity<ManualRequest>();
        manual.ToTable("scheduler_manual_request");
        manual.HasOne<SchedulerJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        manual.HasIndex(x => new { x.ProcessedUtc, x.RequestedUtc });
        manual.HasIndex(x => x.JobId).IsUnique().HasFilter("processed_utc IS NULL")
            .HasDatabaseName("ux_scheduler_manual_request_pending");

        foreach (var property in model.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
            property.SetColumnName(Regex.Replace(property.Name, "(?<!^)[A-Z]", "_$0").ToLowerInvariant());
    }
}
