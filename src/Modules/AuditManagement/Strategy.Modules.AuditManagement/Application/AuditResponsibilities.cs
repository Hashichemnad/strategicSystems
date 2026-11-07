using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.AuditManagement.Application;

/// <summary>
/// Audit Management → Settings → Responsibilities (ADR 0168): the responsible auditor of reports (lead auditor) and of findings. Only Audit
/// Office members may take it over (holders of the Audit Office / Chief Audit Executive roles, checked on the server); the departments of the
/// findings stay as they are (read-only here).
/// </summary>
internal abstract class AuditorResponsibility<TRecord>(IRoleHolderQuery roles, IOrgDirectory org, IEmployeeDirectory employees, IUserDirectory users, IResponsibilityNotifier notifier)
    : ResponsibilitySource<TRecord>(notifier)
    where TRecord : class
{
    public override string SystemCode => AuditPermissions.SystemCode;

    /// <summary>Employees holding an Audit Office role now: users assigned directly and the people of units assigned (and below).</summary>
    public override async Task<IReadOnlyCollection<Guid>?> EligibleAsync(CancellationToken cancellationToken)
    {
        var employeeIds = new HashSet<Guid>();
        var userIds = new List<Guid>();
        foreach (var key in new[] { AuditPermissions.OfficeRoleKey, AuditPermissions.ChiefRoleKey })
        {
            var holders = await roles.GetHoldersAsync(key, cancellationToken).ConfigureAwait(false);
            userIds.AddRange(holders.UserIds);
            var units = new HashSet<Guid>();
            foreach (var unit in holders.UnitIds)
            {
                units.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, cancellationToken).ConfigureAwait(false));
            }

            if (units.Count > 0)
            {
                employeeIds.UnionWith(await employees.GetEmployeeIdsInUnitsAsync(units, cancellationToken).ConfigureAwait(false));
            }
        }

        if (userIds.Count > 0)
        {
            employeeIds.UnionWith((await users.GetManyAsync(userIds.Distinct().ToList(), cancellationToken).ConfigureAwait(false)).Values
                .Where(u => u.Active && u.EmployeeId is not null).Select(u => u.EmployeeId!.Value));
        }

        return employeeIds;
    }
}

internal sealed class ReportLeadAuditorResponsibility(
    AuditMgmtDbContext db, IAccessService access, IApprovalRerouting rerouting, IRoleHolderQuery roles, IOrgDirectory org, IEmployeeDirectory employees, IUserDirectory users,
    IResponsibilityNotifier notifier) : AuditorResponsibility<AuditReport>(roles, org, employees, users, notifier)
{
    /// <summary>Reports carry no owner or unit: the permission's scope All (or a record role) gives them.</summary>
    private static readonly ScopeMap<AuditReport> Scope = new(AuditReport.EntityType, x => x.Id);

    public override ResponsibilityKindDefinition Kind { get; } = new("report-lead-auditor", "المدقق المسؤول عن التقرير", "Report lead auditor",
        RestrictedCandidates: true, NoteKey: "responsibilities.notes.auditOffice", Statuses:
        [
            new(ReportStatuses.Draft, "مسودة", "Draft"), new(ReportStatuses.Issued, "صادر", "Issued"), new(ReportStatuses.UnderFollowUp, "قيد المتابعة", "Under follow-up"),
        ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reports = (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Status != ReportStatuses.Closed && r.Status != ReportStatuses.Cancelled);
        if (query.PersonId is { } person)
        {
            reports = reports.Where(r => r.LeadAuditorEmployeeId == person);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            reports = reports.Where(r => r.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = AuditLike.Pattern(text);
            reports = reports.Where(r => EF.Functions.ILike(r.Number + " " + (r.TitleAr ?? "") + " " + (r.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await reports.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await reports.OrderBy(r => r.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<AuditReport>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await ScopedAsync(user, tracked, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(AuditReport record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.LeadAuditorEmployeeId, null,
            $"/audit-management/reports/{record.Id:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(AuditReport record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.Status is ReportStatuses.Closed or ReportStatuses.Cancelled ? Blocked(row, ResponsibilityMessages.NotEditable) : Allowed(row));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<AuditReport> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var report in batch.Records)
        {
            report.AssignLeadAuditor(batch.NewId);
        }

        if (!await AuditLike.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await rerouting.RerouteAsync(new ApprovalReroute(AuditReport.EntityType, batch.Records.Select(r => r.Id).ToList(), batch.Reason), db, cancellationToken)
            .ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/audit-management/reports/{batch.Records[0].Id:D}" : "/audit-management")
            .ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    private async Task<IQueryable<AuditReport>> ScopedAsync(ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Reports : db.Reports.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), Scope, ct).ConfigureAwait(false);
}

internal sealed class FindingAuditorResponsibility(
    AuditMgmtDbContext db, IAccessService access, IApprovalRerouting rerouting, IRoleHolderQuery roles, IOrgDirectory org, IEmployeeDirectory employees, IUserDirectory users,
    IResponsibilityNotifier notifier) : AuditorResponsibility<Finding>(roles, org, employees, users, notifier)
{
    public override ResponsibilityKindDefinition Kind { get; } = new("finding-auditor", "المدقق المسؤول عن الملاحظة", "Finding responsible auditor",
        RestrictedCandidates: true, NoteKey: "responsibilities.notes.auditOffice", Statuses:
        [
            new(AuditFindingStatuses.Open, "مفتوحة", "Open"), new(AuditFindingStatuses.Late, "متأخرة", "Late"),
        ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var findings = (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false)).Where(f => !AuditFindingStatuses.Final.Contains(f.Status));
        if (query.PersonId is { } person)
        {
            findings = findings.Where(f => f.AuditorEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            findings = findings.Where(f => f.ResponsibleUnitId == unit || f.DepartmentUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            findings = findings.Where(f => f.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = AuditLike.Pattern(text);
            findings = findings.Where(f => EF.Functions.ILike(f.Number + " " + (f.TitleAr ?? "") + " " + (f.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await findings.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await findings.OrderBy(f => f.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Finding>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await ScopedAsync(user, tracked, cancellationToken).ConfigureAwait(false)).Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(Finding record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.AuditorEmployeeId, record.ResponsibleUnitId,
            $"/audit-management/findings/{record.Id:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(Finding record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsOpen ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Finding> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var finding in batch.Records)
        {
            finding.AssignAuditor(batch.NewId);
        }

        if (!await AuditLike.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await rerouting.RerouteAsync(new ApprovalReroute(Finding.EntityType, batch.Records.Select(f => f.Id).ToList(), batch.Reason), db, cancellationToken)
            .ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/audit-management/findings/{batch.Records[0].Id:D}" : "/audit-management")
            .ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    private async Task<IQueryable<Finding>> ScopedAsync(ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Findings : db.Findings.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), AuditScope.Finding, ct)
            .ConfigureAwait(false);
}

internal static class AuditLike
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    public static async Task<bool> SaveAsync(AuditMgmtDbContext db, CancellationToken ct)
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
