using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase10AgileStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "review_date",
                schema: "portfolio",
                table: "project_risk",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agile_approval_mode",
                schema: "portfolio",
                table: "project",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "board",
                schema: "portfolio",
                table: "project",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checklist",
                schema: "portfolio",
                table: "project",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "definition_of_done",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "health_overrides",
                schema: "portfolio",
                table: "project",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "methodology",
                schema: "portfolio",
                table: "project",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "waterfall");

            migrationBuilder.AddColumn<Guid>(
                name: "portfolio_group_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "programme_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replan_change_request_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sprint_length_days",
                schema: "portfolio",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "narrative_ar",
                schema: "portfolio",
                table: "monthly_update",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "narrative_en",
                schema: "portfolio",
                table: "monthly_update",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sprints",
                schema: "portfolio",
                table: "monthly_update",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "delivered_in_sprints",
                schema: "portfolio",
                table: "milestone",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quality_at",
                schema: "portfolio",
                table: "milestone",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "quality_by_employee_id",
                schema: "portfolio",
                table: "milestone",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quality_comment",
                schema: "portfolio",
                table: "milestone",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quality_status",
                schema: "portfolio",
                table: "milestone",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rated_at",
                schema: "portfolio",
                table: "contract",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rated_by_user_id",
                schema: "portfolio",
                table: "contract",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating_comment",
                schema: "portfolio",
                table: "contract",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_communication",
                schema: "portfolio",
                table: "contract",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_quality",
                schema: "portfolio",
                table: "contract",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_timeliness",
                schema: "portfolio",
                table: "contract",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposed_methodology",
                schema: "portfolio",
                table: "change_request",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "baseline",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    taken_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baseline", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "benefit",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    measure = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    baseline_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    target_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    measurement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    measured_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    item_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "communication_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stakeholder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    method = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_due = table.Column<DateOnly>(type: "date", nullable: true),
                    last_done = table.Column<DateOnly>(type: "date", nullable: true),
                    reminded_for = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inactive_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communication_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_payment",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_payment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "epic",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    weight = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    acceptance_criteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_group",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_template",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    methodology = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raci_entry",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inactive_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raci_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raid_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depends_on_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depends_on_milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raid_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "release",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sprint",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    capacity_points = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    committed_points = table.Column<int>(type: "integer", nullable: false),
                    committed_story_ids = table.Column<string>(type: "jsonb", nullable: false),
                    completed_points = table.Column<int>(type: "integer", nullable: false),
                    demo_outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stakeholder_feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    went_well = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    to_improve = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sprint", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "story_event",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sprint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_column = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to_column = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_story_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_story",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sprint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    as_a = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    i_want = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    so_that = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    acceptance_criteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    story_points = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assignee_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    column_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted = table.Column<bool>(type: "boolean", nullable: false),
                    approved_done = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_story", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_portfolio_group_id",
                schema: "portfolio",
                table: "project",
                column: "portfolio_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_programme_id",
                schema: "portfolio",
                table: "project",
                column: "programme_id");

            migrationBuilder.CreateIndex(
                name: "ix_baseline_project_id_version_no",
                schema: "portfolio",
                table: "baseline",
                columns: new[] { "project_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_benefit_project_id",
                schema: "portfolio",
                table: "benefit",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_benefit_request_id",
                schema: "portfolio",
                table: "benefit",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_communication_item_next_due",
                schema: "portfolio",
                table: "communication_item",
                column: "next_due");

            migrationBuilder.CreateIndex(
                name: "ix_communication_item_project_id",
                schema: "portfolio",
                table: "communication_item",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_payment_contract_id",
                schema: "portfolio",
                table: "contract_payment",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_payment_project_id",
                schema: "portfolio",
                table: "contract_payment",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_epic_code",
                schema: "portfolio",
                table: "epic",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_epic_project_id",
                schema: "portfolio",
                table: "epic",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_group_code",
                schema: "portfolio",
                table: "portfolio_group",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_template_code",
                schema: "portfolio",
                table: "project_template",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raci_entry_project_id",
                schema: "portfolio",
                table: "raci_entry",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_item_code",
                schema: "portfolio",
                table: "raid_item",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raid_item_depends_on_project_id",
                schema: "portfolio",
                table: "raid_item",
                column: "depends_on_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_item_project_id",
                schema: "portfolio",
                table: "raid_item",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_project_id",
                schema: "portfolio",
                table: "release",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_project_id_number",
                schema: "portfolio",
                table: "sprint",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_story_event_project_id",
                schema: "portfolio",
                table: "story_event",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_story_event_story_id",
                schema: "portfolio",
                table: "story_event",
                column: "story_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_story_code",
                schema: "portfolio",
                table: "user_story",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_story_epic_id",
                schema: "portfolio",
                table: "user_story",
                column: "epic_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_story_project_id",
                schema: "portfolio",
                table: "user_story",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_story_sprint_id",
                schema: "portfolio",
                table: "user_story",
                column: "sprint_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "baseline",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "benefit",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "communication_item",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "contract_payment",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "epic",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolio_group",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "project_template",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "raci_entry",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "raid_item",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "release",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "sprint",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "story_event",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "user_story",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_project_portfolio_group_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropIndex(
                name: "ix_project_programme_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "review_date",
                schema: "portfolio",
                table: "project_risk");

            migrationBuilder.DropColumn(
                name: "agile_approval_mode",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "board",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "checklist",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "definition_of_done",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "health_overrides",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "methodology",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "portfolio_group_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "programme_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "replan_change_request_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "sprint_length_days",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "narrative_ar",
                schema: "portfolio",
                table: "monthly_update");

            migrationBuilder.DropColumn(
                name: "narrative_en",
                schema: "portfolio",
                table: "monthly_update");

            migrationBuilder.DropColumn(
                name: "sprints",
                schema: "portfolio",
                table: "monthly_update");

            migrationBuilder.DropColumn(
                name: "delivered_in_sprints",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "quality_at",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "quality_by_employee_id",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "quality_comment",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "quality_status",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "rated_at",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "rated_by_user_id",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "rating_comment",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "rating_communication",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "rating_quality",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "rating_timeliness",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "proposed_methodology",
                schema: "portfolio",
                table: "change_request");
        }
    }
}
