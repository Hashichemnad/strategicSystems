using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Http.Access;

namespace Strategy.IntegrationTests.Platform;

public class EndpointCatalogueTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    /// <summary>CLAUDE.md §7: every endpoint declares its permission, or is explicitly public or self-service. No silent gaps.</summary>
    [Fact]
    public void Every_api_endpoint_declares_a_permission_or_is_explicitly_public()
    {
        var endpoints = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        var undeclared = endpoints
            .Where(e => e.Metadata.GetMetadata<RequiredPermission>() is null && e.Metadata.GetMetadata<PublicEndpoint>() is null
                && e.Metadata.GetMetadata<SelfServiceEndpoint>() is null && e.Metadata.GetMetadata<VendorEndpoint>() is null)
            .Select(e => e.DisplayName)
            .ToList();
        undeclared.ShouldBeEmpty();
    }

    [Fact]
    public void Permission_codes_follow_system_object_action()
    {
        var permissions = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => e.Metadata.GetMetadata<RequiredPermission>()?.Permission)
            .OfType<string>()
            .Distinct()
            .ToList();

        permissions.ShouldNotBeEmpty();
        permissions.ShouldAllBe(p => System.Text.RegularExpressions.Regex.IsMatch(p, "^[a-z][a-z0-9-]*\\.[a-z][a-z0-9-]*\\.[a-z][a-z0-9-]*$"));
    }

    /// <summary>ADR 0111: Wasel endpoints declare <c>wasel.*</c> vendor permissions only, and nothing outside <c>/api/wasel</c> does.</summary>
    [Fact]
    public void Vendor_endpoints_live_under_api_wasel_and_declare_wasel_permissions()
    {
        var endpoints = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var vendor = endpoints.Where(e => e.Metadata.GetMetadata<VendorEndpoint>() is not null).ToList();
        vendor.Count.ShouldBeGreaterThan(15);
        vendor.ShouldAllBe(e => e.RoutePattern.RawText!.StartsWith("/api/wasel/", StringComparison.Ordinal)
            && e.Metadata.GetMetadata<VendorEndpoint>()!.Permission.StartsWith("wasel.", StringComparison.Ordinal)
            && e.Metadata.GetMetadata<RequiredPermission>() == null);
        endpoints.Where(e => e.Metadata.GetMetadata<RequiredPermission>() is { } p && p.Permission.StartsWith("wasel.", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
