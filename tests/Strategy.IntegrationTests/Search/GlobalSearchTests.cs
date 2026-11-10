using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Risk;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;
using Strategy.Platform.Search.Application;

namespace Strategy.IntegrationTests.Search;

/// <summary>Counts the calls of a contributor registered for the <c>sample</c> system (proves a system is not queried at all).</summary>
internal sealed class ProbeContributor(ProbeLog log) : ISearchContributor
{
    public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        log.Calls.Add((user.UserId() ?? Guid.Empty, query.Normalised));
        return Task.FromResult<IReadOnlyList<SearchHit>>([]);
    }
}

public sealed class ProbeLog
{
    public ConcurrentBag<(Guid User, string Term)> Calls { get; } = [];
}

public sealed class SearchFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ProbeLog Probe { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton(Probe);
            services.AddSearchContributor<ProbeContributor>("sample");
        });
        await Factory.GrantSystemAccessAsync(RiskPermissions.SystemCode, AccessGrants.Everyone, null);
        await Factory.GrantSystemAccessAsync(KpiPermissions.SystemCode, AccessGrants.Everyone, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>Global search (SPEC-ADDENDUM A1, ADR 0160): scope, system access, switched-off systems, Arabic normalisation, recent items, MCP.</summary>
public sealed class GlobalSearchTests(SearchFixture fx) : IClassFixture<SearchFixture>
{
    private const string Search = "/api/v1/search";
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task Records_outside_the_users_scope_are_never_returned_by_number_title_or_key_field_nor_counted()
    {
        var marker = Marker();
        var own = await fx.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var alice = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: own));
        var bob = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: own));
        var mine = await CreateItemAsync(alice, $"خطة الميزانية {marker}", $"Budget plan {marker} alpha");
        var theirs = await CreateItemAsync(bob, $"خطة الميزانية {marker} ب", $"Budget plan {marker} beta");

        var found = await SearchAsync(alice, marker);
        Ids(found).ShouldBe([mine.Id]);
        var group = found.GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("systemCode").GetString().ShouldBe("sample");
        group.GetProperty("nameEn").GetString().ShouldNotBeNullOrEmpty();
        group.GetProperty("items").GetArrayLength().ShouldBe(1, "the other user's item is not counted either");
        var item = group.GetProperty("items")[0];
        item.GetProperty("matchedOn").GetString().ShouldBe("title");
        item.GetProperty("link").GetString().ShouldBe($"/sample/items/{mine.Id}");
        item.GetProperty("number").GetString().ShouldBe(mine.Number);

        // By number: exact number first; the other user's number finds nothing.
        var byNumber = await SearchAsync(alice, mine.Number!.ToLowerInvariant());
        var first = byNumber.GetProperty("groups")[0].GetProperty("items")[0];
        first.GetProperty("id").GetGuid().ShouldBe(mine.Id);
        first.GetProperty("matchedOn").GetString().ShouldBe("number");
        first.GetProperty("score").GetDouble().ShouldBe(1.0);
        Ids(await SearchAsync(alice, theirs.Number!)).ShouldNotContain(theirs.Id);
        (await alice.GetAsync($"{Items}/{theirs.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Key field (risk event): coordinator B never finds department A's risk, by title, number or event text.
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var eventMarker = Marker();
        var values = JsonSerializer.SerializeToNode(RiskWorld.Values($"Data centre outage {marker}", w.DepartmentA, w.OwnerA, 4, 4))!.AsObject();
        values["eventText"] = $"Cooling failure {eventMarker} during the summer peak";
        var risk = await w.CreateAsync(values);
        (await w.C(w.CoordinatorB).GetAsync($"{RiskWorld.Base}/risks/{risk.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var byEvent = await SearchAsync(w.C(w.CoordinatorA), eventMarker);
        var hit = byEvent.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("systemCode").GetString() == "risk").GetProperty("items")[0];
        hit.GetProperty("id").GetGuid().ShouldBe(risk.Id);
        hit.GetProperty("matchedOn").GetString().ShouldBe("event");
        hit.GetProperty("matchedText").GetString()!.ShouldContain(eventMarker);
        foreach (var term in new[] { eventMarker, marker, risk.Number })
        {
            Ids(await SearchAsync(w.C(w.CoordinatorB), term)).ShouldNotContain(risk.Id, $"coordinator B searching '{term}'");
            Ids(await SearchAsync(w.C(w.Outsider), term)).ShouldNotContain(risk.Id, $"an outsider searching '{term}'");
        }
    }

    [Fact]
    public async Task A_system_the_user_cannot_open_or_that_is_switched_off_is_not_searched_at_all()
    {
        var marker = Marker();
        var editor = await fx.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
        var client = await fx.Factory.SignInAsync(editor);
        var item = await CreateItemAsync(client, $"بند {marker}", $"Item {marker}");
        Ids(await SearchAsync(client, marker)).ShouldBe([item.Id]);
        fx.Probe.Calls.ShouldContain(c => c.User == editor.Id && c.Term == marker);

        // No system access: the sample contributors are not even called.
        var stranger = await fx.Factory.CreateUserAsync();
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            (await scope.ServiceProvider.GetRequiredService<IUserRoleQuery>().OpenSystemsAsync(stranger.Id, stranger.EmployeeId)).ShouldNotContain("sample");
        }

        var strangerMarker = Marker();
        (await SearchAsync(await fx.Factory.SignInAsync(stranger), strangerMarker)).GetProperty("groups").GetArrayLength().ShouldBe(0);
        fx.Probe.Calls.ShouldNotContain(c => c.User == stranger.Id);

        // Switched off: not searched, recent items hidden; back on: found again.
        (await client.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = item.Id })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var admin = await fx.Factory.CreateSignedInClientAsync(admin: true);
        await SetSampleEnabledAsync(admin, false);
        try
        {
            var offMarker = Marker();
            await CreateItemDirectAsync(editor, offMarker);
            (await SearchAsync(client, marker)).GetProperty("groups").GetArrayLength().ShouldBe(0);
            (await SearchAsync(client, offMarker)).GetProperty("groups").GetArrayLength().ShouldBe(0);
            fx.Probe.Calls.ShouldNotContain(c => c.Term == offMarker);
            (await RecentAsync(client)).ShouldNotContain(item.Id);
        }
        finally
        {
            await SetSampleEnabledAsync(admin, true);
        }

        Ids(await SearchAsync(client, marker)).ShouldBe([item.Id]);
        (await RecentAsync(client)).ShouldContain(item.Id);

        // The system filter: only that system's groups.
        (await SearchAsync(client, marker, "&system=risk")).GetProperty("groups").GetArrayLength().ShouldBe(0);
        Ids(await SearchAsync(client, marker, "&system=sample")).ShouldBe([item.Id]);
    }

    [Theory]
    [InlineData("مدرسة أحمد الإبتدائيَّة", "مدرسه احمد")]
    [InlineData("مدرسة أحمد الإبتدائيَّة", "الابتدائيه")]
    [InlineData("مستشفى الأمل", "مستشفي الامل")]
    [InlineData("خطة ٢٠٢٦ للتحـــول", "خطة 2026 للتحول")]
    [InlineData("Annual BUDGET review", "budget REVIEW")]
    public async Task Arabic_text_is_normalised_on_both_sides(string stored, string typed)
    {
        var marker = Marker();
        var client = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor));
        var item = await CreateItemAsync(client, $"{stored} {marker}", null);

        Ids(await SearchAsync(client, $"{typed} {marker}")).ShouldBe([item.Id]);
    }

    [Fact]
    public async Task The_generated_column_matches_the_csharp_normalisation()
    {
        var samples = new[] { "أَحْمَد  إبراهيم ـ آمنة ٱلهدى", "مستشفى  الرحمة ١٢٣ ۴۵", "Strategy   PLAN 2026", "  مدرسة\tالنور  " };
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var source = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        foreach (var text in samples)
        {
            await using var command = source.CreateCommand($"SELECT {QuickSearch.Sql("x")} FROM (SELECT @t::text AS x) s");
            command.Parameters.AddWithValue("t", text);
            ((string)(await command.ExecuteScalarAsync())!).ShouldBe(SearchText.Normalise(text), text);
        }
    }

    [Fact]
    public async Task Recent_items_are_resolved_again_on_read_and_drop_a_record_after_access_is_revoked()
    {
        var marker = Marker();
        var own = await fx.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var readAll = await fx.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.All));
        var bob = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: own));
        var carolUser = await fx.Factory.CreateUserAsync(roles: [own, readAll]);
        var carol = await fx.Factory.SignInAsync(carolUser);
        var outsider = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: own));
        var bobs = await CreateItemAsync(bob, $"بند {marker}", $"Bob item {marker}");
        var carols = await CreateItemAsync(carol, $"بند {marker} ج", $"Carol item {marker}");

        // A record the caller may not see is refused (404) and not stored.
        await (await outsider.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = bobs.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound", "errors.notFound");
        (await RecentAsync(outsider)).ShouldBeEmpty();

        (await carol.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = bobs.Id })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await carol.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = carols.Id })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecentAsync(carol)).ShouldBe([carols.Id, bobs.Id]);
        (await carol.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = bobs.Id })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecentAsync(carol)).ShouldBe([bobs.Id, carols.Id], "a new visit moves the record to the top, without a duplicate");

        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            var code = readAll[(readAll.IndexOf('.', StringComparison.Ordinal) + 1)..];
            var roleId = (await db.Roles.SingleAsync(r => r.SystemCode == "sample" && r.Code == code)).Id;
            var assignment = await db.Assignments.SingleAsync(a => a.RoleId == roleId && a.PrincipalId == carolUser.Id);
            (await scope.ServiceProvider.GetRequiredService<AccessAdministration>().EndAssignmentAsync(assignment.Id, null, assignment.Version, CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
        }

        (await RecentAsync(carol)).ShouldBe([carols.Id], "Bob's item is no longer readable for Carol");
        Ids(await SearchAsync(carol, marker)).ShouldBe([carols.Id]);

        // At most 20 per user: the oldest drop out.
        var many = new List<Guid>();
        for (var i = 0; i < 22; i++)
        {
            var created = await CreateItemAsync(carol, null, $"Recent {marker} {i}");
            many.Add(created.Id);
            (await carol.PostAsJsonAsync($"{Search}/recent", new { type = SamplePermissions.ResourceType, id = created.Id })).EnsureSuccessStatusCode();
        }

        var recent = await RecentAsync(carol);
        recent.Count.ShouldBe(20);
        recent[0].ShouldBe(many[^1]);
        recent.ShouldNotContain(carols.Id);
    }

    [Fact]
    public async Task Validation_problems_carry_message_keys_and_vendors_are_refused()
    {
        var client = await fx.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);

        await (await client.GetAsync($"{Search}?q=a")).ShouldBeValidationProblemAsync("q", "validation.minimumLength");
        await (await client.GetAsync($"{Search}?q=%D9%8E%D9%8E")).ShouldBeValidationProblemAsync("q", "validation.minimumLength"); // diacritics only
        await (await client.GetAsync($"{Search}?q={new string('x', 201)}")).ShouldBeValidationProblemAsync("q", "validation.maximumLength");
        await (await client.GetAsync($"{Search}?q=budget&limit=51")).ShouldBeValidationProblemAsync("limit", "validation.inclusiveBetween");
        await (await client.PostAsJsonAsync($"{Search}/recent", new { type = "unknown.type", id = Guid.CreateVersion7() }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        (await (await fx.Factory.CreateSpaClientAsync()).GetAsync($"{Search}?q=budget")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A vendor (Wasel) principal is refused by the service itself, whatever channel reaches it.
        var vendor = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()), new Claim(StrategyClaims.PrincipalType, StrategyClaims.VendorPrincipal)], "wasel"));
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Wasel, vendor);
        var search = scope.ServiceProvider.GetRequiredService<SearchService>();
        (await search.SearchAsync(vendor, "budget", null, null, CancellationToken.None)).Error!.Kind.ShouldBe(Strategy.Platform.Kernel.Results.ErrorKind.Forbidden);
        (await search.RecentAsync(vendor, CancellationToken.None)).Error!.Kind.ShouldBe(Strategy.Platform.Kernel.Results.ErrorKind.Forbidden);
        (await search.VisitAsync(vendor, new RecentVisitInput(SamplePermissions.ResourceType, Guid.CreateVersion7()), CancellationToken.None)).Error!.Kind
            .ShouldBe(Strategy.Platform.Kernel.Results.ErrorKind.Forbidden);
        var tool = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == "search");
        (await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { query = "budget" }), new ToolContext(vendor, Channels.Mcp, "en"), CancellationToken.None))
            .ErrorCode.ShouldBe(ToolErrors.Forbidden);
    }

    [Fact]
    public async Task The_mcp_search_tool_is_a_default_enabled_read_tool_with_the_users_permissions()
    {
        var marker = Marker();
        var own = await fx.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var aliceUser = await fx.Factory.CreateUserAsync(roles: own);
        var alice = await fx.Factory.SignInAsync(aliceUser);
        var bob = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(roles: own));
        var mine = await CreateItemAsync(alice, null, $"Tool item {marker}");
        var theirs = await CreateItemAsync(bob, null, $"Tool item {marker} b");

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, aliceUser.Id.ToString()),
                new Claim(ClaimTypes.Name, aliceUser.UserName),
                new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
                new Claim(StrategyClaims.EmployeeId, aliceUser.EmployeeId!.Value.ToString()),
            ], "test"));
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Mcp, principal);
        var tool = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == "search");
        tool.Definition.Kind.ShouldBe(ToolKind.Read);
        tool.Definition.DefaultEnabled.ShouldBeTrue();

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { query = marker }), new ToolContext(principal, Channels.Mcp, "en"), CancellationToken.None);
        result.Success.ShouldBeTrue();
        var json = JsonSerializer.Serialize(result.Data);
        json.ShouldContain(mine.Id.ToString());
        json.ShouldNotContain(theirs.Id.ToString());
        (await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { query = "x" }), new ToolContext(principal, Channels.Mcp, "en"), CancellationToken.None))
            .ErrorCode.ShouldBe(ToolErrors.Validation);
    }

    /// <summary>The service swallows a failing contributor (never a broken search box): prove here that every module's query runs.</summary>
    [Fact]
    public async Task Every_module_contributor_runs_its_query_without_error()
    {
        var admin = await fx.Factory.CreateUserAsync(admin: true, roles: TestIdentity.SampleEditor);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new Claim(ClaimTypes.Name, admin.UserName),
                new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
                new Claim(StrategyClaims.EmployeeId, admin.EmployeeId!.Value.ToString()),
            ], "test"));
        var registrations = fx.Factory.Services.GetServices<SearchContributorRegistration>().Where(r => r.ContributorType != typeof(ProbeContributor)).ToList();
        registrations.Select(r => r.SystemCode).Distinct().Count().ShouldBeGreaterThanOrEqualTo(12);
        foreach (var system in registrations.Select(r => r.SystemCode).OfType<string>().Distinct())
        {
            await fx.Factory.GrantSystemAccessAsync(system, AccessGrants.User, admin.Id);
        }
        foreach (var registration in registrations)
        {
            foreach (var term in new[] { "ab", "خطة 2026", "x_%\\" })
            {
                await using var scope = fx.Factory.Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Ui, principal);
                var contributor = (ISearchContributor)scope.ServiceProvider.GetRequiredService(registration.ContributorType);
                await Should.NotThrowAsync(() => contributor.SearchAsync(new SearchQuery(term, SearchText.Normalise(term), 5), principal, CancellationToken.None),
                    $"{registration.ContributorType.Name} '{term}'");
            }
        }
    }

    private static string Marker() => "q" + Guid.CreateVersion7().ToString("N")[^8..];

    private static async Task<SampleItemDto> CreateItemAsync(HttpClient client, string? nameAr, string? nameEn)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameAr, nameEn });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }

    /// <summary>Creates an item while the sample system is off (the API refuses then): through the module's DbContext.</summary>
    private async Task CreateItemDirectAsync(TestUser owner, string marker)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<Strategy.Modules.Sample.Infrastructure.SampleDbContext>();
        db.Items.Add(Strategy.Modules.Sample.Domain.SampleItem.Create(null, "Hidden " + marker, null, null, null, owner.EmployeeId, owner.UnitId));
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> SearchAsync(HttpClient client, string term, string extra = "")
    {
        var response = await client.GetAsync($"{Search}?q={Uri.EscapeDataString(term)}&limit=50{extra}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<Guid> Ids(JsonElement results) =>
        results.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("id").GetGuid()).ToList();

    private static async Task<List<Guid>> RecentAsync(HttpClient client)
    {
        var response = await client.GetAsync($"{Search}/recent");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(r => r.GetProperty("item").GetProperty("id").GetGuid()).ToList();
    }

    private static async Task SetSampleEnabledAsync(HttpClient admin, bool enabled)
    {
        var systems = await admin.GetFromJsonAsync<JsonElement>("/api/v1/access/systems");
        var sample = systems.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "sample");
        (await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled, version = sample.GetProperty("version").GetUInt32() })).EnsureSuccessStatusCode();
    }
}
