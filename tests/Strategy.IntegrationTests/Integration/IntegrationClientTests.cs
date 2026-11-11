using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Feeds;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Integration.Webhooks;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling.Application;

namespace Strategy.IntegrationTests.Integration;

/// <summary>
/// Phase 22 (docs/design/integrations.md, ADR 0190–0195): integration clients read only their allowed systems and data scope through OData
/// and extracts; expired / revoked clients and credentials are refused; IP allow-list and rate limit; OAuth client credentials; OData query
/// options; the deleted feed; signed webhooks with retries, dead letters and replay on the fake clock; the SSRF guard on target URLs.
/// </summary>
public sealed class IntegrationClientTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Clients = "/api/v1/integration/clients";
    private readonly TravellingClock _clock = new();
    private readonly RecordingTransport _transport = new();
    private readonly string _exportRoot = Path.Combine(Path.GetTempPath(), "strategy-exports-" + Guid.CreateVersion7().ToString("N"));
    private ApiFactory _factory = null!;
    private HttpClient? _admin;

    public async ValueTask InitializeAsync() =>
        _factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?>
        {
            ["Integration:AllowHttpWebhooks"] = "true",
            ["Integration:ExportRoot"] = _exportRoot,
        }, configureServices: services =>
        {
            services.AddSingleton<IClock>(_clock);
            services.RemoveAll<IWebhookTransport>();
            services.AddSingleton<IWebhookTransport>(_transport);
            services.AddReportableRecordSource<IntegrationTestFeeds>();
            services.AddWebhookEvent<TestItemChanged>("tests.item.changed", "sample", "تغيّر عنصر", "Item changed", "sample.item", SamplePermissions.Read,
                e => e.ItemId, new TestItemChanged(Guid.Empty, "renamed", DateTimeOffset.UnixEpoch));
        });

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_exportRoot))
        {
            Directory.Delete(_exportRoot, recursive: true);
        }
    }

    private async Task<HttpClient> AdminAsync() => _admin ??= await _factory.CreateSignedInClientAsync(admin: true);

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];

    /// <summary>A unit with an editor in it; returns the unit and a client signed in as the editor.</summary>
    private async Task<(Guid Unit, HttpClient Editor, TestUser User)> UnitWithEditorAsync()
    {
        var user = await _factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
        return (user.UnitId!.Value, await _factory.SignInAsync(user), user);
    }

    private static async Task<(Guid Id, uint Version)> ItemAsync(HttpClient editor, string name, decimal? amount = null)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = name, amount });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var item = (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
        return (item.Id, item.Version);
    }

    private async Task<CreatedClientDto> ClientAsync(
        IReadOnlyList<string> systems, Guid? actingUnit, string kind = "api_key", int rate = 1000, IReadOnlyList<string>? ips = null,
        IReadOnlyList<string>? scopes = null, IReadOnlyList<string>? types = null, params (string Permission, DataScope Scope)[] permissions)
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync(Clients, new ClientInput(null, "Power BI " + Suffix(), null, kind, actingUnit, scopes ?? ["feeds"], systems,
            types ?? [], ips ?? [], rate, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<CreatedClientDto>(JsonSerializerOptions.Web))!;
        if (permissions.Length > 0)
        {
            var roleKey = await _factory.CreateRoleAsync("sample", permissions);
            var code = roleKey[(roleKey.IndexOf('.', StringComparison.Ordinal) + 1)..];
            var options = await admin.GetFromJsonAsync<List<JsonElement>>($"{Clients}/{created.Client.Id}/roles", JsonSerializerOptions.Web);
            var roleId = options!.Single(o => o.GetProperty("code").GetString() == code).GetProperty("roleId").GetGuid();
            var set = await admin.PutAsJsonAsync($"{Clients}/{created.Client.Id}/roles", new ClientRolesInput([roleId]));
            set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        }

        return created;
    }

    private HttpClient Basic(CreatedClientDto client) => Basic(client.Client.ClientId, client.Secret);

    private HttpClient Basic(string clientId, string key)
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{key}")));
        return http;
    }

    private HttpClient Bearer(string token)
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonDocument.Parse(body).RootElement;
    }

    private static List<string?> Names(JsonElement feed) => feed.GetProperty("value").EnumerateArray().Select(v => v.GetProperty("nameEn").GetString()).ToList();

    [Fact]
    public async Task A_client_reads_only_its_allowed_systems_and_unit_scope_through_odata_and_extracts()
    {
        var (unitA, editorA, _) = await UnitWithEditorAsync();
        var (_, editorB, _) = await UnitWithEditorAsync();
        var tag = Suffix();
        await ItemAsync(editorA, $"A1 {tag}", 10m);
        await ItemAsync(editorA, $"A2 {tag}", 20m);
        await ItemAsync(editorB, $"B1 {tag}", 30m);
        var client = await ClientAsync(["sample"], unitA, permissions: (SamplePermissions.Read, DataScope.Unit));
        var http = Basic(client);

        // Service document and metadata: only the sample system, and the amount field (needs sample.item.update) is not offered.
        var service = await JsonAsync(await http.GetAsync("/odata/v1/sample"));
        service.GetProperty("value").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ShouldContain("Items");
        var metadata = await http.GetStringAsync("/odata/v1/sample/$metadata");
        var start = metadata.IndexOf("<EntityType Name=\"Item\">", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);
        var itemType = metadata[start..metadata.IndexOf("</EntityType>", start, StringComparison.Ordinal)];
        itemType.ShouldContain("Name=\"statusLabel\"");
        itemType.ShouldNotContain("Name=\"amount\"");
        (await http.GetAsync("/odata/v1/sample/Items?$select=amount")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await http.GetAsync("/odata/v1/portfolio")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Rows: unit A only; B's item never returned, not even when asked for by name.
        var feed = await JsonAsync(await http.GetAsync($"/odata/v1/sample/Items?$filter=contains(nameEn,'{tag}')&$orderby=nameEn&lang=ar"));
        Names(feed).ShouldBe([$"A1 {tag}", $"A2 {tag}"]);
        feed.GetProperty("value")[0].GetProperty("status").GetString().ShouldBe("active");
        feed.GetProperty("value")[0].GetProperty("statusLabel").GetString().ShouldBe("نشط");
        Names(await JsonAsync(await http.GetAsync($"/odata/v1/sample/Items?$filter=nameEn eq 'B1 {tag}'"))).ShouldBeEmpty();

        // Extracts: the same scope.
        var extract = await JsonAsync(await http.GetAsync("/api/v1/integration/extracts/sample/Items"));
        var names = extract.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nameEn").GetString()).ToList();
        names.ShouldContain($"A1 {tag}");
        names.ShouldNotContain($"B1 {tag}");
        (await http.GetAsync("/api/v1/integration/extracts/portfolio/Projects")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A client of another system sees nothing of sample; a person cannot use the feeds; no credentials → 401 with the Basic challenge.
        var other = await ClientAsync(["portfolio"], unitA);
        (await Basic(other).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editorA.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var anonymous = await _factory.CreateClient().GetAsync("/odata/v1/sample/Items");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.Select(h => h.Scheme).ShouldContain("Basic");

        // A client never reaches the REST API without api.read, nor writes, nor the client administration.
        (await http.GetAsync("/api/v1/sample/items")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await http.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Every feed call is audited with channel api and logged in the usage log.
        await using var scope = TestAccess.SystemScope(_factory);
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        (await audit.Entries.CountAsync(e => e.Action == "integration.feed.query" && e.Channel == "api")).ShouldBeGreaterThan(0);
        var usage = await scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().Usage.Where(u => u.ClientRef == client.Client.Id).ToListAsync();
        usage.ShouldContain(u => u.Path == "/odata/v1/sample/Items" && u.StatusCode == 200 && u.Rows == 2);
    }

    [Fact]
    public async Task OData_filter_select_orderby_expand_paging_and_count_are_correct_and_permission_filtered()
    {
        var (unitA, editorA, _) = await UnitWithEditorAsync();
        var tag = Suffix();
        for (var i = 1; i <= 5; i++)
        {
            await ItemAsync(editorA, $"P{i} {tag}", i * 100m);
        }

        // Read: unit scope. Update: own scope — the client owns nothing, so the "ownItems" expansion is always empty.
        var client = await ClientAsync(["sample"], unitA, permissions: [(SamplePermissions.Read, DataScope.Unit), (SamplePermissions.Update, DataScope.Own)]);
        var http = Basic(client);
        var filter = $"$filter=startswith(nameEn,'P') and contains(nameEn,'{tag}')";

        // $filter with comparisons, in, not, or; $orderby desc; $select (+ label keeps its code); $count.
        var feed = await JsonAsync(await http.GetAsync($"/odata/v1/sample/TestItems?{filter} and amount ge 200 and amount lt 500&$orderby=amount desc&$count=true&$select=nameEn,amount,statusLabel"));
        Names(feed).ShouldBe([$"P4 {tag}", $"P3 {tag}", $"P2 {tag}"]);
        feed.GetProperty("@odata.count").GetInt64().ShouldBe(3);
        var first = feed.GetProperty("value")[0];
        first.GetProperty("amount").GetDecimal().ShouldBe(400m);
        first.GetProperty("status").GetString().ShouldBe("active");
        first.TryGetProperty("number", out _).ShouldBeFalse();
        Names(await JsonAsync(await http.GetAsync($"/odata/v1/sample/TestItems?$filter=nameEn in ('P1 {tag}','P5 {tag}')&$orderby=nameEn")))
            .ShouldBe([$"P1 {tag}", $"P5 {tag}"]);
        Names(await JsonAsync(await http.GetAsync($"/odata/v1/sample/TestItems?{filter} and not (amount le 400 or amount eq null)"))).ShouldBe([$"P5 {tag}"]);

        // Server paging: $top 4 with a page size of 2 → two pages through @odata.nextLink.
        await SetPageSizeAsync(2);
        var page1 = await JsonAsync(await http.GetAsync($"/odata/v1/sample/TestItems?{filter}&$orderby=amount&$top=4"));
        Names(page1).ShouldBe([$"P1 {tag}", $"P2 {tag}"]);
        var next = page1.GetProperty("@odata.nextLink").GetString()!;
        var page2 = await JsonAsync(await http.GetAsync(new Uri(next).PathAndQuery));
        Names(page2).ShouldBe([$"P3 {tag}", $"P4 {tag}"]);
        page2.TryGetProperty("@odata.nextLink", out _).ShouldBeFalse();
        await SetPageSizeAsync(1000);

        // $expand: same-unit items (readable) and own items (none readable for the client).
        var expanded = await JsonAsync(await http.GetAsync(
            $"/odata/v1/sample/TestItems?$filter=nameEn eq 'P1 {tag}'&$expand=unitItems($select=nameEn;$filter=contains(nameEn,'{tag}');$orderby=nameEn),ownItems"));
        var row = expanded.GetProperty("value").EnumerateArray().Single();
        row.GetProperty("unitItems").EnumerateArray().Select(c => c.GetProperty("nameEn").GetString()).ShouldBe(Enumerable.Range(1, 5).Select(i => $"P{i} {tag}"));
        row.GetProperty("ownItems").GetArrayLength().ShouldBe(0);

        // Single entity, $count path, unsupported options.
        var id = row.GetProperty("id").GetGuid();
        (await JsonAsync(await http.GetAsync($"/odata/v1/sample/TestItems({id})"))).GetProperty("nameEn").GetString().ShouldBe($"P1 {tag}");
        (await http.GetStringAsync($"/odata/v1/sample/TestItems/$count?{filter}")).ShouldBe("5");
        (await http.GetAsync("/odata/v1/sample/TestItems?$apply=aggregate(amount with sum as total)")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await http.GetAsync("/odata/v1/sample/TestItems?$filter=statusLabel eq 'x'")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await http.GetAsync($"/odata/v1/sample/TestItems({Guid.CreateVersion7()})")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task SetPageSizeAsync(int size)
    {
        var admin = await AdminAsync();
        var current = (await admin.GetFromJsonAsync<IntegrationSettingsDto>("/api/v1/integration/settings", JsonSerializerOptions.Web))!;
        var saved = await admin.PutAsJsonAsync("/api/v1/integration/settings", new IntegrationSettingsInput(current.Settings with { ODataPageSize = size }, current.Version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_revoked_clients_and_ended_credentials_are_refused_and_rotation_overlaps()
    {
        var (unit, _, _) = await UnitWithEditorAsync();
        var admin = await AdminAsync();
        var client = await ClientAsync(["sample"], unit, permissions: (SamplePermissions.Read, DataScope.Unit));
        (await Basic(client).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Basic(client.Client.ClientId, client.Secret[..^1] + "a").GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Bearer(client.Secret).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Rotation with overlap: both keys work until the overlap ends; then only the new one.
        var rotated = await JsonAsync(await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/credentials/rotate", new ClientRotateInput(2)));
        var newKey = rotated.GetProperty("secret").GetString()!;
        (await Basic(client.Client.ClientId, newKey).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Basic(client).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _clock.Offset = TimeSpan.FromDays(3);
        try
        {
            (await Basic(client).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await Basic(client.Client.ClientId, newKey).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // The client expires (default 365 days).
            _clock.Offset = TimeSpan.FromDays(400);
            (await Basic(client.Client.ClientId, newKey).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            _clock.Offset = TimeSpan.Zero;
        }

        // Revoked: refused at once, for good, and logged with the reason.
        var dto = (await admin.GetFromJsonAsync<ClientDto>($"{Clients}/{client.Client.Id}", JsonSerializerOptions.Web))!;
        (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/revoke", new ClientRevokeInput("contract ended", dto.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Basic(client.Client.ClientId, newKey).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/credentials/rotate", new ClientRotateInput(0))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await using var scope = TestAccess.SystemScope(_factory);
        var usage = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().Usage;
        (await usage.CountAsync(u => u.ClientRef == client.Client.Id && u.RefusedReason == "revoked")).ShouldBe(1);
        (await usage.CountAsync(u => u.ClientRef == client.Client.Id && u.RefusedReason == "credential")).ShouldBe(1);
    }

    [Fact]
    public async Task Rate_limit_and_ip_allow_list_are_enforced()
    {
        var (unit, _, _) = await UnitWithEditorAsync();
        var limited = Basic(await ClientAsync(["sample"], unit, rate: 2, permissions: (SamplePermissions.Read, DataScope.Unit)));
        _clock.Frozen = DateTimeOffset.UtcNow;
        try
        {
            (await limited.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await limited.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
            var third = await limited.GetAsync("/odata/v1/sample/Items");
            third.StatusCode.ShouldBe((HttpStatusCode)429);
            third.Headers.RetryAfter.ShouldNotBeNull();
            _clock.Frozen = _clock.Frozen.Value.AddMinutes(1);
            (await limited.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            _clock.Frozen = null;
        }

        // The test server has no remote address: an allow-list that does not include it refuses.
        var fenced = Basic(await ClientAsync(["sample"], unit, ips: ["10.20.30.0/24"], permissions: (SamplePermissions.Read, DataScope.Unit)));
        (await fenced.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Bad entries are refused on save.
        var admin = await AdminAsync();
        await (await admin.PostAsJsonAsync(Clients, new ClientInput(null, "Bad", null, "api_key", null, ["feeds"], ["sample"], [], ["10.0.0.0/33"], 10, null)))
            .ShouldBeValidationProblemAsync("ipAllowList", "integration.errors.ipEntry");
    }

    [Fact]
    public async Task OAuth_client_credentials_tokens_read_the_feeds_and_nothing_else()
    {
        var (unit, editor, _) = await UnitWithEditorAsync();
        var tag = Suffix();
        await ItemAsync(editor, $"O1 {tag}");
        var client = await ClientAsync(["sample"], unit, kind: "oauth", permissions: (SamplePermissions.Read, DataScope.Unit));
        client.Client.ClientId.ShouldStartWith("ic_");
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var token = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client.Client.ClientId,
            ["client_secret"] = client.Secret,
            ["scope"] = "feeds",
        }));
        var tokenJson = await JsonAsync(token);
        var access = tokenJson.GetProperty("access_token").GetString()!;

        var bearer = Bearer(access);
        Names(await JsonAsync(await bearer.GetAsync($"/odata/v1/sample/Items?$filter=contains(nameEn,'{tag}')"))).ShouldBe([$"O1 {tag}"]);
        (await bearer.GetAsync("/mcp")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bearer.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A wrong secret or a scope the client does not hold gets no token; the API-key form of an OAuth secret is not an API key.
        (await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client.Client.ClientId,
            ["client_secret"] = "sis_wrong",
            ["scope"] = "feeds",
        }))).IsSuccessStatusCode.ShouldBeFalse();
        (await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client.Client.ClientId,
            ["client_secret"] = client.Secret,
            ["scope"] = "api.read",
        }))).IsSuccessStatusCode.ShouldBeFalse();
        (await Basic(client).GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Revoking the client invalidates its token at once; the MCP client list never shows integration clients.
        var admin = await AdminAsync();
        var mcpClients = await admin.GetStringAsync("/api/v1/mcp/clients");
        mcpClients.ShouldNotContain(client.Client.ClientId);
        var dto = (await admin.GetFromJsonAsync<ClientDto>($"{Clients}/{client.Client.Id}", JsonSerializerOptions.Web))!;
        (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/revoke", new ClientRevokeInput("rotated vendor", dto.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bearer.GetAsync("/odata/v1/sample/Items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changed_since_extracts_page_with_a_cursor_and_the_deleted_feed_lists_deactivated_records()
    {
        var (unit, editor, _) = await UnitWithEditorAsync();
        var tag = Suffix();
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var items = new List<(Guid Id, uint Version)>();
        for (var i = 1; i <= 3; i++)
        {
            items.Add(await ItemAsync(editor, $"E{i} {tag}"));
        }

        var http = Basic(await ClientAsync(["sample"], unit, permissions: (SamplePermissions.Read, DataScope.Unit)));
        var since = Uri.EscapeDataString(started.ToString("O"));

        // Changed since, one row per page: the cursor walks the three records in (modifiedAt, id) order without gaps or repeats.
        var seen = new List<string?>();
        string? cursor = null;
        do
        {
            var page = await JsonAsync(await http.GetAsync($"/api/v1/integration/extracts/sample/Items?changedSince={since}&pageSize=1"
                + (cursor is null ? string.Empty : "&cursor=" + cursor)));
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nameEn").GetString()));
            cursor = page.GetProperty("hasMore").GetBoolean() ? page.GetProperty("nextCursor").GetString() : null;
        }
        while (cursor is not null);

        seen.Where(n => n!.EndsWith(tag, StringComparison.Ordinal)).ShouldBe([$"E1 {tag}", $"E2 {tag}", $"E3 {tag}"]);

        // Deactivate E2: it appears in the deleted feed (and still in the changed feed with its new status).
        var deactivated = await editor.PostAsJsonAsync($"/api/v1/sample/items/{items[1].Id}/deactivate", new { version = items[1].Version });
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());
        var deleted = await JsonAsync(await http.GetAsync($"/api/v1/integration/extracts/sample/Items/deleted?changedSince={since}"));
        var deletedRows = deleted.GetProperty("items").EnumerateArray().ToList();
        deletedRows.Select(r => r.GetProperty("id").GetGuid()).ShouldContain(items[1].Id);
        deletedRows.Select(r => r.GetProperty("id").GetGuid()).ShouldNotContain(items[0].Id);
        deletedRows.Single(r => r.GetProperty("id").GetGuid() == items[1].Id).GetProperty("status").GetString().ShouldBe("inactive");

        // CSV with labels and the cursor headers.
        var csv = await http.GetAsync($"/api/v1/integration/extracts/sample/Items?changedSince={since}&format=csv&lang=ar");
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var text = await csv.Content.ReadAsStringAsync();
        text.ShouldContain("statusLabel");
        text.ShouldContain("غير نشط");
        csv.Headers.Contains("X-Watermark").ShouldBeTrue();
        (await http.GetAsync("/api/v1/integration/extracts/sample/Items?cursor=not-a-cursor")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhooks_are_signed_retried_with_backoff_dead_lettered_replayed_and_only_for_readable_records()
    {
        var (unitA, editorA, _) = await UnitWithEditorAsync();
        var (_, editorB, _) = await UnitWithEditorAsync();
        var inScope = await ItemAsync(editorA, "W-A " + Suffix());
        var outOfScope = await ItemAsync(editorB, "W-B " + Suffix());
        var client = await ClientAsync(["sample"], unitA, permissions: (SamplePermissions.Read, DataScope.Unit));
        var admin = await AdminAsync();

        // SSRF guard: link-local / cloud metadata targets are refused on save; unknown events too.
        foreach (var url in new[] { "http://169.254.169.254/latest/meta-data", "http://[fe80::1]/hook" })
        {
            await (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/webhooks", new WebhookSubscriptionInput("Bad", url, ["tests.item.changed"], true)))
                .ShouldBeValidationProblemAsync("targetUrl", "validation.outboundUrlRefused");
        }

        await (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/webhooks", new WebhookSubscriptionInput("Bad", "https://example.test/hook", ["kpi.nope"], true)))
            .ShouldBeValidationProblemAsync("events", "integration.errors.webhookEvent");
        var created = await JsonAsync(await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/webhooks",
            new WebhookSubscriptionInput("ERP", "https://erp.example.test/hooks/strategy", ["tests.item.changed"], true)));
        var secret = created.GetProperty("secret").GetString()!;

        // Two events: only the record the client may read becomes a delivery (published twice: outbox retries never duplicate).
        await PublishAsync(new TestItemChanged(inScope.Id, "renamed", DateTimeOffset.UnixEpoch.AddDays(1)));
        await PublishAsync(new TestItemChanged(inScope.Id, "renamed", DateTimeOffset.UnixEpoch.AddDays(1)));
        await PublishAsync(new TestItemChanged(outOfScope.Id, "renamed", DateTimeOffset.UnixEpoch.AddDays(1)));
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var deliveries = await scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().Deliveries.Where(d => d.ClientRef == client.Client.Id).ToListAsync();
            deliveries.Count.ShouldBe(1);
            deliveries[0].ResourceId.ShouldBe(inScope.Id);
        }

        // Attempt 1 fails (503), retried after the back-off (60 s), attempt 2 succeeds; signature verifies with the shown secret.
        _clock.Frozen = DateTimeOffset.UtcNow;
        try
        {
            _transport.Responses.Enqueue(HttpStatusCode.ServiceUnavailable);
            _transport.Responses.Enqueue(HttpStatusCode.OK);
            (await DispatchAsync()).ShouldBe((0, 1));
            (await DispatchAsync()).ShouldBe((0, 0)); // not due yet
            _clock.Frozen = _clock.Frozen.Value.AddSeconds(61);
            (await DispatchAsync()).ShouldBe((1, 0));
            var sent = _transport.Sent.Last();
            sent.Url.ShouldBe("https://erp.example.test/hooks/strategy");
            WebhookSigner.Verify(secret, sent.Headers[WebhookSigner.TimestampHeader], sent.Headers[WebhookSigner.SignatureHeader], sent.Body, _clock.UtcNow, TimeSpan.FromMinutes(5))
                .ShouldBeTrue();
            WebhookSigner.Verify("whsec_wrong", sent.Headers[WebhookSigner.TimestampHeader], sent.Headers[WebhookSigner.SignatureHeader], sent.Body, _clock.UtcNow, TimeSpan.FromMinutes(5))
                .ShouldBeFalse();
            var payload = JsonDocument.Parse(sent.Body).RootElement;
            payload.GetProperty("type").GetString().ShouldBe("tests.item.changed");
            payload.GetProperty("resource").GetProperty("id").GetGuid().ShouldBe(inScope.Id);
            payload.GetProperty("data").GetProperty("change").GetString().ShouldBe("renamed");

            // A failing receiver: 8 attempts with growing delays, then the dead-letter list; replay delivers it.
            var subscriptionId = created.GetProperty("subscription").GetProperty("id").GetGuid();
            (await admin.PostAsync($"{Clients}/{client.Client.Id}/webhooks/{subscriptionId}/test", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            var delays = new List<TimeSpan>();
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                _transport.Responses.Enqueue(HttpStatusCode.InternalServerError);
                (await DispatchAsync()).ShouldBe((0, 1));
                var next = await NextAttemptAsync(client.Client.Id);
                if (next is { } at)
                {
                    delays.Add(at - _clock.UtcNow);
                    _clock.Frozen = at;
                }
            }

            delays.Select(d => (int)Math.Round(d.TotalSeconds)).ShouldBe([60, 120, 240, 480, 960, 1920, 3840]);
        }
        finally
        {
            _clock.Frozen = null;
        }

        // Back on the real clock (the administrator's session would have idled out): dead-letter list, attempt log, replay.
        var dead = await JsonAsync(await admin.GetAsync($"/api/v1/integration/webhooks/deliveries?clientId={client.Client.Id}&status=dead"));
        var deadId = dead.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var detail = await JsonAsync(await admin.GetAsync($"/api/v1/integration/webhooks/deliveries/{deadId}"));
        detail.GetProperty("attempts").GetArrayLength().ShouldBe(8);
        (await admin.PostAsJsonAsync("/api/v1/integration/webhooks/deliveries/replay", new WebhookReplayInput([deadId]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        _transport.Responses.Enqueue(HttpStatusCode.NoContent);
        (await DispatchAsync()).ShouldBe((1, 0));

        // The catalogue offers the event with a sample payload for the developer portal.
        var events = await JsonAsync(await admin.GetAsync("/api/v1/integration/webhooks/events"));
        events.EnumerateArray().Single(e => e.GetProperty("code").GetString() == "tests.item.changed").GetProperty("samplePayload").GetProperty("type").GetString()
            .ShouldBe("tests.item.changed");
    }

    [Fact]
    public async Task Scheduled_exports_write_the_changes_since_the_last_run_into_the_export_folder_as_the_client()
    {
        var (unit, editor, _) = await UnitWithEditorAsync();
        var (_, other, _) = await UnitWithEditorAsync();
        var tag = Suffix();
        await ItemAsync(editor, $"X1 {tag}");
        await ItemAsync(other, $"Y1 {tag}");
        var client = await ClientAsync(["sample"], unit, permissions: (SamplePermissions.Read, DataScope.Unit));
        var admin = await AdminAsync();
        await (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/exports", new ExportScheduleInput("Nightly", ["sample.item"], "json", "../etc", 24, true)))
            .ShouldBeValidationProblemAsync("folder", "integration.errors.exportFolder");
        (await admin.PostAsJsonAsync($"{Clients}/{client.Client.Id}/exports", new ExportScheduleInput("Nightly", ["sample.item"], "json", "bi/" + tag, 24, true)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = TestAccess.SystemScope(_factory))
        {
            (await scope.ServiceProvider.GetRequiredService<ScheduledExports>().RunDueAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
        }

        var file = Directory.GetFiles(Path.Combine(_exportRoot, "bi", tag)).Single();
        var content = await File.ReadAllTextAsync(file);
        content.ShouldContain($"X1 {tag}");
        content.ShouldNotContain($"Y1 {tag}");
    }

    private async Task PublishAsync<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent
    {
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(integrationEvent);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
            await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchDueAsync(CancellationToken.None);
        }
    }

    private async Task<(int, int)> DispatchAsync()
    {
        await using var scope = TestAccess.SystemScope(_factory);
        return await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().RunDueAsync(CancellationToken.None);
    }

    private async Task<DateTimeOffset?> NextAttemptAsync(Guid clientId)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var delivery = await scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().Deliveries
            .Where(d => d.ClientRef == clientId && d.EventCode == WebhookAdministration.PingEvent).SingleAsync();
        return delivery.Status == "retrying" ? delivery.NextAttemptAt : null;
    }
}

/// <summary>Records webhook requests and answers with queued status codes (200 when the queue is empty).</summary>
internal sealed class RecordingTransport : IWebhookTransport
{
    public System.Collections.Concurrent.ConcurrentQueue<HttpStatusCode> Responses { get; } = new();

    public List<(string Url, Dictionary<string, string> Headers, string Body)> Sent { get; } = [];

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (Sent)
        {
            Sent.Add((request.RequestUri!.ToString(), request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));
        }

        return new HttpResponseMessage(Responses.TryDequeue(out var status) ? status : HttpStatusCode.OK);
    }
}
