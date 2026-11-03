using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Sample.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                schema: "sample",
                table: "sample_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_status",
                schema: "sample",
                table: "sample_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_approval_status",
                schema: "sample",
                table: "sample_items",
                column: "approval_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sample_items_approval_status",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropColumn(
                name: "amount",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropColumn(
                name: "approval_status",
                schema: "sample",
                table: "sample_items");
        }
    }
}
