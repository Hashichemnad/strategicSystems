using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "checkpoint",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkpoint", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entry",
                schema: "audit",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    record_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parent_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    parent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    field = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    change_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    on_behalf_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf_of_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: true),
                    data = table.Column<string>(type: "text", nullable: true),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry", x => x.seq);
                });

            migrationBuilder.CreateTable(
                name: "legacy_import",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "text", nullable: true),
                    entity_type = table.Column<string>(type: "text", nullable: true),
                    record_id = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: true),
                    data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_import", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "verification_run",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_seq = table.Column<long>(type: "bigint", nullable: false),
                    entries_checked = table.Column<long>(type: "bigint", nullable: false),
                    last_seq = table.Column<long>(type: "bigint", nullable: false),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    broken_seq = table.Column<long>(type: "bigint", nullable: true),
                    message_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    triggered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_run", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entry_action_occurred_at",
                schema: "audit",
                table: "entry",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_entity_type_record_id_seq",
                schema: "audit",
                table: "entry",
                columns: new[] { "entity_type", "record_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_id",
                schema: "audit",
                table: "entry",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_occurred_at",
                schema: "audit",
                table: "entry",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_entry_parent_type_parent_id_seq",
                schema: "audit",
                table: "entry",
                columns: new[] { "parent_type", "parent_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_system_code_occurred_at",
                schema: "audit",
                table: "entry",
                columns: new[] { "system_code", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_user_id_occurred_at",
                schema: "audit",
                table: "entry",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_import_occurred_at",
                schema: "audit",
                table: "legacy_import",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_verification_run_started_at",
                schema: "audit",
                table: "verification_run",
                column: "started_at");

            // Append-only (CLAUDE.md §8, ADR 0018): nobody updates, deletes or truncates audit entries, whatever their role.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.refuse_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.entry is append-only' USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                CREATE TRIGGER entry_append_only BEFORE UPDATE OR DELETE ON audit.entry FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
                CREATE TRIGGER entry_no_truncate BEFORE TRUNCATE ON audit.entry FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_change();
                REVOKE UPDATE, DELETE, TRUNCATE ON audit.entry FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS entry_no_truncate ON audit.entry; DROP TRIGGER IF EXISTS entry_append_only ON audit.entry; DROP FUNCTION IF EXISTS audit.refuse_change();");

            migrationBuilder.DropTable(
                name: "checkpoint",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "entry",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "legacy_import",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "verification_run",
                schema: "audit");
        }
    }
}
