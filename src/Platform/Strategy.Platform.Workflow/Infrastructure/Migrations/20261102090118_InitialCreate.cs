using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Strategy.Platform.Workflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "history",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(30)", maxLength: 30, nullable: false),
                    step_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    step_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf_of_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed = table.Column<bool>(type: "boolean", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instance",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    title_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    subject_owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitter_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitter_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    variables = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_sort = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    previous_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "process_version",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_sort = table.Column<int>(type: "integer", nullable: false),
                    step_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    step_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    step_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    acted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acted_by_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf_of_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "process_step",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rule = table.Column<string>(type: "jsonb", nullable: false),
                    condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actions = table.Column<List<string>>(type: "text[]", nullable: false),
                    custom_actions = table.Column<string>(type: "jsonb", nullable: false),
                    comment_on_approve = table.Column<bool>(type: "boolean", nullable: false),
                    comment_on_reject = table.Column<bool>(type: "boolean", nullable: false),
                    comment_on_return = table.Column<bool>(type: "boolean", nullable: false),
                    sla_hours = table.Column<int>(type: "integer", nullable: true),
                    escalation = table.Column<string>(type: "jsonb", nullable: true),
                    on_no_approver = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exclude_submitter = table.Column<bool>(type: "boolean", nullable: false),
                    notification_template = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_process_step_process_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "workflow",
                        principalTable: "process_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_assignee",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignee", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_assignee_task_task_id",
                        column: x => x.task_id,
                        principalSchema: "workflow",
                        principalTable: "task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_history_instance_id_seq",
                schema: "workflow",
                table: "history",
                columns: new[] { "instance_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_created_at",
                schema: "workflow",
                table: "idempotency_record",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_user_id_key",
                schema: "workflow",
                table: "idempotency_record",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instance_process_key_resource_id",
                schema: "workflow",
                table: "instance",
                columns: new[] { "process_key", "resource_id" },
                unique: true,
                filter: "status = 'in_progress'");

            migrationBuilder.CreateIndex(
                name: "ix_instance_resource_type_resource_id",
                schema: "workflow",
                table: "instance",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_submitter_user_id_status",
                schema: "workflow",
                table: "instance",
                columns: new[] { "submitter_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_process_step_version_id_sort",
                schema: "workflow",
                table: "process_step",
                columns: new[] { "version_id", "sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_process_version_process_key_version_no",
                schema: "workflow",
                table: "process_version",
                columns: new[] { "process_key", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_instance_id",
                schema: "workflow",
                table: "task",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_due_at",
                schema: "workflow",
                table: "task",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignee_employee_id_active",
                schema: "workflow",
                table: "task_assignee",
                columns: new[] { "employee_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignee_task_id",
                schema: "workflow",
                table: "task_assignee",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignee_user_id_active",
                schema: "workflow",
                table: "task_assignee",
                columns: new[] { "user_id", "active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "history",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "idempotency_record",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "instance",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "process_step",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "task_assignee",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "process_version",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "task",
                schema: "workflow");
        }
    }
}
