using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Context;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>Test set-up of access data through the access services (never by SQL): roles, assignments, system access, record roles.</summary>
public static class TestAccess
{
    /// <summary>Assigns <c>&lt;system&gt;.&lt;code&gt;</c> to the user, and system access when the role belongs to a module.</summary>
    public static async Task GrantRoleAsync(IServiceProvider provider, Guid userId, string roleKey)
    {
        (await AccessGrants.AssignRoleAsync(provider, roleKey, AccessGrants.User, userId)).ShouldBeTrue($"role {roleKey} exists");
        var system = roleKey[..roleKey.IndexOf('.', StringComparison.Ordinal)];
        if (system is not ("org" or "identity" or "access" or "settings"))
        {
            await AccessGrants.GrantSystemAccessAsync(provider, system, AccessGrants.User, userId);
        }
    }

    public static async Task GrantRoleAsync(this ApiFactory factory, Guid userId, string roleKey)
    {
        await using var scope = SystemScope(factory);
        await GrantRoleAsync(scope.ServiceProvider, userId, roleKey);
    }

    public static async Task GrantSystemAccessAsync(this ApiFactory factory, string system, string principalKind, Guid? principalId)
    {
        await using var scope = SystemScope(factory);
        await AccessGrants.GrantSystemAccessAsync(scope.ServiceProvider, system, principalKind, principalId);
    }

    /// <summary>Creates a role in <paramref name="system"/> with exactly these permissions and scopes; returns its key.</summary>
    public static async Task<string> CreateRoleAsync(this ApiFactory factory, string system, params (string Permission, DataScope Scope)[] permissions)
    {
        await using var scope = SystemScope(factory);
        var administration = scope.ServiceProvider.GetRequiredService<AccessAdministration>();
        var code = "t-" + Guid.CreateVersion7().ToString("N")[^12..];
        var role = await administration.CreateRoleAsync(new RoleInput(system, code, "دور اختبار", "Test role " + code, null, null, false, null), CancellationToken.None);
        role.IsSuccess.ShouldBeTrue(role.IsFailure ? role.Error.Code : null);
        var updated = await administration.SetRolePermissionsAsync(
            role.Value.Id, permissions.Select(p => new RolePermissionDto(p.Permission, p.Scope.ToCode())).ToList(), role.Value.Version, CancellationToken.None);
        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
        return $"{system}.{code}";
    }

    public static async Task GrantRecordRoleAsync(
        this ApiFactory factory, string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode)
    {
        await using var scope = SystemScope(factory);
        await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(resourceType, resourceId, principalKind, principalId, roleCode);
    }

    public static AsyncServiceScope SystemScope(ApiFactory factory)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
