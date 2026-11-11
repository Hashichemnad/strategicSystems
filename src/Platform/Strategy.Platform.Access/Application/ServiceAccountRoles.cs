using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Access.Application;

/// <summary>A role an integration client's service account may hold (ADR 0191), with its permissions and data scopes.</summary>
public sealed record ServiceAccountRoleOption(
    Guid RoleId, string SystemCode, string Code, string? NameAr, string? NameEn, IReadOnlyList<RolePermissionDto> Permissions, bool Assigned);

/// <summary>
/// Role assignments of a service account (an integration client, ADR 0191) through the normal assignment rules (validation, audit, cache
/// invalidation) — the client's permissions and data scopes are ordinary role assignments.
/// </summary>
public sealed class ServiceAccountRoles
{
    private readonly AccessAdministration _administration;

    internal ServiceAccountRoles(AccessAdministration administration)
    {
        _administration = administration;
    }

    /// <summary>Active roles of <paramref name="systems"/>, marking those the account holds today.</summary>
    public async Task<IReadOnlyList<ServiceAccountRoleOption>> ListAsync(Guid userId, IReadOnlyCollection<string> systems, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(systems);
        var held = (await _administration.ListAssignmentsAsync(null, userId, null, includeEnded: false, ct).ConfigureAwait(false))
            .Where(a => a.Active).Select(a => a.RoleId).ToHashSet();
        var roles = await _administration.ListRolesAsync(null, ct).ConfigureAwait(false);
        return roles.Where(r => r.Active && systems.Contains(r.SystemCode))
            .Select(r => new ServiceAccountRoleOption(r.Id, r.SystemCode, r.Code, r.NameAr, r.NameEn, r.Permissions, held.Contains(r.Id)))
            .OrderBy(r => r.SystemCode, StringComparer.Ordinal).ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Makes the account hold exactly <paramref name="roleIds"/> (all must be roles of <paramref name="systems"/>).</summary>
    public async Task<Result> SetAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, IReadOnlyCollection<string> systems, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var options = await ListAsync(userId, systems, ct).ConfigureAwait(false);
        if (roleIds.Any(id => options.All(o => o.RoleId != id)))
        {
            return Error.Validation([new FieldError("roleIds", "validation.unknownReference", "validation.unknownReference")]);
        }

        var current = (await _administration.ListAssignmentsAsync(null, userId, null, includeEnded: false, ct).ConfigureAwait(false)).Where(a => a.Active).ToList();
        foreach (var assignment in current.Where(a => !roleIds.Contains(a.RoleId)))
        {
            var ended = await _administration.EndAssignmentAsync(assignment.Id, null, assignment.Version, ct).ConfigureAwait(false);
            if (ended.IsFailure)
            {
                return ended.Error;
            }
        }

        foreach (var roleId in roleIds.Where(id => current.All(a => a.RoleId != id)))
        {
            var created = await _administration.CreateAssignmentAsync(
                new AssignmentInput(roleId, Domain.PrincipalKinds.User, userId, null, null, "integration client"), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created.Error;
            }
        }

        return Result.Success();
    }
}
