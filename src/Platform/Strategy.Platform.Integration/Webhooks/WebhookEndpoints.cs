using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary><c>/api/v1/integration/clients/{id}/webhooks</c>, <c>/webhooks/deliveries</c>, <c>/webhooks/events</c> (ADR 0194).</summary>
internal static class WebhookEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var client = group.MapGroup("/clients/{clientId:guid}/webhooks");
        client.MapGet("/", async (Guid clientId, WebhookAdministration a, CancellationToken ct) => (await a.ListAsync(clientId, ct)).ToOk())
            .WithName("ListWebhookSubscriptions").RequirePermission(IntegrationPermissions.WebhookManage);
        client.MapPost("/", async (Guid clientId, WebhookSubscriptionInput body, WebhookAdministration a, CancellationToken ct) => (await a.CreateAsync(clientId, body, ct)).ToOk())
            .WithName("CreateWebhookSubscription").RequirePermission(IntegrationPermissions.WebhookManage);
        client.MapPut("/{id:guid}", async (Guid clientId, Guid id, WebhookSubscriptionInput body, WebhookAdministration a, CancellationToken ct) =>
                (await a.UpdateAsync(clientId, id, body, ct)).ToOk())
            .WithName("UpdateWebhookSubscription").RequirePermission(IntegrationPermissions.WebhookManage);
        client.MapPost("/{id:guid}/secret", async (Guid clientId, Guid id, WebhookAdministration a, CancellationToken ct) =>
                (await a.RegenerateSecretAsync(clientId, id, ct)).ToOk())
            .WithName("RegenerateWebhookSecret").RequirePermission(IntegrationPermissions.WebhookManage);
        client.MapPost("/{id:guid}/test", async (Guid clientId, Guid id, WebhookAdministration a, CancellationToken ct) => (await a.SendTestAsync(clientId, id, ct)).ToOk())
            .WithName("SendWebhookTest").RequirePermission(IntegrationPermissions.WebhookManage);

        var webhooks = group.MapGroup("/webhooks");
        webhooks.MapGet("/events", (WebhookAdministration a) => TypedResults.Ok(a.Catalogue()))
            .WithName("ListWebhookEvents").RequirePermission(IntegrationPermissions.PortalView);
        webhooks.MapGet("/deliveries", async (Guid? clientId, string? status, int? page, int? pageSize, WebhookAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.DeliveriesAsync(clientId, status, new PageRequest(page ?? 1, Math.Clamp(pageSize ?? 50, 1, 200)), ct)))
            .WithName("ListWebhookDeliveries").RequirePermission(IntegrationPermissions.WebhookManage);
        webhooks.MapGet("/deliveries/{id:guid}", async (Guid id, WebhookAdministration a, CancellationToken ct) => (await a.DeliveryAsync(id, ct)).ToOk())
            .WithName("GetWebhookDelivery").RequirePermission(IntegrationPermissions.WebhookManage);
        webhooks.MapPost("/deliveries/replay", async (WebhookReplayInput body, WebhookAdministration a, CancellationToken ct) => (await a.ReplayAsync(body, ct)).ToOk())
            .WithName("ReplayWebhookDeliveries").RequirePermission(IntegrationPermissions.WebhookManage);
    }
}
