using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Settings.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveLogosToFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0017 closed: logos move to the Files service. The bytes are staged in files.pending_import (the files schema is
            // migrated first) and written to the file store by the files seeder right after the migrations.
            migrationBuilder.Sql("""
                INSERT INTO files.pending_import (id, purpose, owner_type, owner_id, file_name, content, created_at, created_by)
                SELECT gen_random_uuid(), 'settings.branding-logo', 'settings.branding', kind,
                       kind || CASE content_type WHEN 'image/jpeg' THEN '.jpeg' WHEN 'image/webp' THEN '.webp' ELSE '.png' END,
                       content, updated_at, NULL
                FROM settings.branding_asset;
                """);

            migrationBuilder.DropTable(
                name: "branding_asset",
                schema: "settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branding_asset",
                schema: "settings",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branding_asset", x => x.kind);
                });
        }
    }
}
