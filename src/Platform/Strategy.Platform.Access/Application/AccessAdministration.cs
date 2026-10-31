using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Access.Application;

/// <summary>Systems, roles, permission matrix, assignments and system access (Settings → Access). Every change drops the cache.</summary>
internal sealed partial class AccessAdministration(
    AccessDbContext db,
    AccessModelCache models,
    PrincipalNames names,
    UserRoleQuery roleQuery,
    IOrgDirectory org,
    IUserDirectory users,
    IClock clock)
{
    // ---- Systems ----

    public async Task<IReadOnlyList<SystemDto>> ListSystemsAsync(CancellationToken ct)
    {
        var systems = await db.Systems.AsNoTracking().OrderBy(s => s.Sort).ThenBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        var units = await names.UnitsAsync(systems.Select(s => s.OwningUnitId).OfType<Guid>(), ct).ConfigureAwait(false);
        return systems.Select(s => ToDto(s, units)).ToList();
    }

    public async Task<Result<SystemDto>> UpdateSystemAsync(string code, bool enabled, Guid? owningUnitId, uint version, CancellationToken ct)
    {
        var system = await db.Systems.FirstOrDefaultAsync(s => s.Code == code, ct).ConfigureAwait(false);
        if (system is null)
        {
            return Error.NotFound();
        }

        if (owningUnitId is { } unitId && await org.GetUnitAsync(unitId, ct).ConfigureAwait(false) is null)
        {
            return Field("owningUnitId", "validation.unknownReference");
        }

        var result = system.Configure(enabled, owningUnitId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        db.Entry(system).Property(s => s.Version).OriginalValue = version;
        await SaveAsync(ct).ConfigureAwait(false);
        var units = await names.UnitsAsync(owningUnitId is { } id ? [id] : [], ct).ConfigureAwait(false);
        return ToDto(system, units);
    }

    // ---- Roles and the permission matrix ----

    public async Task<Result<PermissionMatrixDto>> MatrixAsync(string systemCode, CancellationToken ct)
    {
        var system = await db.Systems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == systemCode, ct).ConfigureAwait(false);
        if (system is null)
        {
            return Error.NotFound();
        }

        var permissions = await db.Permissions.AsNoTracking().Where(p => p.SystemCode == systemCode).OrderBy(p => p.Sort).ToListAsync(ct)
            .ConfigureAwait(false);
        var roles = await ListRolesAsync(systemCode, ct).ConfigureAwait(false);
        var recordRoles = await db.RecordRoleTypes.AsNoTracking().Include(t => t.Permissions).Where(t => t.SystemCode == systemCode)
            .OrderBy(t => t.ResourceType).ThenBy(t => t.RoleCode).ToListAsync(ct).ConfigureAwait(false);
        var units = await names.UnitsAsync(system.OwningUnitId is { } id ? [id] : [], ct).ConfigureAwait(false);
        return new PermissionMatrixDto(
            ToDto(system, units),
            permissions.Select(ToDto).ToList(),
            roles,
            recordRoles.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(string? systemCode, CancellationToken ct)
    {
        var today = clock.Today;
        var query = db.Roles.AsNoTracking().Include(r => r.Permissions).AsQueryable();
        if (systemCode is not null)
        {
            query = query.Where(r => r.SystemCode == systemCode);
        }

        var roles = await query.OrderBy(r => r.SystemCode).ThenBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        var ids = roles.Select(r => r.Id).ToList();
        var counts = await db.Assignments.AsNoTracking()
            .Where(a => ids.Contains(a.RoleId) && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today))
            .GroupBy(a => a.RoleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        return roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(RoleInput input, CancellationToken ct)
    {
        var errors = ValidateRoleText(input);
        if (input.Code is null || !RoleCode().IsMatch(input.Code))
        {
            errors.Add(new FieldError("code", "validation.code", "validation.code"));
        }

        if (input.SystemCode is null || !await db.Systems.AnyAsync(s => s.Code == input.SystemCode, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("systemCode", "validation.unknownReference", "validation.unknownReference"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (await db.Roles.AnyAsync(r => r.SystemCode == input.SystemCode && r.Code == input.Code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("access.role.duplicateCode", "access.errors.duplicateRoleCode");
        }

        var role = Role.Create(input.SystemCode!, input.Code!, input.NameAr, input.NameEn, input.DescriptionAr, input.DescriptionEn, input.RequiresMfa);
        db.Roles.Add(role);
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(role, 0);
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(Guid id, RoleInput input, uint version, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound();
        }

        var errors = ValidateRoleText(input);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var result = role.Update(input.NameAr, input.NameEn, input.DescriptionAr, input.DescriptionEn, input.RequiresMfa, input.Active ?? role.Active);
        if (result.IsFailure)
        {
            return result.Error;
        }

        db.Entry(role).Property(r => r.Version).OriginalValue = version;
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(role, await ActiveAssignmentsAsync(role.Id, ct).ConfigureAwait(false));
    }

    public async Task<Result<RoleDto>> SetRolePermissionsAsync(Guid id, IReadOnlyList<RolePermissionDto> permissions, uint version, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound();
        }

        var catalogue = await db.Permissions.AsNoTracking().Where(p => p.SystemCode == role.SystemCode)
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        var wanted = new List<(string, string)>();
        foreach (var (permission, index) in permissions.Select((p, i) => (p, i)))
        {
            if (!catalogue.TryGetValue(permission.Permission, out var definition))
            {
                errors.Add(new FieldError($"permissions[{index}].permission", "validation.unknownReference", "validation.unknownReference"));
            }
            else if (!DataScopes.TryParse(permission.Scope, out _))
            {
                errors.Add(new FieldError($"permissions[{index}].scope", "validation.unknownCode", "validation.unknownCode"));
            }
            else
            {
                wanted.Add((permission.Permission, definition.Scopable ? permission.Scope : DataScope.All.ToCode()));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var result = role.SetPermissions(wanted.DistinctBy(w => w.Item1).ToList());
        if (result.IsFailure)
        {
            return result.Error;
        }

        db.Entry(role).Property(r => r.Version).OriginalValue = version;
        db.Entry(role).State = EntityState.Modified;
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(role, await ActiveAssignmentsAsync(role.Id, ct).ConfigureAwait(false));
    }

    public async Task<Result<RecordRoleTypeDto>> SetRecordRolePermissionsAsync(Guid id, IReadOnlyList<string> permissions, uint version, CancellationToken ct)
    {
        var type = await db.RecordRoleTypes.Include(t => t.Permissions).FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (type is null)
        {
            return Error.NotFound();
        }

        var known = await db.Permissions.AsNoTracking().Where(p => p.SystemCode == type.SystemCode).Select(p => p.Code).ToListAsync(ct)
            .ConfigureAwait(false);
        var unknown = permissions.Select((p, i) => (p, i)).Where(x => !known.Contains(x.p)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation(unknown.Select(x => new FieldError($"permissions[{x.i}]", "validation.unknownReference", "validation.unknownReference")).ToList());
        }

        type.SetPermissions(permissions);
        db.Entry(type).Property(t => t.Version).OriginalValue = version;
        db.Entry(type).State = EntityState.Modified;
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(type);
    }

    // ---- Assignments ----

    public async Task<IReadOnlyList<AssignmentDto>> ListAssignmentsAsync(Guid? roleId, Guid? userId, string? systemCode, bool includeEnded, CancellationToken ct)
    {
        var today = clock.Today;
        var query = db.Assignments.AsNoTracking().AsQueryable();
        if (roleId is not null)
        {
            query = query.Where(a => a.RoleId == roleId);
        }

        if (userId is not null)
        {
            query = query.Where(a => a.PrincipalKind == PrincipalKinds.User && a.PrincipalId == userId);
        }

        if (systemCode is not null)
        {
            var roleIds = db.Roles.Where(r => r.SystemCode == systemCode).Select(r => r.Id);
            query = query.Where(a => roleIds.Contains(a.RoleId));
        }

        if (!includeEnded)
        {
            query = query.Where(a => a.ValidTo == null || a.ValidTo >= today);
        }

        var assignments = await query.OrderBy(a => a.ValidFrom).ThenBy(a => a.Id).Take(2000).ToListAsync(ct).ConfigureAwait(false);
        return await ToDtosAsync(assignments, ct).ConfigureAwait(false);
    }

    public async Task<Result<AssignmentDto>> CreateAssignmentAsync(AssignmentInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var role = input.RoleId is { } roleId ? await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct).ConfigureAwait(false) : null;
        if (role is null)
        {
            errors.Add(new FieldError("roleId", "validation.unknownReference", "validation.unknownReference"));
        }
        else if (!role.Active)
        {
            errors.Add(new FieldError("roleId", "access.errors.roleInactive", "access.errors.roleInactive"));
        }

        await ValidatePrincipalAsync(input.PrincipalKind, input.PrincipalId, errors, ct).ConfigureAwait(false);
        var from = input.ValidFrom ?? clock.Today;
        if (input.ValidTo is { } to && to < from)
        {
            errors.Add(new FieldError("validTo", "validation.dateRange", "validation.dateRange"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var overlapping = await db.Assignments.AnyAsync(
            a => a.RoleId == role!.Id && a.PrincipalKind == input.PrincipalKind && a.PrincipalId == (input.PrincipalKind == PrincipalKinds.Everyone ? null : input.PrincipalId)
                && (a.ValidTo == null || a.ValidTo >= from) && (input.ValidTo == null || a.ValidFrom <= input.ValidTo), ct).ConfigureAwait(false);
        if (overlapping)
        {
            return Error.Conflict("access.assignment.overlap", "access.errors.assignmentOverlap");
        }

        var assignment = RoleAssignment.Create(role!.Id, input.PrincipalKind!, input.PrincipalId, from, input.ValidTo, input.Note);
        db.Assignments.Add(assignment);
        await SaveAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<AssignmentDto>> EndAssignmentAsync(Guid id, DateOnly? lastDay, uint version, CancellationToken ct)
    {
        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        var end = lastDay ?? clock.Today.AddDays(-1);
        if (assignment.ValidTo is { } current && current <= end)
        {
            return Error.Rule("access.assignment.alreadyEnded", "access.errors.assignmentEnded");
        }

        if (await WouldRemoveLastAdministratorAsync(assignment, ct).ConfigureAwait(false))
        {
            return Error.Rule("access.lastAdministrator", "access.errors.lastAdministrator");
        }

        assignment.End(end);
        db.Entry(assignment).Property(a => a.Version).OriginalValue = version;
        await SaveAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], ct).ConfigureAwait(false))[0];
    }

    // ---- System access ----

    public async Task<IReadOnlyList<SystemAccessDto>> ListSystemAccessAsync(string? systemCode, CancellationToken ct)
    {
        var query = db.SystemAccess.AsNoTracking().Where(g => g.Active);
        if (systemCode is not null)
        {
            query = query.Where(g => g.SystemCode == systemCode);
        }

        var grants = await query.OrderBy(g => g.SystemCode).ThenBy(g => g.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var principals = await names.PrincipalsAsync(grants.Select(g => (g.PrincipalKind, g.PrincipalId)), ct).ConfigureAwait(false);
        return grants.Select(g => new SystemAccessDto(g.Id, g.SystemCode, principals[(g.PrincipalKind, g.PrincipalId)], g.Active)).ToList();
    }

    public async Task<Result<SystemAccessDto>> GrantSystemAccessAsync(string? systemCode, string? principalKind, Guid? principalId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var system = systemCode is null ? null : await db.Systems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == systemCode, ct).ConfigureAwait(false);
        if (system is null || system.IsPlatform)
        {
            errors.Add(new FieldError("systemCode", "validation.unknownReference", "validation.unknownReference"));
        }

        await ValidatePrincipalAsync(principalKind, principalId, errors, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var id = principalKind == PrincipalKinds.Everyone ? null : principalId;
        if (await db.SystemAccess.AnyAsync(g => g.Active && g.SystemCode == systemCode && g.PrincipalKind == principalKind && g.PrincipalId == id, ct)
            .ConfigureAwait(false))
        {
            return Error.Conflict("access.systemAccess.duplicate", "access.errors.systemAccessDuplicate");
        }

        var grant = SystemAccessGrant.Create(systemCode!, principalKind!, id);
        db.SystemAccess.Add(grant);
        await SaveAsync(ct).ConfigureAwait(false);
        var principals = await names.PrincipalsAsync([(grant.PrincipalKind, grant.PrincipalId)], ct).ConfigureAwait(false);
        return new SystemAccessDto(grant.Id, grant.SystemCode, principals[(grant.PrincipalKind, grant.PrincipalId)], grant.Active);
    }

    public async Task<Result> RevokeSystemAccessAsync(Guid id, CancellationToken ct)
    {
        var grant = await db.SystemAccess.FirstOrDefaultAsync(g => g.Id == id && g.Active, ct).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound();
        }

        grant.Revoke();
        await SaveAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // ---- Helpers shared with transfer / review ----

    internal async Task<bool> WouldRemoveLastAdministratorAsync(RoleAssignment assignment, CancellationToken ct)
    {
        if (assignment.PrincipalKind != PrincipalKinds.User || assignment.PrincipalId is not { } userId)
        {
            return false;
        }

        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == assignment.RoleId, ct).ConfigureAwait(false);
        return role.SystemCode == "access" && role.Code == "administrator"
            && await roleQuery.IsLastAccessAdministratorAsync(userId, ct).ConfigureAwait(false);
    }

    internal async Task ValidatePrincipalAsync(string? kind, Guid? id, List<FieldError> errors, CancellationToken ct)
    {
        switch (kind)
        {
            case PrincipalKinds.Everyone:
                return;
            case PrincipalKinds.User when id is { } userId:
                if (!(await users.GetManyAsync([userId], ct).ConfigureAwait(false)).ContainsKey(userId))
                {
                    errors.Add(new FieldError("principalId", "validation.unknownReference", "validation.unknownReference"));
                }

                return;
            case PrincipalKinds.Unit when id is { } unitId:
                if (await org.GetUnitAsync(unitId, ct).ConfigureAwait(false) is null)
                {
                    errors.Add(new FieldError("principalId", "validation.unknownReference", "validation.unknownReference"));
                }

                return;
            case PrincipalKinds.User or PrincipalKinds.Unit:
                errors.Add(new FieldError("principalId", "validation.notEmpty", "validation.notEmpty"));
                return;
            default:
                errors.Add(new FieldError("principalKind", "validation.unknownCode", "validation.unknownCode"));
                return;
        }
    }

    internal async Task<IReadOnlyList<AssignmentDto>> ToDtosAsync(IReadOnlyList<RoleAssignment> assignments, CancellationToken ct)
    {
        var today = clock.Today;
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var principals = await names.PrincipalsAsync(assignments.Select(a => (a.PrincipalKind, a.PrincipalId)), ct).ConfigureAwait(false);
        return assignments.Select(a =>
        {
            var role = roles[a.RoleId];
            return new AssignmentDto(
                a.Id, a.RoleId, role.SystemCode, role.Code, role.NameAr, role.NameEn, principals[(a.PrincipalKind, a.PrincipalId)],
                a.ValidFrom, a.ValidTo, a.Note, a.IsActiveOn(today), a.Version);
        }).ToList();
    }

    private async Task<int> ActiveAssignmentsAsync(Guid roleId, CancellationToken ct)
    {
        var today = clock.Today;
        return await db.Assignments.CountAsync(a => a.RoleId == roleId && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today), ct)
            .ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            models.Invalidate();
        }
    }

    private static List<FieldError> ValidateRoleText(RoleInput input)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(new FieldError("nameAr", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        foreach (var (field, value, max) in new[]
                 {
                     ("nameAr", input.NameAr, Role.NameMaxLength), ("nameEn", input.NameEn, Role.NameMaxLength),
                     ("descriptionAr", input.DescriptionAr, 1000), ("descriptionEn", input.DescriptionEn, 1000),
                 })
        {
            if (value?.Trim().Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength",
                    new Dictionary<string, object?> { ["maxLength"] = max }));
            }
        }

        return errors;
    }

    private static Error Field(string field, string key) => Error.Validation([new FieldError(field, key, key)]);

    internal static SystemDto ToDto(AccessSystem s, IReadOnlyDictionary<Guid, UnitReference> units)
    {
        var unit = s.OwningUnitId is { } id ? units.GetValueOrDefault(id) : null;
        return new SystemDto(s.Code, s.Kind, s.NameAr, s.NameEn, s.Enabled, s.OwningUnitId, unit?.NameAr, unit?.NameEn, s.Sort, s.Version);
    }

    internal static PermissionDto ToDto(Permission p) => new(p.Code, p.SystemCode, p.ObjectCode, p.Action, p.LabelAr, p.LabelEn, p.Scopable);

    internal static RoleDto ToDto(Role r, int activeAssignments) => new(
        r.Id, r.SystemCode, r.Code, r.NameAr, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.Builtin, r.Active, r.RequiresMfa, r.Locked,
        r.Permissions.OrderBy(p => p.PermissionCode, StringComparer.Ordinal).Select(p => new RolePermissionDto(p.PermissionCode, p.Scope)).ToList(),
        activeAssignments, r.Version);

    internal static RecordRoleTypeDto ToDto(RecordRoleType t) => new(
        t.Id, t.SystemCode, t.ResourceType, t.RoleCode, t.NameAr, t.NameEn,
        t.Permissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList(), t.Version);

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex RoleCode();
}

public sealed record RoleInput(
    string? SystemCode, string? Code, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, bool RequiresMfa, bool? Active);

public sealed record AssignmentInput(Guid? RoleId, string? PrincipalKind, Guid? PrincipalId, DateOnly? ValidFrom, DateOnly? ValidTo, string? Note);
