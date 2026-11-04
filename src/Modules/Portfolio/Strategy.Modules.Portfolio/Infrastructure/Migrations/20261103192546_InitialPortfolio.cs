using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "portfolio");

            migrationBuilder.CreateTable(
                name: "project",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    scope_of_work = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    section_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategic_objective_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    program_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year1planned_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year2planned_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year3planned_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year4planned_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposal_assessment",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    documents = table.Column<string>(type: "jsonb", nullable: false),
                    usage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal_assessment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    approval_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    approval_step_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approval_step_name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    approval_step_name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_action_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    justifications = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    strategic_objective_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    suggestion_source_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    suggestion_source_other = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    program_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requires_budget = table.Column<bool>(type: "boolean", nullable: true),
                    estimated_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year1cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year2cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year3cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    year4cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    expected_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expected_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    section_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    program_nature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    previous_program_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    scope_of_work = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    screen_core_work = table.Column<bool>(type: "boolean", nullable: true),
                    screen_one_time = table.Column<bool>(type: "boolean", nullable: true),
                    screen_time_bound = table.Column<bool>(type: "boolean", nullable: true),
                    screen_unique_output = table.Column<bool>(type: "boolean", nullable: true),
                    screen_min_cost = table.Column<bool>(type: "boolean", nullable: true),
                    screen_operational_costs = table.Column<bool>(type: "boolean", nullable: true),
                    classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    classification_override = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    override_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    override_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    override_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    priority_scores = table.Column<string>(type: "jsonb", nullable: false),
                    evaluation_score = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    priority_band = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    main_option_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    main_option_other = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    main_advantages = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    main_disadvantages = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    alt_option_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    alt_option_other = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    alt_advantages = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    alt_disadvantages = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    others_bear_costs = table.Column<bool>(type: "boolean", nullable: true),
                    others_cover_implementation = table.Column<bool>(type: "boolean", nullable: true),
                    shared_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    partner_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    partner_share = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    annual_operation_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    annual_licence_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    annual_maintenance_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    annual_other_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    operating_costs_from = table.Column<DateOnly>(type: "date", nullable: true),
                    return_expected = table.Column<bool>(type: "boolean", nullable: true),
                    return_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    return_year = table.Column<int>(type: "integer", nullable: true),
                    reduction_expected = table.Column<bool>(type: "boolean", nullable: true),
                    reduction_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    reduction_year = table.Column<int>(type: "integer", nullable: true),
                    practice_operation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    practice_service = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    practice_project = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    practice_initiative = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cost_path = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cost_proposals_requested = table.Column<bool>(type: "boolean", nullable: true),
                    procurement_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cost_estimation_uploaded = table.Column<bool>(type: "boolean", nullable: false),
                    project_scope_uploaded = table.Column<bool>(type: "boolean", nullable: false),
                    quotations_handed_off_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quotations_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_number",
                schema: "portfolio",
                table: "project",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_request_id",
                schema: "portfolio",
                table: "project",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_unit_id",
                schema: "portfolio",
                table: "project",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_assessment_request_id_version_no",
                schema: "portfolio",
                table: "proposal_assessment",
                columns: new[] { "request_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_request_number",
                schema: "portfolio",
                table: "request",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_request_requester_employee_id",
                schema: "portfolio",
                table: "request",
                column: "requester_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_requester_user_id",
                schema: "portfolio",
                table: "request",
                column: "requester_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_status",
                schema: "portfolio",
                table: "request",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_request_unit_id",
                schema: "portfolio",
                table: "request",
                column: "unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "proposal_assessment",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "request",
                schema: "portfolio");
        }
    }
}
