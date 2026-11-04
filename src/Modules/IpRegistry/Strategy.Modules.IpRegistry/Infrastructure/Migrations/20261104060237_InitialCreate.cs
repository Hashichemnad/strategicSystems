using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.IpRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ip");

            migrationBuilder.CreateTable(
                name: "contributor",
                schema: "ip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    external_organisation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    emirates_id_encrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    emirates_id_last = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    share = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    signature_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    certificate_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contributor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ip_record",
                schema: "ip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    property_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    protection_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_no = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    filing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    application_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registration_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    @internal = table.Column<bool>(name: "internal", type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    moet_rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    search_text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ip_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registration_period",
                schema: "ip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    registration_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    certificate_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_period", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_event",
                schema: "ip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    months = table.Column<int>(type: "integer", nullable: true),
                    expiry = table.Column<DateOnly>(type: "date", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contributor_employee_id",
                schema: "ip",
                table: "contributor",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_contributor_ip_record_id",
                schema: "ip",
                table: "contributor",
                column: "ip_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_ip_record_document_version_id",
                schema: "ip",
                table: "ip_record",
                column: "document_version_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ip_record_expiry_date",
                schema: "ip",
                table: "ip_record",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_ip_record_number",
                schema: "ip",
                table: "ip_record",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ip_record_request_id",
                schema: "ip",
                table: "ip_record",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ip_record_status",
                schema: "ip",
                table: "ip_record",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_registration_period_ip_record_id",
                schema: "ip",
                table: "registration_period",
                column: "ip_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_event_ip_record_id_kind",
                schema: "ip",
                table: "reminder_event",
                columns: new[] { "ip_record_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contributor",
                schema: "ip");

            migrationBuilder.DropTable(
                name: "ip_record",
                schema: "ip");

            migrationBuilder.DropTable(
                name: "registration_period",
                schema: "ip");

            migrationBuilder.DropTable(
                name: "reminder_event",
                schema: "ip");
        }
    }
}
