using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Integration.Api;
using Strategy.Platform.Integration.Application;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Feeds;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Integration.Mcp;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.Platform.Notifications;
using Strategy.Platform.Integration.Webhooks;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration;

public static class IntegrationPermissions
{
    public const string SystemCode = "integration";
    public const string SourceRead = "integration.source-system.read";
    public const string SourceManage = "integration.source-system.manage";

    /// <summary>Integration clients, credentials, roles, usage, export schedules (ADR 0191).</summary>
    public const string ClientManage = "integration.client.manage";

    /// <summary>Webhook subscriptions, delivery log, dead letters, replay (ADR 0194).</summary>
    public const string WebhookManage = "integration.webhook.manage";

    /// <summary>Developer portal (ADR 0195).</summary>
    public const string PortalView = "integration.portal.view";

    /// <summary>Salesforce standby sync status, runs, exceptions (ADR 0200).</summary>
    public const string SyncView = "integration.sync.view";

    /// <summary>Connection, key, schedule, mappings, sync now, exceptions (ADR 0200–0205).</summary>
    public const string SyncManage = "integration.sync.manage";

    /// <summary>Make this system primary, outage report, return to standby (ADR 0207).</summary>
    public const string SyncFailover = "integration.sync.failover";
}

/// <summary>External references (<c>/api/v1/integration</c>, schema <c>integration</c>, ADR 0164 / 0165, docs/design/external-references.md).</summary>
public sealed class IntegrationComponent : IPlatformComponent
{
    public string Name => "integration";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IntegrationDbContext>(IntegrationDbContext.SchemaName);
        services.AddScoped<IExternalReferenceStore, ExternalReferenceStore>();
        services.AddScoped<ReferencedRecordResolver>();
        services.AddScoped<IReferencedRecordResolver>(sp => sp.GetRequiredService<ReferencedRecordResolver>());
        services.AddScoped<SourceSystemAdministration>();
        services.AddAccessCatalogue(new IntegrationCatalogue());
        services.AddLookupType(new LookupTypeDefinition(SourceSystemAdministration.TypeLookup, IntegrationPermissions.SystemCode, "نوع النظام المصدر", "Source system type",
        [
            new("salesforce", "سيلزفورس", "Salesforce"), new("sap", "ساب", "SAP"), new("oracle", "أوراكل", "Oracle"),
            new("dynamics", "مايكروسوفت دايناميكس", "Microsoft Dynamics"), new("custom", "نظام آخر", "Custom"),
        ]));
        services.AddAppTool<FindByExternalReferenceTool>();
        services.AddSearchContributor<ExternalReferenceSearch>(); // cross-system: every hit re-checked through the Links registry

        // Phase 22 (docs/design/integrations.md, ADR 0190–0195): clients, OData feeds, extracts, webhooks, developer portal.
        services.AddHttpContextAccessor();
        services.AddSingleton<ClientRateLimiter>();
        services.AddScoped<IntegrationClientAuthority>();
        services.AddScoped<IIntegrationClientAuthority>(sp => sp.GetRequiredService<IntegrationClientAuthority>());
        services.AddScoped<ClientAdministration>();
        services.AddScoped<FeedCatalog>();
        services.AddScoped<FeedLabels>();
        services.AddScoped<FeedService>();
        services.AddScoped<ScheduledExports>();
        services.AddScoped<WebhookAdministration>();
        services.AddScoped<WebhookDispatcher>();
        services.TryAddSingleton<IWebhookTransport, PolicyWebhookTransport>();
        services.AddScheduledJob(WebhookDispatchJob.Definition);
        services.AddScheduledJob(ExportJob.Definition);
        services.AddScheduledJob(IntegrationMaintenanceJob.Definition);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.Integration, null);

        // Phase 23 (docs/design/salesforce-sync.md, ADR 0200–0208): Salesforce standby sync.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISalesforceTransport, PolicySalesforceTransport>();
        services.AddSingleton<SalesforceSessionFactory>();
        services.AddSingleton<SyncTargetRegistry>();
        services.AddSingleton<SyncStandbyGuard>();
        services.AddSingleton<Kernel.Integration.IStandbyGuard>(sp => sp.GetRequiredService<SyncStandbyGuard>());
        services.AddScoped<EntitySyncWriter>();
        services.AddScoped<SyncEngine>();
        services.AddScoped<SyncAdministration>();
        services.AddScoped<SyncFailover>();
        services.AddScoped<SyncScheduler>();
        services.AddScoped<SyncAlerts>();
        services.AddScheduledJob(SalesforceSyncJob.Definition);
        services.AddScheduledJob(SyncMonitorJob.Definition);
        services.AddAdminHealthContributor<SyncHealthContributor>();
        services.AddNotificationTemplate(SyncAlerts.Failed);
        services.AddNotificationTemplate(SyncAlerts.Late);
        services.AddScoped<IDatabaseSeeder, SyncSeeder>();
    }

    public void UseMiddleware(IApplicationBuilder app) => app.UseMiddleware<IntegrationGateMiddleware>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapPlatformGroup(this);
        IntegrationEndpoints.Map(group);
        ClientEndpoints.Map(group);
        IntegrationCatalogueEndpoint.Map(group);
        WebhookEndpoints.Map(group);
        ScheduledExports.Map(group);
        PortalEndpoints.Map(group);
        ODataEndpoints.MapExtracts(group);
        ODataEndpoints.Map(endpoints);
        SyncEndpoints.Map(group);
    }
}

internal sealed class IntegrationCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(IntegrationPermissions.SystemCode, SystemKind.Platform, "التكامل مع الأنظمة الأخرى", "Integrations", 47);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(IntegrationPermissions.SourceRead, "عرض الأنظمة المصدر", "View source systems", Scopable: false),
        new(IntegrationPermissions.SourceManage, "إدارة الأنظمة المصدر", "Manage source systems", Scopable: false),
        new(IntegrationPermissions.ClientManage, "إدارة جهات التكامل وصلاحياتها", "Manage integration clients and their permissions", Scopable: false),
        new(IntegrationPermissions.WebhookManage, "إدارة اشتراكات Webhook وسجل التسليم", "Manage webhook subscriptions and deliveries", Scopable: false),
        new(IntegrationPermissions.PortalView, "عرض بوابة المطورين", "View the developer portal", Scopable: false),
        new(IntegrationPermissions.SyncView, "عرض حالة المزامنة مع Salesforce", "View the Salesforce sync status", Scopable: false),
        new(IntegrationPermissions.SyncManage, "إدارة المزامنة والربط والمطابقة", "Manage the sync, connection and mappings", Scopable: false),
        new(IntegrationPermissions.SyncFailover, "التحويل إلى نظام أساسي والعودة إلى الاحتياط", "Failover and return to standby", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول التكامل", "Integrations administrator",
            [new(IntegrationPermissions.SourceRead), new(IntegrationPermissions.SourceManage), new(IntegrationPermissions.ClientManage),
             new(IntegrationPermissions.WebhookManage), new(IntegrationPermissions.PortalView), new(IntegrationPermissions.SyncView),
             new(IntegrationPermissions.SyncManage), new(IntegrationPermissions.SyncFailover)], RequiresMfa: true),
        new("sync-operator", "مشغّل المزامنة", "Sync operator", [new(IntegrationPermissions.SyncView), new(IntegrationPermissions.SyncManage)], RequiresMfa: true),
        new("developer", "مطور التكامل", "Integration developer", [new(IntegrationPermissions.PortalView)]),
        new("viewer", "مستعرض الأنظمة المصدر", "Source systems viewer", [new(IntegrationPermissions.SourceRead)], AssignToEveryone: true),
    ];
}
