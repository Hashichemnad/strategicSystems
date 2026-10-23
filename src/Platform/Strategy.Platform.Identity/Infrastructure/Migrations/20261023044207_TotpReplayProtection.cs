using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TotpReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "identity",
                table: "users");
        }
    }
}
