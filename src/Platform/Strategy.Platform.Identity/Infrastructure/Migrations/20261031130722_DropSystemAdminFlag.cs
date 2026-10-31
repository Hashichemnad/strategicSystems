using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropSystemAdminFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0016: Phase 1 administrators are handed to Platform.Access (migrated first); its seeder turns these rows into
            // assignments of the platform administrator roles. One-off data migration, no runtime cross-schema dependency.
            migrationBuilder.Sql(
                "INSERT INTO access.pending_admin_grant (user_id) SELECT id FROM identity.users WHERE is_system_admin ON CONFLICT DO NOTHING;");

            migrationBuilder.DropColumn(
                name: "is_system_admin",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_system_admin",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
