using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
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
        (await ScalarAsync(check, $"select count(*) from access.role_assignment where principal_id = '{adminId}'")).ShouldBe(4);
        (await ScalarAsync(check, "select count(*) from information_schema.columns where table_name = 'users' and column_name = 'is_system_admin'")).ShouldBe(0);
        (await ScalarAsync(check, $"select count(*) from settings.lookup_value where type_code = 'employee.grade' and code = 'LEGACY-{suffix}'")).ShouldBe(1);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
