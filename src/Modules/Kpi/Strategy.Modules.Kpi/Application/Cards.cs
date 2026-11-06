using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

public sealed record KpiCardListItemDto(
    Guid Id, string Code, string? NameAr, string? NameEn, string Status, string? TypeCode, string UnitCode, string MeasuringPeriod, string Direction, string Level,
    KpiUnitDto? ResponsibleUnit, KpiPersonDto? ResponsiblePerson, Guid? ObjectiveId, string? ObjectiveNameAr, string? ObjectiveNameEn, bool IsGlobalStar,
    IReadOnlyList<string> GemCriteria, int? Year, string? TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax, decimal? Achieved, decimal? Actual, decimal? Capped, string Rag);

public sealed record KpiCardTargetDto(int Year, string TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax);

public sealed record KpiChildCardDto(Guid Id, string Code, string? NameAr, string? NameEn, decimal? Weight, decimal? Capped, string Rag);

public sealed record KpiCardDto(
    Guid Id,
    string Code,
    CardValues Values,
    string Status,
    DateOnly? ClosedOn,
    KpiUnitDto? DepartmentUnit,
    KpiUnitDto? ResponsibleUnit,
    KpiPersonDto? ResponsiblePerson,
    string? ObjectiveNameAr,
    string? ObjectiveNameEn,
    Guid? PerspectiveId,
    string? PerspectiveNameAr,
    string? PerspectiveNameEn,
    string? ParentCode,
    IReadOnlyList<KpiCardTargetDto> Targets,
    IReadOnlyList<KpiChildCardDto> Children,
    decimal? CascadedScore,
    decimal? MaxAchieved,
    int DefinitionVersion,
    bool YearStarted,
    bool CanManage,
    bool ChangeControlled,
    uint Version);

public sealed record KpiOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record KpiElementOptionDto(Guid Id, string Kind, string Code, string? NameAr, string? NameEn, Guid? ParentId, bool Active);

public sealed record KpiCardOptionDto(Guid Id, string Code, string? NameAr, string? NameEn);

public sealed record KpiOptionsDto(
    IReadOnlyDictionary<string, IReadOnlyList<KpiOptionDto>> Lookups, IReadOnlyList<KpiElementOptionDto> Elements, IReadOnlyList<KpiCardOptionDto> Cards,
    IReadOnlyList<string> MeasuringPeriods, IReadOnlyList<string> Directions, IReadOnlyList<string> TargetTypes, IReadOnlyList<string> Natures,
    IReadOnlyList<string> Levels, bool ManualCodes, decimal GreenFrom, decimal AmberFrom, IReadOnlyList<ChecklistItem> Checklist, bool CanManageCards,
    bool CanManagePlans, bool CanReview, bool CanManageStrategy);

public sealed record KpiCardInput(string? Code, CardValues Values, string? Reason = null);

public sealed record KpiCardVersionDto(int VersionNo, string Snapshot, string? Reason, Guid? ChangeRequestId, DateTimeOffset CreatedAt, Guid? CreatedBy);

public sealed record KpiYearPointDto(int Year, string TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax, decimal? Achieved, decimal? Actual, decimal? Capped, string Rag,
    int ApprovedCount, int TotalPeriods);

public sealed record KpiPeriodPointDto(Guid ResultId, int Year, string PeriodCode, decimal? Target, decimal? RangeMin, decimal? RangeMax, decimal? Achieve, decimal? Actual,
    decimal? Capped, string Rag, string Status, bool NotApplicable);

public sealed record KpiPerformanceDto(Guid CardId, IReadOnlyList<KpiYearPointDto> Years, IReadOnlyList<KpiPeriodPointDto> Periods, decimal? BenchmarkResult,
    decimal? BaselineValue, int? BaselineYear);

public sealed record ListKpiCards(
    PageRequest Page, string? Search, Guid? UnitId, string? TypeCode, Guid? ObjectiveId, string? Status, string? Gem, bool? GlobalStar, int? Year) : IQuery<PagedResult<KpiCardListItemDto>>;

public sealed record GetKpiCard(Guid Id) : IQuery<KpiCardDto>;

public sealed record GetKpiOptions : IQuery<KpiOptionsDto>;

public sealed record CreateKpiCard(KpiCardInput Input) : ICommand<KpiCardDto>;

public sealed record UpdateKpiCard(Guid Id, KpiCardInput Input, uint Version) : ICommand<KpiCardDto>;

public sealed record SetKpiCardStatus(Guid Id, bool Active, uint Version) : ICommand<KpiCardDto>;

public sealed record SetKpiCardTargets(Guid Id, IReadOnlyList<KpiCardTargetDto> Targets) : ICommand<KpiCardDto>;

public sealed record ListKpiCardVersions(Guid Id) : IQuery<IReadOnlyList<KpiCardVersionDto>>;

public sealed record GetKpiPerformance(Guid Id, int? Year) : IQuery<KpiPerformanceDto>;

public sealed record ListKpiLinkedRecords(Guid Id) : IQuery<IReadOnlyList<LinkView>>;

/// <summary>Card rules (SPEC §2.3 A, §2.7) shared by create, update and the AI builder.</summary>
internal sealed partial class CardRules(KpiDbContext db, ILookupService lookups)
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex ElementCode();

    public async Task<List<FieldError>> ValidateAsync(CardValues v, Guid? cardId, CancellationToken ct)
    {
        if (v is null)
        {
            return [new FieldError("values", "validation.required", "validation.required")];
        }

        var errors = new List<FieldError>();
        void Add(string field, string key, Dictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            Add("nameEn", "validation.atLeastOneLanguage");
        }

        foreach (var (field, value, max) in new[] { ("nameAr", v.NameAr, KpiCard.NameMaxLength), ("nameEn", v.NameEn, KpiCard.NameMaxLength),
                     ("descriptionAr", v.DescriptionAr, KpiCard.TextMaxLength), ("descriptionEn", v.DescriptionEn, KpiCard.TextMaxLength),
                     ("measuringEquation", v.MeasuringEquation, 2000), ("formula", v.Formula, 2000) })
        {
            if (value is { Length: var l } && l > max)
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max });
            }
        }

        if (!Natures.All.Contains(v.Nature))
        {
            Add("nature", "validation.invalid");
        }

        if (!MeasuringPeriods.All.Contains(v.MeasuringPeriod))
        {
            Add("measuringPeriod", "validation.invalid");
        }

        if (!Directions.All.Contains(v.Direction))
        {
            Add("direction", "validation.invalid");
        }

        if (!TargetTypes.All.Contains(v.TargetType))
        {
            Add("targetType", "validation.invalid");
        }

        if (!CardLevels.All.Contains(v.Level))
        {
            Add("level", "validation.invalid");
        }

        async Task Lookup(string field, string type, string? code)
        {
            if (code is not null && !await lookups.IsActiveCodeAsync(type, code, ct).ConfigureAwait(false))
            {
                Add(field, "validation.invalid");
            }
        }

        await Lookup("unitCode", KpiPermissions.UnitLookup, v.UnitCode).ConfigureAwait(false);
        await Lookup("typeCode", KpiPermissions.TypeLookup, v.TypeCode).ConfigureAwait(false);
        await Lookup("indexClassificationCode", KpiPermissions.IndexClassificationLookup, v.IndexClassificationCode).ConfigureAwait(false);
        await Lookup("effectivenessCode", KpiPermissions.EffectivenessLookup, v.EffectivenessCode).ConfigureAwait(false);
        await Lookup("measuringMethodCode", KpiPermissions.MeasuringMethodLookup, v.MeasuringMethodCode).ConfigureAwait(false);
        await Lookup("nationalGoalCode", KpiPermissions.NationalGoalLookup, v.NationalGoalCode).ConfigureAwait(false);
        await Lookup("visionLinkCode", KpiPermissions.VisionLinkLookup, v.VisionLinkCode).ConfigureAwait(false);
        foreach (var code in v.SourceCodes ?? [])
        {
            await Lookup("sourceCodes", KpiPermissions.SourceLookup, code).ConfigureAwait(false);
        }

        foreach (var code in v.GemCriteriaCodes ?? [])
        {
            await Lookup("gemCriteriaCodes", KpiPermissions.GemCriterionLookup, code).ConfigureAwait(false);
        }

        if (v.ObjectiveId is { } objective && !await db.Elements.AnyAsync(e => e.Id == objective && e.Kind == ElementKinds.Objective, ct).ConfigureAwait(false))
        {
            Add("objectiveId", "kpi.errors.notAnObjective");
        }

        if (v.ParentCardId is { } parent)
        {
            if (parent == cardId || !await db.Cards.AnyAsync(c => c.Id == parent, ct).ConfigureAwait(false) || await CreatesCycleAsync(cardId, parent, ct).ConfigureAwait(false))
            {
                Add("parentCardId", "kpi.errors.parentCycle");
            }
        }

        foreach (var (field, value) in new[] { ("weight", v.Weight), ("benchmarkResult", v.BenchmarkResult) })
        {
            if (value is < 0 or > 1000)
            {
                Add(field, "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0, ["to"] = 1000 });
            }
        }

        if (v.GreenFrom is { } g && v.AmberFrom is { } a && (a >= g || a <= 0))
        {
            Add("amberFrom", "kpi.errors.bands");
        }
        else if ((v.GreenFrom is null) != (v.AmberFrom is null))
        {
            Add("amberFrom", "kpi.errors.bands");
        }

        var elements = v.DataElements ?? [];
        if (elements.Any(e => !ElementCode().IsMatch(e.Code ?? string.Empty)) || elements.Select(e => e.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != elements.Count)
        {
            Add("dataElements", "kpi.errors.dataElements");
        }
        else if (!string.IsNullOrWhiteSpace(v.Formula))
        {
            var compiled = CompositeFormula.Compile(v.Formula, elements);
            if (!compiled.Success)
            {
                var first = compiled.Errors.Count > 0 ? compiled.Errors[0] : null;
                Add("formula", first?.MessageKey ?? "kpi.errors.formulaInvalid", new Dictionary<string, object?> { ["position"] = first?.Position });
            }
        }
        else if (elements.Count > 0)
        {
            Add("formula", "validation.notEmpty");
        }

        if (v.Nature == Natures.Qualitative)
        {
            var scale = v.Scale ?? [];
            if (scale.Count < 2 || scale.Select(s => s.Value).Distinct().Count() != scale.Count || scale.Any(s => string.IsNullOrWhiteSpace(s.LabelAr) && string.IsNullOrWhiteSpace(s.LabelEn)))
            {
                Add("scale", "kpi.errors.scale");
            }
        }

        if (v.BaselineYear is < 1990 or > 2200)
        {
            Add("baselineYear", "validation.invalid");
        }

        return errors;
    }

    private async Task<bool> CreatesCycleAsync(Guid? cardId, Guid parent, CancellationToken ct)
    {
        if (cardId is null)
        {
            return false;
        }

        var parents = await db.Cards.AsNoTracking().Select(c => new { c.Id, c.ParentCardId }).ToDictionaryAsync(c => c.Id, c => c.ParentCardId, ct).ConfigureAwait(false);
        var current = (Guid?)parent;
        for (var i = 0; current is { } c && i < 100; i++)
        {
            if (c == cardId)
            {
                return true;
            }

            current = parents.GetValueOrDefault(c);
        }

        return false;
    }
}

internal sealed class CardQueries(KpiDbContext db, KpiReader reader, IRequestContext context, ILookupService lookups, IClock clock, IFeatureFlags features,
    ILinkRegistry links, IEnumerable<IKpiLinkedRecordSource> linkedSources)
    : IQueryHandler<ListKpiCards, PagedResult<KpiCardListItemDto>>, IQueryHandler<GetKpiCard, KpiCardDto>, IQueryHandler<GetKpiOptions, KpiOptionsDto>,
        IQueryHandler<ListKpiCardVersions, IReadOnlyList<KpiCardVersionDto>>, IQueryHandler<GetKpiPerformance, KpiPerformanceDto>,
        IQueryHandler<ListKpiLinkedRecords, IReadOnlyList<LinkView>>
{
    public async Task<Result<PagedResult<KpiCardListItemDto>>> HandleAsync(ListKpiCards query, CancellationToken cancellationToken)
    {
        var page = query.Page.Normalised();
        var source = await reader.CardsAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(c => EF.Functions.ILike(c.Code, pattern, "\\") || EF.Functions.ILike(c.NameAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(c.NameEn ?? string.Empty, pattern, "\\"));
        }

        if (query.UnitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, cancellationToken).ConfigureAwait(false);
            source = source.Where(c => (c.ResponsibleUnitId != null && units.Contains(c.ResponsibleUnitId.Value)) || (c.DepartmentUnitId != null && units.Contains(c.DepartmentUnitId.Value)));
        }

        if (query.TypeCode is { Length: > 0 } type)
        {
            source = source.Where(c => c.TypeCode == type);
        }

        if (query.ObjectiveId is { } objective)
        {
            source = source.Where(c => c.ObjectiveId == objective);
        }

        if (query.Status is { Length: > 0 } status)
        {
            source = source.Where(c => c.Status == status);
        }

        if (query.GlobalStar is true)
        {
            source = source.Where(c => c.IsGlobalStar);
        }

        if (query.Gem is { Length: > 0 } gem)
        {
            var needle = $"\"{gem}\"";
            source = source.Where(c => c.GemCriteriaCodes.Contains(needle));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = page.SortOrder switch
        {
            ("nameEn", var d) => d ? source.OrderByDescending(c => c.NameEn) : source.OrderBy(c => c.NameEn),
            ("nameAr", var d) => d ? source.OrderByDescending(c => c.NameAr) : source.OrderBy(c => c.NameAr),
            ("code", true) => source.OrderByDescending(c => c.Code),
            _ => source.OrderBy(c => c.Code),
        };
        var cards = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = await ToListItemsAsync(cards, query.Year ?? clock.Today.Year, cancellationToken).ConfigureAwait(false);
        return new PagedResult<KpiCardListItemDto>(items, page.Page, page.PageSize, total);
    }

    internal async Task<IReadOnlyList<KpiCardListItemDto>> ToListItemsAsync(IReadOnlyList<KpiCard> cards, int year, CancellationToken ct)
    {
        var ids = cards.Select(c => c.Id).ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.CardId) && p.Year == year).ToDictionaryAsync(p => p.CardId, ct).ConfigureAwait(false);
        var objectives = cards.Select(c => c.ObjectiveId).Where(o => o is not null).Select(o => o!.Value).Distinct().ToList();
        var elements = await db.Elements.AsNoTracking().Where(e => objectives.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(cards.Select(c => c.ResponsibleEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(cards.Select(c => c.ResponsibleUnitId), ct).ConfigureAwait(false);
        return cards.Select(c =>
        {
            var plan = plans.GetValueOrDefault(c.Id);
            var objective = c.ObjectiveId is { } o ? elements.GetValueOrDefault(o) : null;
            return new KpiCardListItemDto(c.Id, c.Code, c.NameAr, c.NameEn, c.Status, c.TypeCode, c.UnitCode, c.MeasuringPeriod, c.Direction, c.Level,
                KpiReader.Unit(c.ResponsibleUnitId, units), KpiReader.Person(c.ResponsibleEmployeeId, people), c.ObjectiveId, objective?.NameAr, objective?.NameEn, c.IsGlobalStar,
                c.GemCriteria, plan?.Year, plan?.TargetType, plan?.Target, plan?.RangeMin, plan?.RangeMax, plan?.AchievedTotal, plan?.ActualPerformance, plan?.CappedPerformance, plan?.Rag ?? RagCodes.None);
        }).ToList();
    }

    public async Task<Result<KpiCardDto>> HandleAsync(GetKpiCard query, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        return card is null ? Error.NotFound() : await ToDtoAsync(card, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<KpiCardDto> ToDtoAsync(KpiCard card, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var objective = card.ObjectiveId is { } o ? await db.Elements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == o, ct).ConfigureAwait(false) : null;
        var perspective = objective?.ParentId is { } p ? await db.Elements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == p, ct).ConfigureAwait(false) : null;
        var parentCode = card.ParentCardId is { } pc ? await db.Cards.AsNoTracking().Where(c => c.Id == pc).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var targets = await db.CardTargets.AsNoTracking().Where(t => t.CardId == card.Id).OrderBy(t => t.Year)
            .Select(t => new KpiCardTargetDto(t.Year, t.TargetType, t.Target, t.RangeMin, t.RangeMax)).ToListAsync(ct).ConfigureAwait(false);
        var children = await db.Cards.AsNoTracking().Where(c => c.ParentCardId == card.Id).OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        var childIds = children.Select(c => c.Id).ToList();
        var childPlans = await db.Plans.AsNoTracking().Where(pl => childIds.Contains(pl.CardId) && pl.Year == year).ToDictionaryAsync(pl => pl.CardId, ct).ConfigureAwait(false);
        var childDtos = children.Select(c =>
        {
            var plan = childPlans.GetValueOrDefault(c.Id);
            return new KpiChildCardDto(c.Id, c.Code, c.NameAr, c.NameEn, c.Weight, plan?.CappedPerformance, plan?.Rag ?? RagCodes.None);
        }).ToList();
        var cascaded = ScorecardCalculator.WeightedAverage(childDtos.Select(c => (c.Weight, c.Capped)));
        var people = await reader.PeopleAsync([card.ResponsibleEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([card.ResponsibleUnitId, card.DepartmentUnitId], ct).ConfigureAwait(false);
        var started = await YearStartedAsync(db, card.Id, year, ct).ConfigureAwait(false);
        var canManage = await reader.CanAsync(context.User, KpiPermissions.CardManage, card, ct).ConfigureAwait(false);
        var controlled = started && await features.IsEnabledAsync(KpiPermissions.ChangeControlFeature, ct).ConfigureAwait(false);
        return new KpiCardDto(card.Id, card.Code, ValuesOf(card), card.Status, card.ClosedOn, KpiReader.Unit(card.DepartmentUnitId, units),
            KpiReader.Unit(card.ResponsibleUnitId, units), KpiReader.Person(card.ResponsibleEmployeeId, people), objective?.NameAr, objective?.NameEn, perspective?.Id,
            perspective?.NameAr, perspective?.NameEn, parentCode, targets, childDtos, cascaded, card.MaxAchieved, card.DefinitionVersion, started, canManage, controlled,
            card.Version);
    }

    internal static Task<bool> YearStartedAsync(KpiDbContext db, Guid cardId, int year, CancellationToken ct) =>
        db.Plans.AnyAsync(p => p.CardId == cardId && p.Year <= year, ct);

    internal static CardValues ValuesOf(KpiCard c) => new(
        c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn, c.MeasuringEquation, c.ProcessName, c.InitiatedOn, c.TypeCode, c.IndexClassificationCode, c.EffectivenessCode,
        c.Nature, c.Sources, c.MeasuringMethodCode, c.CompetitivenessIndicator, c.IsGlobalStar, c.UnitCode, c.MeasuringPeriod, c.Direction, c.TargetType,
        c.TargetSettingMechanism, c.AllowsNegative, c.ObjectiveId, c.SectorGoal, c.NationalGoalCode, c.VisionLinkCode, c.GemCriteria, c.CriteriaText, c.ExcellenceLink,
        c.DepartmentUnitId, c.ResponsibleUnitId, c.ResponsibleEmployeeId, c.ResponsibleTeamId, c.BenchmarkDescription, c.BenchmarkResult, c.Level, c.ParentCardId, c.Weight,
        c.BaselineValue, c.BaselineYear, c.GreenFrom, c.AmberFrom, c.Formula, c.Elements, c.ScaleLevels);

    public async Task<Result<KpiOptionsDto>> HandleAsync(GetKpiOptions query, CancellationToken cancellationToken)
    {
        var types = new[]
        {
            KpiPermissions.TypeLookup, KpiPermissions.IndexClassificationLookup, KpiPermissions.EffectivenessLookup, KpiPermissions.SourceLookup, KpiPermissions.UnitLookup,
            KpiPermissions.MeasuringMethodLookup, KpiPermissions.NationalGoalLookup, KpiPermissions.VisionLinkLookup, KpiPermissions.GemCriterionLookup,
        };
        var result = new Dictionary<string, IReadOnlyList<KpiOptionDto>>();
        foreach (var type in types)
        {
            result[type] = (await lookups.GetValuesAsync(type, false, cancellationToken).ConfigureAwait(false)).OrderBy(v => v.Sort)
                .Select(v => new KpiOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        }

        var elements = await db.Elements.AsNoTracking().OrderBy(e => e.Sort).ThenBy(e => e.Code)
            .Select(e => new KpiElementOptionDto(e.Id, e.Kind, e.Code, e.NameAr, e.NameEn, e.ParentId, e.Active)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var cards = await (await reader.CardsAsync(context.User, cancellationToken).ConfigureAwait(false)).OrderBy(c => c.Code).Take(1000)
            .Select(c => new KpiCardOptionDto(c.Id, c.Code, c.NameAr, c.NameEn)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var user = context.User;
        return new KpiOptionsDto(result, elements, cards, MeasuringPeriods.All, Directions.All, TargetTypes.All, Natures.All, CardLevels.All, settings.ManualCodes,
            settings.GreenFrom, settings.AmberFrom, settings.Checklist,
            await reader.CanAsync(user, KpiPermissions.CardManage, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, KpiPermissions.PlanManage, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, KpiPermissions.ResultReview, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, KpiPermissions.StrategyManage, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<KpiCardVersionDto>>> HandleAsync(ListKpiCardVersions query, CancellationToken cancellationToken)
    {
        if (await reader.CardAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return await db.CardVersions.AsNoTracking().Where(v => v.CardId == query.Id).OrderByDescending(v => v.VersionNo)
            .Select(v => new KpiCardVersionDto(v.VersionNo, v.Snapshot, v.Reason, v.ChangeRequestId, v.CreatedAt, v.CreatedBy)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiPerformanceDto>> HandleAsync(GetKpiPerformance query, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var plans = await db.Plans.AsNoTracking().Where(p => p.CardId == card.Id).OrderBy(p => p.Year).ToListAsync(cancellationToken).ConfigureAwait(false);
        var results = await db.Results.AsNoTracking().Where(r => r.CardId == card.Id).OrderBy(r => r.Year).ThenBy(r => r.Sort).ToListAsync(cancellationToken).ConfigureAwait(false);
        var green = card.GreenFrom ?? settings.GreenFrom;
        var amber = card.AmberFrom ?? settings.AmberFrom;
        var years = plans.Select(p => new KpiYearPointDto(p.Year, p.TargetType, p.Target, p.RangeMin, p.RangeMax, p.AchievedTotal, p.ActualPerformance, p.CappedPerformance,
            p.Rag, p.ApprovedCount, results.Count(r => r.PlanId == p.Id))).ToList();
        var periods = results.Where(r => query.Year is null || r.Year == query.Year).Select(r =>
        {
            var approved = r.Status == ResultStatuses.Approved;
            return new KpiPeriodPointDto(r.Id, r.Year, r.PeriodCode, r.Target, r.RangeMin, r.RangeMax, approved ? r.Achieve : null, approved ? r.ActualPerformance : null,
                approved ? r.CappedPerformance : null, approved ? RagBands.Band(r.CappedPerformance, green, amber) : RagCodes.None, r.Status, r.NotApplicable);
        }).ToList();
        return new KpiPerformanceDto(card.Id, years, periods, card.BenchmarkResult, card.BaselineValue, card.BaselineYear);
    }

    public async Task<Result<IReadOnlyList<LinkView>>> HandleAsync(ListKpiLinkedRecords query, CancellationToken cancellationToken)
    {
        if (await reader.CardAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var references = new List<LinkRef>();
        foreach (var source in linkedSources)
        {
            references.AddRange((await source.GetLinkedRecordIdsAsync(query.Id, cancellationToken).ConfigureAwait(false)).Select(id => new LinkRef(source.ResourceType, id)));
        }

        return references.Count == 0
            ? Result.Success<IReadOnlyList<LinkView>>([])
            : Result.Success(await links.ResolveAsync(references, context.User, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class CardCommands(
    KpiDbContext db, KpiReader reader, CardRules rules, CardQueries queries, IRequestContext context, INumberingService numbering, IClock clock, IFeatureFlags features)
    : ICommandHandler<CreateKpiCard, KpiCardDto>, ICommandHandler<UpdateKpiCard, KpiCardDto>, ICommandHandler<SetKpiCardStatus, KpiCardDto>,
        ICommandHandler<SetKpiCardTargets, KpiCardDto>
{
    public async Task<Result<KpiCardDto>> HandleAsync(CreateKpiCard command, CancellationToken cancellationToken)
    {
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var errors = await rules.ValidateAsync(command.Input.Values, null, cancellationToken).ConfigureAwait(false);
        var manual = settings.ManualCodes && !string.IsNullOrWhiteSpace(command.Input.Code);
        if (manual && await db.Cards.AnyAsync(c => c.Code == command.Input.Code!.Trim(), cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new FieldError("code", "validation.duplicate", "validation.duplicate"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var card = KpiCard.Create(manual ? command.Input.Code! : "pending");
        card.Edit(command.Input.Values);
        if (!await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!manual)
        {
            card.SetCode(await numbering.NextAsync(db, KpiPermissions.CardNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false));
        }

        db.Cards.Add(card);
        db.CardVersions.Add(CardVersion.Of(card, command.Input.Reason, null));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(card, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiCardDto>> HandleAsync(UpdateKpiCard command, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var values = command.Input.Values;
        var errors = await rules.ValidateAsync(values, card.Id, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var before = card.Controlled;
        var after = new ControlledDefinition(StrategyElement.Clean(values.MeasuringEquation), StrategyElement.Clean(values.Formula), values.UnitCode, values.Direction, values.TargetType);
        if (before != after && await CardQueries.YearStartedAsync(db, card.Id, clock.Today.Year, cancellationToken).ConfigureAwait(false)
            && await features.IsEnabledAsync(KpiPermissions.ChangeControlFeature, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("kpi.changeControlled", "kpi.errors.changeControlled");
        }

        var ownershipChanged = card.ResponsibleEmployeeId != values.ResponsibleEmployeeId || card.ResponsibleUnitId != values.ResponsibleUnitId;
        db.Entry(card).Property(c => c.Version).OriginalValue = command.Version;
        card.Edit(values);
        if (card.IsQualitative && !card.ScaleLevels.Any())
        {
            return Error.Validation([new FieldError("scale", "kpi.errors.scale", "kpi.errors.scale")]);
        }

        card.NextVersion();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.CardVersions.Add(CardVersion.Of(card, command.Input.Reason, null));
        if (ownershipChanged)
        {
            await OwnershipSync.ApplyAsync(db, card, clock.Today.Year, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(card, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiCardDto>> HandleAsync(SetKpiCardStatus command, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(card).Property(c => c.Version).OriginalValue = command.Version;
        if (command.Active)
        {
            card.Activate();
        }
        else
        {
            card.Deactivate(clock.Today);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.ToDtoAsync(card, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiCardDto>> HandleAsync(SetKpiCardTargets command, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(command.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = new List<FieldError>();
        for (var i = 0; i < command.Targets.Count; i++)
        {
            var t = command.Targets[i];
            if (t.Year is < 1990 or > 2200 || !TargetTypes.All.Contains(t.TargetType) || TargetErrors.Of(t.TargetType, t.Target, t.RangeMin, t.RangeMax) is not null)
            {
                errors.Add(new FieldError($"targets[{i}]", "kpi.errors.target", "kpi.errors.target"));
            }
        }

        if (command.Targets.Select(t => t.Year).Distinct().Count() != command.Targets.Count)
        {
            errors.Add(new FieldError("targets", "validation.duplicate", "validation.duplicate"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var existing = await db.CardTargets.Where(t => t.CardId == card.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var t in command.Targets)
        {
            var row = existing.FirstOrDefault(e => e.Year == t.Year);
            if (row is null)
            {
                row = CardTarget.Create(card.Id, t.Year);
                db.CardTargets.Add(row);
            }

            row.Set(t.TargetType, t.Target, t.RangeMin, t.RangeMax);
        }

        db.CardTargets.RemoveRange(existing.Where(e => command.Targets.All(t => t.Year != e.Year)));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(card, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Target validation shared by cards, plans and results.</summary>
internal static class TargetErrors
{
    public static string? Of(string targetType, decimal? target, decimal? min, decimal? max) => targetType switch
    {
        TargetTypes.Range when min is null || max is null => "kpi.errors.rangeRequired",
        TargetTypes.Range when min > max => "kpi.errors.rangeOrder",
        TargetTypes.SingleValue when target is null => "kpi.errors.targetRequired",
        _ => null,
    };
}

/// <summary>Moves the data-scope copies of the current and future years with the card's ownership (scopes follow the responsible person / unit).</summary>
internal static class OwnershipSync
{
    public static async Task ApplyAsync(KpiDbContext db, KpiCard card, int fromYear, CancellationToken ct)
    {
        var plans = await db.Plans.Where(p => p.CardId == card.Id && p.Year >= fromYear).ToListAsync(ct).ConfigureAwait(false);
        foreach (var plan in plans)
        {
            plan.SetOwnership(card.ResponsibleEmployeeId, card.ResponsibleUnitId);
        }

        var planIds = plans.Select(p => p.Id).ToList();
        foreach (var result in await db.Results.Where(r => planIds.Contains(r.PlanId)).ToListAsync(ct).ConfigureAwait(false))
        {
            result.SetOwnership(card.ResponsibleEmployeeId, card.ResponsibleUnitId);
        }
    }
}
