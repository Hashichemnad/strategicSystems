using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Organisation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MovePhotosToFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0014 closed: employee photos move to the Files service (staged in files.pending_import, stored by the files seeder).
            migrationBuilder.Sql("""
                INSERT INTO files.pending_import (id, purpose, owner_type, owner_id, file_name, content, created_at, created_by)
                SELECT gen_random_uuid(), 'org.employee-photo', 'org.employee', employee_id::text,
                       'photo' || CASE content_type WHEN 'image/jpeg' THEN '.jpeg' WHEN 'image/webp' THEN '.webp' ELSE '.png' END,
                       content, updated_at, NULL
                FROM core.employee_photo;
                """);

            migrationBuilder.DropTable(
                name: "employee_photo",
                schema: "core");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_photo",
                schema: "core",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_photo", x => x.employee_id);
                    table.ForeignKey(
                        name: "fk_employee_photo_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }
    }
}
