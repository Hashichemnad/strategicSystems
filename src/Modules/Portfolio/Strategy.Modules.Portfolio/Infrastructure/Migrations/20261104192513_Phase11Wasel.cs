using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase11Wasel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "allocation_percent",
                schema: "portfolio",
                table: "team_member",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "milestone_update_mode",
                schema: "portfolio",
                table: "project",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pm-only");

            migrationBuilder.AddColumn<string>(
                name: "pending_source",
                schema: "portfolio",
                table: "milestone",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_vendor_item_id",
                schema: "portfolio",
                table: "milestone",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "portfolio",
                table: "milestone",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "vendor_confirmed_at",
                schema: "portfolio",
                table: "meeting",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_confirmed_by_user_id",
                schema: "portfolio",
                table: "meeting",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "portfolio",
                table: "meeting",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                schema: "portfolio",
                table: "contract",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "portfolio",
                table: "contract",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plan_proposal",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    accepted_items = table.Column<string>(type: "jsonb", nullable: true),
                    diff = table.Column<string>(type: "jsonb", nullable: true),
                    vendor_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    pm_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_proposal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_review",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    review_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_review", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_review_decision",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_review_decision", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    trade_licence_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    licence_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    country_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_assignment",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_until = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_rule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ended_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_assignment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_comment",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_comment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_deliverable",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pm_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_deliverable", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    impact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    probability = table.Column<int>(type: "integer", nullable: true),
                    impact_level = table.Column<int>(type: "integer", nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pm_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_member",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_member", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_rating",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    timeliness = table.Column<int>(type: "integer", nullable: false),
                    quality = table.Column<int>(type: "integer", nullable: false),
                    responsiveness = table.Column<int>(type: "integer", nullable: false),
                    reporting = table.Column<int>(type: "integer", nullable: false),
                    overall = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_rating", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_submission",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_update_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shared_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shared_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    shared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_submission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_submission_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achieve = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    progress_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    causality = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    corrective_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    previous = table.Column<string>(type: "jsonb", nullable: true),
                    review_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    final_achieve = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    final_actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    final_actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    final_causality = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    final_corrective_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    diff = table.Column<string>(type: "jsonb", nullable: true),
                    pm_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_submission_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_user",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    disabled_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_user", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_milestone_vendor_id",
                schema: "portfolio",
                table: "milestone",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_proposal_project_id_vendor_id_sequence",
                schema: "portfolio",
                table: "plan_proposal",
                columns: new[] { "project_id", "vendor_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_review_period",
                schema: "portfolio",
                table: "portfolio_review",
                column: "period");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_review_decision_review_id_project_id",
                schema: "portfolio",
                table: "portfolio_review_decision",
                columns: new[] { "review_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_number",
                schema: "portfolio",
                table: "vendor",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_trade_licence_no",
                schema: "portfolio",
                table: "vendor",
                column: "trade_licence_no",
                unique: true,
                filter: "trade_licence_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_assignment_project_id_vendor_id",
                schema: "portfolio",
                table: "vendor_assignment",
                columns: new[] { "project_id", "vendor_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_assignment_vendor_id",
                schema: "portfolio",
                table: "vendor_assignment",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_comment_project_id_subject_type_subject_id",
                schema: "portfolio",
                table: "vendor_comment",
                columns: new[] { "project_id", "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_deliverable_project_id",
                schema: "portfolio",
                table: "vendor_deliverable",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_item_project_id",
                schema: "portfolio",
                table: "vendor_item",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_member_assignment_id",
                schema: "portfolio",
                table: "vendor_member",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_member_project_id",
                schema: "portfolio",
                table: "vendor_member",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_member_user_id",
                schema: "portfolio",
                table: "vendor_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_rating_project_id",
                schema: "portfolio",
                table: "vendor_rating",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_rating_vendor_id",
                schema: "portfolio",
                table: "vendor_rating",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_submission_monthly_update_id_vendor_id",
                schema: "portfolio",
                table: "vendor_submission",
                columns: new[] { "monthly_update_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_submission_project_id",
                schema: "portfolio",
                table: "vendor_submission",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_submission_item_project_id",
                schema: "portfolio",
                table: "vendor_submission_item",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_submission_item_submission_id_milestone_id",
                schema: "portfolio",
                table: "vendor_submission_item",
                columns: new[] { "submission_id", "milestone_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_user_user_id",
                schema: "portfolio",
                table: "vendor_user",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_user_vendor_id",
                schema: "portfolio",
                table: "vendor_user",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_proposal",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolio_review",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolio_review_decision",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_assignment",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_comment",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_deliverable",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_item",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_member",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_rating",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_submission",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_submission_item",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "vendor_user",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_milestone_vendor_id",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "allocation_percent",
                schema: "portfolio",
                table: "team_member");

            migrationBuilder.DropColumn(
                name: "milestone_update_mode",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "pending_source",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "pending_vendor_item_id",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "vendor_confirmed_at",
                schema: "portfolio",
                table: "meeting");

            migrationBuilder.DropColumn(
                name: "vendor_confirmed_by_user_id",
                schema: "portfolio",
                table: "meeting");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "portfolio",
                table: "meeting");

            migrationBuilder.DropColumn(
                name: "end_date",
                schema: "portfolio",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "portfolio",
                table: "contract");
        }
    }
}
