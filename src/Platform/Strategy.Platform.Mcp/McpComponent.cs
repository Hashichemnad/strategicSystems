using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Mcp.Application;
using Strategy.Platform.Mcp.Infrastructure;
using Strategy.Platform.Mcp.OAuth;
using Strategy.Platform.Mcp.Tools;
using Strategy.Platform.Persistence;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Strategy.Platform.Mcp;

public static class McpScopes
{
    public const string Mcp = "mcp";

    public static readonly IReadOnlyList<string> All = [Mcp];
}

public static class McpPermissions
{
    public const string SystemCode = "mcp";
    public const string Manage = "mcp.settings.manage";
}

/// <summary>
/// MCP server at <c>/mcp</c> and the OAuth 2.1 authorization server for its clients (<c>/api/v1/mcp</c>, schema <c>mcp</c>,
/// docs/design/mcp.md, ADRs 0031–0032).
/// </summary>
public sealed class McpComponent : IPlatformComponent
{
    public string Name => "mcp";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<McpDbContext>(McpDbContext.SchemaName);
        services.AddHttpContextAccessor();
        services.AddSingleton<McpRateLimiter>();
        services.AddScoped<McpAdministration>();
        services.AddAppTool<WhoAmITool>();
        services.AddAccessCatalogue(new McpCatalogue());

        var requireHttps = configuration.GetValue("Mcp:RequireHttps", defaultValue: true);
        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<McpDbContext>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                server.RegisterScopes(McpScopes.Mcp, OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess);
                // RFC 8707: MCP clients name this server's /mcp as the resource. Its URL depends on the installation's host, so the
                // authorization endpoint checks it per request instead of a static list.
                server.Configure(options =>
                {
                    options.DisableResourceValidation = true;
                    options.IgnoreResourcePermissions = true;
                });
                // Tokens are ASP.NET Core Data Protection tokens (key ring in PostgreSQL, ADR 0012): they survive restarts without a certificate.
                // No identity token is issued (no openid scope), so the ephemeral keys only satisfy the server's start-up requirement.
                server.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
                server.UseDataProtection();
                var aspNet = server.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
                if (!requireHttps)
                {
                    aspNet.DisableTransportSecurityRequirement();
                }

                server.AddEventHandler<HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
                {
                    context.Metadata["registration_endpoint"] = new Uri(context.BaseUri!, "connect/register").AbsoluteUri;
                    return default;
                }));
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                // Revocation (client removed, refresh-token reuse) applies at once: each call checks the token and authorization entries.
                validation.EnableAuthorizationEntryValidation();
                validation.EnableTokenEntryValidation();
                validation.UseDataProtection();
                validation.UseAspNetCore();
            });
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, OAuthAuthenticationHandler>(AuthSchemes.OAuth, null);

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "strategy", Title = "Integrated Strategy System", Version = "1.0" };
                options.ServerInstructions =
                    "Tools of the Integrated Strategy System. Every call acts as the signed-in user and returns only data that user may see. " +
                    "Use whoami to see who that is, search_records to find records and record_status for where a record stands.";
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithListToolsHandler(McpToolHandlers.ListAsync)
            .WithCallToolHandler(McpToolHandlers.CallAsync);
    }

    public void UseMiddleware(IApplicationBuilder app) => app.UseMiddleware<McpGateMiddleware>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp");
        AuthorizationEndpoints.Map(endpoints);
        var group = endpoints.MapPlatformGroup(this);
        group.MapGet("/settings", async (McpAdministration a, CancellationToken ct) => TypedResults.Ok(await a.SettingsAsync(ct)))
            .WithName("GetMcpSettings").RequirePermission(McpPermissions.Manage);
        group.MapPut("/settings", async (McpSettingsInput body, McpAdministration a, CancellationToken ct) => (await a.SaveSettingsAsync(body, ct)).ToOk())
            .WithName("SaveMcpSettings").RequirePermission(McpPermissions.Manage);
        group.MapGet("/clients", async (McpAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ClientsAsync(ct)))
            .WithName("ListMcpClients").RequirePermission(McpPermissions.Manage);
        group.MapPost("/clients/{clientId}/revoke", async Task<Results<NoContent, ProblemHttpResult>> (
                string clientId, McpAdministration a, CancellationToken ct) =>
            {
                var result = await a.RevokeClientAsync(clientId, ct);
                return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
            })
            .WithName("RevokeMcpClient").RequirePermission(McpPermissions.Manage);
        group.MapGet("/consent", async (string? clientId, string? redirectUri, McpAdministration a, CancellationToken ct) =>
                (await a.ConsentInfoAsync(clientId, redirectUri, ct)).ToOk())
            .WithName("GetMcpConsent").RequireSignedIn();
        group.MapPost("/consent", async (McpConsentInput body, McpAdministration a, CancellationToken ct) => (await a.ConsentAsync(body, ct)).ToOk())
            .WithName("GrantMcpConsent").RequireSignedIn();
    }
}

internal sealed class McpCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(McpPermissions.SystemCode, SystemKind.Platform, "خادم MCP", "MCP server", 49);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(McpPermissions.Manage, "إدارة خادم MCP", "Manage the MCP server", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول خادم MCP", "MCP administrator", [new(McpPermissions.Manage)], RequiresMfa: true),
    ];
}
