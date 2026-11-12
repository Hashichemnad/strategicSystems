using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationClientsFeedsWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    service_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acting_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auth_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    allowed_systems = table.Column<List<string>>(type: "text[]", nullable: false),
                    allowed_record_types = table.Column<List<string>>(type: "text[]", nullable: false),
                    ip_allow_list = table.Column<List<string>>(type: "text[]", nullable: false),
                    rate_limit_per_minute = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_credential",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_credential", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_usage",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    rows = table.Column<int>(type: "integer", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    refused_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_schedule",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    record_types = table.Column<List<string>>(type: "text[]", nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    folder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    every_hours = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    watermarks = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_attempt",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_attempt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_delivery",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_delivery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscription",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    events = table.Column<List<string>>(type: "text[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    protected_secret = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_subscription", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_client_id",
                schema: "integration",
                table: "client",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_service_user_id",
                schema: "integration",
                table: "client",
                column: "service_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_credential_client_id",
                schema: "integration",
                table: "client_credential",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_credential_prefix",
                schema: "integration",
                table: "client_credential",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_usage_at",
                schema: "integration",
                table: "client_usage",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_client_usage_client_id_at",
                schema: "integration",
                table: "client_usage",
                columns: new[] { "client_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_export_schedule_client_id",
                schema: "integration",
                table: "export_schedule",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_attempt_delivery_id",
                schema: "integration",
                table: "webhook_attempt",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_client_id_created_at",
                schema: "integration",
                table: "webhook_delivery",
                columns: new[] { "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_status_next_attempt_at",
                schema: "integration",
                table: "webhook_delivery",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_subscription_id_event_key",
                schema: "integration",
                table: "webhook_delivery",
                columns: new[] { "subscription_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_subscription_client_id",
                schema: "integration",
                table: "webhook_subscription",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "client_credential",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "client_usage",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "export_schedule",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "webhook_attempt",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "webhook_delivery",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "webhook_subscription",
                schema: "integration");
        }
    }
}
