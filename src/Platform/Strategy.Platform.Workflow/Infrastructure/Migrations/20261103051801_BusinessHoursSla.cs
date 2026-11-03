using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Workflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessHoursSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "business_hours_id",
                schema: "workflow",
                table: "task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_hours_name_ar",
                schema: "workflow",
                table: "task",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_hours_name_en",
                schema: "workflow",
                table: "task",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sla_mode",
                schema: "workflow",
                table: "task",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_hours_id",
                schema: "workflow",
                table: "process_step",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_hours_source",
                schema: "workflow",
                table: "process_step",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "sla_mode",
                schema: "workflow",
                table: "process_step",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "working");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "business_hours_id",
                schema: "workflow",
                table: "task");

            migrationBuilder.DropColumn(
                name: "business_hours_name_ar",
                schema: "workflow",
                table: "task");

            migrationBuilder.DropColumn(
                name: "business_hours_name_en",
                schema: "workflow",
                table: "task");

            migrationBuilder.DropColumn(
                name: "sla_mode",
                schema: "workflow",
                table: "task");

            migrationBuilder.DropColumn(
                name: "business_hours_id",
                schema: "workflow",
                table: "process_step");

            migrationBuilder.DropColumn(
                name: "business_hours_source",
                schema: "workflow",
                table: "process_step");

            migrationBuilder.DropColumn(
                name: "sla_mode",
                schema: "workflow",
                table: "process_step");
        }
    }
}
