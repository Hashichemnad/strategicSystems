using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Organisation.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>
/// The five data scopes (SPEC §0.2, ADR 0015) proven on real data, in the database query and on single records:
/// <code>
/// Root ─┬─ A  (viewer V, colleague E2)
///       │  └─ A1 (E1, reports to V)
///       └─ B  (outsider O)
/// </code>
/// Each of V, E1, E2 and O creates one sample item (owner = creator's employee, unit = creator's unit).
/// </summary>
public class DataScopeTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Items = "/api/v1/sample/items";

    [Theory]
    [InlineData(DataScope.Own, new[] { "V" })]
    [InlineData(DataScope.Team, new[] { "V", "E1" })]
    [InlineData(DataScope.Unit, new[] { "V", "E2" })]
    [InlineData(DataScope.UnitAndBelow, new[] { "V", "E1", "E2" })]
    [InlineData(DataScope.All, new[] { "V", "E1", "E2", "O" })]
    public async Task Each_scope_filters_the_list_in_the_database_and_hides_other_records_with_404(DataScope scope, string[] expected)
    {
        var world = await BuildAsync(scope);

        var page = await world.Viewer.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?pageSize=200");
        var visible = world.Items.Where(i => page!.Items.Any(x => x.Id == i.Value)).Select(i => i.Key).Order().ToArray();
        visible.ShouldBe(expected.Order().ToArray());
        if (scope != DataScope.All)
        {
            page!.Items.Count.ShouldBe(expected.Length, "nothing outside the scope leaks into the list");
        }

        foreach (var (owner, id) in world.Items)
        {
            var response = await world.Viewer.GetAsync($"{Items}/{id}");
            if (expected.Contains(owner))
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{owner}'s item is in the {scope} scope");
            }
            else
            {
                await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound", "errors.notFound");
            }
        }
    }

    [Fact]
    public async Task Readable_record_without_the_action_scope_answers_403_and_inside_it_succeeds()
    {
        var world = await BuildAsync(DataScope.All, updateScope: DataScope.Own);

        var colleagues = await world.Viewer.GetFromJsonAsync<SampleItemDto>($"{Items}/{world.Items["E2"]}");
        await (await world.Viewer.PutAsJsonAsync($"{Items}/{colleagues!.Id}", new { nameEn = "Not mine", version = colleagues.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden", "errors.forbidden");

        var mine = await world.Viewer.GetFromJsonAsync<SampleItemDto>($"{Items}/{world.Items["V"]}");
        (await world.Viewer.PutAsJsonAsync($"{Items}/{mine!.Id}", new { nameEn = "Mine", version = mine.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Several_roles_combine_their_scopes()
    {
        var world = await BuildAsync(DataScope.Own);
        await api.Factory.GrantRoleAsync(world.ViewerId, await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Team)));
        await api.Factory.GrantRoleAsync(world.ViewerId, await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Unit)));

        var page = await world.Viewer.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?pageSize=200");

        page!.Items.Select(i => i.Id).Order().ShouldBe(new[] { world.Items["V"], world.Items["E1"], world.Items["E2"] }.Order());
    }

    [Fact]
    public async Task Unit_and_below_follows_a_structure_change()
    {
        var world = await BuildAsync(DataScope.UnitAndBelow);
        (await world.Viewer.GetAsync($"{Items}/{world.Items["O"]}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B is moved under A: the outsider's item is now in the viewer's unit tree (cached grants refresh on change).
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var b = (await dispatcher.SendAsync(new GetUnit(world.UnitB))).Value;
            (await dispatcher.SendAsync(new MoveUnit(world.UnitB, world.UnitA, null, b.Version))).IsSuccess.ShouldBeTrue();
        }

        (await world.Viewer.GetAsync($"{Items}/{world.Items["O"]}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// ADR 0015: the unit scopes cover the unit the user is placed in and every unit they head. The viewer (placed in A)
    /// is appointed head of B, which has a child unit B1; "team" is unaffected (direct reports only).
    /// </summary>
    [Theory]
    [InlineData(DataScope.Unit, new[] { "V", "E2", "O" })]
    [InlineData(DataScope.UnitAndBelow, new[] { "V", "E1", "E2", "O", "P" })]
    [InlineData(DataScope.Team, new[] { "V", "E1" })]
    public async Task Unit_scopes_include_the_units_the_user_heads(DataScope scope, string[] expected)
    {
        var world = await BuildAsync(scope);
        Guid b1;
        await using (var system = TestAccess.SystemScope(api.Factory))
        {
            var dispatcher = system.ServiceProvider.GetRequiredService<IDispatcher>();
            var suffix = Guid.CreateVersion7().ToString("N")[^8..];
            b1 = (await dispatcher.SendAsync(new CreateUnit($"B1-{suffix}", "وحدة", $"B1 {suffix}", "section", world.UnitB, null, null, null, null))).Value.Id;
        }

        var creator = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Create, DataScope.All), (SamplePermissions.Read, DataScope.Own));
        var p = await api.Factory.CreateUserAsync(unitId: b1, roles: creator);
        world.Items["P"] = await CreateItemAsync(await api.Factory.SignInAsync(p), "P");

        await SetHeadAsync(world.UnitB, world.ViewerEmployeeId);
        (await VisibleAsync(world)).ShouldBe(expected.Order().ToArray());
        foreach (var (owner, id) in world.Items)
        {
            (await world.Viewer.GetAsync($"{Items}/{id}")).StatusCode
                .ShouldBe(expected.Contains(owner) ? HttpStatusCode.OK : HttpStatusCode.NotFound, $"{owner}'s item, {scope}");
        }

        // No longer head of B: B and B1 drop out of the unit scopes at once.
        await SetHeadAsync(world.UnitB, null);
        (await VisibleAsync(world)).ShouldNotContain("O");
        (await VisibleAsync(world)).ShouldNotContain("P");
    }

    private static async Task<string[]> VisibleAsync(World world)
    {
        var page = await world.Viewer.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?pageSize=200");
        return world.Items.Where(i => page!.Items.Any(x => x.Id == i.Value)).Select(i => i.Key).Order().ToArray();
    }

    private async Task SetHeadAsync(Guid unitId, Guid? headEmployeeId)
    {
        await using var system = TestAccess.SystemScope(api.Factory);
        var dispatcher = system.ServiceProvider.GetRequiredService<IDispatcher>();
        var unit = (await dispatcher.SendAsync(new GetUnit(unitId))).Value;
        var updated = await dispatcher.SendAsync(new UpdateUnit(
            unitId, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode, headEmployeeId, null, null, null, unit.Version));
        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
    }

    private async Task<World> BuildAsync(DataScope readScope, DataScope? updateScope = null)
    {
        Guid root, a, a1, b;
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var suffix = Guid.CreateVersion7().ToString("N")[^8..];
            async Task<Guid> Unit(string code, string level, Guid? parent) =>
                (await dispatcher.SendAsync(new CreateUnit($"{code}-{suffix}", "وحدة", $"{code} {suffix}", level, parent, null, null, null, null))).Value.Id;
            root = await Unit("ROOT", "organisation", null);
            a = await Unit("A", "sector", root);
            a1 = await Unit("A1", "department", a);
            b = await Unit("B", "department", root);
        }

        // Creators can create and see only their own items, so they never widen anyone else's view.
        var creator = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Create, DataScope.All), (SamplePermissions.Read, DataScope.Own));
        var permissions = new List<(string, DataScope)> { (SamplePermissions.Create, DataScope.All), (SamplePermissions.Read, readScope) };
        if (updateScope is { } update)
        {
            permissions.Add((SamplePermissions.Update, update));
        }

        var viewerRole = await api.Factory.CreateRoleAsync("sample", [.. permissions]);
        var viewer = await api.Factory.CreateUserAsync(unitId: a, roles: viewerRole);
        var e1 = await api.Factory.CreateUserAsync(unitId: a1, managerEmployeeId: viewer.EmployeeId, roles: creator);
        var e2 = await api.Factory.CreateUserAsync(unitId: a, roles: creator);
        var o = await api.Factory.CreateUserAsync(unitId: b, roles: creator);

        var items = new Dictionary<string, Guid>();
        var viewerClient = await api.Factory.SignInAsync(viewer);
        items["V"] = await CreateItemAsync(viewerClient, "V");
        foreach (var (name, user) in new[] { ("E1", e1), ("E2", e2), ("O", o) })
        {
            items[name] = await CreateItemAsync(await api.Factory.SignInAsync(user), name);
        }

        return new World(viewerClient, viewer.Id, viewer.EmployeeId!.Value, items, a, b);
    }

    private static async Task<Guid> CreateItemAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameEn = $"Item of {name}" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private sealed record World(HttpClient Viewer, Guid ViewerId, Guid ViewerEmployeeId, Dictionary<string, Guid> Items, Guid UnitA, Guid UnitB);
}
