using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Integration;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Infrastructure;

/// <summary>
/// KPI record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): KPI card, annual plan and period result. Only the listed
/// properties are written. Performance is never copied from the source: the hooks re-calculate it with <see cref="PerformanceCalculator"/>
/// and the plan aggregation (CLAUDE.md §15); text targets with "-" arrive as the Range target type (range min / max).
/// </summary>
internal static class KpiSyncTargets
{
    private static readonly IReadOnlyList<string> PlanStatusCodes = [PlanStatuses.Draft, PlanStatuses.Confirmed];

    public static IServiceCollection AddKpiSyncTargets(this IServiceCollection services)
    {
        const string System = KpiPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<KpiDbContext, KpiCard>(KpiCard.EntityType, System, "بطاقة مؤشر أداء", "KPI card")
            .Text("code", nameof(KpiCard.Code), required: true)
            .Text("nameAr", nameof(KpiCard.NameAr)).Text("nameEn", nameof(KpiCard.NameEn))
            .Text("descriptionAr", nameof(KpiCard.DescriptionAr)).Text("descriptionEn", nameof(KpiCard.DescriptionEn))
            .Text("measuringEquation", nameof(KpiCard.MeasuringEquation)).Text("processName", nameof(KpiCard.ProcessName))
            .Choice("status", nameof(KpiCard.Status), CardStatuses.All)
            .Date("initiatedOn", nameof(KpiCard.InitiatedOn)).Date("closedOn", nameof(KpiCard.ClosedOn))
            .Lookup("type", nameof(KpiCard.TypeCode), KpiPermissions.TypeLookup)
            .Lookup("indexClassification", nameof(KpiCard.IndexClassificationCode), KpiPermissions.IndexClassificationLookup)
            .Lookup("effectiveness", nameof(KpiCard.EffectivenessCode), KpiPermissions.EffectivenessLookup)
            .Choice("nature", nameof(KpiCard.Nature), Natures.All)
            .Codes("sources", nameof(KpiCard.SourceCodes), KpiPermissions.SourceLookup)
            .Lookup("measuringMethod", nameof(KpiCard.MeasuringMethodCode), KpiPermissions.MeasuringMethodLookup)
            .Bool("competitivenessIndicator", nameof(KpiCard.CompetitivenessIndicator)).Bool("isGlobalStar", nameof(KpiCard.IsGlobalStar))
            .Lookup("unit", nameof(KpiCard.UnitCode), KpiPermissions.UnitLookup)
            .Choice("measuringPeriod", nameof(KpiCard.MeasuringPeriod), MeasuringPeriods.All)
            .Choice("direction", nameof(KpiCard.Direction), Directions.All)
            .Choice("targetType", nameof(KpiCard.TargetType), TargetTypes.All)
            .Text("targetSettingMechanism", nameof(KpiCard.TargetSettingMechanism))
            .Bool("allowsNegative", nameof(KpiCard.AllowsNegative))
            .Text("sectorGoal", nameof(KpiCard.SectorGoal))
            .Lookup("nationalGoal", nameof(KpiCard.NationalGoalCode), KpiPermissions.NationalGoalLookup)
            .Lookup("visionLink", nameof(KpiCard.VisionLinkCode), KpiPermissions.VisionLinkLookup)
            .Codes("gemCriteria", nameof(KpiCard.GemCriteriaCodes), KpiPermissions.GemCriterionLookup)
            .Text("criteriaText", nameof(KpiCard.CriteriaText)).Text("excellenceLink", nameof(KpiCard.ExcellenceLink))
            .Unit("departmentUnitId", nameof(KpiCard.DepartmentUnitId)).Unit("responsibleUnitId", nameof(KpiCard.ResponsibleUnitId))
            .Person("responsibleEmployeeId", nameof(KpiCard.ResponsibleEmployeeId))
            .Text("benchmarkDescription", nameof(KpiCard.BenchmarkDescription)).Ratio("benchmarkResult", nameof(KpiCard.BenchmarkResult))
            .Choice("level", nameof(KpiCard.Level), CardLevels.All)
            .Reference("parentCardId", nameof(KpiCard.ParentCardId), KpiCard.EntityType)
            .Number("weight", nameof(KpiCard.Weight))
            .Number("baselineValue", nameof(KpiCard.BaselineValue)).WholeNumber("baselineYear", nameof(KpiCard.BaselineYear))
            .Ratio("greenFrom", nameof(KpiCard.GreenFrom)).Ratio("amberFrom", nameof(KpiCard.AmberFrom))
            .Default("status", CardStatuses.Active));

        services.AddSyncTarget(SyncTarget.Entity<KpiDbContext, AnnualPlan>(AnnualPlan.EntityType, System, "الخطة السنوية للمؤشر", "KPI annual plan")
            .Reference("cardId", nameof(AnnualPlan.CardId), KpiCard.EntityType, required: true)
            .WholeNumber("year", nameof(AnnualPlan.Year), required: true)
            .Choice("measuringPeriod", nameof(AnnualPlan.MeasuringPeriod), MeasuringPeriods.All)
            .Lookup("unit", nameof(AnnualPlan.UnitCode), KpiPermissions.UnitLookup)
            .Choice("direction", nameof(AnnualPlan.Direction), Directions.All)
            .Bool("isAccumulated", nameof(AnnualPlan.IsAccumulated))
            .Choice("targetType", nameof(AnnualPlan.TargetType), TargetTypes.All)
            .Number("target", nameof(AnnualPlan.Target))
            .Number("rangeMin", nameof(AnnualPlan.RangeMin)).Number("rangeMax", nameof(AnnualPlan.RangeMax))
            .Person("responsibleEmployeeId", nameof(AnnualPlan.ResponsibleEmployeeId)).Unit("responsibleUnitId", nameof(AnnualPlan.ResponsibleUnitId))
            .Choice("status", nameof(AnnualPlan.Status), PlanStatusCodes)
            .Default("status", PlanStatuses.Confirmed)
            .After<KpiPlanSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<KpiDbContext, KpiResult>(KpiResult.EntityType, System, "نتيجة مؤشر أداء", "KPI result")
            .Reference("planId", nameof(KpiResult.PlanId), AnnualPlan.EntityType, required: true)
            .Choice("periodCode", nameof(KpiResult.PeriodCode), PeriodCodes.All, required: true)
            .Choice("targetType", nameof(KpiResult.TargetType), TargetTypes.All)
            .Number("target", nameof(KpiResult.Target))
            .Number("rangeMin", nameof(KpiResult.RangeMin)).Number("rangeMax", nameof(KpiResult.RangeMax))
            .Number("achieve", nameof(KpiResult.Achieve))
            .Text("causality", nameof(KpiResult.Causality)).Text("correctiveActions", nameof(KpiResult.CorrectiveActions)).Text("notes", nameof(KpiResult.Notes))
            .Bool("notApplicable", nameof(KpiResult.NotApplicable)).Text("naReason", nameof(KpiResult.NaReason))
            .Choice("status", nameof(KpiResult.Status), ResultStatuses.All)
            .Person("ownerEmployeeId", nameof(KpiResult.OwnerEmployeeId)).Unit("ownerUnitId", nameof(KpiResult.OwnerUnitId))
            .Default("status", ResultStatuses.Open)
            .After<KpiResultSyncHook>());

        return services;
    }
}

/// <summary>The target of a synced plan / result after the Range rule (SPEC-ADDENDUM G3: text targets with "-" → Range target type).</summary>
internal sealed record ResolvedTarget(string TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax);

/// <summary>Pure rules of the KPI sync hooks (unit-tested).</summary>
internal static class KpiSyncRules
{
    /// <summary>
    /// The target type and values of a synced plan / result: an explicit valid type wins; else both range bounds → Range (a Salesforce text
    /// target "a-b" carries its bounds in Range_Min / Range_Max); else a single value → Single; else the fallback (the plan for a result, the
    /// card's default type for a plan) with its values. Reversed bounds are swapped.
    /// </summary>
    public static ResolvedTarget Resolve(string? explicitType, decimal? target, decimal? rangeMin, decimal? rangeMax, ResolvedTarget fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var type = explicitType is not null && TargetTypes.All.Contains(explicitType, StringComparer.Ordinal)
            ? explicitType
            : rangeMin is not null && rangeMax is not null ? TargetTypes.Range
            : target is not null ? TargetTypes.SingleValue
            : null;
        if (type is null)
        {
            return fallback;
        }

        if (type == TargetTypes.Range)
        {
            var (min, max) = (rangeMin, rangeMax);
            if (min is null && max is null && fallback.TargetType == TargetTypes.Range)
            {
                (min, max) = (fallback.RangeMin, fallback.RangeMax);
            }

            return min is { } a && max is { } b && a > b ? new(type, null, b, a) : new(type, null, min, max);
        }

        return new(type, target ?? (fallback.TargetType == TargetTypes.SingleValue ? fallback.Target : null), null, null);
    }

    /// <summary>
    /// The default target of one period of a plan (the fallback of a result without its own target): the plan's annual target split or
    /// repeated by the aggregation rule (<see cref="PeriodSchedule.DefaultTargets"/>); the annual values when the period is not one of the
    /// plan's measuring period.
    /// </summary>
    public static ResolvedTarget PeriodDefault(string aggregationRule, string measuringPeriod, string periodCode, ResolvedTarget annual)
    {
        ArgumentNullException.ThrowIfNull(annual);
        var codes = MeasuringPeriods.All.Contains(measuringPeriod, StringComparer.Ordinal) ? PeriodSchedule.CodesFor(measuringPeriod).ToList() : [];
        var index = codes.IndexOf(periodCode);
        if (index < 0)
        {
            return annual;
        }

        var period = PeriodSchedule.DefaultTargets(aggregationRule, annual.TargetType, annual.Target, annual.RangeMin, annual.RangeMax, codes.Count)[index];
        return new(annual.TargetType, period.Target, period.RangeMin, period.RangeMax);
    }

    internal static string? ExplicitType(IReadOnlyDictionary<string, object?> values) =>
        values.TryGetValue("targetType", out var value) ? value as string : null;
}

/// <summary>
/// After a synced annual plan: fills what Salesforce does not carry from the card (measuring period, unit, direction, responsibility) on a new
/// plan, applies the Range rule and re-calculates the plan from its approved results (<see cref="PlanCalculator"/>) — Salesforce
/// <c>Performance__c</c> is not copied (the plan has no source-performance field).
/// </summary>
internal sealed class KpiPlanSyncHook(ISettingsStore settings) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var db = (KpiDbContext)transactionContext;
        var plan = await db.Plans.FirstAsync(p => p.Id == recordId, cancellationToken).ConfigureAwait(false);
        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == plan.CardId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return;
        }

        var entry = db.Entry(plan);
        if (created)
        {
            if (!values.ContainsKey("measuringPeriod"))
            {
                entry.Property(p => p.MeasuringPeriod).CurrentValue = card.MeasuringPeriod;
            }

            if (!values.ContainsKey("unit"))
            {
                entry.Property(p => p.UnitCode).CurrentValue = card.UnitCode;
            }

            if (!values.ContainsKey("direction"))
            {
                entry.Property(p => p.Direction).CurrentValue = card.Direction;
            }
        }

        if (plan.ResponsibleEmployeeId is null && plan.ResponsibleUnitId is null)
        {
            plan.SetOwnership(card.ResponsibleEmployeeId, card.ResponsibleUnitId);
        }

        var target = KpiSyncRules.Resolve(KpiSyncRules.ExplicitType(values), plan.Target, plan.RangeMin, plan.RangeMax, new(card.TargetType, null, null, null));
        plan.SetTarget(target.TargetType, target.Target, target.RangeMin, target.RangeMax);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await new PlanCalculator(db, settings).RecalculateAsync(plan.Id, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// After a synced period result: aligns it with its plan (card, year, period dates, ownership), applies the Range rule (a result without its
/// own target takes the plan's default period target), re-calculates actual / capped performance with <see cref="PerformanceCalculator"/> and then the
/// plan aggregation. Salesforce <c>Performance__c</c> is not copied (the result has no source-performance field).
/// </summary>
internal sealed class KpiResultSyncHook(ISettingsStore settings) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var db = (KpiDbContext)transactionContext;
        var result = await db.Results.FirstAsync(r => r.Id == recordId, cancellationToken).ConfigureAwait(false);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == result.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return;
        }

        var entry = db.Entry(result);
        entry.Property(r => r.CardId).CurrentValue = plan.CardId;
        entry.Property(r => r.Year).CurrentValue = plan.Year;
        if (PeriodSchedule.MeasuringPeriodOf(result.PeriodCode) is not null)
        {
            var period = PeriodSchedule.Spec(plan.Year, result.PeriodCode);
            entry.Property(r => r.PeriodStart).CurrentValue = period.Start;
            entry.Property(r => r.PeriodEnd).CurrentValue = period.End;
            entry.Property(r => r.Sort).CurrentValue = period.Sort;
        }

        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == plan.CardId, cancellationToken).ConfigureAwait(false);
        if (result.OwnerEmployeeId is null && result.OwnerUnitId is null)
        {
            result.SetOwnership(plan.ResponsibleEmployeeId ?? card?.ResponsibleEmployeeId, plan.ResponsibleUnitId ?? card?.ResponsibleUnitId);
        }

        var rule = (await settings.GetAsync<KpiSettings>(cancellationToken).ConfigureAwait(false)).RuleFor(card?.MeasuringMethodCode);
        var periodDefault = KpiSyncRules.PeriodDefault(rule, plan.MeasuringPeriod, result.PeriodCode, new(plan.TargetType, plan.Target, plan.RangeMin, plan.RangeMax));
        var target = KpiSyncRules.Resolve(KpiSyncRules.ExplicitType(values), result.Target, result.RangeMin, result.RangeMax, periodDefault);
        result.SetTarget(target.TargetType, target.Target, target.RangeMin, target.RangeMax);
        result.Recalculate(PerformanceCalculator.Calculate(result.TargetType, plan.Direction, result.Target, result.RangeMin, result.RangeMax, result.Achieve));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await new PlanCalculator(db, settings).RecalculateAsync(plan.Id, cancellationToken).ConfigureAwait(false);
    }
}
