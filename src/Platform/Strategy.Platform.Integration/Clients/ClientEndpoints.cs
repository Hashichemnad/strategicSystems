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
