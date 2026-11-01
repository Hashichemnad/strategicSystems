using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Notifications.Api;

internal static class NotificationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("/mine");
        mine.MapGet("/", async (int? page, int? pageSize, bool? unread, NotificationAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.MineAsync(new PageRequest(page ?? 1, pageSize ?? 20), unread ?? false, ct)))
            .WithName("ListMyNotifications").RequireSignedIn();
        mine.MapPost("/{id:guid}/read", async (Guid id, NotificationAdministration a, CancellationToken ct) => NoContent(await a.MarkReadAsync(id, ct)))
            .WithName("MarkNotificationRead").RequireSignedIn();
        mine.MapPost("/read-all", async (NotificationAdministration a, CancellationToken ct) => NoContent(await a.MarkReadAsync(null, ct)))
            .WithName("MarkAllNotificationsRead").RequireSignedIn();

        group.MapGet("/preferences", async (NotificationAdministration a, CancellationToken ct) => TypedResults.Ok(await a.PreferencesAsync(ct)))
            .WithName("GetNotificationPreferences").RequireSignedIn();
        group.MapPut("/preferences", async (PreferencesInput body, NotificationAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.SavePreferencesAsync(body, ct)))
            .WithName("SaveNotificationPreferences").RequireSignedIn();

        var templates = group.MapGroup("/templates");
        templates.MapGet("/", async (NotificationAdministration a, CancellationToken ct) => TypedResults.Ok(await a.TemplatesAsync(ct)))
            .WithName("ListNotificationTemplates").RequirePermission(SettingsPermissions.NotificationManage);
        templates.MapPut("/{key}", async (string key, TemplateInput body, NotificationAdministration a, CancellationToken ct) =>
                (await a.UpdateTemplateAsync(key, body, ct)).ToOk())
            .WithName("UpdateNotificationTemplate").RequirePermission(SettingsPermissions.NotificationManage);
        templates.MapPost("/{key}/reset", async (string key, VersionBody body, NotificationAdministration a, CancellationToken ct) =>
                (await a.ResetTemplateAsync(key, body.Version, ct)).ToOk())
            .WithName("ResetNotificationTemplate").RequirePermission(SettingsPermissions.NotificationManage);
        templates.MapPost("/{key}/preview", async (string key, TemplateInput? body, NotificationAdministration a, CancellationToken ct) =>
                (await a.PreviewAsync(key, body, ct)).ToOk())
            .WithName("PreviewNotificationTemplate").RequirePermission(SettingsPermissions.NotificationManage);

        group.MapGet("/deliveries", async (int? page, int? pageSize, string? status, NotificationAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.DeliveriesAsync(new PageRequest(page ?? 1, pageSize ?? 50), status, ct)))
            .WithName("ListEmailDeliveries").RequirePermission(SettingsPermissions.NotificationManage);
    }

    private static Results<NoContent, ProblemHttpResult> NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
}

internal sealed record VersionBody(uint Version);
