using Strategy.Modules.Sample.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Sample.Application;

/// <summary>Permission codes (<c>&lt;system&gt;.&lt;object&gt;.&lt;action&gt;</c>), declared with labels in <see cref="SampleAccessCatalogue"/>.</summary>
public static class SamplePermissions
{
    public const string ResourceType = "sample.item";
    public const string Read = "sample.item.read";
    public const string Create = "sample.item.create";
    public const string Update = "sample.item.update";

    /// <summary>Record role: may read and edit one item (granted by an editor of the item).</summary>
    public const string EditorRole = "editor";

    public const string NumberingFormat = "sample.item";
}

/// <summary>How a sample item maps to data scopes (ADR 0015): owner employee and unit, plus record roles by id.</summary>
public static class SampleScope
{
    public static readonly ScopeMap<SampleItem> Map = new(
        SamplePermissions.ResourceType, x => x.Id, ownerEmployee: x => x.OwnerEmployeeId, ownerUser: x => x.CreatedBy, unit: x => x.UnitId);
}

/// <summary>The template module's catalogue: permissions, default roles and record roles (docs/design/access.md).</summary>
internal sealed class SampleAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("sample", SystemKind.Module, "النظام التجريبي", "Sample system", 900);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(SamplePermissions.Read, "عرض البنود", "View items"),
        new(SamplePermissions.Create, "إنشاء البنود", "Create items", Scopable: false),
        new(SamplePermissions.Update, "تعديل البنود", "Edit items"),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("viewer", "مستعرض", "Viewer", [new(SamplePermissions.Read, DataScope.All)]),
        new("editor", "محرر", "Editor",
            [new(SamplePermissions.Read, DataScope.All), new(SamplePermissions.Create), new(SamplePermissions.Update, DataScope.Own)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(SamplePermissions.ResourceType, SamplePermissions.EditorRole, "محرر البند", "Item editor", [SamplePermissions.Read, SamplePermissions.Update]),
    ];

    public static readonly NumberingDefinition Numbering = new(
        SamplePermissions.NumberingFormat, "sample", "رقم البند التجريبي", "Sample item number", "SMP-{YYYY}-{SEQ:4}");
}
