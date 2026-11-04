using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.DocumentRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "docreg");

            migrationBuilder.CreateTable(
                name: "acknowledgement",
                schema: "docreg",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acknowledgement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document",
                schema: "docreg",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    doc_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sensitivity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_version_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    share_to_all = table.Column<bool>(type: "boolean", nullable: false),
                    ack_required = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    search_text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    file_search_text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_version",
                schema: "docreg",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    major = table.Column<int>(type: "integer", nullable: false),
                    minor = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_no = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    change_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    final_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_new_document = table.Column<bool>(type: "boolean", nullable: false),
                    draft_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text_extract = table.Column<string>(type: "text", nullable: true),
                    summary_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    summary_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "validity_event",
                schema: "docreg",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    days = table.Column<int>(type: "integer", nullable: true),
                    old_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    new_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_validity_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acknowledgement_version_id_employee_id",
                schema: "docreg",
                table: "acknowledgement",
                columns: new[] { "version_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_document_no",
                schema: "docreg",
                table: "document",
                column: "document_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_expiry_date",
                schema: "docreg",
                table: "document",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_document_owner_unit_id",
                schema: "docreg",
                table: "document",
                column: "owner_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_status_sensitivity",
                schema: "docreg",
                table: "document",
                columns: new[] { "status", "sensitivity" });

            migrationBuilder.CreateIndex(
                name: "ix_document_version_document_id",
                schema: "docreg",
                table: "document_version",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_version_request_id",
                schema: "docreg",
                table: "document_version",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_validity_event_document_id_kind",
                schema: "docreg",
                table: "validity_event",
                columns: new[] { "document_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acknowledgement",
                schema: "docreg");

            migrationBuilder.DropTable(
                name: "document",
                schema: "docreg");

            migrationBuilder.DropTable(
                name: "document_version",
                schema: "docreg");

            migrationBuilder.DropTable(
                name: "validity_event",
                schema: "docreg");
        }
    }
}
