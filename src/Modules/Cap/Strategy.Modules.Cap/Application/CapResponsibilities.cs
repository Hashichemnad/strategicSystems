using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Cap.Application;

/// <summary>
/// CAP → Settings → Responsibilities (ADR 0168): remark owners and action assignees of open records. The change uses the domain methods of the
/// existing *Reassign* command (<see cref="Remark.Reassign"/>), the record-role sync of the remark (ADR 0125) and re-routes approvals in progress.
/// The office acts as the CAP administrator, so the "reassign within the unit" rule of department users does not apply.
/// </summary>
internal sealed class RemarkOwnerResponsibility(CapDbContext db, IAccessService access, CapService service, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<Remark>(notifier)
{
    public override string SystemCode => CapPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("remark-owner", "مسؤول الملاحظة", "Remark owner", Statuses:
    [
        new(CapRemarkStatuses.Assigned, "مسندة", "Assigned"), new(CapRemarkStatuses.Planning, "تخطيط", "Planning"),
        new(CapRemarkStatuses.Implementation, "تنفيذ", "Implementation"), new(CapRemarkStatuses.Verification, "تحقق", "Verification"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var remarks = CapResponsibilityScope.Open(await CapResponsibilityScope.RemarksAsync(db, access, user, cancellationToken).ConfigureAwait(false));
        if (query.PersonId is { } person)
        {
            remarks = remarks.Where(r => r.OwnerEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            remarks = remarks.Where(r => r.DepartmentUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            remarks = remarks.Where(r => r.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = Like.Pattern(text);
            remarks = remarks.Where(r => EF.Functions.ILike(r.Number + " " + (r.TextAr ?? "") + " " + (r.TextEn ?? ""), pattern, "\\"));
        }

        var total = await remarks.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await remarks.OrderBy(r => r.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Remark>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await CapResponsibilityScope.RemarksAsync(db, access, user, cancellationToken, tracked).ConfigureAwait(false)).Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(Remark record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TextAr, record.TextEn, record.Status, record.OwnerEmployeeId, record.DepartmentUnitId,
            CapService.Link(record));
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(Remark record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsOpen ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Remark> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var remark in batch.Records)
        {
            remark.Reassign(batch.NewId);
        }

        if (!await CapResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        foreach (var remark in batch.Records)
        {
            await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        }

        await rerouting.RerouteAsync(new ApprovalReroute(Remark.EntityType, batch.Records.Select(r => r.Id).ToList(), batch.Reason, batch.NewId), db, cancellationToken)
            .ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? CapService.Link(batch.Records[0]) : "/cap").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal sealed class CapActionAssigneeResponsibility(
    CapDbContext db, IAccessService access, CapService service, IApprovalRerouting rerouting, IResponsibilityNotifier notifier) : ResponsibilitySource<CapAction>(notifier)
{
    public override string SystemCode => CapPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("action-assignee", "المسند إليه الإجراء", "Action assignee", Statuses:
    [
        new(ActionStatuses.NotStarted, "لم يبدأ", "Not started"), new(ActionStatuses.InProgress, "قيد التنفيذ", "In progress"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var remarks = await CapResponsibilityScope.RemarksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        if (query.UnitId is { } unit)
        {
            remarks = remarks.Where(r => r.DepartmentUnitId == unit);
        }

        var actions = Scoped(remarks).Where(a => a.Status != ActionStatuses.Completed && a.Status != ActionStatuses.Cancelled);
        actions = query.PersonId is { } person ? actions.Where(a => a.AssignedEmployeeId == person) : actions;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            actions = actions.Where(a => a.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = Like.Pattern(text);
            actions = actions.Where(a => EF.Functions.ILike(a.Number + " " + (a.DetailsAr ?? "") + " " + (a.DetailsEn ?? ""), pattern, "\\"));
        }

        var total = await actions.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await actions.OrderBy(a => a.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<CapAction>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        // The subquery shares the query's tracking: AsNoTracking anywhere would leave the loaded actions untracked.
        var remarks = await CapResponsibilityScope.RemarksAsync(db, access, user, cancellationToken, tracked).ConfigureAwait(false);
        return await Scoped(remarks, tracked).Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(CapAction record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.DetailsAr, record.DetailsEn, record.Status, record.AssignedEmployeeId, null, CapService.Link(record));
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(CapAction record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsOpen ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<CapAction> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var action in batch.Records)
        {
            action.Reassign(batch.NewId);
        }

        if (!await CapResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        foreach (var action in batch.Records)
        {
            await service.SyncRolesForActionAsync(action.Id, cancellationToken).ConfigureAwait(false);
        }

        var ids = batch.Records.Select(a => a.Id).ToList();
        await rerouting.RerouteAsync(new ApprovalReroute(CapAction.EntityType, ids, batch.Reason, batch.NewId), db, cancellationToken).ConfigureAwait(false);
        var remarkIds = await db.RemarkActions.AsNoTracking().Where(l => ids.Contains(l.ActionId)).Select(l => l.RemarkId).Distinct().ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await rerouting.RerouteAsync(new ApprovalReroute(Remark.EntityType, remarkIds, batch.Reason), db, cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? CapService.Link(batch.Records[0]) : "/cap").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    /// <summary>Actions serving at least one remark within the scope.</summary>
    private IQueryable<CapAction> Scoped(IQueryable<Remark> remarks, bool tracked = false) =>
        (tracked ? db.Actions : db.Actions.AsNoTracking())
            .Where(a => db.RemarkActions.Any(l => l.ActionId == a.Id && remarks.Any(r => r.Id == l.RemarkId)));
}

internal static class CapResponsibilityScope
{
    public static async Task<IQueryable<Remark>> RemarksAsync(CapDbContext db, IAccessService access, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await access.ApplyScopeAsync(tracked ? db.Remarks : db.Remarks.AsNoTracking(), user, ResponsibilityPermissions.Manage(CapPermissions.SystemCode), CapScope.Remark, ct)
            .ConfigureAwait(false);

    public static IQueryable<Remark> Open(IQueryable<Remark> remarks) =>
        remarks.Where(r => r.Status != CapRemarkStatuses.Completed && r.Status != CapRemarkStatuses.Cancelled && r.Status != CapRemarkStatuses.Rejected);

    public static async Task<bool> SaveAsync(CapDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
