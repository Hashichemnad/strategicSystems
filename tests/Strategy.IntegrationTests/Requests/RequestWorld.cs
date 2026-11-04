using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Teams;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting.Pdf;

namespace Strategy.IntegrationTests.Requests;

/// <summary>One host per test class with a travelling clock and a capturing PDF renderer; Requests, Document Registry and IP open for everyone.</summary>
public sealed class RequestsFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public CapturingPdfRenderer Pdf { get; } = new();

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(Pdf);
        });
        foreach (var system in new[] { "requests", "docreg", "ip" })
        {
            await Factory.GrantSystemAccessAsync(system, AccessGrants.Everyone, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// People of the request tests: an office member (Strategy Office / PM Office of requests, document registry and IP, MFA), a type administrator
/// (MFA), two employees in two departments and an outsider.
/// </summary>
internal sealed class RequestWorld
{
    public const string Base = "/api/v1/requests";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public required ApiFactory Factory { get; init; }

    public required Guid OrganisationId { get; init; }

    public required Guid DepartmentA { get; init; }

    public required Guid DepartmentB { get; init; }

    public required TestUser Office { get; init; }

    public required TestUser Admin { get; init; }

    public required TestUser EmployeeA { get; init; }

    public required TestUser EmployeeB { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<RequestWorld> CreateAsync(ApiFactory factory, params string[] officeRoles)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        Guid organisation, a, b;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"RO{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            a = (await dispatcher.SendAsync(new CreateUnit($"RA{suffix}", "إدارة أ", $"Department A {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            b = (await dispatcher.SendAsync(new CreateUnit($"RB{suffix}", "إدارة ب", $"Department B {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var office = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: ["requests.office", .. officeRoles]);
        var admin = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: "requests.type-administrator");
        var employeeA = await factory.CreateUserAsync(unitId: a);
        var employeeB = await factory.CreateUserAsync(unitId: b);
        var outsider = await factory.CreateUserAsync();
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { office, admin, employeeA, employeeB, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new RequestWorld
        {
            Factory = factory,
            OrganisationId = organisation,
            DepartmentA = a,
            DepartmentB = b,
            Office = office,
            Admin = admin,
            EmployeeA = employeeA,
            EmployeeB = employeeB,
            Outsider = outsider,
            Clients = clients,
        };
    }

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue($"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public async Task<RequestDto> DraftAsync(TestUser user, string type, object values) =>
        await OkAsync<RequestDto>(C(user).PostAsJsonAsync(Base, new { typeCode = type, data = values }));

    public async Task<RequestDto> SubmitAsync(TestUser user, RequestDto request) =>
        await OkAsync<RequestDto>(C(user).PostAsJsonAsync($"{Base}/{request.Id}/submit", new { version = request.Version }));

    public async Task<RequestDto> GetAsync(TestUser user, Guid id) => await OkAsync<RequestDto>(C(user).GetAsync($"{Base}/{id}"));

    public async Task<RequestDto> DecideAsync(TestUser user, Guid id, string action, object? form = null, string? comment = null) =>
        await OkAsync<RequestDto>(C(user).PostAsJsonAsync($"{Base}/{id}/decide", new { action, comment, form }));

    public async Task<RequestDto> ActAsync(TestUser user, Guid id, string action, object? form = null, string? comment = null)
    {
        var current = await GetAsync(user, id);
        return await OkAsync<RequestDto>(C(user).PostAsJsonAsync($"{Base}/{id}/actions/{action}", new { version = current.Version, comment, form }));
    }

    /// <summary>Uploads a file to the request (generic attachments API) and returns its id.</summary>
    public async Task<Guid> UploadAsync(TestUser user, Guid requestId, string name, byte[] content, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", name);
        var uploaded = await OkAsync<JsonObject>(C(user).PostAsync($"/api/v1/files/owners/requests.request/{requestId}", form));
        return Guid.Parse(uploaded["id"]!.GetValue<string>());
    }

    public static byte[] Pdf(string text) => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n"
        + $"% {text}\ntrailer<</Root 1 0 R>>\n%%EOF");

    public static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> call) => (await call).StatusCode;
}
