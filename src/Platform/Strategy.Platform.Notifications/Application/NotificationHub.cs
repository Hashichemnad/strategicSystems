using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Strategy.Platform.Access;

namespace Strategy.Platform.Notifications.Application;

/// <summary>
/// Live notification counter (<c>/hubs/notifications</c>). Signed-in internal users join their own group only; the server pushes,
/// clients never send. Vendor (Wasel) principals are refused.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public static string GroupOf(Guid userId) => "user:" + userId.ToString("N");

    public override async Task OnConnectedAsync()
    {
        if (Context.User is not { } user || !user.IsInternal() || user.UserId() is not { } userId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(userId)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
