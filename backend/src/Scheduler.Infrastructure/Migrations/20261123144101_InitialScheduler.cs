using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduler_db_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    secret_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    admin_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduler_db_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    database_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sql_text = table.Column<string>(type: "text", nullable: false),
                    schedule = table.Column<string>(type: "jsonb", nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    paused = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    next_run_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduler_job", x => x.id);
                    table.CheckConstraint("ck_scheduler_job_timeout", "timeout_seconds BETWEEN 1 AND 300");
                    table.ForeignKey(
                        name: "FK_scheduler_job_scheduler_db_connection_database_connection_id",
                        column: x => x.database_connection_id,
                        principalTable: "scheduler_db_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    occurred_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    manual_request_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduler_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduler_audit_log_scheduler_job_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduler_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rows_affected = table.Column<int>(type: "integer", nullable: true),
                    rows_read = table.Column<int>(type: "integer", nullable: true),
                    row_limit_reached = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduler_execution", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduler_execution_scheduler_job_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduler_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_manual_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduler_manual_request", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduler_manual_request_scheduler_job_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduler_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_audit_log_job_id_occurred_utc",
                table: "scheduler_audit_log",
                columns: new[] { "job_id", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_audit_log_occurred_utc",
                table: "scheduler_audit_log",
                column: "occurred_utc");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_db_connection_name",
                table: "scheduler_db_connection",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_db_connection_secret_key",
                table: "scheduler_db_connection",
                column: "secret_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_execution_end_utc",
                table: "scheduler_execution",
                column: "end_utc");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_execution_job_id_scheduled_utc",
                table: "scheduler_execution",
                columns: new[] { "job_id", "scheduled_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_execution_scheduled_utc",
                table: "scheduler_execution",
                column: "scheduled_utc");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_execution_start_utc",
                table: "scheduler_execution",
                column: "start_utc");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_execution_status_scheduled_utc",
                table: "scheduler_execution",
                columns: new[] { "status", "scheduled_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_job_created_by_deleted_utc_updated_utc",
                table: "scheduler_job",
                columns: new[] { "created_by", "deleted_utc", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_job_database_connection_id",
                table: "scheduler_job",
                column: "database_connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_job_deleted_utc",
                table: "scheduler_job",
                column: "deleted_utc");

            migrationBuilder.CreateIndex(
                name: "IX_scheduler_manual_request_processed_utc_requested_utc",
                table: "scheduler_manual_request",
                columns: new[] { "processed_utc", "requested_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_scheduler_manual_request_pending",
                table: "scheduler_manual_request",
                column: "job_id",
                unique: true,
                filter: "processed_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduler_audit_log");

            migrationBuilder.DropTable(
                name: "scheduler_execution");

            migrationBuilder.DropTable(
                name: "scheduler_manual_request");

            migrationBuilder.DropTable(
                name: "scheduler_job");

            migrationBuilder.DropTable(
                name: "scheduler_db_connection");
        }
    }
}
