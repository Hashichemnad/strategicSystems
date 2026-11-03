using System.Text.Json;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Notifications.Application;

namespace Strategy.Platform.Notifications.Mcp;

/// <summary><c>my_notifications</c>: the token owner's latest in-app notifications.</summary>
internal sealed class MyNotificationsTool(NotificationAdministration notifications) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_notifications", "notifications", ToolKind.Read, "إشعاراتي", "My notifications",
        "Lists the signed-in user's latest in-app notifications (newest first), optionally unread only, with links to the records.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "unreadOnly":{"type":"boolean"},
              "limit":{"type":"integer","minimum":1,"maximum":50}}}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(arguments.Number("limit") ?? 20, 1, 50);
        var mine = await notifications.MineAsync(new PageRequest(1, limit), arguments.Flag("unreadOnly") ?? false, cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new
        {
            mine.Unread,
            items = mine.Items.Select(n => new { n.Id, title = context.Language == "ar" ? n.TitleAr : n.TitleEn, n.TemplateKey, n.Link, n.CreatedAt, n.ReadAt }),
        });
    }
}
