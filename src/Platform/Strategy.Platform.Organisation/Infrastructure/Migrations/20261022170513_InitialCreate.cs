using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Strategy.Platform.Organisation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "org_level",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_level", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_settings",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enforce_level_order = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_unit",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    level_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    head_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deputy_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_centre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    name_search = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_unit", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_unit_org_unit_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "core",
                        principalTable: "org_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    mobile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    job_title_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_title_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    grade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    join_date = table.Column<DateOnly>(type: "date", nullable: true),
                    name_search = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_employee_manager_employee_id",
                        column: x => x.manager_employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "org_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_unit_version",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    level_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    head_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deputy_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_centre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_unit_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_unit_version_org_unit_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "org_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delegation",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scope = table.Column<List<string>>(type: "text[]", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegation", x => x.id);
                    table.ForeignKey(
                        name: "fk_delegation_employees_delegate_employee_id",
                        column: x => x.delegate_employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delegation_employees_delegator_employee_id",
                        column: x => x.delegator_employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_assignment",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_title_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_assignment_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_photo",
                schema: "core",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_photo", x => x.employee_id);
                    table.ForeignKey(
                        name: "fk_employee_photo_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "core",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "org_level",
                columns: new[] { "id", "active", "code", "created_at", "created_by", "name_ar", "name_en", "sort", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("0192e3a0-0000-7000-8000-000000000001"), true, "organisation", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "المؤسسة", "Organisation", 10, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0192e3a0-0000-7000-8000-000000000002"), true, "sector", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "القطاع", "Sector", 20, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0192e3a0-0000-7000-8000-000000000003"), true, "department", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "الإدارة", "Department", 30, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0192e3a0-0000-7000-8000-000000000004"), true, "section", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "القسم", "Section", 40, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0192e3a0-0000-7000-8000-000000000005"), true, "unit", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "الوحدة", "Unit", 50, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "org_settings",
                columns: new[] { "id", "enforce_level_order" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), true });

            migrationBuilder.CreateIndex(
                name: "ix_delegation_delegate_employee_id_status",
                schema: "core",
                table: "delegation",
                columns: new[] { "delegate_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delegation_delegator_employee_id_status",
                schema: "core",
                table: "delegation",
                columns: new[] { "delegator_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_email",
                schema: "core",
                table: "employee",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_employee_no",
                schema: "core",
                table: "employee",
                column: "employee_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_manager_employee_id",
                schema: "core",
                table: "employee",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_name_search",
                schema: "core",
                table: "employee",
                column: "name_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_unit_id",
                schema: "core",
                table: "employee",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignment_employee_id_valid_from",
                schema: "core",
                table: "employee_assignment",
                columns: new[] { "employee_id", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_level_code",
                schema: "core",
                table: "org_level",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_code",
                schema: "core",
                table: "org_unit",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_name_search",
                schema: "core",
                table: "org_unit",
                column: "name_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_parent_id",
                schema: "core",
                table: "org_unit",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_path",
                schema: "core",
                table: "org_unit",
                column: "path")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_version_unit_id_valid_from",
                schema: "core",
                table: "org_unit_version",
                columns: new[] { "unit_id", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_version_valid_from_valid_to",
                schema: "core",
                table: "org_unit_version",
                columns: new[] { "valid_from", "valid_to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delegation",
                schema: "core");

            migrationBuilder.DropTable(
                name: "employee_assignment",
                schema: "core");

            migrationBuilder.DropTable(
                name: "employee_photo",
                schema: "core");

            migrationBuilder.DropTable(
                name: "org_level",
                schema: "core");

            migrationBuilder.DropTable(
                name: "org_settings",
                schema: "core");

            migrationBuilder.DropTable(
                name: "org_unit_version",
                schema: "core");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "core");

            migrationBuilder.DropTable(
                name: "org_unit",
                schema: "core");
        }
    }
}
