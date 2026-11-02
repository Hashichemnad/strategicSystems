using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.IntegrationTests.Audit;

/// <summary>
/// Tamper evidence (BUILD_PLAN Phase 3: "manually altering an audit row is flagged by the verification screen") and the append-only
/// database role. Each test owns its database, because it breaks the chain on purpose.
/// </summary>
public class AuditChainTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Altering_an_entry_in_the_database_is_detected_by_verification_and_alerts_the_verifiers()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var adminUser = await (await admin.GetAsync("/api/v1/identity/me")).Content.ReadFromJsonAsync<JsonElement>();
        await (await factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor)).PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Evidence" });

        var good = await VerifyAsync(admin, "full");
        good.GetProperty("ok").GetBoolean().ShouldBeTrue();
        var checkedCount = good.GetProperty("entriesChecked").GetInt64();
        checkedCount.ShouldBeGreaterThan(10);

        var target = await ScalarAsync(factory, "select seq from audit.entry where entity_type = 'sample.item' and action = 'create' order by seq desc limit 1");
        await ExecuteAsTamperingDbaAsync(factory, $"update audit.entry set new_value = replace(new_value, 'Evidence', 'Forged') where seq = {target}");

        var bad = await VerifyAsync(admin, "full");
        bad.GetProperty("ok").GetBoolean().ShouldBeFalse();
        bad.GetProperty("brokenSeq").GetInt64().ShouldBe(target);
        bad.GetProperty("messageKey").GetString().ShouldBe("audit.verify.hashMismatch");

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit/verification");
        status.GetProperty("runs")[0].GetProperty("ok").GetBoolean().ShouldBeFalse();

        // The holders of audit.chain.verify are alerted in the notification centre.
        var adminId = adminUser.GetProperty("id").GetGuid();
        await Eventually(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotifyDbContext>();
            return await db.Notifications.AnyAsync(n => n.UserId == adminId && n.TemplateKey == "audit.chainBroken");
        });
    }

    [Fact]
    public async Task A_deleted_entry_and_a_truncated_tail_are_detected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var editor = await factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        for (var i = 0; i < 3; i++)
        {
            await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = $"Row {i}" });
        }

        (await VerifyAsync(admin, "full")).GetProperty("ok").GetBoolean().ShouldBeTrue(); // sets the checkpoint at the current tail
        var max = await ScalarAsync(factory, "select max(seq) from audit.entry");

        // A row in the middle disappears.
        var middle = max - 5;
        await ExecuteAsTamperingDbaAsync(factory, $"delete from audit.entry where seq = {middle}");
        var gap = await VerifyAsync(admin, "full");
        gap.GetProperty("ok").GetBoolean().ShouldBeFalse();
        gap.GetProperty("brokenSeq").GetInt64().ShouldBe(middle);
        gap.GetProperty("messageKey").GetString().ShouldBe("audit.verify.missingEntry");

        // The tail beyond the last checkpoint is cut off.
        await ExecuteAsTamperingDbaAsync(factory, $"delete from audit.entry where seq >= {middle - 2}");
        var truncated = await VerifyAsync(admin, "incremental");
        truncated.GetProperty("ok").GetBoolean().ShouldBeFalse();
        truncated.GetProperty("messageKey").GetString().ShouldBe("audit.verify.truncated");
    }

    [Fact]
    public async Task The_runtime_role_may_only_read_and_append_audit_entries()
    {
        var database = await postgres.CreateDatabaseAsync();
        var role = "strategy_app_" + Guid.NewGuid().ToString("N")[..8];
        await using (var owner = new NpgsqlConnection(database))
        {
            await owner.OpenAsync();
            await using var create = new NpgsqlCommand($"create role {role} login password 'app'; grant connect on database \"{owner.Database}\" to {role}", owner);
            await create.ExecuteNonQueryAsync();
        }

        var runtime = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = "app" }.ConnectionString;
        await using (var migrate = new ApiFactory(runtime, settings: new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            ["Database:UseMigrationsConnection"] = "true",
            ["ConnectionStrings:StrategyMigrations"] = database,
        }, backgroundServices: false))
        {
            await migrate.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
        }

        // The application itself runs as the runtime role: it works normally and writes audit entries.
        await using var app = new ApiFactory(runtime, settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });
        var editor = await app.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        (await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "As runtime role" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var connection = new NpgsqlConnection(runtime);
        await connection.OpenAsync();
        await using (var read = new NpgsqlCommand("select count(*) from audit.entry", connection))
        {
            ((long)(await read.ExecuteScalarAsync())!).ShouldBeGreaterThan(0);
        }

        await using (var alter = new NpgsqlCommand("alter table audit.entry disable trigger entry_append_only", connection))
        {
            (await Should.ThrowAsync<PostgresException>(() => alter.ExecuteNonQueryAsync())).SqlState.ShouldBe("42501");
        }

        foreach (var sql in new[] { "update audit.entry set reason = 'x'", "delete from audit.entry", "truncate audit.entry" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            (await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState.ShouldBe("42501");
        }
    }

    private static async Task<JsonElement> VerifyAsync(HttpClient admin, string mode)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/audit/verification", new { mode });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>What a database superuser could do: switch the guard off, change rows, switch it back on.</summary>
    private static async Task ExecuteAsTamperingDbaAsync(ApiFactory factory, string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"alter table audit.entry disable trigger entry_append_only; {sql}; alter table audit.entry enable trigger entry_append_only;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(ApiFactory factory, string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new ShouldAssertException("Condition not met within 10 s.");
    }
}
