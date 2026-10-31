namespace Strategy.Platform.Access;

/// <summary>Whether a system is switched on in Settings (cached). Platform systems are always on.</summary>
public interface ISystemState
{
    Task<bool> IsEnabledAsync(string systemCode, CancellationToken cancellationToken = default);
}

/// <summary>Who holds a record role: an employee (internal people, also before they have an account) or a user.</summary>
public static class RecordPrincipalKinds
{
    public const string Employee = "employee";
    public const string User = "user";
}

/// <summary>Record roles, granted and revoked by the owning module only (CLAUDE.md §7), never by SQL.</summary>
public interface IRecordRoleService
{
    Task GrantAsync(
        string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode,
        DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null, CancellationToken cancellationToken = default);

    /// <summary>Ends every active grant of <paramref name="roleCode"/> on the record for the principal (no hard delete).</summary>
    Task RevokeAsync(
        string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordRoleHolder>> GetHoldersAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}

public sealed record RecordRoleHolder(string RoleCode, string PrincipalKind, Guid PrincipalId, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo);

/// <summary>A user as the access service needs it. Implemented by Platform.Identity.</summary>
public sealed record UserReference(Guid Id, string UserName, Guid? EmployeeId, bool Active);

/// <summary>User lookups for access checks and administration; implemented by Platform.Identity.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserReference>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Users linked to the given employees.</summary>
    Task<IReadOnlyList<UserReference>> GetByEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Up to 20 internal users whose user name or e-mail contains <paramref name="search"/>, or whose employee matches it.</summary>
    Task<IReadOnlyList<UserReference>> SearchAsync(string search, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Every internal user (access review).</summary>
    Task<IReadOnlyList<UserReference>> ListAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Role facts Platform.Identity needs (MFA policy, last-administrator guard).</summary>
public interface IUserRoleQuery
{
    /// <summary>True when the user currently holds a role that requires multi-factor sign-in.</summary>
    Task<bool> RequiresMfaAsync(Guid userId, Guid? employeeId, CancellationToken cancellationToken = default);

    /// <summary>True when deactivating this user would leave no active access administrator.</summary>
    Task<bool> IsLastAccessAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Grants the platform administrator roles to a user (install command).</summary>
    Task GrantPlatformAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Called during a bulk role transfer so pending approvals and open tasks move too (approval engine, Phase 4).</summary>
public interface IRoleTransferHook
{
    Task OnRoleTransferredAsync(RoleTransfer transfer, CancellationToken cancellationToken);
}

public sealed record RoleTransfer(Guid TransferId, IReadOnlyList<string> RoleKeys, IReadOnlyList<Guid> FromUserIds, IReadOnlyList<Guid> ToUserIds);

/// <summary>
/// Raised by platform services whose data feeds access decisions (organisation structure, employee placement, manager), so
/// cached grants and scopes are rebuilt at the next check instead of after the cache lifetime.
/// </summary>
public static class AccessChangeSignal
{
    private static long _version;

    public static long Version => Interlocked.Read(ref _version);

    public static void Raise() => Interlocked.Increment(ref _version);
}
