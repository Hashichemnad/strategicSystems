using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Http.Access;

namespace Strategy.IntegrationTests.Platform;

/// <summary>
/// Negative authorization tests generated from the endpoint catalogue (CLAUDE.md §16): every endpoint that is not public
/// refuses anonymous callers (401), and every administration endpoint refuses a signed-in non-administrator (403).
/// </summary>
public partial class AuthorizationMatrixTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly string[] AdminPermissionPrefixes = ["org.", "identity."];

    public static TheoryData<string, string> NonPublicEndpoints() => Endpoints(e => e.Metadata.GetMetadata<PublicEndpoint>() is null);

    public static TheoryData<string, string> AdminEndpoints() => Endpoints(e =>
        e.Metadata.GetMetadata<RequiredPermission>() is { } p
        && AdminPermissionPrefixes.Any(prefix => p.Permission.StartsWith(prefix, StringComparison.Ordinal))
        && !p.Permission.EndsWith(".read", StringComparison.Ordinal));

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

    [Fact]
    public void Administration_endpoint_list_is_not_empty() => AdminEndpoints().Count.ShouldBeGreaterThan(30);

    private static HttpRequestMessage Request(string method, string route)
    {
        var path = RouteParameter().Replace(route, match => match.Groups["name"].Value switch
        {
            "kind" => "units",
            "code" => "unknown",
            _ => Guid.CreateVersion7().ToString(),
        });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            // A body every endpoint can bind, so the request reaches the access check (binding errors would answer 400 first).
            var form = new MultipartFormDataContent();
            if (route.Contains("photo", StringComparison.Ordinal) || route.Contains("import", StringComparison.Ordinal))
            {
                var file = new ByteArrayContent(Encoding.UTF8.GetBytes("x"));
                file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "file", "x.csv");
                request.Content = form;
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
        // Only the endpoint catalogue is needed: no database, no migrations.
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Port=1;Database=unused;Timeout=1", settings: new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Where(filter)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (Method: m, Route: e.RoutePattern.RawText!)))
            .Distinct()
            .OrderBy(x => x.Route, StringComparer.Ordinal).ThenBy(x => x.Method, StringComparer.Ordinal);
        var data = new TheoryData<string, string>();
        foreach (var (method, route) in endpoints)
        {
            data.Add(method, route);
        }

        return data;
    }

    [GeneratedRegex(@"\{(?<name>[a-zA-Z]+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();
}
