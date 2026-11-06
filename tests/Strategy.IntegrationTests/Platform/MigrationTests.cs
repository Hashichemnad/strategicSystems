using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.IntegrationTests.Platform;

public class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_from_an_empty_database_into_module_schemas()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select table_schema || '.' || table_name from information_schema.tables where table_schema = 'sample' order by 1",
            connection);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.ShouldBe(["sample.__ef_migrations_history", "sample.sample_items"]);
        (await ScalarAsync(connection, "select count(*) from access.permission")).ShouldBeGreaterThan(20);
        (await ScalarAsync(connection, "select count(*) from access.role where locked")).ShouldBe(1);
        (await ScalarAsync(connection, "select count(*) from settings.lookup_value where type_code = 'employee.grade'")).ShouldBeGreaterThan(0);
        await using var publicTables = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema = 'public'", connection);
        ((long)(await publicTables.ExecuteScalarAsync())!).ShouldBe(0);
    }

    /// <summary>
    /// ADR 0016 upgrade path: a Phase 1 database with a system administrator (the old flag) and a free-text grade is upgraded;
    /// the administrator holds the platform administrator roles afterwards and the grade became a lookup value.
    /// </summary>
    [Fact]
    public async Task Upgrading_a_phase_1_database_turns_administrators_into_role_holders_and_grades_into_lookup_values()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(), settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" }, backgroundServices: false);
        var adminId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Phase 1 state: every schema up to date except identity, which stops before the Phase 2 migration.
            foreach (var database in scope.ServiceProvider.GetServices<ModuleDatabase>())
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);
                if (context is IdentityStoreDbContext)
                {
                    await context.GetService<IMigrator>().MigrateAsync("TotpReplayProtection");
                }
                else if (database.Schema is not ("access" or "settings"))
                {
                    await context.Database.MigrateAsync();
                }
            }
        }

        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                $"""
                insert into identity.users (id, principal_type, active, totp_enabled, email_otp_enabled, created_at, user_name, normalized_user_name,
                    email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count, is_system_admin)
                values ('{adminId}', 'internal', true, false, false, now(), 'old.admin', 'OLD.ADMIN', true, false, false, true, 0, true);
                insert into core.org_unit (id, code, level_code, path, depth, active, effective_from, name_search, created_at, updated_at)
                values ('{unitId}', 'OLD-{suffix}', 'organisation', '/{unitId}/', 0, true, current_date, 'old', now(), now());
                insert into core.employee (id, employee_no, email, unit_id, status, name_search, created_at, updated_at, grade)
                values (gen_random_uuid(), 'OLD-{suffix}', 'old.{suffix}@example.test', '{unitId}', 'active', 'old', now(), now(), 'LEGACY-{suffix}');
                """, connection);
            await insert.ExecuteNonQueryAsync();
        }

        // `--migrate` on upgrade: the remaining migrations, then the seeders.
        await factory.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IUserRoleQuery>();
            (await roles.RequiresMfaAsync(adminId, null)).ShouldBeTrue();
            (await roles.IsLastAccessAdministratorAsync(adminId)).ShouldBeTrue();
        }

        await using var check = new NpgsqlConnection(factory.ConnectionString);
        await check.OpenAsync();
        (await ScalarAsync(check, "select count(*) from access.pending_admin_grant")).ShouldBe(0);
        (await ScalarAsync(check, $"select count(*) from access.role_assignment where principal_id = '{adminId}'")).ShouldBe(11); // org, identity, access, settings, audit, workflow, calendar, ai, mcp, tasks, teams administrators
        (await ScalarAsync(check, "select count(*) from information_schema.columns where table_name = 'users' and column_name = 'is_system_admin'")).ShouldBe(0);
        (await ScalarAsync(check, $"select count(*) from settings.lookup_value where type_code = 'employee.grade' and code = 'LEGACY-{suffix}'")).ShouldBe(1);
    }

    /// <summary>
    /// Phase 3 upgrade path (ADR 0014 / 0017 / 0018): a Phase 2 database keeps its employee photo, its branding logo and its sign-in
    /// history. Photos and logos move into the Files service, sign-in events into the hash-chained audit trail, and the chain verifies.
    /// </summary>
    [Fact]
    public async Task Upgrading_a_phase_2_database_keeps_photos_logos_and_sign_in_history()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(), settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" }, backgroundServices: false);
        await MigrateToPhase2Async(factory);

        var userId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var employeeId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var signedInAt = new DateTimeOffset(2026, 5, 4, 7, 30, 0, TimeSpan.Zero);
        byte[] photo = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];
        byte[] logo = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 6, 5, 4, 3, 2, 1];
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                $"""
                insert into identity.users (id, principal_type, active, totp_enabled, email_otp_enabled, created_at, user_name, normalized_user_name,
                    email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
                values ('{userId}', 'internal', true, false, false, now(), 'phase2.user', 'PHASE2.USER', true, false, false, true, 0);
                insert into identity.auth_event (id, type, user_id, user_name, success, ip_address, correlation_id, at)
                values ('{eventId}', 'login', '{userId}', 'phase2.user', true, '10.1.2.3', 'phase2-correlation', @at);
                insert into core.org_unit (id, code, level_code, path, depth, active, effective_from, name_search, created_at, updated_at)
                values ('{unitId}', 'P2-{suffix}', 'organisation', '/{unitId}/', 0, true, current_date, 'p2', now(), now());
                insert into core.employee (id, employee_no, email, unit_id, status, name_search, created_at, updated_at)
                values ('{employeeId}', 'P2-{suffix}', 'p2.{suffix}@example.test', '{unitId}', 'active', 'p2', now(), now());
                insert into core.employee_assignment (id, employee_id, unit_id, valid_from, created_at, updated_at)
                values (gen_random_uuid(), '{employeeId}', '{unitId}', date '2024-01-01', now(), now());
                insert into core.employee_photo (employee_id, content_type, content, updated_at) values ('{employeeId}', 'image/png', @photo, @at);
                insert into settings.branding_asset (kind, content_type, content, updated_at) values ('logo-light', 'image/jpeg', @logo, @at);
                """, connection);
            insert.Parameters.AddWithValue("at", signedInAt);
            insert.Parameters.AddWithValue("photo", photo);
            insert.Parameters.AddWithValue("logo", logo);
            await insert.ExecuteNonQueryAsync();
        }

        // `--migrate` on upgrade: the Phase 3 migrations stage the old rows, then the seeders carry them into Files and the audit chain.
        await factory.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var files = scope.ServiceProvider.GetRequiredService<IFileService>();
            var storedPhoto = await files.FindSingleAsync("org.employee-photo", employeeId.ToString("D"));
            storedPhoto.ShouldNotBeNull();
            storedPhoto.OwnerType.ShouldBe("org.employee");
            storedPhoto.UpdatedAt.ShouldBeGreaterThan(signedInAt); // carried over now; the version keeps the original date
            (await ReadAllAsync(files, storedPhoto.Id)).ShouldBe(photo);
            var versions = await files.VersionsAsync(storedPhoto.Id);
            versions.ShouldHaveSingleItem().UploadedAt.ShouldBe(signedInAt);

            var storedLogo = await files.FindSingleAsync("settings.branding-logo", "logo-light");
            storedLogo.ShouldNotBeNull();
            storedLogo.ContentType.ShouldBe("image/jpeg");
            (await ReadAllAsync(files, storedLogo.Id)).ShouldBe(logo);

            var events = await scope.ServiceProvider.GetRequiredService<AuthEventLog>()
                .ListAsync(new Strategy.Platform.Kernel.Paging.PageRequest(1, 50), userId, null, CancellationToken.None);
            var login = events.Items.ShouldHaveSingleItem();
            login.Type.ShouldBe("login");
            login.At.ShouldBe(signedInAt);
            login.IpAddress.ShouldBe("10.1.2.3");
            login.UserName.ShouldBe("phase2.user");

            var verifier = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Audit.Application.AuditVerifier>();
            (await verifier.VerifyAsync("full", CancellationToken.None)).Ok.ShouldBeTrue();
        }

        await using var check = new NpgsqlConnection(factory.ConnectionString);
        await check.OpenAsync();
        (await ScalarAsync(check, "select count(*) from files.pending_import")).ShouldBe(0);
        (await ScalarAsync(check, "select count(*) from audit.legacy_import")).ShouldBe(0);
        (await ScalarAsync(check, $"select count(*) from core.employee_assignment where employee_id = '{employeeId}'")).ShouldBe(1);
        (await ScalarAsync(check, "select count(*) from information_schema.tables where table_name in ('employee_photo', 'branding_asset', 'auth_event')")).ShouldBe(0);
        (await ScalarAsync(check, $"select count(*) from audit.entry where action = 'auth.login' and record_id = '{userId}' and channel = 'import'")).ShouldBe(1);
    }

    /// <summary>Brings every Phase 1–2 schema to its last Phase 2 migration; the Phase 3 schemas do not exist yet.</summary>
    private static async Task MigrateToPhase2Async(ApiFactory factory)
    {
        var phase2 = new Dictionary<string, string>
        {
            ["identity"] = "DropSystemAdminFlag",
            ["settings"] = "20261031130718_InitialCreate",
            ["access"] = "20261031130714_InitialCreate",
            ["core"] = "20261022170513_InitialCreate",
            ["sample"] = "NumberOwnerAndUnit",
        };
        await using var scope = factory.Services.CreateAsyncScope();
        foreach (var database in scope.ServiceProvider.GetServices<ModuleDatabase>())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);
            if (context is Strategy.Platform.Persistence.DataProtectionKeysDbContext)
            {
                await context.Database.MigrateAsync();
            }
            else if (phase2.TryGetValue(database.Schema, out var target))
            {
                await context.GetService<IMigrator>().MigrateAsync(target);
            }
        }
    }

    /// <summary>
    /// ADR 0018 upgrade step on an installation that ran as the owner role: <c>--setup-db-roles</c> creates the runtime role, upgrades the
    /// database and grants it; the application then runs as that role, keeps its data, and cannot alter the audit trail. A second run is
    /// harmless (password updated, grants re-applied).
    /// </summary>
    [Fact]
    public async Task Setup_db_roles_moves_a_phase_2_installation_to_a_runtime_role_that_cannot_alter_the_audit_trail()
    {
        var owner = await postgres.CreateDatabaseAsync();
        await using (var phase2 = new ApiFactory(owner, settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" }, backgroundServices: false))
        {
            await MigrateToPhase2Async(phase2);
        }

        var itemId = Guid.CreateVersion7();
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                $"insert into sample.sample_items (id, name_en, status, created_at, updated_at) values ('{itemId}', 'Phase 2 item', 'active', now(), now())",
                connection);
            await insert.ExecuteNonQueryAsync();
        }

        var role = "strategy_rt_" + Guid.CreateVersion7().ToString("N")[^8..];
        var runtime = new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = "First#Pass1" }.ConnectionString;
        var setupSettings = new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            ["Database:UseMigrationsConnection"] = "true", // what Program sets for --setup-db-roles
            ["ConnectionStrings:StrategyMigrations"] = owner,
        };
        await using (var setup = new ApiFactory(runtime, settings: setupSettings, backgroundServices: false))
        {
            var output = new StringWriter();
            (await Strategy.Host.Infrastructure.Bootstrap.SetupDatabaseRolesAsync(setup.Services, output)).ShouldBe(0, output.ToString());
            output.ToString().ShouldContain("created");
        }

        // Run it again with a new password: the role is updated, not duplicated.
        runtime = new NpgsqlConnectionStringBuilder(runtime) { Password = "Second#Pass2" }.ConnectionString;
        await using (var again = new ApiFactory(runtime, settings: setupSettings, backgroundServices: false))
        {
            var output = new StringWriter();
            (await Strategy.Host.Infrastructure.Bootstrap.SetupDatabaseRolesAsync(again.Services, output)).ShouldBe(0, output.ToString());
            output.ToString().ShouldContain("updated");
        }

        await using var app = new ApiFactory(runtime, settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });
        var editor = await app.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        (await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Runtime role item" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var asRuntime = new NpgsqlConnection(runtime);
        await asRuntime.OpenAsync();
        (await ScalarAsync(asRuntime, $"select count(*) from sample.sample_items where id = '{itemId}'")).ShouldBe(1); // Phase 2 data kept
        (await ScalarAsync(asRuntime, "select count(*) from audit.entry")).ShouldBeGreaterThan(0);
        await using (var who = new NpgsqlCommand("select current_user", asRuntime))
        {
            ((string)(await who.ExecuteScalarAsync())!).ShouldBe(role);
        }

        foreach (var sql in new[] { "update audit.entry set reason = 'x'", "delete from audit.entry", "truncate audit.entry",
            "alter table audit.entry disable trigger entry_append_only" })
        {
            await using var command = new NpgsqlCommand(sql, asRuntime);
            (await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState.ShouldBe("42501", sql);
        }

        // Without a separate owner connection the command explains what is missing and changes nothing.
        await using var unconfigured = new ApiFactory(owner, settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" },
            backgroundServices: false);
        var message = new StringWriter();
        (await Strategy.Host.Infrastructure.Bootstrap.SetupDatabaseRolesAsync(unconfigured.Services, message)).ShouldBe(2);
        message.ToString().ShouldContain("ConnectionStrings__StrategyMigrations");
    }

    /// <summary>
    /// The documented first start (deploy/README.md) on a fresh two-role install: PostgreSQL's init script creates the runtime role with
    /// CONNECT only; the <c>migrate</c> service migrates as the owner and grants the runtime role; then <c>--create-admin</c> runs in the
    /// <c>app</c> service (runtime connection only) and, again, in the <c>migrate</c> service (both connections). It used to migrate on the
    /// runtime connection and fail with "permission denied for schema core". Before <c>--migrate</c> it explains the order and changes nothing.
    /// </summary>
    [Fact]
    public async Task First_start_on_a_fresh_two_role_database_migrates_as_the_owner_and_creates_the_administrator_as_the_runtime_role()
    {
        var owner = await postgres.CreateDatabaseAsync();
        var role = "strategy_app_" + Guid.CreateVersion7().ToString("N")[^8..];
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            // deploy/postgres/init-roles.sh
            await using var init = new NpgsqlCommand(
                $"CREATE ROLE \"{role}\" LOGIN PASSWORD 'App#Pass1'; GRANT CONNECT, TEMPORARY ON DATABASE \"{connection.Database}\" TO \"{role}\";",
                connection);
            await init.ExecuteNonQueryAsync();
        }

        var runtime = new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = "App#Pass1" }.ConnectionString;
        var passwordFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(passwordFile, "First#Admin2026\n");
        var appService = new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" };
        var migrateService = new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            ["ConnectionStrings:StrategyMigrations"] = owner,
        };

        // Out of order: --create-admin before --migrate changes nothing and says what to do.
        await using (var early = new ApiFactory(runtime, settings: appService, backgroundServices: false))
        {
            Strategy.Host.Infrastructure.Bootstrap.HasSeparateOwnerConnection(early.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>())
                .ShouldBeFalse();
            var output = new StringWriter();
            (await Strategy.Host.Infrastructure.Bootstrap.CreateAdminAsync(
                early.Services, ["--create-admin", "--email", "admin@example.org", "--password-file", passwordFile], output)).ShouldBe(3, output.ToString());
            output.ToString().ShouldContain("--migrate");
        }

        // `up -d --build`: the migrate service (`--migrate` → Database:UseMigrationsConnection) migrates as the owner and grants the role.
        await using (var migrate = new ApiFactory(
            runtime, settings: new Dictionary<string, string?>(migrateService) { ["Database:UseMigrationsConnection"] = "true" }, backgroundServices: false))
        {
            await migrate.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
        }

        // `run --rm app --create-admin …`: runtime connection only.
        await using (var app = new ApiFactory(runtime, settings: appService, backgroundServices: false))
        {
            var output = new StringWriter();
            (await Strategy.Host.Infrastructure.Bootstrap.CreateAdminAsync(
                app.Services, ["--create-admin", "--email", "admin@example.org", "--password-file", passwordFile], output)).ShouldBe(0, output.ToString());
            output.ToString().ShouldContain("created");
        }

        // The same command in the migrate service (both connections; Program migrates as the owner first, then creates as the runtime role).
        await using (var migrateRun = new ApiFactory(runtime, settings: migrateService, backgroundServices: false))
        {
            var configuration = migrateRun.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            Strategy.Host.Infrastructure.Bootstrap.HasSeparateOwnerConnection(configuration).ShouldBeTrue();
            var output = new StringWriter();
            (await Strategy.Host.Infrastructure.Bootstrap.CreateAdminAsync(
                migrateRun.Services, ["--create-admin", "--email", "second@example.org", "--password-file", passwordFile], output)).ShouldBe(0, output.ToString());
        }

        File.Delete(passwordFile);
        await using var asRuntime = new NpgsqlConnection(runtime);
        await asRuntime.OpenAsync();
        (await ScalarAsync(asRuntime, "select count(*) from identity.users where user_name in ('admin@example.org', 'second@example.org')")).ShouldBe(2);
        (await ScalarAsync(asRuntime,
            "select count(*) from access.role_assignment ra join identity.users u on u.id = ra.principal_id where u.user_name = 'admin@example.org'"))
            .ShouldBeGreaterThan(0);
        (await ScalarAsync(asRuntime, "select count(*) from audit.entry")).ShouldBeGreaterThan(0);

        // The administrator signs in with the app running as the runtime role.
        await using var running = new ApiFactory(runtime, settings: appService);
        var client = running.CreateClient();
        await client.RefreshAntiforgeryAsync();
        var login = await client.PostAsJsonAsync("/api/v1/identity/auth/login", new { login = "admin@example.org", password = "First#Admin2026" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
    }

    private static async Task<byte[]> ReadAllAsync(IFileService files, Guid fileId)
    {
        var opened = await files.OpenAsync(fileId, auditDownload: false);
        await using var stream = opened.Value.Stream;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
