using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Access.Domain;

/// <summary>Principal kinds of role assignments and system-access grants.</summary>
public static class PrincipalKinds
{
    public const string User = "user";

    /// <summary>Every user whose employee is in the unit or below it.</summary>
    public const string Unit = "unit";

    public const string Everyone = "everyone";

    public static readonly IReadOnlyList<string> All = [User, Unit, Everyone];
}

/// <summary>A system in the registry (permission prefix). Modules can be switched off; platform areas cannot.</summary>
public sealed class AccessSystem : AuditableEntity
{
    private AccessSystem()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Kind { get; private set; } = "module";

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public Guid? OwningUnitId { get; private set; }

    public int Sort { get; private set; }

    public bool IsPlatform => Kind == SystemKinds.Platform;

    public static AccessSystem Create(SystemDefinition definition, bool enabled) => new()
    {
        Code = definition.Code,
        Kind = definition.Kind == SystemKind.Platform ? SystemKinds.Platform : SystemKinds.Module,
        NameAr = definition.NameAr,
        NameEn = definition.NameEn,
        Sort = definition.Sort,
        Enabled = definition.Kind == SystemKind.Platform || enabled,
    };

    public void SyncDefinition(SystemDefinition definition)
    {
        Kind = definition.Kind == SystemKind.Platform ? SystemKinds.Platform : SystemKinds.Module;
        NameAr = definition.NameAr;
        NameEn = definition.NameEn;
        Sort = definition.Sort;
        if (IsPlatform)
        {
            Enabled = true;
        }
    }

    public Kernel.Results.Result Configure(bool enabled, Guid? owningUnitId)
    {
        if (IsPlatform && !enabled)
        {
            return Kernel.Results.Error.Rule("access.system.platformAlwaysOn", "access.errors.platformAlwaysOn");
        }

        Enabled = enabled;
        OwningUnitId = owningUnitId;
        return Kernel.Results.Result.Success();
    }
}

public static class SystemKinds
{
    public const string Platform = "platform";
    public const string Module = "module";
}

/// <summary>A permission of the catalogue, synchronised from code at start-up (labels follow the code).</summary>
public sealed class Permission
{
    public string Code { get; set; } = string.Empty;

    public string SystemCode { get; set; } = string.Empty;

    public string ObjectCode { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;

    public bool Scopable { get; set; }

    public int Sort { get; set; }
}

/// <summary>A system role: a named set of permissions, each with a data scope.</summary>
public sealed class Role : AuditableEntity
{
    public const int CodeMaxLength = 64;
    public const int NameMaxLength = 200;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public string SystemCode { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public bool Builtin { get; private set; }

    public bool Active { get; private set; } = true;

    public bool RequiresMfa { get; private set; }

    /// <summary>Locked roles keep their permissions and stay active, so administration can never lock itself out.</summary>
    public bool Locked { get; private set; }

    public IReadOnlyList<RolePermission> Permissions => _permissions;

    /// <summary>Stable key across installations: <c>&lt;system&gt;.&lt;code&gt;</c>.</summary>
    public string Key => $"{SystemCode}.{Code}";

    public static Role Create(
        string systemCode, string code, string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn,
        bool requiresMfa, bool builtin = false, bool locked = false) => new()
        {
            SystemCode = systemCode,
            Code = code,
            NameAr = Clean(nameAr),
            NameEn = Clean(nameEn),
            DescriptionAr = Clean(descriptionAr),
            DescriptionEn = Clean(descriptionEn),
            RequiresMfa = requiresMfa,
            Builtin = builtin,
            Locked = locked,
        };

    public Kernel.Results.Result Update(string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, bool requiresMfa, bool active)
    {
        if (Locked && (!active || !requiresMfa))
        {
            return Kernel.Results.Error.Rule("access.role.locked", "access.errors.roleLocked");
        }

        NameAr = Clean(nameAr);
        NameEn = Clean(nameEn);
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
        RequiresMfa = requiresMfa;
        Active = active;
        return Kernel.Results.Result.Success();
    }

    /// <summary>Replaces the permission set. Returns false when a locked role would lose a permission.</summary>
    public Kernel.Results.Result SetPermissions(IReadOnlyCollection<(string Permission, string Scope)> permissions)
    {
        if (Locked && _permissions.Any(p => permissions.All(n => n.Permission != p.PermissionCode || n.Scope != p.Scope)))
        {
            return Kernel.Results.Error.Rule("access.role.locked", "access.errors.roleLocked");
        }

        _permissions.RemoveAll(p => permissions.All(n => n.Permission != p.PermissionCode));
        foreach (var (permission, scope) in permissions)
        {
            var existing = _permissions.Find(p => p.PermissionCode == permission);
            if (existing is null)
            {
                _permissions.Add(new RolePermission { RoleId = Id, PermissionCode = permission, Scope = scope });
            }
            else
            {
                existing.Scope = scope;
            }
        }

        return Kernel.Results.Result.Success();
    }

    /// <summary>Seeding: makes sure a locked role still holds every permission its seed defines.</summary>
    public void EnsurePermission(string permission, string scope)
    {
        if (_permissions.All(p => p.PermissionCode != permission))
        {
            _permissions.Add(new RolePermission { RoleId = Id, PermissionCode = permission, Scope = scope });
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>A <see cref="DataScopes"/> code.</summary>
    public string Scope { get; set; } = "all";
}

/// <summary>Who holds a role, from when to when (dates inclusive). Ended, never deleted.</summary>
public sealed class RoleAssignment : AuditableEntity
{
    private RoleAssignment()
    {
    }

    public Guid RoleId { get; private set; }

    public string PrincipalKind { get; private set; } = PrincipalKinds.User;

    public Guid? PrincipalId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public string? Note { get; private set; }

    public Guid? TransferId { get; private set; }

    public static RoleAssignment Create(Guid roleId, string principalKind, Guid? principalId, DateOnly validFrom, DateOnly? validTo, string? note, Guid? transferId = null) => new()
    {
        RoleId = roleId,
        PrincipalKind = principalKind,
        PrincipalId = principalKind == PrincipalKinds.Everyone ? null : principalId,
        ValidFrom = validFrom,
        ValidTo = validTo,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        TransferId = transferId,
    };

    public bool IsActiveOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || ValidTo >= date);

    /// <summary>Ends the assignment on <paramref name="lastDay"/> (inclusive); an assignment not yet started ends before it starts.</summary>
    public void End(DateOnly lastDay)
    {
        ValidTo = lastDay < ValidFrom ? ValidFrom.AddDays(-1) : lastDay;
    }
}

/// <summary>System access (layer 1): may the principal open the system at all.</summary>
public sealed class SystemAccessGrant : AuditableEntity
{
    private SystemAccessGrant()
    {
    }

    public string SystemCode { get; private set; } = string.Empty;

    public string PrincipalKind { get; private set; } = PrincipalKinds.User;

    public Guid? PrincipalId { get; private set; }

    public bool Active { get; private set; } = true;

    public static SystemAccessGrant Create(string systemCode, string principalKind, Guid? principalId) => new()
    {
        SystemCode = systemCode,
        PrincipalKind = principalKind,
        PrincipalId = principalKind == PrincipalKinds.Everyone ? null : principalId,
    };

    public void Revoke() => Active = false;
}

/// <summary>A record role type (e.g. <c>sample.item</c> / <c>editor</c>) and the permissions it gives on its record.</summary>
public sealed class RecordRoleType : AuditableEntity
{
    private readonly List<RecordRolePermission> _permissions = [];

    private RecordRoleType()
    {
    }

    public string SystemCode { get; private set; } = string.Empty;

    public string ResourceType { get; private set; } = string.Empty;

    public string RoleCode { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public IReadOnlyList<RecordRolePermission> Permissions => _permissions;

    public static RecordRoleType Create(string systemCode, RecordRoleDefinition definition)
    {
        var type = new RecordRoleType
        {
            SystemCode = systemCode,
            ResourceType = definition.ResourceType,
            RoleCode = definition.RoleCode,
            NameAr = definition.NameAr,
            NameEn = definition.NameEn,
        };
        type.SetPermissions(definition.Permissions);
        return type;
    }

    public void SyncNames(RecordRoleDefinition definition)
    {
        NameAr = definition.NameAr;
        NameEn = definition.NameEn;
    }

    public void SetPermissions(IEnumerable<string> permissions)
    {
        var wanted = permissions.Distinct(StringComparer.Ordinal).ToList();
        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
        foreach (var permission in wanted.Where(w => _permissions.All(p => p.PermissionCode != w)))
        {
            _permissions.Add(new RecordRolePermission { TypeId = Id, PermissionCode = permission });
        }
    }
}

public sealed class RecordRolePermission
{
    public Guid TypeId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;
}

/// <summary>A person's role on one record (CLAUDE.md §7 <c>access.record_role</c>). Ended, never deleted.</summary>
public sealed class RecordRoleGrant : AuditableEntity
{
    private RecordRoleGrant()
    {
    }

    public string ResourceType { get; private set; } = string.Empty;

    public Guid ResourceId { get; private set; }

    public string PrincipalKind { get; private set; } = RecordPrincipalKinds.Employee;

    public Guid PrincipalId { get; private set; }

    public string RoleCode { get; private set; } = string.Empty;

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public static RecordRoleGrant Create(
        string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode, DateTimeOffset validFrom, DateTimeOffset? validTo) => new()
        {
            ResourceType = resourceType,
            ResourceId = resourceId,
            PrincipalKind = principalKind,
            PrincipalId = principalId,
            RoleCode = roleCode,
            ValidFrom = validFrom,
            ValidTo = validTo,
        };

    public void End(DateTimeOffset at) => ValidTo = at;
}

/// <summary>Pending migration of the Phase 1 administrator flag (ADR 0016): converted into role assignments by the seeder.</summary>
public sealed class PendingAdministratorGrant
{
    public Guid UserId { get; set; }
}

/// <summary>Log of a bulk role transfer (SPEC §0.2). The hash-chained audit trail arrives in Phase 3.</summary>
public sealed class RoleTransferLog : AuditableEntity
{
    private RoleTransferLog()
    {
    }

    public string RoleKeys { get; private set; } = string.Empty;

    public string FromDescription { get; private set; } = string.Empty;

    public string ToDescription { get; private set; } = string.Empty;

    public int MovedCount { get; private set; }

    public string? Reason { get; private set; }

    public static RoleTransferLog Create(Guid id, string roleKeys, string from, string to, int moved, string? reason) => new()
    {
        Id = id,
        RoleKeys = roleKeys,
        FromDescription = from,
        ToDescription = to,
        MovedCount = moved,
        Reason = reason,
    };
}
