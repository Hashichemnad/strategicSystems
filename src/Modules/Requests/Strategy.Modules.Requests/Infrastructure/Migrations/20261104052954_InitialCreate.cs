using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Requests.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "requests");

            migrationBuilder.CreateTable(
                name: "request",
                schema: "requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    system_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    title_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stage = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    process_data = table.Column<string>(type: "jsonb", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stage_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stage_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title_search = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "request_event",
                schema: "requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    from_stage = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    to_stage = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    on_behalf_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request_type",
                schema: "requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    system_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    number_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owning_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitters = table.Column<string>(type: "jsonb", nullable: false),
                    form = table.Column<string>(type: "jsonb", nullable: false),
                    stages = table.Column<string>(type: "jsonb", nullable: false),
                    pdf_templates = table.Column<string>(type: "jsonb", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    show_in_catalogue = table.Column<bool>(type: "boolean", nullable: false),
                    embeddable = table.Column<bool>(type: "boolean", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_request_number",
                schema: "requests",
                table: "request",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_request_requester_employee_id",
                schema: "requests",
                table: "request",
                column: "requester_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_requester_user_id",
                schema: "requests",
                table: "request",
                column: "requester_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_subject_type_subject_id",
                schema: "requests",
                table: "request",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_request_type_code_status_stage",
                schema: "requests",
                table: "request",
                columns: new[] { "type_code", "status", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_request_event_request_id_at",
                schema: "requests",
                table: "request_event",
                columns: new[] { "request_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_request_type_code",
                schema: "requests",
                table: "request_type",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request",
                schema: "requests");

            migrationBuilder.DropTable(
                name: "request_event",
                schema: "requests");

            migrationBuilder.DropTable(
                name: "request_type",
                schema: "requests");
        }
    }
}
