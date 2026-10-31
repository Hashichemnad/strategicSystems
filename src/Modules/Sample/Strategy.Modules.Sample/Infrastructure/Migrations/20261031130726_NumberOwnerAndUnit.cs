using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Sample.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NumberOwnerAndUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "number",
                schema: "sample",
                table: "sample_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_employee_id",
                schema: "sample",
                table: "sample_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "unit_id",
                schema: "sample",
                table: "sample_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_number",
                schema: "sample",
                table: "sample_items",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_owner_employee_id",
                schema: "sample",
                table: "sample_items",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_unit_id",
                schema: "sample",
                table: "sample_items",
                column: "unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sample_items_number",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropIndex(
                name: "ix_sample_items_owner_employee_id",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropIndex(
                name: "ix_sample_items_unit_id",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropColumn(
                name: "owner_employee_id",
                schema: "sample",
                table: "sample_items");

            migrationBuilder.DropColumn(
                name: "unit_id",
                schema: "sample",
                table: "sample_items");
        }
    }
}
