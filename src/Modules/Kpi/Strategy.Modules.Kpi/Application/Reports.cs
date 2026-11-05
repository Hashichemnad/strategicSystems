using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

public sealed record KpiScoreNodeDto(
    Guid? Id, string Kind, string? Code, string? NameAr, string? NameEn, decimal? Weight, decimal? Score, string Rag, decimal? Actual, int KpiCount,
    IReadOnlyList<KpiScoreNodeDto> Children);

public sealed record KpiScorecardDto(int Year, KpiUnitDto? Unit, KpiScoreNodeDto Root, IReadOnlyList<KpiDepartmentScoreDto> Departments);

public sealed record KpiDepartmentScoreDto(KpiUnitDto Unit, decimal? Score, string Rag, int KpiCount, int Reported);

public sealed record KpiMapObjectiveDto(Guid Id, string Code, string? NameAr, string? NameEn, decimal? Score, string Rag, int KpiCount);

public sealed record KpiMapPerspectiveDto(Guid? Id, string? Code, string? NameAr, string? NameEn, decimal? Score, string Rag, IReadOnlyList<KpiMapObjectiveDto> Objectives);

public sealed record KpiStrategyMapDto(int Year, string? VisionNameAr, string? VisionNameEn, decimal? Score, string Rag, IReadOnlyList<KpiMapPerspectiveDto> Perspectives);

public sealed record KpiCountDto(string Key, string? LabelAr, string? LabelEn, int Count, decimal? Score);

public sealed record KpiPerformerDto(Guid CardId, string Code, string? NameAr, string? NameEn, decimal? Actual, decimal? Capped, string Rag);

public sealed record KpiDashboardDto(
    int Year, int TotalCards, int ActiveCards, int PlansThisYear, decimal? OrganisationScore, string OrganisationRag, IReadOnlyList<KpiCountDto> ByType,
    IReadOnlyList<KpiCountDto> ByDepartment, IReadOnlyList<KpiCountDto> ByObjective, IReadOnlyList<KpiCountDto> Achievement, int PendingUpdates, int Overdue,
    int ApprovalBacklog, int ApprovedResults, IReadOnlyList<KpiPerformerDto> Top, IReadOnlyList<KpiPerformerDto> Bottom, IReadOnlyList<KpiCountDto> Rag);

public sealed record KpiDataQualityDto(KpiUnitDto Unit, int Due, int Submitted, decimal? OnTimeRate, decimal? RejectionRate, decimal? EvidenceCompleteness, decimal? Score);

public sealed record GetKpiScorecard(int? Year, Guid? UnitId) : IQuery<KpiScorecardDto>;

public sealed record GetKpiStrategyMap(int? Year) : IQuery<KpiStrategyMapDto>;

/// <remarks>
/// <c>From</c> / <c>To</c> (inclusive dates, normally the first day of the first month and the last day of the last month of the period): the result
/// figures (updates, overdue, backlog, approved) count the results whose period ends in the range instead of the year's; plans, scores and
/// performers stay those of <c>Year</c> (default: the year of <c>To</c>, else of <c>From</c>, else this year). Docs/design/reporting.md §2.3.
/// </remarks>
public sealed record GetKpiDashboard(int? Year, DateOnly? From = null, DateOnly? To = null) : IQuery<KpiDashboardDto>;

public sealed record GetKpiDataQuality(int? Year) : IQuery<IReadOnlyList<KpiDataQualityDto>>;

public sealed record ExportKpi(string Kind, string Format, int? Year, string? Language, string? Gem, bool? GlobalStar) : IQuery<KpiExportedFile>;

public sealed record KpiExportedFile(byte[] Content, string ContentType, string FileName);

/// <summary>Scorecards with capped performance (SPEC K5, §2.7; ADR 0062). Only approved results count (plans are recalculated from them).</summary>
internal sealed class ScorecardBuilder(KpiDbContext db, KpiReader reader, IAccessService access, IRequestContext context, ISettingsStore settings)
{
    public async Task<List<KpiCard>> CardsAsync(string permission, Guid? unitId, CancellationToken ct)
    {
        var source = await access.ApplyScopeAsync(db.Cards.AsNoTracking(), context.User, permission, KpiScope.Card, ct).ConfigureAwait(false);
        source = source.Where(c => c.Status == CardStatuses.Active);
        if (unitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, ct).ConfigureAwait(false);
            source = source.Where(c => (c.ResponsibleUnitId != null && units.Contains(c.ResponsibleUnitId.Value)) || (c.DepartmentUnitId != null && units.Contains(c.DepartmentUnitId.Value)));
        }

        return await source.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<KpiScoreNodeDto> BuildAsync(IReadOnlyList<KpiCard> cards, int year, CancellationToken ct)
    {
        var configuration = await settings.GetAsync<KpiSettings>(ct).ConfigureAwait(false);
        var ids = cards.Select(c => c.Id).ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.CardId) && p.Year == year).ToDictionaryAsync(p => p.CardId, ct).ConfigureAwait(false);
        var elements = await db.Elements.AsNoTracking().Where(e => e.Active).OrderBy(e => e.Sort).ThenBy(e => e.Code).ToListAsync(ct).ConfigureAwait(false);
        string Band(decimal? score) => RagBands.Band(score, configuration.GreenFrom, configuration.AmberFrom);
        var byParent = cards.Where(c => c.ParentCardId is not null && ids.Contains(c.ParentCardId.Value)).ToLookup(c => c.ParentCardId!.Value);
        var cache = new Dictionary<Guid, decimal?>();
        decimal? CardScore(KpiCard card, int depth)
        {
            if (cache.TryGetValue(card.Id, out var cached))
            {
                return cached;
            }

            var own = plans.GetValueOrDefault(card.Id)?.CappedPerformance;
            var score = own ?? (depth > 20 ? null : ScorecardCalculator.WeightedAverage(byParent[card.Id].Select(child => (child.Weight, CardScore(child, depth + 1)))));
            cache[card.Id] = score;
            return score;
        }

        KpiScoreNodeDto Kpi(KpiCard card)
        {
            var plan = plans.GetValueOrDefault(card.Id);
            var score = CardScore(card, 0);
            return new KpiScoreNodeDto(card.Id, "kpi", card.Code, card.NameAr, card.NameEn, card.Weight, score,
                RagBands.Band(score, card.GreenFrom ?? configuration.GreenFrom, card.AmberFrom ?? configuration.AmberFrom), plan?.ActualPerformance, 1, []);
        }

        KpiScoreNodeDto Node(StrategyElement element)
        {
            var children = element.Kind == ElementKinds.Objective
                ? cards.Where(c => c.ObjectiveId == element.Id).OrderBy(c => c.Code).Select(Kpi).ToList()
                : elements.Where(e => e.ParentId == element.Id).Select(Node).Where(n => n.KpiCount > 0 || n.Kind == ElementKinds.Objective).ToList();
            var score = ScorecardCalculator.WeightedAverage(children.Select(c => (c.Weight, c.Score)));
            return new KpiScoreNodeDto(element.Id, element.Kind, element.Code, element.NameAr, element.NameEn, element.Weight, score, Band(score), null,
                children.Sum(c => c.KpiCount), children);
        }

        var top = elements.Where(e => e.ParentId == null && e.Kind != ElementKinds.Objective).Select(Node).ToList();
        var unaligned = cards.Where(c => c.ObjectiveId is null || elements.All(e => e.Id != c.ObjectiveId)).OrderBy(c => c.Code).Select(Kpi).ToList();
        if (unaligned.Count > 0)
        {
            var score = ScorecardCalculator.WeightedAverage(unaligned.Select(c => (c.Weight, c.Score)));
            top.Add(new KpiScoreNodeDto(null, "unaligned", null, null, null, null, score, Band(score), null, unaligned.Count, unaligned));
        }

        var aligned = top.Where(t => t.Kind != "unaligned").ToList();
        var rootChildren = aligned.Count > 0 ? top : unaligned;
        var rootScore = ScorecardCalculator.WeightedAverage((aligned.Count > 0 ? aligned : unaligned).Select(c => (c.Weight, c.Score)));
        return new KpiScoreNodeDto(null, "organisation", null, null, null, null, rootScore, Band(rootScore), null, cards.Count, rootChildren);
    }

    /// <summary>Department scores: the KPIs of each department (card department, else responsible unit) — weighted by card weight.</summary>
    public async Task<IReadOnlyList<KpiDepartmentScoreDto>> DepartmentsAsync(IReadOnlyList<KpiCard> cards, int year, CancellationToken ct)
    {
        var configuration = await settings.GetAsync<KpiSettings>(ct).ConfigureAwait(false);
        var ids = cards.Select(c => c.Id).ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.CardId) && p.Year == year).ToDictionaryAsync(p => p.CardId, ct).ConfigureAwait(false);
        var groups = cards.GroupBy(c => c.DepartmentUnitId ?? c.ResponsibleUnitId).Where(g => g.Key is not null).ToList();
        var units = await reader.UnitsAsync(groups.Select(g => g.Key), ct).ConfigureAwait(false);
        return groups.Select(g =>
        {
            var score = ScorecardCalculator.WeightedAverage(g.Select(c => (c.Weight, plans.GetValueOrDefault(c.Id)?.CappedPerformance)));
            return new KpiDepartmentScoreDto(KpiReader.Unit(g.Key, units)!, score, RagBands.Band(score, configuration.GreenFrom, configuration.AmberFrom), g.Count(),
                g.Count(c => plans.GetValueOrDefault(c.Id)?.CappedPerformance is not null));
        }).OrderByDescending(d => d.Score ?? -1).ToList();
    }
}

internal sealed class ReportQueries(
    KpiDbContext db, KpiReader reader, ScorecardBuilder scorecards, IRequestContext context, IClock clock, ISettingsStore settings, IFileService files, CardQueries cardQueries,
    IPdfRenderer pdf, IAuditLog audit, ILookupService lookups)
    : IQueryHandler<GetKpiScorecard, KpiScorecardDto>, IQueryHandler<GetKpiStrategyMap, KpiStrategyMapDto>, IQueryHandler<GetKpiDashboard, KpiDashboardDto>,
        IQueryHandler<GetKpiDataQuality, IReadOnlyList<KpiDataQualityDto>>, IQueryHandler<ExportKpi, KpiExportedFile>
{
    public async Task<Result<KpiScorecardDto>> HandleAsync(GetKpiScorecard query, CancellationToken cancellationToken)
    {
        var year = query.Year ?? clock.Today.Year;
        var cards = await scorecards.CardsAsync(KpiPermissions.ScorecardRead, query.UnitId, cancellationToken).ConfigureAwait(false);
        var root = await scorecards.BuildAsync(cards, year, cancellationToken).ConfigureAwait(false);
        var departments = await scorecards.DepartmentsAsync(cards, year, cancellationToken).ConfigureAwait(false);
        var unit = query.UnitId is { } u ? KpiReader.Unit(u, await reader.UnitsAsync([u], cancellationToken).ConfigureAwait(false)) : null;
        return new KpiScorecardDto(year, unit, root, departments);
    }

    public async Task<Result<KpiStrategyMapDto>> HandleAsync(GetKpiStrategyMap query, CancellationToken cancellationToken)
    {
        var year = query.Year ?? clock.Today.Year;
        var cards = await scorecards.CardsAsync(KpiPermissions.ScorecardRead, null, cancellationToken).ConfigureAwait(false);
        var root = await scorecards.BuildAsync(cards, year, cancellationToken).ConfigureAwait(false);
        var perspectives = new List<KpiMapPerspectiveDto>();
        KpiScoreNodeDto? vision = null;
        foreach (var node in root.Children)
        {
            if (node.Kind == ElementKinds.Vision)
            {
                vision ??= node;
                perspectives.AddRange(node.Children.Where(c => c.Kind == ElementKinds.Perspective).Select(Perspective));
            }
            else if (node.Kind == ElementKinds.Perspective)
            {
                perspectives.Add(Perspective(node));
            }
        }

        return new KpiStrategyMapDto(year, vision?.NameAr, vision?.NameEn, root.Score, root.Rag, perspectives);
    }

    private static KpiMapPerspectiveDto Perspective(KpiScoreNodeDto node) => new(node.Id, node.Code, node.NameAr, node.NameEn, node.Score, node.Rag,
        node.Children.Where(c => c.Kind == ElementKinds.Objective).Select(o => new KpiMapObjectiveDto(o.Id!.Value, o.Code!, o.NameAr, o.NameEn, o.Score, o.Rag, o.KpiCount))
            .ToList());

    public async Task<Result<KpiDashboardDto>> HandleAsync(GetKpiDashboard query, CancellationToken cancellationToken)
    {
        if (query.From is { } rangeFrom && query.To is { } rangeTo && rangeFrom > rangeTo)
        {
            return Error.Validation([new FieldError("to", "validation.invalid", "validation.invalid")]);
        }

        var year = query.Year ?? query.To?.Year ?? query.From?.Year ?? clock.Today.Year;
        var visible = await reader.CardsAsync(context.User, cancellationToken).ConfigureAwait(false);
        var all = await visible.ToListAsync(cancellationToken).ConfigureAwait(false);
        var active = all.Where(c => c.Status == CardStatuses.Active).ToList();
        var ids = all.Select(c => c.Id).ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.CardId) && p.Year == year).ToListAsync(cancellationToken).ConfigureAwait(false);
        var planByCard = plans.ToDictionary(p => p.CardId);
        var scopedResults = await reader.ResultsAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (query.From is null && query.To is null)
        {
            scopedResults = scopedResults.Where(r => r.Year == year);
        }

        if (query.From is { } from)
        {
            scopedResults = scopedResults.Where(r => r.PeriodEnd >= from);
        }

        if (query.To is { } to)
        {
            scopedResults = scopedResults.Where(r => r.PeriodEnd <= to);
        }

        var results = await scopedResults.Select(r => new { r.Status, r.DueAt }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var types = (await lookups.GetValuesAsync(KpiPermissions.TypeLookup, true, cancellationToken).ConfigureAwait(false)).ToDictionary(v => v.Code);
        var byType = active.GroupBy(c => c.TypeCode ?? "-").Select(g => new KpiCountDto(g.Key, types.GetValueOrDefault(g.Key)?.LabelAr, types.GetValueOrDefault(g.Key)?.LabelEn,
            g.Count(), ScorecardCalculator.WeightedAverage(g.Select(c => (c.Weight, planByCard.GetValueOrDefault(c.Id)?.CappedPerformance))))).OrderByDescending(c => c.Count).ToList();
        var departments = await scorecards.DepartmentsAsync(active, year, cancellationToken).ConfigureAwait(false);
        var byDepartment = departments.Select(d => new KpiCountDto(d.Unit.Id.ToString("D"), d.Unit.NameAr, d.Unit.NameEn, d.KpiCount, d.Score)).ToList();
        var objectives = await db.Elements.AsNoTracking().Where(e => e.Kind == ElementKinds.Objective).ToDictionaryAsync(e => e.Id, cancellationToken).ConfigureAwait(false);
        var byObjective = active.Where(c => c.ObjectiveId is not null && objectives.ContainsKey(c.ObjectiveId.Value)).GroupBy(c => c.ObjectiveId!.Value)
            .Select(g => new KpiCountDto(objectives[g.Key].Code, objectives[g.Key].NameAr, objectives[g.Key].NameEn, g.Count(),
                ScorecardCalculator.WeightedAverage(g.Select(c => (c.Weight, planByCard.GetValueOrDefault(c.Id)?.CappedPerformance))))).OrderBy(c => c.Key).ToList();
        var activePlans = plans.Where(p => active.Any(c => c.Id == p.CardId)).ToList();
        var achievement = new List<KpiCountDto>
        {
            new("onTarget", null, null, activePlans.Count(p => p.Rag == RagCodes.Green), null),
            new("below", null, null, activePlans.Count(p => p.Rag is RagCodes.Amber or RagCodes.Red), null),
            new("notReported", null, null, activePlans.Count(p => p.Rag == RagCodes.None), null),
        };
        var rag = RagCodes.All.Select(code => new KpiCountDto(code, null, null, activePlans.Count(p => p.Rag == code), null)).ToList();
        var scored = activePlans.Where(p => p.CappedPerformance is not null).ToList();
        var cardsById = all.ToDictionary(c => c.Id);
        KpiPerformerDto Performer(AnnualPlan p) => new(p.CardId, cardsById[p.CardId].Code, cardsById[p.CardId].NameAr, cardsById[p.CardId].NameEn, p.ActualPerformance,
            p.CappedPerformance, p.Rag);
        var top = scored.OrderByDescending(p => p.ActualPerformance).ThenBy(p => cardsById[p.CardId].Code).Take(5).Select(Performer).ToList();
        var bottom = scored.OrderBy(p => p.CappedPerformance).ThenBy(p => cardsById[p.CardId].Code).Take(5).Select(Performer).ToList();
        var root = await scorecards.BuildAsync(active, year, cancellationToken).ConfigureAwait(false);
        return new KpiDashboardDto(year, all.Count, active.Count, plans.Count, root.Score, root.Rag, byType, byDepartment, byObjective, achievement,
            results.Count(r => r.Status is ResultStatuses.Open or ResultStatuses.Returned),
            results.Count(r => r.Status is ResultStatuses.Open or ResultStatuses.Returned && r.DueAt is { } due && due < now),
            results.Count(r => r.Status == ResultStatuses.PendingApproval), results.Count(r => r.Status == ResultStatuses.Approved), top, bottom, rag);
    }

    public async Task<Result<IReadOnlyList<KpiDataQualityDto>>> HandleAsync(GetKpiDataQuality query, CancellationToken cancellationToken)
    {
        var year = query.Year ?? clock.Today.Year;
        var results = await (await reader.ResultsAsync(context.User, cancellationToken).ConfigureAwait(false)).Where(r => r.Year == year && r.Status != ResultStatuses.NotOpen)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var cardIds = results.Select(r => r.CardId).Distinct().ToList();
        var departmentOf = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DepartmentUnitId ?? c.ResponsibleUnitId,
            cancellationToken).ConfigureAwait(false);
        var withEvidence = await files.OwnersWithFilesAsync(KpiFilePurposes.Evidence.Key, results.Select(r => r.Id.ToString("D")).ToList(), cancellationToken).ConfigureAwait(false);
        var groups = results.GroupBy(r => departmentOf.GetValueOrDefault(r.CardId) ?? r.OwnerUnitId).Where(g => g.Key is not null).ToList();
        var units = await reader.UnitsAsync(groups.Select(g => g.Key), cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        return groups.Select(g =>
        {
            var submitted = g.Where(r => r.SubmittedAt is not null).Select(r => new SubmissionFacts(r.Late, r.ReturnCount > 0, withEvidence.Contains(r.Id.ToString("D")), r.NotApplicable))
                .ToList();
            var due = g.Count(r => r.SubmittedAt is not null || (r.DueAt is { } d && d < now));
            var quality = DataQualityCalculator.Calculate(submitted, due);
            return new KpiDataQualityDto(KpiReader.Unit(g.Key, units)!, quality.Due, quality.Submitted, quality.OnTimeRate, quality.RejectionRate, quality.EvidenceCompleteness,
                quality.Score);
        }).OrderByDescending(q => q.Score ?? -1).ToList();
    }

    public async Task<Result<KpiExportedFile>> HandleAsync(ExportKpi query, CancellationToken cancellationToken)
    {
        if (query.Kind is not ("cards" or "scorecard" or "results" or "excellence" or "data-quality") || query.Format is not ("xlsx" or "pdf"))
        {
            return Error.Validation([new FieldError("kind", "validation.invalid", "validation.invalid")]);
        }

        var year = query.Year ?? clock.Today.Year;
        var arabic = (query.Language ?? "ar") == "ar";
        string P(decimal? ratio) => ratio is { } r ? (r * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %" : "—";
        string N(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "—";
        string? Name(string? ar, string? en) => arabic ? ar ?? en : en ?? ar;
        var headers = new List<(string Ar, string En)>();
        var rows = new List<string?[]>();
        switch (query.Kind)
        {
            case "cards" or "excellence":
                {
                    var page = await cardQueries.HandleAsync(new ListKpiCards(new PageRequest(1, PageRequest.MaxPageSize), null, null, null, null,
                        query.Kind == "excellence" ? CardStatuses.Active : null, query.Gem, query.Kind == "excellence" ? query.GlobalStar : null, year), cancellationToken)
                        .ConfigureAwait(false);
                    var items = page.Value.Items.Where(i => query.Kind != "excellence" || i.IsGlobalStar || i.GemCriteria.Count > 0).ToList();
                    headers.AddRange([("الرمز", "Code"), ("المؤشر", "KPI"), ("الحالة", "Status"), ("الوحدة المسؤولة", "Responsible unit"), ("الهدف", "Objective"),
                        ("معايير التميز", "GEM criteria"), ("النجمة العالمية", "Global Star"), ("المستهدف", "Target"), ("المحقق", "Achieved"), ("الأداء", "Performance"),
                        ("الأداء الفعلي", "Actual")]);
                    rows.AddRange(items.Select(i => new[]
                    {
                        i.Code, Name(i.NameAr, i.NameEn), i.Status, Name(i.ResponsibleUnit?.NameAr, i.ResponsibleUnit?.NameEn), Name(i.ObjectiveNameAr, i.ObjectiveNameEn),
                        string.Join(", ", i.GemCriteria), i.IsGlobalStar ? "✓" : string.Empty, N(i.Target), N(i.Achieved), P(i.Capped), P(i.Actual),
                    }));
                    break;
                }

            case "scorecard":
                {
                    var scorecard = await HandleAsync(new GetKpiScorecard(year, null), cancellationToken).ConfigureAwait(false);
                    headers.AddRange([("المستوى", "Level"), ("الرمز", "Code"), ("الاسم", "Name"), ("الوزن", "Weight"), ("الأداء", "Score"), ("التصنيف", "Band")]);
                    void Walk(KpiScoreNodeDto node, int depth)
                    {
                        rows.Add([node.Kind, node.Code, new string(' ', depth * 2) + (Name(node.NameAr, node.NameEn) ?? node.Kind), N(node.Weight), P(node.Score), node.Rag]);
                        foreach (var child in node.Children)
                        {
                            Walk(child, depth + 1);
                        }
                    }

                    Walk(scorecard.Value.Root, 0);
                    break;
                }

            case "results":
                {
                    var results = await (await reader.ResultsAsync(context.User, cancellationToken).ConfigureAwait(false)).Where(r => r.Year == year)
                        .OrderBy(r => r.CardId).ThenBy(r => r.Sort).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var ids = results.Select(r => r.CardId).Distinct().ToList();
                    var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
                    headers.AddRange([("الرمز", "Code"), ("المؤشر", "KPI"), ("الفترة", "Period"), ("المستهدف", "Target"), ("المحقق", "Achieved"), ("الأداء", "Performance"),
                        ("الأداء الفعلي", "Actual"), ("الحالة", "Status"), ("متأخرة", "Late")]);
                    rows.AddRange(results.Select(r => new[]
                    {
                        cards[r.CardId].Code, Name(cards[r.CardId].NameAr, cards[r.CardId].NameEn), r.PeriodCode.ToUpperInvariant(),
                        r.TargetType == TargetTypes.Range ? $"{N(r.RangeMin)} – {N(r.RangeMax)}" : N(r.Target),
                        r.Status == ResultStatuses.Approved ? (r.NotApplicable ? "N/A" : N(r.Achieve)) : "—", r.Status == ResultStatuses.Approved ? P(r.CappedPerformance) : "—",
                        r.Status == ResultStatuses.Approved ? P(r.ActualPerformance) : "—", r.Status, r.Late ? "✓" : string.Empty,
                    }));
                    break;
                }

            default:
                {
                    var quality = await HandleAsync(new GetKpiDataQuality(year), cancellationToken).ConfigureAwait(false);
                    headers.AddRange([("الإدارة", "Department"), ("المستحقة", "Due"), ("المقدمة", "Submitted"), ("الالتزام بالموعد", "On time"), ("نسبة الإعادة", "Rejection"),
                        ("اكتمال الأدلة", "Evidence"), ("جودة البيانات", "Data quality")]);
                    rows.AddRange(quality.Value.Select(q => new[]
                    {
                        Name(q.Unit.NameAr, q.Unit.NameEn), q.Due.ToString(CultureInfo.InvariantCulture), q.Submitted.ToString(CultureInfo.InvariantCulture), P(q.OnTimeRate),
                        P(q.RejectionRate), P(q.EvidenceCompleteness), P(q.Score),
                    }));
                    break;
                }
        }

        var (titleAr, titleEn) = query.Kind switch
        {
            "cards" => ("سجل مؤشرات الأداء", "KPI register"),
            "scorecard" => ($"بطاقة الأداء المؤسسي {year}", $"Organisation scorecard {year}"),
            "results" => ($"نتائج المؤشرات {year}", $"KPI results {year}"),
            "excellence" => ($"مؤشرات التميز {year}", $"Excellence KPIs {year}"),
            _ => ($"جودة البيانات {year}", $"Data quality {year}"),
        };
        KpiExportedFile file;
        if (query.Format == "pdf")
        {
            var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
            var document = new ReportDocument
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                OrganisationAr = branding.OrganisationNameAr,
                OrganisationEn = branding.OrganisationNameEn,
                PrimaryColor = branding.PrimaryColor,
                Language = arabic ? "ar" : "en",
            }.Table(headers.Select(h => new ReportColumn(h.Ar, h.En)).ToList(), rows);
            file = new KpiExportedFile(await pdf.RenderAsync(document.ToHtml(), cancellationToken).ConfigureAwait(false), "application/pdf", $"kpi-{query.Kind}.pdf");
        }
        else
        {
            file = new KpiExportedFile(ExcelExport.Build(arabic ? titleAr : titleEn, headers.Select(h => arabic ? h.Ar : h.En).ToList(),
                rows.Select(r => (IReadOnlyList<object?>)r.Cast<object?>().ToList()), arabic), ExcelExport.ContentType, $"kpi-{query.Kind}.xlsx");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, KpiPermissions.SystemCode, KpiCard.EntityType, null,
            new { kind = query.Kind, format = query.Format, year, rows = rows.Count }), cancellationToken).ConfigureAwait(false);
        return file;
    }
}
