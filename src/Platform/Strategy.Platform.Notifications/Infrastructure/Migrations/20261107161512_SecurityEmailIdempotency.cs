using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityEmailIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "notify",
                table: "delivery",
                type: "character varying(530)",
                maxLength: 530,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sent_request",
                schema: "notify",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    template_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sent_request", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_idempotency_key",
                schema: "notify",
                table: "delivery",
                column: "idempotency_key",
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ix_sent_request_created_at",
                schema: "notify",
                table: "sent_request",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sent_request",
                schema: "notify");

            migrationBuilder.DropIndex(
                name: "ix_delivery_idempotency_key",
                schema: "notify",
                table: "delivery");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "notify",
                table: "delivery");
        }
    }
}
