using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveAuthEventsToAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0018: the Phase 1 sign-in events move into the audit trail. They are staged in audit.legacy_import (the audit schema
            // is migrated first) and chained, oldest first, by the audit seeder that runs right after the migrations.
            migrationBuilder.Sql("""
                INSERT INTO audit.legacy_import (id, occurred_at, action, system_code, entity_type, record_id, user_id, user_name, channel,
                    correlation_id, ip_address, user_agent, success, data)
                SELECT id, at, 'auth.' || type, 'identity', 'identity.user', user_id::text, coalesce(actor_user_id, user_id), user_name, 'import',
                    correlation_id, ip_address, user_agent, success,
                    json_strip_nulls(json_build_object('userName', user_name, 'provider', provider, 'detail', detail))::text
                FROM identity.auth_event;
                """);

            migrationBuilder.DropTable(
                name: "auth_event",
                schema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_event",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_event_at",
                schema: "identity",
                table: "auth_event",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_auth_event_user_id_at",
                schema: "identity",
                table: "auth_event",
                columns: new[] { "user_id", "at" });
        }
    }
}
