using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.CreateTable(
                name: "external_ref",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_origin = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sync_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_ref", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_system",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_system", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_ref_entity_type_record_id",
                schema: "integration",
                table: "external_ref",
                columns: new[] { "entity_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_ref_external_id",
                schema: "integration",
                table: "external_ref",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ux_external_ref_external",
                schema: "integration",
                table: "external_ref",
                columns: new[] { "source_system", "entity_type", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_external_ref_record",
                schema: "integration",
                table: "external_ref",
                columns: new[] { "source_system", "entity_type", "record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_system_code",
                schema: "integration",
                table: "source_system",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_ref",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "source_system",
                schema: "integration");
        }
    }
}
