using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Integration.Api;
using Strategy.Platform.Integration.Application;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Integration.Mcp;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration;

public static class IntegrationPermissions
{
    public const string SystemCode = "integration";
    public const string SourceRead = "integration.source-system.read";
    public const string SourceManage = "integration.source-system.manage";
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
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => IntegrationEndpoints.Map(endpoints.MapPlatformGroup(this));
}

internal sealed class IntegrationCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(IntegrationPermissions.SystemCode, SystemKind.Platform, "التكامل مع الأنظمة الأخرى", "Integrations", 47);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(IntegrationPermissions.SourceRead, "عرض الأنظمة المصدر", "View source systems", Scopable: false),
        new(IntegrationPermissions.SourceManage, "إدارة الأنظمة المصدر", "Manage source systems", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول التكامل", "Integrations administrator",
            [new(IntegrationPermissions.SourceRead), new(IntegrationPermissions.SourceManage)], RequiresMfa: true),
        new("viewer", "مستعرض الأنظمة المصدر", "Source systems viewer", [new(IntegrationPermissions.SourceRead)], AssignToEveryone: true),
    ];
}
