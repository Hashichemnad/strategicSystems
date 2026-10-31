using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Application;

/// <summary>Users for Platform.Access (scopes, assignments, review).</summary>
internal sealed class UserDirectory(IdentityStoreDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserReference>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new UserReference(u.Id, u.UserName!, u.EmployeeId, u.Active))
            .ToDictionaryAsync(u => u.Id, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<UserReference>> GetByEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value))
            .Select(u => new UserReference(u.Id, u.UserName!, u.EmployeeId, u.Active))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<UserReference>> SearchAsync(
        string search, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        var term = search.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.PrincipalType == StrategyClaims.InternalPrincipal
                && (u.NormalizedUserName!.Contains(term) || u.NormalizedEmail!.Contains(term)
                    || (u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value))))
            .OrderBy(u => u.UserName).Take(20)
            .Select(u => new UserReference(u.Id, u.UserName!, u.EmployeeId, u.Active))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserReference>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.PrincipalType == StrategyClaims.InternalPrincipal)
            .OrderBy(u => u.UserName)
            .Select(u => new UserReference(u.Id, u.UserName!, u.EmployeeId, u.Active))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal static class IdentityFeatures
{
    /// <summary>Users may create personal access tokens (Settings → Features).</summary>
    public const string AccessTokens = "identity.access-tokens";

    public static readonly FeatureDefinition AccessTokensFeature = new(
        AccessTokens, "identity", "رموز الوصول الشخصية", "Personal access tokens", DefaultEnabled: true);
}

internal sealed class IdentityAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("identity", SystemKind.Platform, "المستخدمون وتسجيل الدخول", "Users and sign-in", 20);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(IdentityPermissions.UserManage, "إدارة المستخدمين", "Manage users", Scopable: false),
        new(IdentityPermissions.ProviderManage, "إدارة مزودي الهوية", "Manage identity providers", Scopable: false),
        new(IdentityPermissions.SecurityManage, "إعدادات الأمان", "Security settings", Scopable: false),
        new(IdentityPermissions.SmtpManage, "إعدادات خادم البريد", "Email server settings", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول المستخدمين وتسجيل الدخول", "Users and sign-in administrator",
            [new(IdentityPermissions.UserManage), new(IdentityPermissions.ProviderManage), new(IdentityPermissions.SecurityManage),
             new(IdentityPermissions.SmtpManage)],
            RequiresMfa: true),
    ];
}
