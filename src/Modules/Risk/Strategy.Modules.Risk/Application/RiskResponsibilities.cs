using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Application;

/// <summary>
/// Risk → Settings → Responsibilities (ADR 0168): risk owner, treatment action owners and control owners of open risks. The change uses the
/// domain setters, re-syncs the action / control owner record roles (<see cref="RiskRoleSync"/>) and re-routes the risk's approvals in progress.
/// </summary>
internal sealed class RiskOwnerResponsibility(RiskDbContext db, IAccessService access, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<RiskRecord>(notifier)
{
    public override string SystemCode => RiskPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("risk-owner", "مالك الخطر", "Risk owner", Statuses:
    [
        new(RiskStatuses.Draft, "مسودة", "Draft"), new(RiskStatuses.UnderReview, "قيد المراجعة", "Under review"), new(RiskStatuses.Active, "نشط", "Active"),
        new(RiskStatuses.Closed, "مغلق", "Closed"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        if (query.PersonId is { } person)
        {
            risks = risks.Where(r => r.OwnerEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            risks = risks.Where(r => r.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            risks = risks.Where(r => r.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = Like.Pattern(text);
            risks = risks.Where(r => EF.Functions.ILike(r.Number + " " + (r.TitleAr ?? "") + " " + (r.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await risks.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await risks.OrderBy(r => r.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<RiskRecord>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken, tracked).ConfigureAwait(false);
        return await risks.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(RiskRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.OwnerEmployeeId, record.UnitId, $"/risk/risks/{record.Id:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(RiskRecord record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsOpen ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<RiskRecord> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var risk in batch.Records)
        {
            risk.AssignOwner(batch.NewId);
        }

        if (!await RiskResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await rerouting.RerouteAsync(new ApprovalReroute(RiskRecord.EntityType, batch.Records.Select(r => r.Id).ToList(), batch.Reason, batch.NewId), db, cancellationToken)
            .ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/risk/risks/{batch.Records[0].Id:D}" : "/risk").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal sealed class RiskActionOwnerResponsibility(RiskDbContext db, IAccessService access, RiskRoleSync roles, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<TreatmentAction>(notifier)
{
    public override string SystemCode => RiskPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("action-owner", "مسؤول إجراء المعالجة", "Treatment action owner", Statuses:
    [
        new(ActionStatuses.NotStarted, "لم يبدأ", "Not started"), new(ActionStatuses.InProgress, "قيد التنفيذ", "In progress"),
        new(ActionStatuses.Completed, "مكتمل", "Completed"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        if (query.UnitId is { } unit)
        {
            risks = risks.Where(r => r.UnitId == unit);
        }

        var actions = db.Actions.AsNoTracking().Where(a => a.Status != ActionStatuses.Cancelled && risks.Any(r => r.Id == a.RiskId));
        if (query.PersonId is { } person)
        {
            actions = actions.Where(a => a.OwnerEmployeeId == person);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            actions = actions.Where(a => a.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = Like.Pattern(text);
            actions = actions.Where(a => EF.Functions.ILike(a.Number + " " + (a.TitleAr ?? "") + " " + (a.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await actions.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await actions.OrderBy(a => a.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Join(db.Risks.AsNoTracking(), a => a.RiskId, r => r.Id, (a, r) => new { a, r.Number, r.UnitId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(page.Select(x => Describe(x.a) with { UnitId = x.UnitId, ContextAr = x.Number, ContextEn = x.Number }).ToList(), total);
    }

    protected override async Task<IReadOnlyList<TreatmentAction>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        var source = tracked ? db.Actions : db.Actions.AsNoTracking();
        return await source.Where(a => ids.Contains(a.Id) && risks.Any(r => r.Id == a.RiskId)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(TreatmentAction record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.OwnerEmployeeId, null, $"/risk/risks/{record.RiskId:D}");
    }

    protected override async Task<ResponsibilityOutcome> CheckAsync(
        TreatmentAction record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        record.Status is ActionStatuses.Completed or ActionStatuses.Cancelled
        || !await db.Risks.AnyAsync(r => r.Id == record.RiskId && RiskStatuses.Open.Contains(r.Status), cancellationToken).ConfigureAwait(false)
            ? Blocked(row, ResponsibilityMessages.NotEditable)
            : Allowed(row);

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(
        ResponsibilityBatch<TreatmentAction> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var action in batch.Records)
        {
            action.AssignOwner(batch.NewId);
        }

        if (!await RiskResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var riskIds = batch.Records.Select(a => a.RiskId).Distinct().ToList();
        foreach (var riskId in riskIds)
        {
            await roles.SyncAsync(riskId, cancellationToken).ConfigureAwait(false);
        }

        await rerouting.RerouteAsync(new ApprovalReroute(RiskRecord.EntityType, riskIds, batch.Reason), db, cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: riskIds.Count == 1 ? $"/risk/risks/{riskIds[0]:D}" : "/risk").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal sealed class RiskControlOwnerResponsibility(RiskDbContext db, IAccessService access, RiskRoleSync roles, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<Control>(notifier)
{
    public override string SystemCode => RiskPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("control-owner", "مسؤول الضابط", "Control owner");

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        if (query.UnitId is { } unit)
        {
            risks = risks.Where(r => r.UnitId == unit);
        }

        var controls = db.Controls.AsNoTracking().Where(c => c.Status == ControlStatuses.Active && risks.Any(r => r.Id == c.RiskId));
        if (query.PersonId is { } person)
        {
            controls = controls.Where(c => c.OwnerEmployeeId == person);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = Like.Pattern(text);
            controls = controls.Where(c => EF.Functions.ILike((c.NameAr ?? "") + " " + (c.NameEn ?? ""), pattern, "\\"));
        }

        var total = await controls.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await controls.OrderBy(c => c.NameEn ?? c.NameAr).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Join(db.Risks.AsNoTracking(), c => c.RiskId, r => r.Id, (c, r) => new { c, r.Number, r.UnitId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(page.Select(x => Describe(x.c) with { UnitId = x.UnitId, ContextAr = x.Number, ContextEn = x.Number }).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Control>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        var risks = await RiskResponsibilityScope.RisksAsync(db, access, user, cancellationToken).ConfigureAwait(false);
        var source = tracked ? db.Controls : db.Controls.AsNoTracking();
        return await source.Where(c => ids.Contains(c.Id) && risks.Any(r => r.Id == c.RiskId)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(Control record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, null, record.NameAr, record.NameEn, record.Status, record.OwnerEmployeeId, null, $"/risk/risks/{record.RiskId:D}");
    }

    protected override async Task<ResponsibilityOutcome> CheckAsync(Control record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        record.Status != ControlStatuses.Active
        || !await db.Risks.AnyAsync(r => r.Id == record.RiskId && RiskStatuses.Open.Contains(r.Status), cancellationToken).ConfigureAwait(false)
            ? Blocked(row, ResponsibilityMessages.NotEditable)
            : Allowed(row);

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Control> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var control in batch.Records)
        {
            control.AssignOwner(batch.NewId);
        }

        if (!await RiskResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var riskIds = batch.Records.Select(c => c.RiskId).Distinct().ToList();
        foreach (var riskId in riskIds)
        {
            await roles.SyncAsync(riskId, cancellationToken).ConfigureAwait(false);
        }

        await rerouting.RerouteAsync(new ApprovalReroute(RiskRecord.EntityType, riskIds, batch.Reason), db, cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: riskIds.Count == 1 ? $"/risk/risks/{riskIds[0]:D}" : "/risk").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal static class RiskResponsibilityScope
{
    /// <summary>Risks within the scope of <c>risk.responsibilities.manage</c> (owner / unit, as the read scope).</summary>
    public static async Task<IQueryable<RiskRecord>> RisksAsync(RiskDbContext db, IAccessService access, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await access.ApplyScopeAsync(tracked ? db.Risks : db.Risks.AsNoTracking(), user, ResponsibilityPermissions.Manage(RiskPermissions.SystemCode), RiskScope.Risk, ct)
            .ConfigureAwait(false);

    /// <summary>False when a record changed since it was loaded (nothing saved).</summary>
    public static async Task<bool> SaveAsync(RiskDbContext db, CancellationToken ct)
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
