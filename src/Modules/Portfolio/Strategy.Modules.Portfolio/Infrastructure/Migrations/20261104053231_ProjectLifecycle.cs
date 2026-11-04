using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProjectLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "annual_licence_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "annual_maintenance_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "annual_operation_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "annual_other_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_process",
                schema: "portfolio",
                table: "project",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_result",
                schema: "portfolio",
                table: "project",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_step_code",
                schema: "portfolio",
                table: "project",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_step_name_ar",
                schema: "portfolio",
                table: "project",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_step_name_en",
                schema: "portfolio",
                table: "project",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "community_satisfaction",
                schema: "portfolio",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                schema: "portfolio",
                table: "project",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "contracted_initial_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_performance",
                schema: "portfolio",
                table: "project",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "employee_satisfaction",
                schema: "portfolio",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "impact_notes",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "lpo_uploaded",
                schema: "portfolio",
                table: "project",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "main_deliverables",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_project_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "monthly_approved_at",
                schema: "portfolio",
                table: "project",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "operating_costs_from",
                schema: "portfolio",
                table: "project",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operational_themes",
                schema: "portfolio",
                table: "project",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ownership_taken_at",
                schema: "portfolio",
                table: "project",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "performance",
                schema: "portfolio",
                table: "project",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "planned_progress",
                schema: "portfolio",
                table: "project",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "pm_employee_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "previous_operational_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "progress_status",
                schema: "portfolio",
                table: "project",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "on-track");

            migrationBuilder.AddColumn<string>(
                name: "resources_contract",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resources_financial",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resources_human",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resources_other",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resources_technical",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "structural_changes",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sub_state",
                schema: "portfolio",
                table: "project",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "ownership");

            migrationBuilder.AddColumn<string>(
                name: "summary_of_achievement",
                schema: "portfolio",
                table: "project",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "team_formed_at",
                schema: "portfolio",
                table: "project",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "team_id",
                schema: "portfolio",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template",
                schema: "portfolio",
                table: "project",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "update_opened_at",
                schema: "portfolio",
                table: "project",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vendor_satisfaction",
                schema: "portfolio",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year1actual_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year1contracted_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year2actual_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year2contracted_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year3actual_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year3contracted_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year4actual_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "year4contracted_cost",
                schema: "portfolio",
                table: "project",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "challenge",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    impact = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    achieved_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    recommendation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    report_date = table.Column<DateOnly>(type: "date", nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_challenge", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_request",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    areas = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    impact_of_non_response = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    proposed_name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proposed_name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proposed_description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    proposed_scope = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    proposed_start = table.Column<DateOnly>(type: "date", nullable: true),
                    proposed_end = table.Column<DateOnly>(type: "date", nullable: true),
                    proposed_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_affected = table.Column<bool>(type: "boolean", nullable: false),
                    new_pm_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merge_into_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    implemented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied = table.Column<string>(type: "jsonb", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    supplier = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    contract_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_contract", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recommendation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_lesson", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
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
                    table.PrimaryKey("pk_meeting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "milestone",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_header = table.Column<bool>(type: "boolean", nullable: false),
                    high_level = table.Column<bool>(type: "boolean", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    max_achieved = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    pending_achieve = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    pending_actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    pending_actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    update_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    causality = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    corrective_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    evidence_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_deliverable = table.Column<bool>(type: "boolean", nullable: false),
                    main_deliverable = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    acceptance_criteria = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    quality_met = table.Column<bool>(type: "boolean", nullable: true),
                    is_innovative = table.Column<bool>(type: "boolean", nullable: false),
                    innovative_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestone", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monthly_update",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_by = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submit_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_update", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planning_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_planning_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_kpi",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kpi_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    equation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    index_class_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    target_year = table.Column<int>(type: "integer", nullable: true),
                    when_measured = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    current_result = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_project_kpi", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_risk",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    category_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    probability = table.Column<int>(type: "integer", nullable: false),
                    impact = table.Column<int>(type: "integer", nullable: false),
                    risk_factor = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    control_evaluation = table.Column<int>(type: "integer", nullable: true),
                    inherent_score = table.Column<int>(type: "integer", nullable: false),
                    inherent_band = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    residual_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    residual_band = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    response = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mitigation_actions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    implementation_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    implementation_start = table.Column<DateOnly>(type: "date", nullable: true),
                    implementation_end = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rms_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_project_risk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stakeholder",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    classification_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    interest_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    impact_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    communication_method = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    communication_frequency = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_stakeholder", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_member",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    role_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    responsibilities = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_team_member", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_pm_employee_id",
                schema: "portfolio",
                table: "project",
                column: "pm_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_section_unit_id",
                schema: "portfolio",
                table: "project",
                column: "section_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_status_sub_state",
                schema: "portfolio",
                table: "project",
                columns: new[] { "status", "sub_state" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_project_id",
                schema: "portfolio",
                table: "challenge",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_request_number",
                schema: "portfolio",
                table: "change_request",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_request_project_id",
                schema: "portfolio",
                table: "change_request",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_project_id",
                schema: "portfolio",
                table: "contract",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_project_id",
                schema: "portfolio",
                table: "lesson",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_project_id",
                schema: "portfolio",
                table: "meeting",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_milestone_code",
                schema: "portfolio",
                table: "milestone",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_milestone_project_id",
                schema: "portfolio",
                table: "milestone",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_update_project_id_period",
                schema: "portfolio",
                table: "monthly_update",
                columns: new[] { "project_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monthly_update_status",
                schema: "portfolio",
                table: "monthly_update",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_planning_item_project_id",
                schema: "portfolio",
                table: "planning_item",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_kpi_kpi_card_id",
                schema: "portfolio",
                table: "project_kpi",
                column: "kpi_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_kpi_project_id",
                schema: "portfolio",
                table: "project_kpi",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_risk_project_id",
                schema: "portfolio",
                table: "project_risk",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_stakeholder_project_id",
                schema: "portfolio",
                table: "stakeholder",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_member_project_id",
                schema: "portfolio",
                table: "team_member",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "change_request",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "contract",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "lesson",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "meeting",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "milestone",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "monthly_update",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "planning_item",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "project_kpi",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "project_risk",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "stakeholder",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "team_member",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_project_pm_employee_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropIndex(
                name: "ix_project_section_unit_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropIndex(
                name: "ix_project_status_sub_state",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "annual_licence_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "annual_maintenance_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "annual_operation_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "annual_other_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "approval_process",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "approval_result",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "approval_step_code",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "approval_step_name_ar",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "approval_step_name_en",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "community_satisfaction",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "completed_at",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "contracted_initial_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "cost_performance",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "employee_satisfaction",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "impact_notes",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "lpo_uploaded",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "main_deliverables",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "merged_into_project_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "monthly_approved_at",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "operating_costs_from",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "operational_themes",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "ownership_taken_at",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "performance",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "planned_progress",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "pm_employee_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "previous_operational_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "progress_status",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "resources_contract",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "resources_financial",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "resources_human",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "resources_other",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "resources_technical",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "structural_changes",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "sub_state",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "summary_of_achievement",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "team_formed_at",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "team_id",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "template",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "update_opened_at",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "vendor_satisfaction",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year1actual_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year1contracted_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year2actual_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year2contracted_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year3actual_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year3contracted_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year4actual_cost",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "year4contracted_cost",
                schema: "portfolio",
                table: "project");
        }
    }
}
