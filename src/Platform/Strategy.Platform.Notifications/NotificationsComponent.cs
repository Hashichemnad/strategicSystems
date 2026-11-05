using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Notifications.Api;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Notifications.Email;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Notifications;

/// <summary>Notifications (<c>/api/v1/notifications</c>, hub <c>/hubs/notifications</c>, schema <c>notify</c>).</summary>
public sealed class NotificationsComponent : IPlatformComponent
{
    public const string HubPath = "/hubs/notifications";

    public string Name => "notifications";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotifyDbContext>(NotifyDbContext.SchemaName);
        services.AddSignalR();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();
        services.AddScoped<ITestEmailSender, TestEmailSender>();
        services.AddScoped<NotificationAdministration>();
        services.AddMyWorkCardSource<NotificationsMyWorkCard>();
        services.AddAppTool<Mcp.MyNotificationsTool>();
        services.AddScoped<IDatabaseSeeder, NotificationSeeder>();
        services.AddOutboxHandler<EmailOutboxHandler>();
        services.AddOutboxHandler<InAppOutboxHandler>();
        services.AddScheduledJob(DigestJob.Definition);
        services.AddNotificationTemplate(NotificationTemplates.Layout);
        services.AddNotificationTemplate(NotificationTemplates.Digest);
        services.AddNotificationTemplate(NotificationTemplates.Test);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        NotificationEndpoints.Map(endpoints.MapPlatformGroup(this));
        endpoints.MapHub<NotificationHub>(HubPath);
    }
}
