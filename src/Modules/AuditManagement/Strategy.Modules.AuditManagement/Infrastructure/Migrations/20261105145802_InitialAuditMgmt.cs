using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.AuditManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditMgmt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit_mgmt");

            migrationBuilder.CreateTable(
                name: "auditable_entity",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inherent_rating = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_audited_on = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditable_entity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "engagement",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    objectives = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    scope = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_auditor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_quarter = table.Column<int>(type: "integer", nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    budget_hours = table.Column<int>(type: "integer", nullable: true),
                    fieldwork_end_on = table.Column<DateOnly>(type: "date", nullable: true),
                    stage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    risk_score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    satisfaction = table.Column<int>(type: "integer", nullable: true),
                    satisfaction_comment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engagement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "finding",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    condition = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    criteria = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    criteria_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cause = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    effect = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    finding_date = table.Column<DateOnly>(type: "date", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    responsible_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    repeat = table.Column<bool>(type: "boolean", nullable: false),
                    repeat_of_finding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_closing_on = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    completion = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    auditor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cap_remark_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cap_remark_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "finding_risk",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    control_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flagged_for_reassessment = table.Column<bool>(type: "boolean", nullable: false),
                    control_lowered_to = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding_risk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "follow_up",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    follow_up_on = table.Column<DateOnly>(type: "date", nullable: false),
                    achieved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    actions_implemented = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    evidence_text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    next_follow_up_on = table.Column<DateOnly>(type: "date", nullable: true),
                    comment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    recorded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    review_note = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    applied = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    action_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    management_response_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    management_comment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    implementation_plan = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    contact_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    revised_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    revised_reason = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latest_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_follow_up_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_follow_up_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_comment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    risk_acceptance_justification = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    risk_acceptance_reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    risk_accepted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    follow_up_reminded_for = table.Column<DateOnly>(type: "date", nullable: true),
                    due_reminded_for = table.Column<DateOnly>(type: "date", nullable: true),
                    late_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_log",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    previous_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    new_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    justification = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    recorded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auditor_body = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    lead_auditor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audited_unit_ids = table.Column<string>(type: "jsonb", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: true),
                    period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    report_date = table.Column<DateOnly>(type: "date", nullable: true),
                    distribution_employee_ids = table.Column<string>(type: "jsonb", nullable: false),
                    distribution_note = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    summary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cap_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                name: "work_paper",
                schema: "audit_mgmt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    test_step = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    sample = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    result = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    conclusion = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prepared_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_paper", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditable_entity_risk_id",
                schema: "audit_mgmt",
                table: "auditable_entity",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "ix_auditable_entity_unit_id",
                schema: "audit_mgmt",
                table: "auditable_entity",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagement_number",
                schema: "audit_mgmt",
                table: "engagement",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_engagement_plan_id",
                schema: "audit_mgmt",
                table: "engagement",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_department_unit_id",
                schema: "audit_mgmt",
                table: "finding",
                column: "department_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_number",
                schema: "audit_mgmt",
                table: "finding",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_finding_report_id",
                schema: "audit_mgmt",
                table: "finding",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_responsible_unit_id",
                schema: "audit_mgmt",
                table: "finding",
                column: "responsible_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_status",
                schema: "audit_mgmt",
                table: "finding",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_finding_year",
                schema: "audit_mgmt",
                table: "finding",
                column: "year");

            migrationBuilder.CreateIndex(
                name: "ix_finding_risk_finding_id",
                schema: "audit_mgmt",
                table: "finding_risk",
                column: "finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_risk_finding_id_risk_id_control_id",
                schema: "audit_mgmt",
                table: "finding_risk",
                columns: new[] { "finding_id", "risk_id", "control_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_number",
                schema: "audit_mgmt",
                table: "follow_up",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_recommendation_id",
                schema: "audit_mgmt",
                table: "follow_up",
                column: "recommendation_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_year",
                schema: "audit_mgmt",
                table: "plan",
                column: "year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_finding_id",
                schema: "audit_mgmt",
                table: "recommendation",
                column: "finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_next_follow_up_on",
                schema: "audit_mgmt",
                table: "recommendation",
                column: "next_follow_up_on");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_number",
                schema: "audit_mgmt",
                table: "recommendation",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_status",
                schema: "audit_mgmt",
                table: "recommendation",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_log_recommendation_id",
                schema: "audit_mgmt",
                table: "recommendation_log",
                column: "recommendation_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_engagement_id",
                schema: "audit_mgmt",
                table: "report",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_number",
                schema: "audit_mgmt",
                table: "report",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_status",
                schema: "audit_mgmt",
                table: "report",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_work_paper_engagement_id",
                schema: "audit_mgmt",
                table: "work_paper",
                column: "engagement_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditable_entity",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "engagement",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "finding",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "finding_risk",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "follow_up",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "plan",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "recommendation",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "recommendation_log",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "report",
                schema: "audit_mgmt");

            migrationBuilder.DropTable(
                name: "work_paper",
                schema: "audit_mgmt");
        }
    }
}
