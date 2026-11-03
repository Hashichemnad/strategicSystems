using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Kpi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialKpi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kpi");

            migrationBuilder.CreateTable(
                name: "annual_plan",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    measuring_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_accumulated = table.Column<bool>(type: "boolean", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    achieved_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    actual_performance = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    capped_performance = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    rag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_count = table.Column<int>(type: "integer", nullable: false),
                    approved_count = table.Column<int>(type: "integer", nullable: false),
                    na_count = table.Column<int>(type: "integer", nullable: false),
                    last_achieved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_annual_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "card_target",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_target", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "card_version",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "definition_change",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    proposed = table.Column<string>(type: "jsonb", nullable: false),
                    current = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definition_change", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kpi_card",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    measuring_equation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    process_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initiated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    index_classification_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    effectiveness_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nature = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_codes = table.Column<string>(type: "jsonb", nullable: false),
                    measuring_method_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    competitiveness_indicator = table.Column<bool>(type: "boolean", nullable: false),
                    is_global_star = table.Column<bool>(type: "boolean", nullable: false),
                    unit_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    measuring_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_setting_mechanism = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    allows_negative = table.Column<bool>(type: "boolean", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sector_goal = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    national_goal_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vision_link_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gem_criteria_codes = table.Column<string>(type: "jsonb", nullable: false),
                    criteria_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    excellence_link = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    department_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    benchmark_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    benchmark_result = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    baseline_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    baseline_year = table.Column<int>(type: "integer", nullable: true),
                    green_from = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    amber_from = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    formula = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    data_elements = table.Column<string>(type: "jsonb", nullable: false),
                    scale = table.Column<string>(type: "jsonb", nullable: false),
                    max_achieved = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kpi_card", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_window",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    period_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    opens_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_window", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "result",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    period_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    range_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    achieve = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    element_values = table.Column<string>(type: "jsonb", nullable: false),
                    scale_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    actual_performance = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    capped_performance = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    achieved = table.Column<bool>(type: "boolean", nullable: true),
                    causality = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    corrective_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    not_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    na_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    opens_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    late = table.Column<bool>(type: "boolean", nullable: false),
                    days_late = table.Column<int>(type: "integer", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    return_count = table.Column<int>(type: "integer", nullable: false),
                    reopened_count = table.Column<int>(type: "integer", nullable: false),
                    status_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    checklist = table.Column<string>(type: "jsonb", nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    draft_from_mcp = table.Column<bool>(type: "boolean", nullable: false),
                    opened_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_result", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_element",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    cycle_start_year = table.Column<int>(type: "integer", nullable: true),
                    cycle_end_year = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_element", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_annual_plan_card_id_year",
                schema: "kpi",
                table: "annual_plan",
                columns: new[] { "card_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_annual_plan_year",
                schema: "kpi",
                table: "annual_plan",
                column: "year");

            migrationBuilder.CreateIndex(
                name: "ix_card_target_card_id_year",
                schema: "kpi",
                table: "card_target",
                columns: new[] { "card_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_version_card_id_version_no",
                schema: "kpi",
                table: "card_version",
                columns: new[] { "card_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_definition_change_card_id",
                schema: "kpi",
                table: "definition_change",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_code",
                schema: "kpi",
                table: "kpi_card",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_objective_id",
                schema: "kpi",
                table: "kpi_card",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_parent_card_id",
                schema: "kpi",
                table: "kpi_card",
                column: "parent_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_responsible_employee_id",
                schema: "kpi",
                table: "kpi_card",
                column: "responsible_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_responsible_unit_id",
                schema: "kpi",
                table: "kpi_card",
                column: "responsible_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_window_year_period_code",
                schema: "kpi",
                table: "period_window",
                columns: new[] { "year", "period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_result_card_id",
                schema: "kpi",
                table: "result",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_result_owner_employee_id",
                schema: "kpi",
                table: "result",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_result_owner_unit_id",
                schema: "kpi",
                table: "result",
                column: "owner_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_result_plan_id_period_code",
                schema: "kpi",
                table: "result",
                columns: new[] { "plan_id", "period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_result_year_status",
                schema: "kpi",
                table: "result",
                columns: new[] { "year", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_strategy_element_code",
                schema: "kpi",
                table: "strategy_element",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strategy_element_parent_id",
                schema: "kpi",
                table: "strategy_element",
                column: "parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annual_plan",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "card_target",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "card_version",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "definition_change",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "kpi_card",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "period_window",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "result",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "strategy_element",
                schema: "kpi");
        }
    }
}
