using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;

namespace Strategy.Platform.Access.Application;

/// <summary>
/// System → Settings → Members (SPEC-ADDENDUM §D1, ADRs 0166 / 0167): lists of administrators, users and extra roles of one system over the
/// existing role assignments and system-access grants. Every change goes through <see cref="AccessAdministration"/> (same rules, audit and
/// cache invalidation); only the caller's right to manage this system's members is decided here.
/// </summary>
internal sealed class MemberAdministration(
    AccessDbContext db,
    AccessAdministration administration,
    IAccessService access,
    ISystemState systemState,
    PrincipalNames names,
    IEnumerable<IAccessCatalogueSource> sources,
    INotificationService notifications,
    IAuditReason auditReason,
    IClock clock)
{
    public const string Administrators = "administrators";
    public const string Users = "users";
    public const string ExtraPrefix = "extra:";
    public const string AddedTemplateKey = "access.members.added";
    public const string RemovedTemplateKey = "access.members.removed";

    private const int MaxRolesPerList = 20;

    // ---- Who may manage which system ----

    /// <summary>The systems whose Members the caller may manage (navigation, the Members hub).</summary>
    public async Task<IReadOnlyList<SystemDto>> ManageableSystemsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var platform = await access.CanAsync(user, AccessPermissions.AssignmentManage, cancellationToken: ct).ConfigureAwait(false);
        var codes = Defaults.Keys.ToList();
        var systems = await db.Systems.AsNoTracking().Where(s => codes.Contains(s.Code)).OrderBy(s => s.Sort).ThenBy(s => s.Code).ToListAsync(ct)
            .ConfigureAwait(false);
        var result = new List<SystemDto>();
        foreach (var system in systems)
        {
            if (await systemState.IsEnabledAsync(system.Code, ct).ConfigureAwait(false)
                && (platform || await access.CanAsync(user, SystemMemberRoles.ManagePermission(system.Code), cancellationToken: ct).ConfigureAwait(false)))
            {
                result.Add(AccessAdministration.ToDto(system, new Dictionary<Guid, Organisation.Contracts.UnitReference>()));
            }
        }

        return result;
    }

    /// <summary>404 for an unknown / disabled system or one without member roles; 403 without the right for this system.</summary>
    private async Task<Result<MemberScope>> AuthoriseAsync(ClaimsPrincipal user, string code, CancellationToken ct)
    {
        if (!Defaults.TryGetValue(code, out var defaults)
            || await db.Systems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct).ConfigureAwait(false) is not { } system
            || !await systemState.IsEnabledAsync(code, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var platform = await access.CanAsync(user, AccessPermissions.AssignmentManage, cancellationToken: ct).ConfigureAwait(false);
        if (!platform && !await access.CanAsync(user, SystemMemberRoles.ManagePermission(code), cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var configured = await db.MemberRoles.AsNoTracking().FirstOrDefaultAsync(c => c.SystemCode == code, ct).ConfigureAwait(false);
        var memberRoles = configured?.ToRoles() ?? defaults;
        var roles = await db.Roles.AsNoTracking().Where(r => r.SystemCode == code).ToListAsync(ct).ConfigureAwait(false);
        var byCode = roles.Where(r => r.Active).ToDictionary(r => r.Code, StringComparer.Ordinal);
        List<Role> Resolve(IEnumerable<string> codes) => codes.Distinct(StringComparer.Ordinal).Select(c => byCode.GetValueOrDefault(c)).OfType<Role>().ToList();

        var lists = new List<MemberList> { new(Administrators, Administrators, Resolve(memberRoles.AdministratorRoles)), new(Users, Users, Resolve(memberRoles.UserRoles)) };
        lists.AddRange(Resolve(memberRoles.ExtraRoles).Select(r => new MemberList(ExtraPrefix + r.Code, "extra", [r])));
        return new MemberScope(system, platform, lists, roles.Select(r => r.Id).ToList());
    }

    // ---- Lists ----

    public async Task<Result<SystemMembersDto>> ListAsync(ClaimsPrincipal user, string code, CancellationToken ct)
    {
        var scope = await AuthoriseAsync(user, code, ct).ConfigureAwait(false);
        return scope.IsFailure ? scope.Error : await BuildAsync(scope.Value, ct).ConfigureAwait(false);
    }

    private async Task<SystemMembersDto> BuildAsync(MemberScope scope, CancellationToken ct)
    {
        var today = clock.Today;
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => scope.SystemRoleIds.Contains(a.RoleId) && (a.ValidTo == null || a.ValidTo >= today))
            .OrderBy(a => a.ValidFrom).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        var grants = scope.System.IsPlatform
            ? []
            : await db.SystemAccess.AsNoTracking().Where(g => g.Active && g.SystemCode == scope.System.Code).OrderBy(g => g.CreatedAt).ToListAsync(ct)
                .ConfigureAwait(false);
        var principals = await names.PrincipalsAsync(
            assignments.Select(a => (a.PrincipalKind, a.PrincipalId)).Concat(grants.Select(g => (g.PrincipalKind, g.PrincipalId))), ct).ConfigureAwait(false);

        var otherListRoles = scope.Lists.Where(l => l.Key != Users).SelectMany(l => l.Roles).Select(r => r.Id).ToHashSet();
        var inOtherLists = assignments.Where(a => otherListRoles.Contains(a.RoleId)).Select(a => (a.PrincipalKind, a.PrincipalId)).ToHashSet();
        var grantOf = grants.ToDictionary(g => (g.PrincipalKind, g.PrincipalId));

        var lists = new List<SystemMemberListDto>();
        foreach (var list in scope.Lists)
        {
            var roleIds = list.Roles.Select(r => r.Id).ToHashSet();
            var held = assignments.Where(a => roleIds.Contains(a.RoleId)).GroupBy(a => (a.PrincipalKind, a.PrincipalId)).ToDictionary(g => g.Key, g => g.ToList());
            var keys = held.Keys.ToList();
            if (list.Key == Users)
            {
                keys.AddRange(grants.Select(g => (g.PrincipalKind, g.PrincipalId)).Where(p => !held.ContainsKey(p) && !inOtherLists.Contains(p)));
            }

            var members = keys.Select(key =>
            {
                var rows = held.GetValueOrDefault(key) ?? [];
                var grant = grantOf.GetValueOrDefault(key);
                var id = list.Key == Users && grant is not null ? grant.Id : rows[0].Id;
                var hasAccess = scope.System.IsPlatform || grant is not null;
                return new SystemMemberDto(
                    id,
                    principals[key],
                    rows.Count == 0 ? null : rows.Min(a => a.ValidFrom),
                    rows.Count == 0 || rows.Any(a => a.ValidTo is null) ? null : rows.Max(a => a.ValidTo),
                    rows.Count == 0 ? hasAccess : rows.Any(a => a.IsActiveOn(today)),
                    hasAccess,
                    rows.Select(a => list.Roles.First(r => r.Id == a.RoleId).Code).Distinct(StringComparer.Ordinal).ToList());
            }).ToList();
            lists.Add(new SystemMemberListDto(
                list.Key, list.Kind, list.Roles.Select(r => new SystemMemberRoleDto(r.Code, r.NameAr, r.NameEn)).ToList(), CanManage(scope, list), members));
        }

        return new SystemMembersDto(
            AccessAdministration.ToDto(scope.System, new Dictionary<Guid, Organisation.Contracts.UnitReference>()), scope.Platform, lists);
    }

    // ---- Add ----

    public async Task<Result<SystemMembersDto>> AddAsync(ClaimsPrincipal user, string code, AddSystemMemberInput input, CancellationToken ct)
    {
        var resolved = await AuthoriseAsync(user, code, ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var scope = resolved.Value;
        var list = scope.Lists.FirstOrDefault(l => l.Key == input.List);
        if (list is null)
        {
            return Field("list", "validation.unknownCode");
        }

        if (!CanManage(scope, list))
        {
            return Error.Forbidden("access.members.platformRole");
        }

        var errors = new List<FieldError>();
        if (list.Key == Administrators && input.PrincipalKind is not null && input.PrincipalKind != PrincipalKinds.User)
        {
            errors.Add(new FieldError("principalKind", "access.errors.membersPeopleOnly", "access.errors.membersPeopleOnly"));
        }

        await administration.ValidatePrincipalAsync(input.PrincipalKind, input.PrincipalId, errors, ct).ConfigureAwait(false);
        var from = input.ValidFrom ?? clock.Today;
        if (input.ValidTo is { } to && to < from)
        {
            errors.Add(new FieldError("validTo", "validation.dateRange", "validation.dateRange"));
        }

        if (input.Note?.Trim().Length > 1000)
        {
            errors.Add(new FieldError("note", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 1000 }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var needsGrant = !scope.System.IsPlatform;
        if (list.Roles.Count == 0 && !(list.Key == Users && needsGrant))
        {
            return Error.Rule("access.members.noRoles", "access.errors.membersNoRoles");
        }

        var kind = input.PrincipalKind!;
        var principalId = kind == PrincipalKinds.Everyone ? null : input.PrincipalId;
        auditReason.Reason = Blank(input.Note);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var changed = 0;
        if (needsGrant && !await db.SystemAccess.AnyAsync(
                g => g.Active && g.SystemCode == code && g.PrincipalKind == kind && g.PrincipalId == principalId, ct).ConfigureAwait(false))
        {
            var grant = await administration.GrantSystemAccessAsync(code, kind, principalId, ct).ConfigureAwait(false);
            if (grant.IsFailure)
            {
                return grant.Error;
            }

            changed++;
        }

        foreach (var role in list.Roles)
        {
            var assignment = await administration.CreateAssignmentAsync(
                new AssignmentInput(role.Id, kind, principalId, from, input.ValidTo, input.Note), ct).ConfigureAwait(false);
            if (assignment.IsSuccess)
            {
                changed++;
            }
            else if (assignment.Error.Code != "access.assignment.overlap")
            {
                return assignment.Error;
            }
        }

        if (changed == 0)
        {
            return Error.Conflict("access.members.alreadyMember", "access.errors.membersAlreadyMember");
        }

        if (kind == PrincipalKinds.User && principalId is { } userId)
        {
            await NotifyAsync(AddedTemplateKey, userId, scope.System, list, new Dictionary<string, string>
            {
                ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = input.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—",
            }, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await BuildAsync(scope, ct).ConfigureAwait(false);
    }

    // ---- Remove ----

    public async Task<Result<SystemMembersDto>> RemoveAsync(ClaimsPrincipal user, string code, Guid id, string? reason, CancellationToken ct)
    {
        var resolved = await AuthoriseAsync(user, code, ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var scope = resolved.Value;
        if (reason?.Trim().Length > 1000)
        {
            return Error.Validation([new FieldError("reason", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 1000 })]);
        }

        var today = clock.Today;
        MemberList? list;
        (string Kind, Guid? Id) principal;
        if (await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && scope.SystemRoleIds.Contains(a.RoleId), ct).ConfigureAwait(false)
            is { } found)
        {
            list = scope.Lists.FirstOrDefault(l => l.Roles.Any(r => r.Id == found.RoleId));
            principal = (found.PrincipalKind, found.PrincipalId);
        }
        else if (await db.SystemAccess.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.Active && g.SystemCode == code, ct).ConfigureAwait(false)
                 is { } foundGrant)
        {
            list = scope.Lists.First(l => l.Key == Users);
            principal = (foundGrant.PrincipalKind, foundGrant.PrincipalId);
        }
        else
        {
            return Error.NotFound();
        }

        if (list is null)
        {
            return Error.NotFound();
        }

        if (!CanManage(scope, list))
        {
            return Error.Forbidden("access.members.platformRole");
        }

        auditReason.Reason = Blank(reason);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var roleIds = list.Roles.Select(r => r.Id).ToList();
        var ending = await db.Assignments.AsNoTracking()
            .Where(a => roleIds.Contains(a.RoleId) && a.PrincipalKind == principal.Kind && a.PrincipalId == principal.Id && (a.ValidTo == null || a.ValidTo >= today))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var assignment in ending)
        {
            var ended = await administration.EndAssignmentAsync(assignment.Id, null, assignment.Version, ct).ConfigureAwait(false);
            if (ended.IsFailure)
            {
                return ended.Error;
            }
        }

        var revoked = false;
        if (!scope.System.IsPlatform
            && await db.SystemAccess.AsNoTracking().FirstOrDefaultAsync(
                g => g.Active && g.SystemCode == code && g.PrincipalKind == principal.Kind && g.PrincipalId == principal.Id, ct).ConfigureAwait(false) is { } grant)
        {
            // Users: access ends unless another list still holds the principal. Administrators / extra: unless any role of the system remains.
            var keepRoleIds = list.Key == Users
                ? scope.Lists.Where(l => l.Key != Users).SelectMany(l => l.Roles).Select(r => r.Id).ToList()
                : scope.SystemRoleIds;
            var keep = await db.Assignments.AnyAsync(
                a => keepRoleIds.Contains(a.RoleId) && a.PrincipalKind == principal.Kind && a.PrincipalId == principal.Id
                    && (a.ValidTo == null || a.ValidTo >= today), ct).ConfigureAwait(false);
            if (!keep)
            {
                var result = await administration.RevokeSystemAccessAsync(grant.Id, ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    return result.Error;
                }

                revoked = true;
            }
        }

        if (ending.Count == 0 && !revoked)
        {
            return Error.Rule("access.assignment.alreadyEnded", "access.errors.assignmentEnded");
        }

        if (principal.Kind == PrincipalKinds.User && principal.Id is { } userId)
        {
            await NotifyAsync(RemovedTemplateKey, userId, scope.System, list, new Dictionary<string, string> { ["reason"] = Blank(reason) ?? "—" }, ct)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await BuildAsync(scope, ct).ConfigureAwait(false);
    }

    /// <summary>Authorises a people search for the Members dialog (the caller may lack <c>access.assignment.read</c>).</summary>
    public async Task<Result> AuthoriseSearchAsync(ClaimsPrincipal user, string code, CancellationToken ct)
    {
        var scope = await AuthoriseAsync(user, code, ct).ConfigureAwait(false);
        return scope.IsFailure ? scope.Error : Result.Success();
    }

    // ---- Member-role configuration (platform administrator, Settings) ----

    public async Task<Result<SystemMemberRolesConfigDto>> GetConfigurationAsync(string code, CancellationToken ct)
    {
        if (!Defaults.TryGetValue(code, out var defaults))
        {
            return Error.NotFound();
        }

        var configured = await db.MemberRoles.AsNoTracking().FirstOrDefaultAsync(c => c.SystemCode == code, ct).ConfigureAwait(false);
        return ToDto(code, configured, defaults);
    }

    public async Task<Result<SystemMemberRolesConfigDto>> SetConfigurationAsync(string code, SystemMemberRolesInput input, CancellationToken ct)
    {
        if (!Defaults.TryGetValue(code, out var defaults))
        {
            return Error.NotFound();
        }

        var known = await db.Roles.AsNoTracking().Where(r => r.SystemCode == code).Select(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> Check(string field, IReadOnlyList<string>? codes)
        {
            var list = (codes ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count > MaxRolesPerList)
            {
                errors.Add(new FieldError(field, "access.errors.memberRolesTooMany", "access.errors.memberRolesTooMany", new Dictionary<string, object?> { ["max"] = MaxRolesPerList }));
            }

            foreach (var (role, index) in list.Select((c, i) => (c, i)))
            {
                if (!known.Contains(role))
                {
                    errors.Add(new FieldError($"{field}[{index}]", "validation.unknownReference", "validation.unknownReference"));
                }
                else if (!seen.Add(role))
                {
                    errors.Add(new FieldError($"{field}[{index}]", "access.errors.memberRolesOverlap", "access.errors.memberRolesOverlap"));
                }
            }

            return list;
        }

        var administrators = Check("administratorRoles", input.AdministratorRoles);
        var users = Check("userRoles", input.UserRoles);
        var extra = Check("extraRoles", input.ExtraRoles);
        if (administrators.Count == 0)
        {
            errors.Add(new FieldError("administratorRoles", "validation.notEmpty", "validation.notEmpty"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var roles = new SystemMemberRoles(administrators, users, extra);
        var configured = await db.MemberRoles.FirstOrDefaultAsync(c => c.SystemCode == code, ct).ConfigureAwait(false);
        if (configured is null)
        {
            configured = SystemMemberRolesConfig.Create(code, roles);
            db.MemberRoles.Add(configured);
        }
        else
        {
            configured.Set(roles);
            db.Entry(configured).Property(c => c.Version).OriginalValue = input.Version ?? configured.Version;
            db.Entry(configured).State = EntityState.Modified;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(code, configured, defaults);
    }

    // ---- Helpers ----

    /// <summary>The code defaults per system: every catalogue that declares member roles.</summary>
    private Dictionary<string, SystemMemberRoles> Defaults =>
        _defaults ??= sources.Where(s => s.MemberRoles is not null).DistinctBy(s => s.System.Code)
            .ToDictionary(s => s.System.Code, s => s.MemberRoles!, StringComparer.Ordinal);

    private Dictionary<string, SystemMemberRoles>? _defaults;

    /// <summary>ADR 0167: a delegated administrator never assigns or ends a platform administrator role.</summary>
    private static bool CanManage(MemberScope scope, MemberList list) =>
        scope.Platform || !list.Roles.Any(r => AdministratorRoles.Contains(r.SystemCode, r.Code));

    private async Task NotifyAsync(
        string template, Guid userId, AccessSystem system, MemberList list, Dictionary<string, string> values, CancellationToken ct)
    {
        var (listAr, listEn) = list.Key switch
        {
            Administrators => ("مسؤولو النظام", "System administrators"),
            Users => ("المستخدمون", "Users"),
            _ => (list.Roles[0].NameAr ?? list.Roles[0].NameEn ?? list.Roles[0].Code, list.Roles[0].NameEn ?? list.Roles[0].NameAr ?? list.Roles[0].Code),
        };
        values["systemAr"] = system.NameAr;
        values["systemEn"] = system.NameEn;
        values["listAr"] = listAr;
        values["listEn"] = listEn;
        await notifications.SendAsync(new NotificationRequest(template, [NotificationRecipient.User(userId)], values, "/"), db, ct).ConfigureAwait(false);
    }

    private static SystemMemberRolesConfigDto ToDto(string code, SystemMemberRolesConfig? configured, SystemMemberRoles defaults)
    {
        var current = configured?.ToRoles() ?? defaults;
        return new SystemMemberRolesConfigDto(
            code, current.AdministratorRoles, current.UserRoles, current.ExtraRoles,
            new SystemMemberRolesDefaultsDto(defaults.AdministratorRoles, defaults.UserRoles, defaults.ExtraRoles), configured is null, configured?.Version ?? 0);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error Field(string field, string key) => Error.Validation([new FieldError(field, key, key)]);

    private sealed record MemberList(string Key, string Kind, IReadOnlyList<Role> Roles);

    private sealed record MemberScope(AccessSystem System, bool Platform, IReadOnlyList<MemberList> Lists, IReadOnlyList<Guid> SystemRoleIds);
}

/// <summary>Bilingual notifications to a person added to / removed from a system's Members list (editable in Settings → Notifications).</summary>
internal static class MemberNotifications
{
    public static readonly NotificationTemplateDefinition Added = new(
        MemberAdministration.AddedTemplateKey, "access", "إضافة إلى أعضاء نظام", "Added to a system's members",
        "أُضفت إلى {{systemAr}}: {{listAr}}", "You were added to {{systemEn}}: {{listEn}}",
        "مرحباً {{name}}،\nأُضفت إلى قائمة «{{listAr}}» في نظام {{systemAr}}.\nالفترة: من {{from}} إلى {{to}}.\n{{button}}",
        "Hello {{name}},\nYou were added to the list \"{{listEn}}\" of {{systemEn}}.\nPeriod: from {{from}} to {{to}}.\n{{button}}",
        "فتح النظام", "Open the application", ["name", "systemAr", "systemEn", "listAr", "listEn", "from", "to"]);

    public static readonly NotificationTemplateDefinition Removed = new(
        MemberAdministration.RemovedTemplateKey, "access", "إزالة من أعضاء نظام", "Removed from a system's members",
        "أُزلت من {{systemAr}}: {{listAr}}", "You were removed from {{systemEn}}: {{listEn}}",
        "مرحباً {{name}}،\nأُزلت من قائمة «{{listAr}}» في نظام {{systemAr}}.\nالسبب: {{reason}}\n{{button}}",
        "Hello {{name}},\nYou were removed from the list \"{{listEn}}\" of {{systemEn}}.\nReason: {{reason}}\n{{button}}",
        "فتح النظام", "Open the application", ["name", "systemAr", "systemEn", "listAr", "listEn", "reason"]);
}

/// <param name="List"><c>administrators</c>, <c>users</c> or <c>extra:&lt;roleCode&gt;</c>.</param>
/// <param name="PrincipalKind"><c>user</c>, <c>unit</c> (and its sub-units) or <c>everyone</c>; administrators are people only.</param>
/// <param name="PrincipalId">The user or unit (none for everyone).</param>
/// <param name="ValidFrom">First day (default today).</param>
/// <param name="ValidTo">Last day (default open-ended).</param>
/// <param name="Note">Stored on the assignments and used as the audit reason.</param>
public sealed record AddSystemMemberInput(string? List, string? PrincipalKind, Guid? PrincipalId, DateOnly? ValidFrom, DateOnly? ValidTo, string? Note);

public sealed record RemoveSystemMemberInput(string? Reason);

public sealed record SystemMemberRolesInput(IReadOnlyList<string>? AdministratorRoles, IReadOnlyList<string>? UserRoles, IReadOnlyList<string>? ExtraRoles, uint? Version);
