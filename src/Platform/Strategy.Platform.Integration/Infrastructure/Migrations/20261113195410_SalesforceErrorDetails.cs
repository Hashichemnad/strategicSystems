using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesforceErrorDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_test_detail",
                schema: "integration",
                table: "sync_connection",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_test_detail",
                schema: "integration",
                table: "sync_connection");
        }
    }
}
