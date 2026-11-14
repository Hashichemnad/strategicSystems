using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Host.Infrastructure;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Integration.Sync;

namespace Strategy.IntegrationTests.Integration.Sync;

/// <summary>
/// The Development demo org (<c>Development:MockSalesforce</c>, used by the E2E spec and demonstrations) with its recorded User / Contact
/// describes: the objects the E2E spec enables, plus those 1.0.1 changed, validate against it — including the full field check (ADR 0212) —
/// and a full load runs without Salesforce errors. (The demo derives other objects' field types as text, so date / number kind checks of the
/// remaining pack objects are left to the real describe.)
/// </summary>
public sealed class MockSalesforceDemoTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Sync = "/api/v1/integration/sync";
    private static readonly string[] Objects =
        ["department", "user", "contact", "program-request", "project", "work-plan", "project-risk", "files", "attachments", "approval-history", "history-project"];

    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddMockSalesforce());
        _admin = await _factory.SignInAsync(await _factory.CreateUserAsync(admin: true));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Every_shipped_mapping_validates_against_the_demo_org_and_a_full_load_has_no_salesforce_errors()
    {
        var org = _factory.Services.GetRequiredService<MockSalesforceOrg>();
        var connection = (await _admin.GetFromJsonAsync<List<SyncConnectionDto>>($"{Sync}/connections"))!.Single(c => c.SourceSystem == "salesforce");
        (await _admin.PutAsJsonAsync($"{Sync}/connections/{connection.Id}/key", new SyncKeyInput(org.PrivateKeyPem))).StatusCode.ShouldBe(HttpStatusCode.OK);
        connection = (await _admin.GetFromJsonAsync<SyncConnectionDto>($"{Sync}/connections/{connection.Id}"))!;
        (await _admin.PutAsJsonAsync($"{Sync}/connections/{connection.Id}", new SyncConnectionInput(MockSalesforceOrg.LoginUrl, org.ClientId, org.Username,
            MockSalesforceOrg.ApiVersion, 2000, 2000, connection.MappingPackId, connection.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var tested = (await (await _admin.PostAsync($"{Sync}/connections/{connection.Id}/test", null)).Content.ReadFromJsonAsync<SyncConnectionDto>())!;
        tested.LastTest!.Ok.ShouldBeTrue(tested.LastTest.MessageKey);

        var failures = new List<string>();
        foreach (var mapping in (await _admin.GetFromJsonAsync<List<ObjectMappingDto>>($"{Sync}/packs/{connection.MappingPackId}/mappings"))!.Where(m => Objects.Contains(m.Key)))
        {
            var validated = (await (await _admin.PostAsync($"{Sync}/connections/{connection.Id}/mappings/{mapping.Id}/validate", null)).Content
                .ReadFromJsonAsync<MappingValidationDto>())!;
            if (!validated.Ok)
            {
                failures.Add(mapping.Key + ": " + string.Join("; ", validated.Issues.Where(i => i.Severity == "error").Select(i => i.MessageKey + " " + JsonSerializer.Serialize(i.Params))));
                continue;
            }

            var current = (await _admin.GetFromJsonAsync<ObjectMappingDto>($"{Sync}/mappings/{mapping.Id}"))!;
            (await _admin.PostAsJsonAsync($"{Sync}/mappings/{mapping.Id}/enabled", new ObjectMappingEnableInput(true, current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        failures.ShouldBeEmpty();
        (await _admin.PostAsync($"{Sync}/connections/{connection.Id}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = TestAccess.SystemScope(_factory);
        var run = (await scope.ServiceProvider.GetRequiredService<SyncEngine>().RunAsync(connection.Id, RunTriggers.Full, true, null, null, CancellationToken.None))!;
        var detail = (await _admin.GetFromJsonAsync<SyncRunDetailDto>($"{Sync}/connections/{connection.Id}/runs/{run.Id}"))!;
        detail.Log.Where(l => l.Level == "error").Select(l => $"{l.ObjectKey} {l.MessageKey} {JsonSerializer.Serialize(l.Params)}").ShouldBeEmpty();
        run.Status.ShouldNotBe(RunStatuses.Failed);
        org.Violations.ShouldBeEmpty(); // read-only, and every request understood
    }
}
