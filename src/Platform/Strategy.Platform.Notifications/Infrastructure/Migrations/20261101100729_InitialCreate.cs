using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.CreateTable(
                name: "delivery",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    template_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    digested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    awaiting_digest = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preference",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<bool>(type: "boolean", nullable: false),
                    in_app = table.Column<bool>(type: "boolean", nullable: false),
                    digest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preference", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subject_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body_ar = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    body_en = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    button_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    button_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    placeholders = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    customised = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_at",
                schema: "notify",
                table: "delivery",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_notification_user_id_created_at",
                schema: "notify",
                table: "notification",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_user_id_read_at",
                schema: "notify",
                table: "notification",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_preference_user_id_event_key",
                schema: "notify",
                table: "preference",
                columns: new[] { "user_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_key",
                schema: "notify",
                table: "template",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "preference",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "template",
                schema: "notify");
        }
    }
}
