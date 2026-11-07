using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MemberRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_member_roles",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    administrator_roles = table.Column<string>(type: "jsonb", nullable: false),
                    user_roles = table.Column<string>(type: "jsonb", nullable: false),
                    extra_roles = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_member_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_member_roles_system_code",
                schema: "access",
                table: "system_member_roles",
                column: "system_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_member_roles",
                schema: "access");
        }
    }
}
