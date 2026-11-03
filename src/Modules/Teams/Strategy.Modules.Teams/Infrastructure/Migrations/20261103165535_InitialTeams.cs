using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Teams.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "teams");

            migrationBuilder.CreateTable(
                name: "attendance",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_template",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    agenda = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    minutes = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    decisions = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minutes_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    minutes_delay_hours = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    minutes_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_drafted = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_evaluation",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    role_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    points = table.Column<int>(type: "integer", nullable: true),
                    grade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attendance_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    evaluated_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    calculated_reward = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    approved_reward = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    adjustment_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    certificate = table.Column<bool>(type: "boolean", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    certificate_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_reward = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_evaluation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_role",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_results = table.Column<bool>(type: "boolean", nullable: false),
                    counts_toward_quorum = table.Column<bool>(type: "boolean", nullable: false),
                    is_secretary = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closure_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    evidence_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reward_matrix",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reward_matrix", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    type_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permanent = table.Column<bool>(type: "boolean", nullable: false),
                    subject = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    objectives = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    mandate = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    owning_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sponsor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leader_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    memo_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    memo_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    memo_date = table.Column<DateOnly>(type: "date", nullable: true),
                    memo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    memo_draft = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    report_to = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    workload = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    replaces_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluator_rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_after_evaluation = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_evaluation",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evaluator_rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leader_evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criteria = table.Column<string>(type: "jsonb", nullable: false),
                    score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    workload = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    return_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reward_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reward_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    reward_matrix_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flags = table.Column<string>(type: "jsonb", nullable: false),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_evaluation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_member",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    external_organisation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    external_email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    external_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    role_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    responsibilities = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    required_skills = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    allocation_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: true),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    replaced_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leave_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_member", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_report",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    remind_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_report", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_type",
                schema: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    permanence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    requires_memo = table.Column<bool>(type: "boolean", nullable: false),
                    linkable_types = table.Column<string[]>(type: "text[]", nullable: false),
                    default_cadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evaluation_trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    annual_month = table.Column<int>(type: "integer", nullable: false),
                    annual_day = table.Column<int>(type: "integer", nullable: false),
                    evaluator_rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluator_member_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    leader_evaluator_rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    leader_evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leader_evaluator_member_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_team_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_meeting_id_member_id",
                schema: "teams",
                table: "attendance",
                columns: new[] { "meeting_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_template_year_version_no",
                schema: "teams",
                table: "evaluation_template",
                columns: new[] { "year", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_number",
                schema: "teams",
                table: "meeting",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_team_id_scheduled_at",
                schema: "teams",
                table: "meeting",
                columns: new[] { "team_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_evaluation_employee_id",
                schema: "teams",
                table: "member_evaluation",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_evaluation_evaluation_id_member_id",
                schema: "teams",
                table: "member_evaluation",
                columns: new[] { "evaluation_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_role_code",
                schema: "teams",
                table: "member_role",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_number",
                schema: "teams",
                table: "recommendation",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_owner_employee_id",
                schema: "teams",
                table: "recommendation",
                column: "owner_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_team_id",
                schema: "teams",
                table: "recommendation",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_reward_matrix_year_version_no",
                schema: "teams",
                table: "reward_matrix",
                columns: new[] { "year", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_leader_employee_id",
                schema: "teams",
                table: "team",
                column: "leader_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_number",
                schema: "teams",
                table: "team",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_team_owning_unit_id",
                schema: "teams",
                table: "team",
                column: "owning_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_status",
                schema: "teams",
                table: "team",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_team_type_code",
                schema: "teams",
                table: "team",
                column: "type_code");

            migrationBuilder.CreateIndex(
                name: "ix_team_evaluation_evaluator_employee_id",
                schema: "teams",
                table: "team_evaluation",
                column: "evaluator_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_evaluation_number",
                schema: "teams",
                table: "team_evaluation",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_team_evaluation_team_id_cycle",
                schema: "teams",
                table: "team_evaluation",
                columns: new[] { "team_id", "cycle" },
                unique: true,
                filter: "status <> 'cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_team_member_employee_id",
                schema: "teams",
                table: "team_member",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_member_team_id",
                schema: "teams",
                table: "team_member",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_report_due_at",
                schema: "teams",
                table: "team_report",
                column: "due_at");

            migrationBuilder.CreateIndex(
                name: "ix_team_report_team_id_period_start",
                schema: "teams",
                table: "team_report",
                columns: new[] { "team_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_type_code1",
                schema: "teams",
                table: "team_type",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "evaluation_template",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "meeting",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "member_evaluation",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "member_role",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "recommendation",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "reward_matrix",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "team",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "team_evaluation",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "team_member",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "team_report",
                schema: "teams");

            migrationBuilder.DropTable(
                name: "team_type",
                schema: "teams");
        }
    }
}
