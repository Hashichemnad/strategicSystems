using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>
/// Benchmarking → Settings → Responsibilities (ADR 0168): the coordinator (responsible employee) of active studies. The coordinator record
/// role follows through <see cref="BenchRoles"/>; the study's approvals in progress follow the new owner.
/// </summary>
internal sealed class StudyCoordinatorResponsibility(BenchDbContext db, IAccessService access, BenchRoles roles, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<Study>(notifier)
{
    public override string SystemCode => BenchPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("study-coordinator", "منسق الدراسة", "Study coordinator", Statuses:
        [new(StudyStatuses.Active, "نشطة", "Active"), new(StudyStatuses.Completed, "مكتملة", "Completed")]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var studies = (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false)).Where(s => s.Status != StudyStatuses.Cancelled);
        if (query.PersonId is { } person)
        {
            studies = studies.Where(s => s.ResponsibleEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            studies = studies.Where(s => s.ResponsibleUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            studies = studies.Where(s => s.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            studies = studies.Where(s => EF.Functions.ILike(s.Number + " " + (s.TitleAr ?? "") + " " + (s.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await studies.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await studies.OrderBy(s => s.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Study>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await ScopedAsync(user, tracked, cancellationToken).ConfigureAwait(false)).Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(Study record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.ResponsibleEmployeeId, record.ResponsibleUnitId,
            $"/bench/studies/{record.Id:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(Study record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsActive ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Study> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var study in batch.Records)
        {
            study.AssignCoordinator(batch.NewId);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        foreach (var study in batch.Records)
        {
            await roles.SyncAsync(study, cancellationToken).ConfigureAwait(false);
        }

        await rerouting.RerouteAsync(new ApprovalReroute(Study.EntityType, batch.Records.Select(s => s.Id).ToList(), batch.Reason, batch.NewId), db, cancellationToken)
            .ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/bench/studies/{batch.Records[0].Id:D}" : "/bench").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    private async Task<IQueryable<Study>> ScopedAsync(ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Studies : db.Studies.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), BenchScope.Study, ct)
            .ConfigureAwait(false);
}
