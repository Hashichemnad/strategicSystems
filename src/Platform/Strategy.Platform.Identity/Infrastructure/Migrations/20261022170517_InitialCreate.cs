using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Strategy.Platform.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "auth_event",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_provider",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    secret_protected = table.Column<string>(type: "text", nullable: true),
                    match_field = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    auto_link = table.Column<bool>(type: "boolean", nullable: false),
                    jit_create_user = table.Column<bool>(type: "boolean", nullable: false),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_provider", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_settings",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_min_length = table.Column<int>(type: "integer", nullable: false),
                    password_require_upper = table.Column<bool>(type: "boolean", nullable: false),
                    password_require_lower = table.Column<bool>(type: "boolean", nullable: false),
                    password_require_digit = table.Column<bool>(type: "boolean", nullable: false),
                    password_require_symbol = table.Column<bool>(type: "boolean", nullable: false),
                    password_history = table.Column<int>(type: "integer", nullable: false),
                    lockout_threshold = table.Column<int>(type: "integer", nullable: false),
                    lockout_minutes = table.Column<int>(type: "integer", nullable: false),
                    session_idle_minutes = table.Column<int>(type: "integer", nullable: false),
                    session_absolute_hours = table.Column<int>(type: "integer", nullable: false),
                    mfa_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mfa_allow_totp = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_allow_email = table.Column<bool>(type: "boolean", nullable: false),
                    require_local_mfa_after_sso = table.Column<bool>(type: "boolean", nullable: false),
                    token_default_days = table.Column<int>(type: "integer", nullable: false),
                    token_max_days = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "smtp_settings",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: false),
                    security = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_protected = table.Column<string>(type: "text", nullable: true),
                    from_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    from_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    from_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    public_base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_smtp_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    principal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_system_admin = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    theme = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    totp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    email_otp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_history",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_history_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "personal_access_token",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personal_access_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_personal_access_token_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "identity",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_session_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "security_settings",
                columns: new[] { "id", "lockout_minutes", "lockout_threshold", "mfa_allow_email", "mfa_allow_totp", "mfa_policy", "password_history", "password_min_length", "password_require_digit", "password_require_lower", "password_require_symbol", "password_require_upper", "require_local_mfa_after_sso", "session_absolute_hours", "session_idle_minutes", "token_default_days", "token_max_days" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), 15, 5, true, true, "admins", 5, 12, true, true, true, true, false, 12, 30, 90, 365 });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "smtp_settings",
                columns: new[] { "id", "enabled", "from_address", "from_name_ar", "from_name_en", "host", "password_protected", "port", "public_base_url", "security", "user_name" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), false, null, null, null, null, null, 587, null, "starttls", null });

            migrationBuilder.CreateIndex(
                name: "ix_auth_event_at",
                schema: "identity",
                table: "auth_event",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_auth_event_user_id_at",
                schema: "identity",
                table: "auth_event",
                columns: new[] { "user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_provider_code",
                schema: "identity",
                table: "identity_provider",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_history_user_id_created_at",
                schema: "identity",
                table: "password_history",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_personal_access_token_prefix",
                schema: "identity",
                table: "personal_access_token",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personal_access_token_user_id",
                schema: "identity",
                table: "personal_access_token",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "identity",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "identity",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_user_id_revoked_at",
                schema: "identity",
                table: "user_session",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_employee_id",
                schema: "identity",
                table: "users",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                schema: "identity",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                schema: "identity",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_event",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "identity_provider",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "password_history",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "personal_access_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "security_settings",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "smtp_settings",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
