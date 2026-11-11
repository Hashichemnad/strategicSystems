using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Platform.Integration.Clients;

/// <summary><c>/api/v1/integration/clients</c> and <c>/settings</c> (ADR 0191): Settings → Integrations → Clients.</summary>
internal static class ClientEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var clients = group.MapGroup("/clients");
        clients.MapGet("/", async (ClientAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListIntegrationClients").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPost("/", async (ClientInput body, ClientAdministration a, CancellationToken ct) => (await a.CreateAsync(body, ct)).ToOk())
            .WithName("CreateIntegrationClient").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapGet("/{id:guid}", async (Guid id, ClientAdministration a, CancellationToken ct) => (await a.GetAsync(id, ct)).ToOk())
            .WithName("GetIntegrationClient").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPut("/{id:guid}", async (Guid id, ClientInput body, ClientAdministration a, CancellationToken ct) => (await a.UpdateAsync(id, body, ct)).ToOk())
            .WithName("UpdateIntegrationClient").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPost("/{id:guid}/revoke", async (Guid id, ClientRevokeInput body, ClientAdministration a, CancellationToken ct) => (await a.RevokeAsync(id, body, ct)).ToOk())
            .WithName("RevokeIntegrationClient").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapGet("/{id:guid}/credentials", async (Guid id, ClientAdministration a, CancellationToken ct) => (await a.CredentialsAsync(id, ct)).ToOk())
            .WithName("ListIntegrationClientCredentials").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPost("/{id:guid}/credentials/rotate", async (Guid id, ClientRotateInput body, ClientAdministration a, CancellationToken ct) =>
                (await a.RotateAsync(id, body, ct)).ToOk())
            .WithName("RotateIntegrationClientCredential").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPost("/{id:guid}/credentials/{credentialId:guid}/revoke", async (Guid id, Guid credentialId, ClientAdministration a, CancellationToken ct) =>
                (await a.RevokeCredentialAsync(id, credentialId, ct)).ToOk())
            .WithName("RevokeIntegrationClientCredential").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapGet("/{id:guid}/roles", async (Guid id, ClientAdministration a, CancellationToken ct) => (await a.RolesAsync(id, ct)).ToOk())
            .WithName("ListIntegrationClientRoles").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapPut("/{id:guid}/roles", async (Guid id, ClientRolesInput body, ClientAdministration a, CancellationToken ct) => (await a.SetRolesAsync(id, body, ct)).ToOk())
            .WithName("SetIntegrationClientRoles").RequirePermission(IntegrationPermissions.ClientManage);
        clients.MapGet("/{id:guid}/usage", async (Guid id, int? page, int? pageSize, ClientAdministration a, CancellationToken ct) =>
                (await a.UsageAsync(id, new PageRequest(page ?? 1, Math.Clamp(pageSize ?? 50, 1, 200)), ct)).ToOk())
            .WithName("GetIntegrationClientUsage").RequirePermission(IntegrationPermissions.ClientManage);

        group.MapGet("/settings", async (ClientAdministration a, CancellationToken ct) => TypedResults.Ok(await a.SettingsAsync(ct)))
            .WithName("GetIntegrationSettings").RequirePermission(IntegrationPermissions.ClientManage);
        group.MapPut("/settings", async (IntegrationSettingsInput body, ClientAdministration a, CancellationToken ct) => (await a.SaveSettingsAsync(body, ct)).ToOk())
            .WithName("SaveIntegrationSettings").RequirePermission(IntegrationPermissions.ClientManage);
    }
}

public sealed record CatalogueRecordTypeDto(string Code, string EntitySet, string LabelAr, string LabelEn);

public sealed record CatalogueEventDto(string Code, string LabelAr, string LabelEn);

public sealed record CatalogueSystemDto(string Code, string NameAr, string NameEn, IReadOnlyList<CatalogueRecordTypeDto> RecordTypes, IReadOnlyList<CatalogueEventDto> Events);

/// <summary>What a client can be given (Settings → Integrations → Clients forms): systems with record types and webhook events.</summary>
internal static class IntegrationCatalogueEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/catalogue", (Feeds.FeedCatalog feeds, IEnumerable<Webhooks.WebhookEventDefinition> events, IEnumerable<Access.IAccessCatalogueSource> catalogues) =>
            {
                var names = catalogues.Select(c => c.System).GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                return TypedResults.Ok(feeds.Systems.Select(code => new CatalogueSystemDto(
                    code,
                    names.TryGetValue(code, out var s) ? s.NameAr : code,
                    names.TryGetValue(code, out var e) ? e.NameEn : code,
                    feeds.AllTypes.Where(t => t.SystemCode == code).Select(t => new CatalogueRecordTypeDto(t.Code, t.EntitySet, t.LabelAr, t.LabelEn)).ToList(),
                    events.Where(d => d.SystemCode == code).OrderBy(d => d.Code, StringComparer.Ordinal).Select(d => new CatalogueEventDto(d.Code, d.LabelAr, d.LabelEn)).ToList()))
                    .ToList());
            })
            .WithName("GetIntegrationCatalogue").RequirePermission(IntegrationPermissions.ClientManage);
}
