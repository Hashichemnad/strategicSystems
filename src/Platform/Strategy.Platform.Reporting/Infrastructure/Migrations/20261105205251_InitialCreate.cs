using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Strategy.Platform.Reporting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "brief",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scope_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    ai_generated = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brief", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dashboard",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_ar = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    draft_json = table.Column<string>(type: "jsonb", nullable: true),
                    published_version = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seed_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_role",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    rows = table.Column<int>(type: "integer", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fact",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    metric = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dims_json = table.Column<string>(type: "jsonb", nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "my_work_layout",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cards_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_my_work_layout", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_log",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_month = table.Column<DateOnly>(type: "date", nullable: false),
                    to_month = table.Column<DateOnly>(type: "date", nullable: false),
                    facts = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dataset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_filter",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    filters_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_filter", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    period_rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    recipients_json = table.Column<string>(type: "jsonb", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "share",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    principal_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_pin",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_pin", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brief_created_by",
                schema: "reporting",
                table: "brief",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_code",
                schema: "reporting",
                table: "dashboard",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_role_dashboard_id_role_key",
                schema: "reporting",
                table: "dashboard_role",
                columns: new[] { "dashboard_id", "role_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_role_role_key",
                schema: "reporting",
                table: "dashboard_role",
                column: "role_key");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_schedule_id_user_id_period_key",
                schema: "reporting",
                table: "delivery",
                columns: new[] { "schedule_id", "user_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_metric_month",
                schema: "reporting",
                table: "fact",
                columns: new[] { "metric", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_my_work_layout_user_id",
                schema: "reporting",
                table: "my_work_layout",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_log_system_code_at",
                schema: "reporting",
                table: "refresh_log",
                columns: new[] { "system_code", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_report_owner_user_id",
                schema: "reporting",
                table: "report",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_filter_user_id_dashboard_id",
                schema: "reporting",
                table: "saved_filter",
                columns: new[] { "user_id", "dashboard_id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_active_next_run_at",
                schema: "reporting",
                table: "schedule",
                columns: new[] { "active", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_share_target_kind_target_id_principal_kind_principal_ref",
                schema: "reporting",
                table: "share",
                columns: new[] { "target_kind", "target_id", "principal_kind", "principal_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_pin_user_id",
                schema: "reporting",
                table: "user_pin",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brief",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "dashboard",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "dashboard_role",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "delivery",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "my_work_layout",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "refresh_log",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "saved_filter",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "schedule",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "share",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "user_pin",
                schema: "reporting");
        }
    }
}
