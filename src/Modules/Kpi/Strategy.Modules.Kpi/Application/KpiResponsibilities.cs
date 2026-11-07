using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Kpi.Application;

/// <summary>
/// KPI → Settings → Responsibilities (ADR 0168): the responsible person or unit of KPI cards. Moving it is a card change (new definition
/// version with the reason) that moves the current and future plans and results with it (<see cref="OwnershipSync"/>); result approvals in
/// progress follow the new owner / unit.
/// </summary>
internal abstract class KpiCardResponsibility(KpiDbContext db, IAccessService access, IApprovalRerouting rerouting, IClock clock, IResponsibilityNotifier notifier)
    : ResponsibilitySource<KpiCard>(notifier)
{
    public static readonly IReadOnlyList<ResponsibilityStatusOption> Statuses =
        [new(CardStatuses.Active, "نشطة", "Active"), new(CardStatuses.Inactive, "غير نشطة", "Inactive")];

    public override string SystemCode => KpiPermissions.SystemCode;

    protected abstract bool UnitKind { get; }

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cards = await access.ApplyScopeAsync(db.Cards.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), KpiScope.Card, cancellationToken)
            .ConfigureAwait(false);
        if (query.PersonId is { } person)
        {
            cards = UnitKind ? cards.Where(c => c.ResponsibleUnitId == person) : cards.Where(c => c.ResponsibleEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            cards = cards.Where(c => c.ResponsibleUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            cards = cards.Where(c => c.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = KpiLike.Pattern(text);
            cards = cards.Where(c => EF.Functions.ILike(c.Code + " " + (c.NameAr ?? "") + " " + (c.NameEn ?? ""), pattern, "\\"));
        }

        var total = await cards.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await cards.OrderBy(c => c.Code).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<KpiCard>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        var source = tracked ? db.Cards : db.Cards.AsNoTracking();
        var scoped = await access.ApplyScopeAsync(source, user, ResponsibilityPermissions.Manage(SystemCode), KpiScope.Card, cancellationToken).ConfigureAwait(false);
        return await scoped.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(KpiCard record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Code, record.NameAr, record.NameEn, record.Status, UnitKind ? record.ResponsibleUnitId : record.ResponsibleEmployeeId,
            record.ResponsibleUnitId, $"/kpi/cards/{record.Id:D}");
    }

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<KpiCard> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var year = clock.Today.Year;
        foreach (var card in batch.Records)
        {
            if (UnitKind)
            {
                card.AssignResponsible(card.ResponsibleEmployeeId, batch.NewId);
            }
            else
            {
                card.AssignResponsible(batch.NewId, card.ResponsibleUnitId);
            }

            card.NextVersion();
            db.CardVersions.Add(CardVersion.Of(card, batch.Reason, null));
            await OwnershipSync.ApplyAsync(db, card, year, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        // Result approvals in progress: the subject's owner / unit follow (visibility, "of the record" approver rules).
        var cardIds = batch.Records.Select(c => c.Id).ToList();
        var resultIds = await db.Results.AsNoTracking().Where(r => cardIds.Contains(r.CardId) && r.Year >= year).Select(r => r.Id).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await rerouting.RerouteAsync(new ApprovalReroute(KpiResult.EntityType, resultIds, batch.Reason, UnitKind ? null : batch.NewId, UnitKind ? batch.NewId : null),
            db, cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/kpi/cards/{batch.Records[0].Id:D}" : "/kpi/cards").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal sealed class KpiCardPersonResponsibility(KpiDbContext db, IAccessService access, IApprovalRerouting rerouting, IClock clock, IResponsibilityNotifier notifier)
    : KpiCardResponsibility(db, access, rerouting, clock, notifier)
{
    public override ResponsibilityKindDefinition Kind { get; } =
        new("card-person", "الموظف المسؤول عن بطاقة المؤشر", "KPI card responsible person", Statuses: Statuses);

    protected override bool UnitKind => false;
}

internal sealed class KpiCardUnitResponsibility(KpiDbContext db, IAccessService access, IApprovalRerouting rerouting, IClock clock, IResponsibilityNotifier notifier)
    : KpiCardResponsibility(db, access, rerouting, clock, notifier)
{
    public override ResponsibilityKindDefinition Kind { get; } =
        new("card-unit", "الوحدة المسؤولة عن بطاقة المؤشر", "KPI card responsible unit", ResponsibilityTargets.Unit, Statuses: Statuses);

    protected override bool UnitKind => true;
}

internal static class KpiLike
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
