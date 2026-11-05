using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Cap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cap");

            migrationBuilder.CreateTable(
                name: "achievement_update",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_achievement_update", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "action",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    home_remark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    details_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    budget_required = table.Column<bool>(type: "boolean", nullable: false),
                    budget_available = table.Column<bool>(type: "boolean", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    related_process = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    assigned_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    revised_end = table.Column<DateOnly>(type: "date", nullable: true),
                    validation_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    validation_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    program_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    program_request_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    achieved_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pending_process = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    completion_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    monthly_reminded_for = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    late_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pmo_escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "effectiveness_check",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    remark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    effective = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_effectiveness_check", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "extension_request",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_end = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_end = table.Column<DateOnly>(type: "date", nullable: false),
                    justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extension_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remark",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    text_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    criterion_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    department_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    pmo_decision = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pmo_decision_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    root_cause_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    root_cause = table.Column<string>(type: "jsonb", nullable: false),
                    root_cause_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    linked_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    linked_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    action_seq = table.Column<int>(type: "integer", nullable: false),
                    all_rejected = table.Column<bool>(type: "boolean", nullable: false),
                    reopened_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verification_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remark", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remark_action",
                schema: "cap",
                columns: table => new
                {
                    remark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remark_action", x => new { x.remark_id, x.action_id });
                });

            migrationBuilder.CreateTable(
                name: "source",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    source_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    linked_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    linked_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    viewer_employee_ids = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suggestion",
                schema: "cap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    remark_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dismiss_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_achievement_update_action_id",
                schema: "cap",
                table: "achievement_update",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_assigned_employee_id",
                schema: "cap",
                table: "action",
                column: "assigned_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_home_remark_id",
                schema: "cap",
                table: "action",
                column: "home_remark_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_number",
                schema: "cap",
                table: "action",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_action_status",
                schema: "cap",
                table: "action",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_effectiveness_check_remark_id",
                schema: "cap",
                table: "effectiveness_check",
                column: "remark_id");

            migrationBuilder.CreateIndex(
                name: "ix_extension_request_action_id",
                schema: "cap",
                table: "extension_request",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_remark_department_unit_id",
                schema: "cap",
                table: "remark",
                column: "department_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_remark_number",
                schema: "cap",
                table: "remark",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remark_owner_employee_id",
                schema: "cap",
                table: "remark",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_remark_source_id",
                schema: "cap",
                table: "remark",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_remark_source_id_external_key",
                schema: "cap",
                table: "remark",
                columns: new[] { "source_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_remark_status",
                schema: "cap",
                table: "remark",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_remark_action_action_id",
                schema: "cap",
                table: "remark_action",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_source_linked_type_linked_id",
                schema: "cap",
                table: "source",
                columns: new[] { "linked_type", "linked_id" },
                unique: true,
                filter: "linked_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_source_number",
                schema: "cap",
                table: "source",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_kind_resource_id",
                schema: "cap",
                table: "suggestion",
                columns: new[] { "kind", "resource_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_status",
                schema: "cap",
                table: "suggestion",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement_update",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "action",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "effectiveness_check",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "extension_request",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "remark",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "remark_action",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "source",
                schema: "cap");

            migrationBuilder.DropTable(
                name: "suggestion",
                schema: "cap");
        }
    }
}
