using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Access;

/// <summary>Layer 1 (system access) and the module switch in Settings. Each test owns its host: they change global state.</summary>
public class SystemAccessTests(PostgresFixture postgres)
{
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task A_role_without_system_access_opens_nothing_and_grants_by_user_unit_or_everyone_open_the_system()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var user = await factory.CreateUserAsync();
        await factory.GrantRoleAsync(user.Id, "sample.viewer");
        var client = await factory.SignInAsync(user);
        (await client.GetAsync(Items)).StatusCode.ShouldBe(HttpStatusCode.OK, "the helper grants system access with a module role");

        // Revoke the user's grant: the role alone no longer opens the system.
        var grants = await admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/access/system-access?system=sample");
        var grant = grants!.Single(g => g.GetProperty("principal").GetProperty("id").GetString() == user.Id.ToString());
        (await admin.PostAsync($"/api/v1/access/system-access/{grant.GetProperty("id").GetGuid()}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.GetAsync(Items)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        (await MySystemsAsync(client)).ShouldNotContain("sample");

        // A grant to the user's unit (the unit and everything below it) opens it again.
        (await admin.PostAsJsonAsync("/api/v1/access/system-access", new { systemCode = "sample", principalKind = "unit", principalId = user.UnitId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(Items)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MySystemsAsync(client)).ShouldContain("sample");

        // Duplicate grants are refused; platform systems need no grant.
        await (await admin.PostAsJsonAsync("/api/v1/access/system-access", new { systemCode = "sample", principalKind = "unit", principalId = user.UnitId }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "access.systemAccess.duplicate");
        await (await admin.PostAsJsonAsync("/api/v1/access/system-access", new { systemCode = "org", principalKind = "everyone" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Disabling_a_system_in_settings_hides_it_and_blocks_its_api_until_enabled_again()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var client = await factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        (await MySystemsAsync(client)).ShouldContain("sample");

        var sample = await SystemAsync(admin, "sample");
        var disabled = await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled = false, version = sample.GetProperty("version").GetUInt32() });
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());

        await (await client.GetAsync(Items)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "module.disabled", "errors.moduleDisabled");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/access/me");
        me.GetProperty("systems").EnumerateArray().Select(s => s.GetString()).ShouldNotContain("sample");
        me.GetProperty("permissions").EnumerateArray().Select(s => s.GetString()).ShouldNotContain(p => p!.StartsWith("sample.", StringComparison.Ordinal));

        sample = await SystemAsync(admin, "sample");
        (await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled = true, version = sample.GetProperty("version").GetUInt32() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(Items)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Platform_systems_cannot_be_disabled_and_the_owning_unit_is_one_setting()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var unit = (await factory.CreateUserAsync()).UnitId;

        var org = await SystemAsync(admin, "org");
        await (await admin.PutAsJsonAsync("/api/v1/access/systems/org", new { enabled = false, version = org.GetProperty("version").GetUInt32() }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "access.system.platformAlwaysOn");

        var sample = await SystemAsync(admin, "sample");
        var owned = await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled = true, owningUnitId = unit, version = sample.GetProperty("version").GetUInt32() });
        owned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("owningUnitId").GetGuid().ShouldBe(unit!.Value);

        // A stale version is a conflict.
        await (await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled = true, version = sample.GetProperty("version").GetUInt32() }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict");
    }

    private static async Task<List<string?>> MySystemsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/access/me")).GetProperty("systems").EnumerateArray().Select(s => s.GetString()).ToList();

    private static async Task<JsonElement> SystemAsync(HttpClient admin, string code) =>
        (await admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/access/systems"))!.Single(s => s.GetProperty("code").GetString() == code);
}
