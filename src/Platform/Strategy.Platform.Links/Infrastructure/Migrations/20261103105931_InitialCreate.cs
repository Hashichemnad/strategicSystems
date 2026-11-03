using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Links.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "links");

            migrationBuilder.CreateTable(
                name: "record_link",
                schema: "links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_link", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_record_link_from_type_from_id",
                schema: "links",
                table: "record_link",
                columns: new[] { "from_type", "from_id" });

            migrationBuilder.CreateIndex(
                name: "ix_record_link_kind_from_type_from_id_to_type_to_id",
                schema: "links",
                table: "record_link",
                columns: new[] { "kind", "from_type", "from_id", "to_type", "to_id" },
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_record_link_to_type_to_id",
                schema: "links",
                table: "record_link",
                columns: new[] { "to_type", "to_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "record_link",
                schema: "links");
        }
    }
}
