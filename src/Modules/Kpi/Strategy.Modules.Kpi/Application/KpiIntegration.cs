using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

/// <summary>Evidence of a result: read with the result, written by the owner while the result is Open for Update.</summary>
internal sealed class ResultFiles(KpiReader reader) : IFileOwnerAccess
{
    public string OwnerType => KpiResult.EntityType;

    public string AttachmentPurpose => KpiFilePurposes.Evidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.ResultAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.ResultAsync(id, user, cancellationToken).ConfigureAwait(false) is { IsOpenForUpdate: true } result
        && await reader.CanAsync(user, KpiPermissions.ResultSubmit, result, cancellationToken).ConfigureAwait(false);
}

/// <summary>KPI description documents: read with the card, written by card managers.</summary>
internal sealed class CardFiles(KpiReader reader) : IFileOwnerAccess
{
    public string OwnerType => KpiCard.EntityType;

    public string AttachmentPurpose => KpiFilePurposes.CardDocument.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.CardAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.CardAsync(id, user, cancellationToken).ConfigureAwait(false) is { } card
        && await reader.CanAsync(user, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false);
}

/// <summary>KPI cards are task-able (SPEC §0.0): badge = code, name, status.</summary>
internal sealed class CardLinkTargets(KpiReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        KpiCard.EntityType, KpiPermissions.SystemCode, "بطاقة مؤشر أداء", "KPI card", [LinkCapabilities.TaskAble, LinkCapabilities.RemarkSource], KpiPermissions.CardRead, "/kpi/cards/{id}",
        KpiPermissions.CardManage);

    public string ResourceType => KpiCard.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var cards = await (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false)).Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return cards.ToDictionary(c => c.Id, c => new LinkTarget(c.Id, c.Code, c.NameAr, c.NameEn, c.Status, null, null,
            c.Status == CardStatuses.Active ? "success" : "neutral", c.Status == CardStatuses.Inactive));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.CardAsync(id, user, cancellationToken).ConfigureAwait(false) is { } card && await reader.CanAsync(user, permission, card, cancellationToken).ConfigureAwait(false);
}

/// <summary>KPI results are task-able: a task to prepare the evidence (BUILD_PLAN Phase 12); the owner follows the tasks linked to it.</summary>
internal sealed class ResultLinkTargets(KpiDbContext db, KpiReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        KpiResult.EntityType, KpiPermissions.SystemCode, "نتيجة مؤشر أداء", "KPI result", [LinkCapabilities.TaskAble, LinkCapabilities.RemarkSource], KpiPermissions.ResultRead, "/kpi/results/{id}",
        KpiPermissions.ResultSubmit);

    public string ResourceType => KpiResult.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var results = await (await reader.ResultsAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var cardIds = results.Select(r => r.CardId).Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return results.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, $"{cards[r.CardId].Code} · {r.PeriodCode.ToUpperInvariant()} {r.Year}", cards[r.CardId].NameAr,
            cards[r.CardId].NameEn, r.Status, null, null, Tone(r.Status), r.Status is ResultStatuses.Approved or ResultStatuses.Locked));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.ResultAsync(id, user, cancellationToken).ConfigureAwait(false) is { } result && await reader.CanAsync(user, permission, result, cancellationToken).ConfigureAwait(false);

    public static string Tone(string status) => status switch
    {
        ResultStatuses.Approved => "success",
        ResultStatuses.PendingApproval => "info",
        ResultStatuses.Returned => "warning",
        ResultStatuses.Open => "info",
        _ => "neutral",
    };
}

/// <summary>Global search over KPI cards (code, names), scope-filtered in the query.</summary>
internal sealed class CardSearchSource(KpiReader reader) : IRecordSearchSource
{
    public string ResourceType => KpiCard.EntityType;

    public string SystemCode => KpiPermissions.SystemCode;

    public string ReadPermission => KpiPermissions.CardRead;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var cards = await (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(c => EF.Functions.ILike(c.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(c.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(c.Code, pattern, "\\"))
            .OrderBy(c => c.Code).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return cards.Select(c => Hit(c, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(c => c.Id != excludeId)
            .Select(c => new { Card = c, Score = Math.Max(EF.Functions.TrigramsSimilarity(c.NameEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(c.NameAr ?? string.Empty, text)) })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => Hit(h.Card, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(KpiCard c, double score) =>
        new(KpiCard.EntityType, c.Id, KpiPermissions.SystemCode, c.Code, c.NameAr, c.NameEn, c.Status, $"/kpi/cards/{c.Id:D}", score);
}

/// <summary>The KPI catalog for other modules (Portfolio "Existing KPI", Risk KRIs) — ADR 0065.</summary>
internal sealed class KpiCatalog(KpiDbContext db, KpiReader reader) : IKpiCatalog
{
    public async Task<IReadOnlyList<KpiCardSummary>> SearchAsync(string? text, ClaimsPrincipal user, int take = 20, CancellationToken cancellationToken = default)
    {
        var source = (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false)).Where(c => c.Status == CardStatuses.Active);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = Like.Pattern(text);
            source = source.Where(c => EF.Functions.ILike(c.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(c.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(c.Code, pattern, "\\"));
        }

        var cards = await source.OrderBy(c => c.Code).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return cards.Select(Summary).ToList();
    }

    public async Task<KpiCardSummary?> GetAsync(Guid cardId, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await reader.CardAsync(cardId, user, cancellationToken).ConfigureAwait(false) is { } card ? Summary(card) : null;

    public async Task<KpiYearPerformance?> GetPerformanceAsync(Guid cardId, int year, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (await reader.CardAsync(cardId, user, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.CardId == cardId && p.Year == year, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return null;
        }

        var total = await db.Results.CountAsync(r => r.PlanId == plan.Id, cancellationToken).ConfigureAwait(false);
        return new KpiYearPerformance(cardId, year, plan.AchievedTotal, plan.ActualPerformance, plan.CappedPerformance, plan.Rag, plan.ApprovedCount, total);
    }

    private static KpiCardSummary Summary(KpiCard c) => new(c.Id, c.Code, c.NameAr, c.NameEn, c.Status, c.UnitCode, c.MeasuringPeriod, c.Direction, c.ResponsibleUnitId,
        c.ResponsibleEmployeeId, $"/kpi/cards/{c.Id:D}");
}

/// <summary>The K5 calculator with the installation's RAG bands, for other modules (Q-P6).</summary>
internal sealed class KpiPerformance(ISettingsStore settings) : IKpiPerformance
{
    public Performance Calculate(string targetType, string? direction, decimal? target, decimal? rangeMin, decimal? rangeMax, decimal? achieve) =>
        PerformanceCalculator.Calculate(targetType, direction, target, rangeMin, rangeMax, achieve);

    public async Task<string> BandAsync(decimal? capped, decimal? greenFrom = null, decimal? amberFrom = null, CancellationToken cancellationToken = default)
    {
        var configuration = await settings.GetAsync<KpiSettings>(cancellationToken).ConfigureAwait(false);
        return RagBands.Band(capped, greenFrom ?? configuration.GreenFrom, amberFrom ?? configuration.AmberFrom);
    }
}
