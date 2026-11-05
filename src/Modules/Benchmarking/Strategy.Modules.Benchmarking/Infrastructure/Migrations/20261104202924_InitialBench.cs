using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Benchmarking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bench");

            migrationBuilder.CreateTable(
                name: "best_practice",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    topic = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    applicability = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    sector_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    study_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comparator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    practice_row_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_best_practice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comparator",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sector_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparator", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "impact_review",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    implemented = table.Column<bool>(type: "boolean", nullable: false),
                    kpis_moved = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impact_review", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indicator_row",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    kpi_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kpi_card_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    indicator = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    our_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    their_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    period = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    update_kpi_benchmark = table.Column<bool>(type: "boolean", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_row", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outreach",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on = table.Column<DateOnly>(type: "date", nullable: false),
                    channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    response = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outreach", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_circulation",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    circulated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    circulated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recipients = table.Column<int>(type: "integer", nullable: false),
                    studies = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_circulation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_item",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    priority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    budget_required = table.Column<bool>(type: "boolean", nullable: false),
                    remark_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "practice_row",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    our_practice = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    their_practice = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    applicable = table.Column<bool>(type: "boolean", nullable: false),
                    add_to_library = table.Column<bool>(type: "boolean", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_practice_row", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "study",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    responsible_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_year = table.Column<int>(type: "integer", nullable: false),
                    target_quarter = table.Column<int>(type: "integer", nullable: false),
                    comparison_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    purpose_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    stage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pending_process = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    returned = table.Column<bool>(type: "boolean", nullable: false),
                    return_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    search_submitted = table.Column<bool>(type: "boolean", nullable: false),
                    search_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cap_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    analysis_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    impact_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    impact_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "study_entity",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    comparator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    available_info = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    contact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    code_of_conduct_ack = table.Column<bool>(type: "boolean", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    rating_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_entity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_topic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visit",
                schema: "bench",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    comparator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    participant_ids = table.Column<string>(type: "jsonb", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questionnaire = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    questionnaire_sent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_best_practice_practice_row_id",
                schema: "bench",
                table: "best_practice",
                column: "practice_row_id",
                unique: true,
                filter: "practice_row_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_best_practice_study_id",
                schema: "bench",
                table: "best_practice",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_impact_review_study_id",
                schema: "bench",
                table: "impact_review",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_row_study_id",
                schema: "bench",
                table: "indicator_row",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_outreach_study_entity_id",
                schema: "bench",
                table: "outreach",
                column: "study_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_circulation_year",
                schema: "bench",
                table: "plan_circulation",
                column: "year");

            migrationBuilder.CreateIndex(
                name: "ix_plan_item_study_id",
                schema: "bench",
                table: "plan_item",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_practice_row_study_id",
                schema: "bench",
                table: "practice_row",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_number",
                schema: "bench",
                table: "study",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_request_id",
                schema: "bench",
                table: "study",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_responsible_unit_id",
                schema: "bench",
                table: "study",
                column: "responsible_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_target_year_target_quarter",
                schema: "bench",
                table: "study",
                columns: new[] { "target_year", "target_quarter" });

            migrationBuilder.CreateIndex(
                name: "ix_study_entity_comparator_id",
                schema: "bench",
                table: "study_entity",
                column: "comparator_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_entity_study_id_comparator_id",
                schema: "bench",
                table: "study_entity",
                columns: new[] { "study_id", "comparator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_study_id",
                schema: "bench",
                table: "topic",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_study_id",
                schema: "bench",
                table: "visit",
                column: "study_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "best_practice",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "comparator",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "impact_review",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "indicator_row",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "outreach",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "plan_circulation",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "plan_item",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "practice_row",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "study",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "study_entity",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "topic",
                schema: "bench");

            migrationBuilder.DropTable(
                name: "visit",
                schema: "bench");
        }
    }
}
