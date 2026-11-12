using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Reporting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportingTwo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "options_json",
                schema: "reporting",
                table: "schedule",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "dataset",
                schema: "reporting",
                table: "report",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                schema: "reporting",
                table: "report",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dossier_template",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dataset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    label_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seed_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dossier_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_folder",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_folder", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_type",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    label_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_ar = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seed_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_folder_id",
                schema: "reporting",
                table: "report",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_dossier_template_code",
                schema: "reporting",
                table: "dossier_template",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_folder_owner_user_id",
                schema: "reporting",
                table: "report_folder",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_type_code",
                schema: "reporting",
                table: "report_type",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dossier_template",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_folder",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_type",
                schema: "reporting");

            migrationBuilder.DropIndex(
                name: "ix_report_folder_id",
                schema: "reporting",
                table: "report");

            migrationBuilder.DropColumn(
                name: "options_json",
                schema: "reporting",
                table: "schedule");

            migrationBuilder.DropColumn(
                name: "folder_id",
                schema: "reporting",
                table: "report");

            migrationBuilder.AlterColumn<string>(
                name: "dataset",
                schema: "reporting",
                table: "report",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);
        }
    }
}
