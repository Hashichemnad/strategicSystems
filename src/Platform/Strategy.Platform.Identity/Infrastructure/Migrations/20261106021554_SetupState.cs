using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "setup_state",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    token_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bootstrapped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    bootstrapped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setup_state", x => x.id);
                });

            // ADR 0150: installations that already have accounts (created before Phase 18) are set up; the wizard never opens there.
            migrationBuilder.Sql(
                "INSERT INTO identity.setup_state (id, completed_at) " +
                "SELECT '00000000-0000-0000-0000-000000000150', now() WHERE EXISTS (SELECT 1 FROM identity.users);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "setup_state",
                schema: "identity");
        }
    }
}
