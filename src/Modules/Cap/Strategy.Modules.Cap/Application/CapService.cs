using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Cap.Application;

/// <summary>
/// Operations shared by the API handlers, approval hooks, the source-system contract and the integrations (docs/design/cap.md §5). Every
/// method expects an open transaction on <see cref="CapDbContext"/> (ADR 0023) and saves its changes.
/// </summary>
internal sealed class CapService(
    CapDbContext db, CapReader reader, CapDeadlines deadlines, CapNotifier notifier, INumberingService numbering, IRecordRoleService recordRoles,
    IIntegrationEventPublisher events, IClock clock)
{
    public async Task<CapSource> CreateSourceAsync(SourceValues values, string origin, string? linkedType, Guid? linkedId, CancellationToken ct)
    {
        var number = await numbering.NextAsync(db, CapPermissions.SourceNumbering, new NumberingContext(values.ReceivedOn), ct).ConfigureAwait(false);
        var source = CapSource.Create(number, values, origin, linkedType, linkedId);
        db.Sources.Add(source);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return source;
    }

    /// <summary>Validation of remark values; field paths for the dialog.</summary>
    public async Task<List<FieldError>> ValidateRemarkAsync(RemarkValues v, ILookupService lookups, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key, Dictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        if (string.IsNullOrWhiteSpace(v.TextAr) && string.IsNullOrWhiteSpace(v.TextEn))
        {
            Add("textEn", "validation.atLeastOneLanguage");
        }

        foreach (var (field, value) in new[] { ("textAr", v.TextAr), ("textEn", v.TextEn) })
        {
            if (value is { Length: > CapSource.TextMaxLength })
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = CapSource.TextMaxLength });
            }
        }

        if (!CapPriorities.All.Contains(v.Priority))
        {
            Add("priority", "validation.invalid");
        }

        if (!await lookups.IsActiveCodeAsync(CapPermissions.RemarkTypeLookup, v.TypeCode, ct).ConfigureAwait(false))
        {
            Add("typeCode", "validation.invalid");
        }

        if (v.CriterionCode is { } c && !await lookups.IsActiveCodeAsync(CapPermissions.CriterionLookup, c, ct).ConfigureAwait(false))
        {
            Add("criterionCode", "validation.invalid");
        }

        if (v.DepartmentUnitId == Guid.Empty || await reader.UnitAsync(v.DepartmentUnitId, ct).ConfigureAwait(false) is null)
        {
            Add("departmentUnitId", v.DepartmentUnitId == Guid.Empty ? "validation.required" : "validation.invalid");
        }

        if (v.SectionUnitId is { } s && await reader.UnitAsync(s, ct).ConfigureAwait(false) is null)
        {
            Add("sectionUnitId", "validation.invalid");
        }

        if (v.OwnerEmployeeId is { } o && await reader.EmployeeAsync(o, ct).ConfigureAwait(false) is null)
        {
            Add("ownerEmployeeId", "validation.invalid");
        }

        return errors;
    }

    /// <summary>Creates a remark under the source: number from the department code, owner default, due date by priority, roles, notification.</summary>
    public async Task<Remark> CreateRemarkAsync(
        CapSource source, RemarkValues values, DateOnly assignedOn, string? externalKey, string? linkedType, Guid? linkedId, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (values.OwnerEmployeeId is null)
        {
            values = values with { OwnerEmployeeId = await reader.DefaultOwnerAsync(values.DepartmentUnitId, values.SectionUnitId, ct).ConfigureAwait(false) };
        }

        var unit = await reader.UnitAsync(values.DepartmentUnitId, ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, CapPermissions.RemarkNumbering, new NumberingContext(assignedOn, unit?.Code), ct).ConfigureAwait(false);
        var (dueOn, dueAt) = await deadlines.DueAsync(values.SectionUnitId ?? values.DepartmentUnitId, assignedOn, settings.DueDays(values.Priority), settings.CountWorkingDays, ct)
            .ConfigureAwait(false);
        var remark = Remark.Create(number, source.Id, values, assignedOn, dueOn, dueAt, externalKey, linkedType, linkedId);
        db.Remarks.Add(remark);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await SyncRolesAsync(remark, ct).ConfigureAwait(false);
        await notifier.SendAsync(CapNotifications.RemarkAssigned, [remark.OwnerEmployeeId], CapNotifier.Values(remark), Link(remark), db, ct).ConfigureAwait(false);
        await PublishAsync(remark, source, ct).ConfigureAwait(false);
        return remark;
    }

    /// <summary>Recomputes the due date after the priority changed (ADR 0122).</summary>
    public async Task RescheduleAsync(Remark remark, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var (dueOn, dueAt) = await deadlines.DueAsync(remark.SectionUnitId ?? remark.DepartmentUnitId, remark.AssignedOn, settings.DueDays(remark.Priority),
            settings.CountWorkingDays, ct).ConfigureAwait(false);
        remark.SetDue(dueOn, dueAt);
    }

    public async Task<CapAction> NewActionAsync(Remark home, ActionValues values, string? externalKey, CancellationToken ct)
    {
        var action = CapAction.Create($"{home.Number}-{home.NextActionNo()}", home.Id, values, externalKey);
        db.Actions.Add(action);
        db.RemarkActions.Add(RemarkAction.Create(home.Id, action.Id, clock.UtcNow, null));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return action;
    }

    /// <summary>Validation of action values (field paths for the dialog).</summary>
    public static async Task<List<FieldError>> ValidateActionAsync(ActionValues v, ILookupService lookups, CapReader reader, bool submitting, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (string.IsNullOrWhiteSpace(v.DetailsAr) && string.IsNullOrWhiteSpace(v.DetailsEn))
        {
            Add("detailsEn", "validation.atLeastOneLanguage");
        }

        if (v.DetailsAr is { Length: > CapSource.TextMaxLength } || v.DetailsEn is { Length: > CapSource.TextMaxLength })
        {
            Add("detailsEn", "validation.maximumLength");
        }

        if (v.TypeCode is { } t && !await lookups.IsActiveCodeAsync(CapPermissions.ActionTypeLookup, t, ct).ConfigureAwait(false))
        {
            Add("typeCode", "validation.invalid");
        }

        if (v.AssignedEmployeeId is { } a && await reader.EmployeeAsync(a, ct).ConfigureAwait(false) is null)
        {
            Add("assignedEmployeeId", "validation.invalid");
        }

        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            Add("plannedEnd", "cap.errors.endBeforeStart");
        }

        if (v.EstimatedCost is < 0)
        {
            Add("estimatedCost", "validation.invalid");
        }

        if (submitting)
        {
            if (v.TypeCode is null)
            {
                Add("typeCode", "validation.required");
            }

            if (v.AssignedEmployeeId is null)
            {
                Add("assignedEmployeeId", "validation.required");
            }

            if (v.PlannedEnd is null)
            {
                Add("plannedEnd", "validation.required");
            }

            if (v.BudgetRequired && v.BudgetAvailable is null)
            {
                Add("budgetAvailable", "validation.required");
            }
        }

        return errors;
    }

    /// <summary>Record roles of a remark (ADR 0125): owner, assignees of its actions, source-system viewers.</summary>
    public async Task SyncRolesAsync(Remark remark, CancellationToken ct)
    {
        var assignees = await db.RemarkActions.AsNoTracking().Where(l => l.RemarkId == remark.Id)
            .Join(db.Actions.AsNoTracking(), l => l.ActionId, a => a.Id, (l, a) => a)
            .Where(a => a.Status != ActionStatuses.Cancelled && a.AssignedEmployeeId != null).Select(a => a.AssignedEmployeeId!.Value).Distinct().ToListAsync(ct)
            .ConfigureAwait(false);
        var source = await db.Sources.AsNoTracking().FirstAsync(s => s.Id == remark.SourceId, ct).ConfigureAwait(false);
        var holders = await recordRoles.GetHoldersAsync(Remark.EntityType, remark.Id, ct).ConfigureAwait(false);
        await SyncRoleAsync(remark.Id, CapPermissions.RemarkOwnerRole, remark.OwnerEmployeeId is { } o ? [o] : [], holders, ct).ConfigureAwait(false);
        await SyncRoleAsync(remark.Id, CapPermissions.AssigneeRole, assignees, holders, ct).ConfigureAwait(false);
        await SyncRoleAsync(remark.Id, CapPermissions.SourceViewerRole, source.ViewerEmployeeIds, holders, ct).ConfigureAwait(false);
    }

    /// <summary>Re-syncs the roles of every remark an action serves.</summary>
    public async Task SyncRolesForActionAsync(Guid actionId, CancellationToken ct)
    {
        foreach (var remark in await RemarksOfAsync(actionId, ct).ConfigureAwait(false))
        {
            await SyncRolesAsync(remark, ct).ConfigureAwait(false);
        }
    }

    public async Task<List<Remark>> RemarksOfAsync(Guid actionId, CancellationToken ct)
    {
        var ids = await db.RemarkActions.Where(l => l.ActionId == actionId).Select(l => l.RemarkId).ToListAsync(ct).ConfigureAwait(false);
        return await db.Remarks.Where(r => ids.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<CapAction>> ActionsOfAsync(Guid remarkId, CancellationToken ct, bool tracked = true)
    {
        var ids = db.RemarkActions.Where(l => l.RemarkId == remarkId).Select(l => l.ActionId);
        return await (tracked ? db.Actions : db.Actions.AsNoTracking()).Where(a => ids.Contains(a.Id)).OrderBy(a => a.Number).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-derives the remark's status from its actions (docs/design/cap.md §4.1) and reports the change (event, completion notice). Saves.
    /// </summary>
    public async Task RecomputeAsync(Remark remark, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var actions = await ActionsOfAsync(remark.Id, ct, tracked: false).ConfigureAwait(false);
        var target = RemarkProgress.Derive(remark.Status, actions.Select(a => new RemarkProgress.ActionState(a.ValidationStatus, a.Status)).ToList(),
            settings.EffectivenessVerification);
        if (target is null || target == remark.Status)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        if (RemarkProgress.ActionFor(remark.Status, target) is not { } action)
        {
            return;
        }

        DateOnly? verificationDue = null;
        if (target == CapRemarkStatuses.Verification)
        {
            verificationDue = (await deadlines.TodayAsync(ct).ConfigureAwait(false)).AddMonths(settings.EffectivenessMonths);
        }

        var applied = remark.Apply(action, clock.UtcNow, verificationDue: verificationDue);
        if (applied.IsFailure)
        {
            return;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (target == CapRemarkStatuses.Completed)
        {
            await notifier.SendAsync(CapNotifications.RemarkCompleted, [remark.OwnerEmployeeId], CapNotifier.Values(remark), Link(remark), db, ct,
                [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
        }

        await PublishAsync(remark, null, ct).ConfigureAwait(false);
    }

    /// <summary>Reports a remark's status to the source system (outbox, ADR 0123).</summary>
    public async Task PublishAsync(Remark remark, CapSource? source, CancellationToken ct)
    {
        source ??= await db.Sources.AsNoTracking().FirstAsync(s => s.Id == remark.SourceId, ct).ConfigureAwait(false);
        await events.PublishAsync(new CapRemarkStatusChanged(remark.Id, remark.Number, source.Id, source.LinkedType, source.LinkedId, remark.LinkedType, remark.LinkedId,
            remark.ExternalKey, remark.Status, remark.Stage, clock.UtcNow), db, ct).ConfigureAwait(false);
    }

    public async Task PublishActionCompletedAsync(CapAction action, CancellationToken ct)
    {
        var remarks = await db.RemarkActions.AsNoTracking().Where(l => l.ActionId == action.Id).Select(l => l.RemarkId).ToListAsync(ct).ConfigureAwait(false);
        await events.PublishAsync(new CapActionCompleted(action.Id, action.Number, remarks, clock.UtcNow), db, ct).ConfigureAwait(false);
    }

    public static string Link(Remark remark) => $"/cap/remarks/{remark.Id:D}";

    public static string Link(CapAction action) => $"/cap/actions/{action.Id:D}";

    private async Task SyncRoleAsync(Guid remarkId, string role, IReadOnlyList<Guid> wanted, IReadOnlyList<RecordRoleHolder> holders, CancellationToken ct)
    {
        var current = holders.Where(h => h.RoleCode == role && h.PrincipalKind == RecordPrincipalKinds.Employee).Select(h => h.PrincipalId).ToHashSet();
        foreach (var employee in wanted.Where(e => !current.Contains(e)))
        {
            await recordRoles.GrantInTransactionAsync(db, Remark.EntityType, remarkId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }

        foreach (var employee in current.Where(e => !wanted.Contains(e)))
        {
            await recordRoles.RevokeInTransactionAsync(db, Remark.EntityType, remarkId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }
    }
}

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
