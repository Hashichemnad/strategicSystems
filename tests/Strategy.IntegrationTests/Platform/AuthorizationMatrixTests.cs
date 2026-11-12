using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Http.Access;

namespace Strategy.IntegrationTests.Platform;

/// <summary>
/// Authorization tests generated from the endpoint catalogue (CLAUDE.md §16), so every Phase 0–2 endpoint and every later one is
/// covered without a hand-written list:
/// <list type="bullet">
/// <item>every endpoint that is not public refuses anonymous callers (401);</item>
/// <item>every permission-guarded endpoint refuses a signed-in user who does not hold its permission (403) — only the
/// organisation directory reads are granted to everyone by default (role <c>org.viewer</c>);</item>
/// <item>a user granted exactly that one permission (a role holding only it, plus system access for modules) gets past the
/// permission check;</item>
/// <item>an empty body (<c>{}</c> / <c>[]</c>) past the permission check is a validation problem, never a 500 (ASVS V5 / V13);</item>
/// <item>every permission an endpoint declares exists in the seeded catalogue (unknown permissions are refused).</item>
/// </list>
/// </summary>
public partial class AuthorizationMatrixTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly string[] EveryonePermissions =
    [
        "org.unit.read", "org.employee.read",
        // Phase 5: the seeded "AI user" role (assigned to everyone) holds the AI feature permissions; administrators narrow it per role.
        "ai.extract.use", "ai.brief.use", "ai.translate.use", "ai.similar.use", "ai.ask.use",
        // Phase 6: the seeded task roles "Employee" and "Manager" (assigned to everyone): own / team tasks, create, dashboard, AI drafts.
        "tasks.task.read", "tasks.task.create", "tasks.dashboard.read", "tasks.performance.read",
        // Phase 7: the seeded Teams "Employee" role (assigned to everyone): own teams, request a team, own memberships, AI assists.
        "teams.team.read", "teams.team.request", "teams.membership.read", "teams.ai.formation", "teams.ai.members", "teams.ai.evaluation",
        "teams.ai.minutes",
        // Phase 17: the seeded "Dashboard user" role (assigned to everyone): dashboards, My Work cards, run and export reports.
        "reporting.dashboard.read", "reporting.report.run", "reporting.report.export",
        // Phase 21: record dossiers for everyone (each record and section is still checked as the reader, ADR 0184).
        "reporting.dossier.run",
        // Phase 19: the seeded "Integration viewer" role (assigned to everyone): source system names for the External references section (ADR 0164).
        "integration.source-system.read",
    ];

    public static TheoryData<string, string> NonPublicEndpoints() => Endpoints(e => e.Metadata.GetMetadata<PublicEndpoint>() is null);

    public static TheoryData<string, string> AdminEndpoints() => Endpoints(e =>
        e.Metadata.GetMetadata<RequiredPermission>() is { } p && !EveryonePermissions.Contains(p.Permission));

    public static TheoryData<string, string, string> GuardedEndpoints()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (method, route, permission) in EndpointList(e => e.Metadata.GetMetadata<RequiredPermission>() is not null))
        {
            data.Add(method, route, permission!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NonPublicEndpoints))]
    public async Task Anonymous_callers_get_401(string method, string route)
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var response = await client.SendAsync(Request(method, route));

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.required", "errors.unauthorized");
    }

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task Signed_in_non_administrators_get_403_from_administration_endpoints(string method, string route)
    {
        var client = await api.UserClientAsync();

        var response = await client.SendAsync(Request(method, route));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden", "errors.forbidden");
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task A_user_holding_only_the_endpoint_permission_gets_past_the_permission_check(string method, string route, string permission)
    {
        var client = await api.ClientWithOnlyAsync(permission);

        var response = await client.SendAsync(Request(method, route));

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, $"{method} {route} with {permission}: {await response.Content.ReadAsStringAsync()}");
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);

        // An empty body ({} / []) past the access check is a validation problem (400), never an unhandled error (ASVS V5 / V13).
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, $"{method} {route}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Every_declared_permission_is_in_the_seeded_catalogue_with_labels()
    {
        var admin = await api.AdminClientAsync();
        var catalogue = new HashSet<string>(StringComparer.Ordinal);
        foreach (var system in (await admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/access/systems"))!)
        {
            var matrix = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/access/systems/{system.GetProperty("code").GetString()}/matrix");
            foreach (var permission in matrix.GetProperty("permissions").EnumerateArray())
            {
                permission.GetProperty("labelAr").GetString().ShouldNotBeNullOrWhiteSpace();
                permission.GetProperty("labelEn").GetString().ShouldNotBeNullOrWhiteSpace();
                catalogue.Add(permission.GetProperty("code").GetString()!);
            }
        }

        var declared = EndpointList(e => e.Metadata.GetMetadata<RequiredPermission>() is not null).Select(e => e.Permission!).Distinct().ToList();
        declared.Where(p => !catalogue.Contains(p)).ShouldBeEmpty();
    }

    [Fact]
    public void Administration_endpoint_list_is_not_empty() => AdminEndpoints().Count.ShouldBeGreaterThan(60);

    private static HttpRequestMessage Request(string method, string route)
    {
        var path = RouteParameter().Replace(route, match => match.Groups["name"].Value switch
        {
            "kind" => route.Contains("logo", StringComparison.Ordinal) ? "logo-light" : "units",
            "code" or "key" or "type" => "unknown",
            "versionNo" => "1",
            "year" => "2030",
            "period" => "q1",
            _ => Guid.CreateVersion7().ToString(),
        });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            // A body every endpoint can bind, so the request reaches the access check (binding errors would answer 400 first).
            var form = new MultipartFormDataContent();
            if (route.Contains("photo", StringComparison.Ordinal) || route.Contains("import", StringComparison.Ordinal)
                || route.Contains("logo", StringComparison.Ordinal) || route.EndsWith("/files/{kind}", StringComparison.Ordinal)
                || route.EndsWith("/template", StringComparison.Ordinal) || route.EndsWith("/document", StringComparison.Ordinal)
                || route.EndsWith("/ai-fill", StringComparison.Ordinal) || route.EndsWith("/evidence", StringComparison.Ordinal)
                || route.EndsWith("/evidence-review", StringComparison.Ordinal))
            {
                var file = new ByteArrayContent(Encoding.UTF8.GetBytes("x"));
                file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "file", "x.csv");
                request.Content = form;
            }
            else if (route.StartsWith("/api/v1/reporting/", StringComparison.Ordinal)
                && (route.EndsWith("/roles", StringComparison.Ordinal) || route.EndsWith("/shares", StringComparison.Ordinal) || route.EndsWith("/layout", StringComparison.Ordinal)))
            {
                // Phase 17 endpoints whose body is a list.
                request.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            }
            else
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }
        }

        return request;
    }

    private static TheoryData<string, string> Endpoints(Func<RouteEndpoint, bool> filter)
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, route, _) in EndpointList(filter))
        {
            data.Add(method, route);
        }

        return data;
    }

    private static List<(string Method, string Route, string? Permission)> EndpointList(Func<RouteEndpoint, bool> filter)
    {
        // Only the endpoint catalogue is needed: no database, no migrations.
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Port=1;Database=unused;Timeout=1", settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" },
            backgroundServices: false);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Where(filter)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (Method: m, Route: e.RoutePattern.RawText!, e.Metadata.GetMetadata<RequiredPermission>()?.Permission)))
            .Distinct()
            .OrderBy(x => x.Route, StringComparer.Ordinal).ThenBy(x => x.Method, StringComparer.Ordinal);
        return [.. endpoints];
    }

    [GeneratedRegex(@"\{(?<name>[a-zA-Z]+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();
}
