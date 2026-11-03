using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Tasks.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tasks");

            migrationBuilder.CreateTable(
                name: "assignment_group",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completion_rule = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidential = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "checklist_item",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    done_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comment",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mentions = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    author_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dependency",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dependency", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "follow_up",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    can_upload = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidential = table.Column<bool>(type: "boolean", nullable: false),
                    creator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    linked_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_soon_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalate_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    business_hours_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accept_remind_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completion_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    feedback_point = table.Column<int>(type: "integer", nullable: true),
                    feedback_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    percent_complete = table.Column<int>(type: "integer", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    actual_seconds = table.Column<long>(type: "bigint", nullable: false),
                    time_to_accept_seconds = table.Column<long>(type: "bigint", nullable: true),
                    time_to_complete_seconds = table.Column<long>(type: "bigint", nullable: true),
                    completed_on_time = table.Column<bool>(type: "boolean", nullable: true),
                    recurrence = table.Column<string>(type: "jsonb", nullable: true),
                    recurrence_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_occurrence_on = table.Column<DateOnly>(type: "date", nullable: true),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accept_reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                name: "task_event",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "timesheet_period",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<long>(type: "bigint", nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    end_event = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timesheet_period", x => x.id);
                    table.ForeignKey(
                        name: "fk_timesheet_period_task_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_item_task_id",
                schema: "tasks",
                table: "checklist_item",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_task_id",
                schema: "tasks",
                table: "comment",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_depends_on_task_id",
                schema: "tasks",
                table: "dependency",
                column: "depends_on_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_task_id_depends_on_task_id",
                schema: "tasks",
                table: "dependency",
                columns: new[] { "task_id", "depends_on_task_id" },
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_employee_id",
                schema: "tasks",
                table: "follow_up",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_task_id_employee_id",
                schema: "tasks",
                table: "follow_up",
                columns: new[] { "task_id", "employee_id" },
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignee_employee_id",
                schema: "tasks",
                table: "task",
                column: "assignee_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_creator_employee_id",
                schema: "tasks",
                table: "task",
                column: "creator_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_due_at",
                schema: "tasks",
                table: "task",
                column: "due_at");

            migrationBuilder.CreateIndex(
                name: "ix_task_group_id",
                schema: "tasks",
                table: "task",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_linked_type_linked_id",
                schema: "tasks",
                table: "task",
                columns: new[] { "linked_type", "linked_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_next_occurrence_on",
                schema: "tasks",
                table: "task",
                column: "next_occurrence_on",
                filter: "next_occurrence_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_number",
                schema: "tasks",
                table: "task",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_parent_task_id",
                schema: "tasks",
                table: "task",
                column: "parent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status",
                schema: "tasks",
                table: "task",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_task_unit_id",
                schema: "tasks",
                table: "task",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_event_task_id_occurred_at",
                schema: "tasks",
                table: "task_event",
                columns: new[] { "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_timesheet_period_number",
                schema: "tasks",
                table: "timesheet_period",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_timesheet_period_task_id",
                schema: "tasks",
                table: "timesheet_period",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_group",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "checklist_item",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "comment",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "dependency",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "follow_up",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_event",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "timesheet_period",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task",
                schema: "tasks");
        }
    }
}
