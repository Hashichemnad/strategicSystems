using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Settings.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "branding_asset",
                schema: "settings",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branding_asset", x => x.kind);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lookup_type",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parent_type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    builtin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookup_type", x => x.id);
                    table.UniqueConstraint("ak_lookup_type_code", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "numbering_counter",
                schema: "settings",
                columns: table => new
                {
                    format_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    partition = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numbering_counter", x => new { x.format_key, x.partition });
                });

            migrationBuilder.CreateTable(
                name: "numbering_format",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pattern = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    default_pattern = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numbering_format", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translation_override",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    @namespace = table.Column<string>(name: "namespace", type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_override", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lookup_value",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    label_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    parent_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookup_value", x => x.id);
                    table.ForeignKey(
                        name: "fk_lookup_value_lookup_type_type_code",
                        column: x => x.type_code,
                        principalSchema: "settings",
                        principalTable: "lookup_type",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lookup_value_lookup_value_parent_value_id",
                        column: x => x.parent_value_id,
                        principalSchema: "settings",
                        principalTable: "lookup_value",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_flag_code",
                schema: "settings",
                table: "feature_flag",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lookup_type_code",
                schema: "settings",
                table: "lookup_type",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lookup_value_parent_value_id",
                schema: "settings",
                table: "lookup_value",
                column: "parent_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_lookup_value_type_code_code",
                schema: "settings",
                table: "lookup_value",
                columns: new[] { "type_code", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_numbering_format_key",
                schema: "settings",
                table: "numbering_format",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_setting_key",
                schema: "settings",
                table: "setting",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_override_language_namespace_key",
                schema: "settings",
                table: "translation_override",
                columns: new[] { "language", "namespace", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branding_asset",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "feature_flag",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "lookup_value",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "numbering_counter",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "numbering_format",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "translation_override",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "lookup_type",
                schema: "settings");
        }
    }
}
