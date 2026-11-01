using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Strategy.Platform.Scheduling.Infrastructure;

#nullable disable

namespace Strategy.Platform.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "job_run",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    triggered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    instance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_run_job_key_fired_at",
                schema: "scheduling",
                table: "job_run",
                columns: new[] { "job_key", "fired_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_dead_at",
                schema: "scheduling",
                table: "outbox_message",
                column: "dead_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "scheduling",
                table: "outbox_message",
                column: "available_at",
                filter: "processed_at is null and dead_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                schema: "scheduling",
                table: "outbox_message",
                column: "processed_at");

            // Quartz.NET job store (official PostgreSQL script, embedded) in this schema; Quartz uses the "scheduling.qrtz_" table prefix.
            migrationBuilder.Sql("SET LOCAL search_path TO scheduling;\n" + QuartzScript.Read() + "\nSET LOCAL search_path TO DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS scheduling.qrtz_fired_triggers, scheduling.qrtz_paused_trigger_grps, scheduling.qrtz_scheduler_state,
                    scheduling.qrtz_locks, scheduling.qrtz_simprop_triggers, scheduling.qrtz_simple_triggers, scheduling.qrtz_cron_triggers,
                    scheduling.qrtz_blob_triggers, scheduling.qrtz_triggers, scheduling.qrtz_job_details, scheduling.qrtz_calendars;
                """);

            migrationBuilder.DropTable(
                name: "job_run",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "scheduling");
        }
    }
}
