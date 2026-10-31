using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Platform.Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "pending_admin_grant",
                schema: "access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_admin_grant", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "permission",
                schema: "access",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    @object = table.Column<string>(name: "object", type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    label_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scopable = table.Column<bool>(type: "boolean", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "record_role",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "record_role_definition",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("pk_record_role_definition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description_en = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    builtin = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    requires_mfa = table.Column<bool>(type: "boolean", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_transfer",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_keys = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    from_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    to_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    moved_count = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_transfer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    owning_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_access",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    principal_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_access", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "record_role_permission",
                schema: "access",
                columns: table => new
                {
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_role_permission", x => new { x.definition_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_record_role_permission_permission_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permission",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_record_role_permission_record_role_definition_definition_id",
                        column: x => x.definition_id,
                        principalSchema: "access",
                        principalTable: "record_role_definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_assignment",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_assignment_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "access",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                schema: "access",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permission", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permission_permission_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permission",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permission_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "access",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_permission_system_code",
                schema: "access",
                table: "permission",
                column: "system_code");

            migrationBuilder.CreateIndex(
                name: "ix_record_role_principal_kind_principal_id_resource_type",
                schema: "access",
                table: "record_role",
                columns: new[] { "principal_kind", "principal_id", "resource_type" });

            migrationBuilder.CreateIndex(
                name: "ix_record_role_resource_type_resource_id",
                schema: "access",
                table: "record_role",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_record_role_definition_resource_type_role_code",
                schema: "access",
                table: "record_role_definition",
                columns: new[] { "resource_type", "role_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_record_role_permission_permission_code",
                schema: "access",
                table: "record_role_permission",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_role_system_code_code",
                schema: "access",
                table: "role",
                columns: new[] { "system_code", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_assignment_principal_kind_principal_id",
                schema: "access",
                table: "role_assignment",
                columns: new[] { "principal_kind", "principal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignment_role_id",
                schema: "access",
                table: "role_assignment",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_permission_code",
                schema: "access",
                table: "role_permission",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_system_code",
                schema: "access",
                table: "system",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_access_system_code_principal_kind_principal_id",
                schema: "access",
                table: "system_access",
                columns: new[] { "system_code", "principal_kind", "principal_id" },
                unique: true,
                filter: "active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_admin_grant",
                schema: "access");

            migrationBuilder.DropTable(
                name: "record_role",
                schema: "access");

            migrationBuilder.DropTable(
                name: "record_role_permission",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_assignment",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_permission",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_transfer",
                schema: "access");

            migrationBuilder.DropTable(
                name: "system",
                schema: "access");

            migrationBuilder.DropTable(
                name: "system_access",
                schema: "access");

            migrationBuilder.DropTable(
                name: "record_role_definition",
                schema: "access");

            migrationBuilder.DropTable(
                name: "permission",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role",
                schema: "access");
        }
    }
}
