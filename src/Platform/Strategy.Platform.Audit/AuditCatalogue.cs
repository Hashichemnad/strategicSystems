using Strategy.Platform.Access;

namespace Strategy.Platform.Audit;

public static class AuditPermissions
{
    public const string LogView = "audit.log.view";
    public const string LogExport = "audit.log.export";
    public const string ChainVerify = "audit.chain.verify";
}

internal sealed class AuditCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(AuditComponent.SystemCode, SystemKind.Platform, "سجل التدقيق", "Audit trail", 50);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(AuditPermissions.LogView, "عرض سجل التدقيق الشامل", "View the global audit log", Scopable: false),
        new(AuditPermissions.LogExport, "تصدير سجل التدقيق", "Export the audit log", Scopable: false),
        new(AuditPermissions.ChainVerify, "التحقق من سلامة سجل التدقيق", "Verify the audit chain", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول سجل التدقيق", "Audit administrator",
            [new(AuditPermissions.LogView), new(AuditPermissions.LogExport), new(AuditPermissions.ChainVerify)], RequiresMfa: true),
        new("auditor", "مدقق", "Auditor", [new(AuditPermissions.LogView), new(AuditPermissions.LogExport)]),
    ];
}
