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
        (await ScalarAsync(check, $"select count(*) from access.role_assignment where principal_id = '{adminId}'")).ShouldBe(5); // org, identity, access, settings, audit administrators
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
        var phase2 = new Dictionary<string, string>
        {
            ["identity"] = "DropSystemAdminFlag",
            ["settings"] = "20261031130718_InitialCreate",
            ["access"] = "20261031130714_InitialCreate",
            ["core"] = "20261022170513_InitialCreate",
            ["sample"] = "NumberOwnerAndUnit",
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
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
