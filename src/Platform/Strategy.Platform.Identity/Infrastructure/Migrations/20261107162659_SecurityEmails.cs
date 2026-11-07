using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notify_new_device",
                schema: "identity",
                table: "security_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "security_contact_ar",
                schema: "identity",
                table: "security_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "security_contact_en",
                schema: "identity",
                table: "security_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "known_device",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_known_device", x => x.id);
                    table.ForeignKey(
                        name: "fk_known_device_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "security_settings",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "notify_new_device", "security_contact_ar", "security_contact_en" },
                values: new object[] { false, null, null });

            migrationBuilder.CreateIndex(
                name: "ix_known_device_user_id_fingerprint",
                schema: "identity",
                table: "known_device",
                columns: new[] { "user_id", "fingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "known_device",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "notify_new_device",
                schema: "identity",
                table: "security_settings");

            migrationBuilder.DropColumn(
                name: "security_contact_ar",
                schema: "identity",
                table: "security_settings");

            migrationBuilder.DropColumn(
                name: "security_contact_en",
                schema: "identity",
                table: "security_settings");
        }
    }
}
