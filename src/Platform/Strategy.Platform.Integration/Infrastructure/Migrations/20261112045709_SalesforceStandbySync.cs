using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesforceStandbySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_history",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    process_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    process_instance_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    process_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    step_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    comments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    field = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    old_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_connection",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    connector = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    login_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    client_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    username = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    private_key_protected = table.Column<string>(type: "text", nullable: true),
                    key_thumbprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    api_version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    bulk_threshold = table.Column<int>(type: "integer", nullable: false),
                    page_size = table.Column<int>(type: "integer", nullable: false),
                    schedule = table.Column<string>(type: "jsonb", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    late_after_minutes = table.Column<int>(type: "integer", nullable: true),
                    mapping_pack_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instance_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_limits = table.Column<string>(type: "jsonb", nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    run_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_requested_full = table.Column<bool>(type: "boolean", nullable: false),
                    run_requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    run_requested_objects = table.Column<string>(type: "jsonb", nullable: true),
                    late_alert_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_exception",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_object = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_exception", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_mapping_pack",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pack_version = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    connector = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_mapping_pack", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_object_mapping",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_object = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    load_order = table.Column<int>(type: "integer", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    where = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    watermark_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    use_bulk = table.Column<bool>(type: "boolean", nullable: false),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    defaults = table.Column<string>(type: "jsonb", nullable: false),
                    ignore = table.Column<string>(type: "jsonb", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    notes_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_full_load_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_object_mapping", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_outage",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    report_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    report_acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_outage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_outage_record",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    resubmit_required = table.Column<bool>(type: "boolean", nullable: false),
                    changed_locally = table.Column<bool>(type: "boolean", nullable: false),
                    re_entered = table.Column<bool>(type: "boolean", nullable: false),
                    re_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    re_entered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    conflict = table.Column<bool>(type: "boolean", nullable: false),
                    resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pending_payload = table.Column<string>(type: "jsonb", nullable: true),
                    pending_mapping_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_outage_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_person",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_object = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_person", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_run",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    full = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    objects = table.Column<string>(type: "jsonb", nullable: false),
                    api_calls = table.Column<int>(type: "integer", nullable: false),
                    message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    message_params = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_run_log",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    object_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_run_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_history_entity_type_record_id_occurred_at",
                schema: "integration",
                table: "source_history",
                columns: new[] { "entity_type", "record_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_source_history_source_system_kind_external_id",
                schema: "integration",
                table: "source_history",
                columns: new[] { "source_system", "kind", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_connection_source_system",
                schema: "integration",
                table: "sync_connection",
                column: "source_system",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_exception_connection_id_status_reason",
                schema: "integration",
                table: "sync_exception",
                columns: new[] { "connection_id", "status", "reason" });

            migrationBuilder.CreateIndex(
                name: "ux_sync_exception_open",
                schema: "integration",
                table: "sync_exception",
                columns: new[] { "connection_id", "mapping_id", "external_id" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_sync_mapping_pack_code",
                schema: "integration",
                table: "sync_mapping_pack",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_object_mapping_pack_id_key",
                schema: "integration",
                table: "sync_object_mapping",
                columns: new[] { "pack_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_outage_connection_id_status",
                schema: "integration",
                table: "sync_outage",
                columns: new[] { "connection_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_outage_record_entity_type_record_id",
                schema: "integration",
                table: "sync_outage_record",
                columns: new[] { "entity_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_outage_record_outage_id_entity_type_record_id",
                schema: "integration",
                table: "sync_outage_record",
                columns: new[] { "outage_id", "entity_type", "record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_person_employee_id",
                schema: "integration",
                table: "sync_person",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_person_source_system_external_id",
                schema: "integration",
                table: "sync_person",
                columns: new[] { "source_system", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_run_connection_id_started_at",
                schema: "integration",
                table: "sync_run",
                columns: new[] { "connection_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_run_log_run_id_at",
                schema: "integration",
                table: "sync_run_log",
                columns: new[] { "run_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_history",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_connection",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_exception",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_mapping_pack",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_object_mapping",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_outage",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_outage_record",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_person",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_run",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "sync_run_log",
                schema: "integration");
        }
    }
}
