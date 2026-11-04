using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Risk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "action_update",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    completion_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: true),
                    draft = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_update", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assessment",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    inherent_likelihood = table.Column<int>(type: "integer", nullable: false),
                    inherent_impact = table.Column<int>(type: "integer", nullable: false),
                    inherent_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    inherent_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    residual_likelihood = table.Column<int>(type: "integer", nullable: true),
                    residual_impact = table.Column<int>(type: "integer", nullable: true),
                    residual_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    residual_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    residual_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    target_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    control_effectiveness = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    framework_version_no = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appetite = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "committee_decision",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_committee_decision", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "control",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nature_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    frequency_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    effectiveness_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_assessed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    source_action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_control", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_risk",
                schema: "risk",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_risk", x => new { x.event_id, x.risk_id });
                });

            migrationBuilder.CreateTable(
                name: "framework_version",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recalculation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_framework_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kri",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kpi_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    card_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    card_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    warning_threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    breach_threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    last_level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_period = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kri", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    residual_likelihood = table.Column<int>(type: "integer", nullable: false),
                    residual_impact = table.Column<int>(type: "integer", nullable: false),
                    control_ratings = table.Column<string>(type: "jsonb", nullable: false),
                    action_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    commentary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    register_year = table.Column<int>(type: "integer", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    cause = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    event_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    consequence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    determined_by_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    identified_on = table.Column<DateOnly>(type: "date", nullable: false),
                    exposure_start = table.Column<DateOnly>(type: "date", nullable: true),
                    risk_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_codes = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    emerging = table.Column<bool>(type: "boolean", nullable: false),
                    opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    coordinator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    follow_up_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    objective_codes = table.Column<string>(type: "jsonb", nullable: false),
                    processes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    inherent_likelihood = table.Column<int>(type: "integer", nullable: false),
                    inherent_impact = table.Column<int>(type: "integer", nullable: false),
                    inherent_impact_area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    inherent_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    inherent_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    control_effectiveness_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    control_effectiveness_override = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    residual_likelihood = table.Column<int>(type: "integer", nullable: true),
                    residual_impact = table.Column<int>(type: "integer", nullable: true),
                    residual_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    residual_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    residual_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    residual_status_override = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    residual_status_justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    target_likelihood = table.Column<int>(type: "integer", nullable: true),
                    target_impact = table.Column<int>(type: "integer", nullable: true),
                    target_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    target_band = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    framework_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    within_appetite = table.Column<bool>(type: "boolean", nullable: true),
                    needs_follow_up = table.Column<bool>(type: "boolean", nullable: false),
                    top10 = table.Column<bool>(type: "boolean", nullable: false),
                    top10rank = table.Column<int>(type: "integer", nullable: true),
                    escalated = table.Column<bool>(type: "boolean", nullable: false),
                    escalation_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_review_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_review_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_review_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reassessment_required = table.Column<bool>(type: "boolean", nullable: false),
                    reassessment_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pending_process = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    returned = table.Column<bool>(type: "boolean", nullable: false),
                    return_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    closure_reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    closure_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk_event",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    financial_impact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    other_impact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    near_miss = table.Column<bool>(type: "boolean", nullable: false),
                    root_cause = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    lessons = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cap_remark_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "treatment_action",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    title_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_unit_ids = table.Column<string>(type: "jsonb", nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    completion_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    converted_control_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treatment_action", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_update_action_id",
                schema: "risk",
                table: "action_update",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_risk_id_at",
                schema: "risk",
                table: "assessment",
                columns: new[] { "risk_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_category_code",
                schema: "risk",
                table: "category",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_committee_decision_risk_id",
                schema: "risk",
                table: "committee_decision",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_control_owner_employee_id",
                schema: "risk",
                table: "control",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_control_risk_id",
                schema: "risk",
                table: "control",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_risk_risk_id",
                schema: "risk",
                table: "event_risk",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_framework_version_version_no",
                schema: "risk",
                table: "framework_version",
                column: "version_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kri_kpi_card_id",
                schema: "risk",
                table: "kri",
                column: "kpi_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_kri_risk_id",
                schema: "risk",
                table: "kri",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_risk_id",
                schema: "risk",
                table: "review",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_coordinator_employee_id",
                schema: "risk",
                table: "risk",
                column: "coordinator_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_next_review_on",
                schema: "risk",
                table: "risk",
                column: "next_review_on");

            migrationBuilder.CreateIndex(
                name: "ix_risk_number",
                schema: "risk",
                table: "risk",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_risk_owner_employee_id",
                schema: "risk",
                table: "risk",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_status",
                schema: "risk",
                table: "risk",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_risk_unit_id",
                schema: "risk",
                table: "risk",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_event_number",
                schema: "risk",
                table: "risk_event",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatment_action_number",
                schema: "risk",
                table: "treatment_action",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatment_action_owner_employee_id",
                schema: "risk",
                table: "treatment_action",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_action_risk_id",
                schema: "risk",
                table: "treatment_action",
                column: "risk_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_update",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "assessment",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "category",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "committee_decision",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "control",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "event_risk",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "framework_version",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "kri",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "review",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "risk",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "risk_event",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "treatment_action",
                schema: "risk");
        }
    }
}
