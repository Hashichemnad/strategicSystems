using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Modules.Kpi.Application;

public sealed record KpiYearCardDto(
    Guid CardId, string Code, string? NameAr, string? NameEn, KpiUnitDto? ResponsibleUnit, string MeasuringPeriod, string UnitCode, string Direction, string TargetType,
    decimal? Target, decimal? RangeMin, decimal? RangeMax, bool IsAccumulated, string TargetSource, bool HasPlan);

public sealed record KpiYearPreviewDto(int Year, IReadOnlyList<KpiYearCardDto> Cards, int ExistingPlans);

public sealed record KpiYearItemInput(
    Guid CardId, bool Include, string MeasuringPeriod, string UnitCode, string Direction, string TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax,
    bool IsAccumulated);

public sealed record KpiYearStartedDto(int Year, int PlansCreated, int ResultsCreated, int Skipped, int WindowsCreated);

public sealed record KpiWindowDto(int Year, string PeriodCode, DateOnly OpensOn, DateOnly DueOn, bool Locked, uint Version);

public sealed record KpiWindowInput(string PeriodCode, DateOnly OpensOn, DateOnly DueOn);

public sealed record KpiPlanListItemDto(
    Guid Id, Guid CardId, string CardCode, string? CardNameAr, string? CardNameEn, int Year, string MeasuringPeriod, string UnitCode, string Direction, string TargetType,
    decimal? Target, decimal? RangeMin, decimal? RangeMax, string Status, KpiUnitDto? ResponsibleUnit, decimal? Achieved, decimal? Actual, decimal? Capped, string Rag,
    int ApprovedCount, int TotalPeriods, uint Version);

public sealed record KpiPlanPeriodDto(Guid ResultId, string PeriodCode, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? Target, decimal? RangeMin, decimal? RangeMax,
    string Status, DateOnly? OpensOn, DateOnly? DueOn, decimal? Achieve, decimal? Actual, decimal? Capped, bool NotApplicable, bool Late);

public sealed record KpiPlanDto(KpiPlanListItemDto Plan, bool IsAccumulated, KpiPersonDto? ResponsiblePerson, IReadOnlyList<KpiPlanPeriodDto> Periods, string AggregationRule,
    bool CanManage);

public sealed record KpiPeriodTargetInput(Guid ResultId, decimal? Target, decimal? RangeMin, decimal? RangeMax);

public sealed record KpiPlanInput(
    string TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax, string UnitCode, string Direction, bool IsAccumulated, Guid? ResponsibleEmployeeId,
    IReadOnlyList<KpiPeriodTargetInput>? Periods, uint Version);

public sealed record PreviewKpiYear(int Year) : IQuery<KpiYearPreviewDto>;

public sealed record StartKpiYear(int Year, IReadOnlyList<KpiYearItemInput> Items) : ICommand<KpiYearStartedDto>;

public sealed record ListKpiWindows(int Year) : IQuery<IReadOnlyList<KpiWindowDto>>;

public sealed record UpdateKpiWindows(int Year, IReadOnlyList<KpiWindowInput> Windows) : ICommand<IReadOnlyList<KpiWindowDto>>;

public sealed record LockKpiPeriod(int Year, string PeriodCode, bool Lock, string? Reason) : ICommand<KpiWindowDto>;

public sealed record ListKpiPlans(int Year, Guid? UnitId, string? Status) : IQuery<IReadOnlyList<KpiPlanListItemDto>>;

public sealed record GetKpiPlan(Guid Id) : IQuery<KpiPlanDto>;

public sealed record UpdateKpiPlan(Guid Id, KpiPlanInput Input) : ICommand<KpiPlanDto>;

public sealed record ConfirmKpiPlans(IReadOnlyList<Guid> PlanIds) : ICommand<int>;

/// <summary>New measuring year (SPEC K1), period windows (K2) and annual plans. <c>kpi.plan.manage</c> for every change.</summary>
internal sealed class YearHandlers(
    KpiDbContext db, KpiReader reader, IRequestContext context, IClock clock, KpiDeadlines deadlines, ResultOpener opener, PlanCalculator calculator, KpiNotifier notifier)
    : IQueryHandler<PreviewKpiYear, KpiYearPreviewDto>, ICommandHandler<StartKpiYear, KpiYearStartedDto>, IQueryHandler<ListKpiWindows, IReadOnlyList<KpiWindowDto>>,
        ICommandHandler<UpdateKpiWindows, IReadOnlyList<KpiWindowDto>>, ICommandHandler<LockKpiPeriod, KpiWindowDto>, IQueryHandler<ListKpiPlans, IReadOnlyList<KpiPlanListItemDto>>,
        IQueryHandler<GetKpiPlan, KpiPlanDto>, ICommandHandler<UpdateKpiPlan, KpiPlanDto>, ICommandHandler<ConfirmKpiPlans, int>
{
    private Task<bool> CanManageAsync(CancellationToken ct) => reader.CanAsync(context.User, KpiPermissions.PlanManage, ct);

    private static bool ValidYear(int year) => year is >= 2000 and <= 2200;

    public async Task<Result<KpiYearPreviewDto>> HandleAsync(PreviewKpiYear query, CancellationToken cancellationToken)
    {
        if (!ValidYear(query.Year))
        {
            return Error.Validation([new FieldError("year", "validation.invalidValue", "validation.invalidValue")]);
        }

        if (!await CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var year = query.Year;
        var cards = await db.Cards.AsNoTracking().Where(c => c.Status == CardStatuses.Active).OrderBy(c => c.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = cards.Select(c => c.Id).ToList();
        var targets = await db.CardTargets.AsNoTracking().Where(t => ids.Contains(t.CardId) && t.Year == year).ToDictionaryAsync(t => t.CardId, cancellationToken)
            .ConfigureAwait(false);
        var previous = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.CardId) && p.Year < year).GroupBy(p => p.CardId)
            .Select(g => g.OrderByDescending(p => p.Year).First()).ToListAsync(cancellationToken).ConfigureAwait(false);
        var previousByCard = previous.ToDictionary(p => p.CardId);
        var existing = await db.Plans.AsNoTracking().Where(p => p.Year == year).Select(p => p.CardId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync(cards.Select(c => c.ResponsibleUnitId), cancellationToken).ConfigureAwait(false);
        var items = cards.Select(c =>
        {
            string source;
            string type = c.TargetType;
            decimal? target = null, min = null, max = null;
            if (targets.GetValueOrDefault(c.Id) is { } t)
            {
                (source, type, target, min, max) = ("multi-year", t.TargetType, t.Target, t.RangeMin, t.RangeMax);
            }
            else if (previousByCard.GetValueOrDefault(c.Id) is { } p)
            {
                (source, type, target, min, max) = ("previous-year", p.TargetType, p.Target, p.RangeMin, p.RangeMax);
            }
            else
            {
                source = "none";
            }

            var previousPlan = previousByCard.GetValueOrDefault(c.Id);
            return new KpiYearCardDto(c.Id, c.Code, c.NameAr, c.NameEn, KpiReader.Unit(c.ResponsibleUnitId, units), c.MeasuringPeriod, c.UnitCode, c.Direction, type, target,
                min, max, previousPlan?.IsAccumulated ?? false, source, existing.Contains(c.Id));
        }).ToList();
        return new KpiYearPreviewDto(year, items, existing.Count);
    }

    public async Task<Result<KpiYearStartedDto>> HandleAsync(StartKpiYear command, CancellationToken cancellationToken)
    {
        if (!ValidYear(command.Year))
        {
            return Error.Validation([new FieldError("year", "validation.invalidValue", "validation.invalidValue")]);
        }

        if (!await CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = new List<FieldError>();
        var included = command.Items.Where(i => i.Include).ToList();
        for (var i = 0; i < command.Items.Count; i++)
        {
            var item = command.Items[i];
            if (!item.Include)
            {
                continue;
            }

            if (!MeasuringPeriods.All.Contains(item.MeasuringPeriod) || !Directions.All.Contains(item.Direction) || !TargetTypes.All.Contains(item.TargetType)
                || string.IsNullOrWhiteSpace(item.UnitCode))
            {
                errors.Add(new FieldError($"items[{i}]", "validation.invalidValue", "validation.invalidValue"));
            }
            else if (TargetErrors.Of(item.TargetType, item.Target, item.RangeMin, item.RangeMax) is { } targetError)
            {
                errors.Add(new FieldError($"items[{i}]", targetError, targetError));
            }
        }

        if (included.Count == 0)
        {
            errors.Add(new FieldError("items", "kpi.errors.nothingSelected", "kpi.errors.nothingSelected"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var ids = included.Select(i => i.CardId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id) && c.Status == CardStatuses.Active).ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);
        var existing = await db.Plans.AsNoTracking().Where(p => p.Year == command.Year && ids.Contains(p.CardId)).Select(p => p.CardId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var windows = await db.Windows.Where(w => w.Year == command.Year).ToListAsync(cancellationToken).ConfigureAwait(false);
        var windowsCreated = 0;
        foreach (var code in PeriodCodes.All)
        {
            if (windows.All(w => w.PeriodCode != code))
            {
                var (opens, due) = WindowRules.Window(PeriodSchedule.Spec(command.Year, code), settings.WindowRules);
                var window = PeriodWindow.Create(command.Year, code, opens, due);
                db.Windows.Add(window);
                windows.Add(window);
                windowsCreated++;
            }
        }

        int plansCreated = 0, resultsCreated = 0, skipped = 0;
        foreach (var item in included)
        {
            if (!cards.TryGetValue(item.CardId, out var card) || existing.Contains(item.CardId))
            {
                skipped++;
                continue;
            }

            var plan = AnnualPlan.Create(card.Id, command.Year, item.MeasuringPeriod, item.UnitCode.Trim(), item.Direction, item.IsAccumulated, card.ResponsibleEmployeeId,
                card.ResponsibleUnitId);
            plan.SetTarget(item.TargetType, item.Target, item.RangeMin, item.RangeMax);
            db.Plans.Add(plan);
            plansCreated++;
            var periods = PeriodSchedule.For(command.Year, item.MeasuringPeriod);
            var periodTargets = PeriodSchedule.DefaultTargets(settings.RuleFor(card.MeasuringMethodCode), item.TargetType, item.Target, item.RangeMin, item.RangeMax, periods.Count);
            for (var p = 0; p < periods.Count; p++)
            {
                var result = KpiResult.Create(plan, periods[p], periodTargets[p], card.ResponsibleEmployeeId, card.ResponsibleUnitId);
                var window = windows.First(w => w.PeriodCode == periods[p].Code);
                result.SetWindow(window.OpensOn, window.DueOn, await deadlines.DueAtAsync(card.ResponsibleUnitId, window.DueOn, cancellationToken).ConfigureAwait(false));
                db.Results.Add(result);
                resultsCreated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.RequirePlanConfirmation)
        {
            await opener.OpenDueAsync(null, cancellationToken).ConfigureAwait(false);
        }

        return new KpiYearStartedDto(command.Year, plansCreated, resultsCreated, skipped, windowsCreated);
    }

    public async Task<Result<IReadOnlyList<KpiWindowDto>>> HandleAsync(ListKpiWindows query, CancellationToken cancellationToken) =>
        await db.Windows.AsNoTracking().Where(w => w.Year == query.Year).OrderBy(w => w.OpensOn).ThenBy(w => w.PeriodCode)
            .Select(w => new KpiWindowDto(w.Year, w.PeriodCode, w.OpensOn, w.DueOn, w.Locked, w.Version)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<KpiWindowDto>>> HandleAsync(UpdateKpiWindows command, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = new List<FieldError>();
        for (var i = 0; i < command.Windows.Count; i++)
        {
            var w = command.Windows[i];
            if (!PeriodCodes.All.Contains(w.PeriodCode) || w.DueOn < w.OpensOn)
            {
                errors.Add(new FieldError($"windows[{i}]", "kpi.errors.windowDates", "kpi.errors.windowDates"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var windows = await db.Windows.Where(w => w.Year == command.Year).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var input in command.Windows)
        {
            var window = windows.FirstOrDefault(w => w.PeriodCode == input.PeriodCode);
            if (window is null)
            {
                continue;
            }

            window.SetDates(input.OpensOn, input.DueOn);
            var results = await db.Results.Where(r => r.Year == command.Year && r.PeriodCode == input.PeriodCode
                && (r.Status == ResultStatuses.NotOpen || r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
            {
                result.SetWindow(input.OpensOn, input.DueOn, await deadlines.DueAtAsync(result.OwnerUnitId, input.DueOn, cancellationToken).ConfigureAwait(false));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await opener.OpenDueAsync(null, cancellationToken).ConfigureAwait(false);
        return await HandleAsync(new ListKpiWindows(command.Year), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiWindowDto>> HandleAsync(LockKpiPeriod command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, KpiPermissions.ResultReopen, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!command.Lock && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        var window = await db.Windows.FirstOrDefaultAsync(w => w.Year == command.Year && w.PeriodCode == command.PeriodCode, cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var statuses = command.Lock ? new[] { ResultStatuses.NotOpen, ResultStatuses.Open, ResultStatuses.Returned } : [ResultStatuses.Locked];
        var results = await db.Results.Where(r => r.Year == command.Year && r.PeriodCode == command.PeriodCode && statuses.Contains(r.Status)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var result in results)
        {
            result.Apply(command.Lock ? ResultActions.Lock : ResultActions.Reopen, now, context.UserId, command.Reason);
        }

        if (command.Lock)
        {
            window.Lock();
        }
        else
        {
            window.Unlock();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (!command.Lock)
        {
            var cards = await db.Cards.AsNoTracking().Where(c => results.Select(r => r.CardId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
            foreach (var result in results)
            {
                await notifier.SendAsync(KpiNotifications.Reopened, [result.OwnerEmployeeId], KpiNotifier.Values(cards[result.CardId], result, KpiCalendar.Date(result.DueOn),
                    null, command.Reason), $"/kpi/results/{result.Id:D}", db, cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new KpiWindowDto(window.Year, window.PeriodCode, window.OpensOn, window.DueOn, window.Locked, window.Version);
    }

    public async Task<Result<IReadOnlyList<KpiPlanListItemDto>>> HandleAsync(ListKpiPlans query, CancellationToken cancellationToken)
    {
        var source = await VisiblePlansAsync(cancellationToken).ConfigureAwait(false);
        source = source.Where(p => p.Year == query.Year);
        if (query.Status is { Length: > 0 } status)
        {
            source = source.Where(p => p.Status == status);
        }

        if (query.UnitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, cancellationToken).ConfigureAwait(false);
            source = source.Where(p => p.ResponsibleUnitId != null && units.Contains(p.ResponsibleUnitId.Value));
        }

        var plans = await source.ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ToItemsAsync(plans, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IQueryable<AnnualPlan>> VisiblePlansAsync(CancellationToken ct)
    {
        var cards = await reader.CardsAsync(context.User, ct).ConfigureAwait(false);
        return db.Plans.AsNoTracking().Where(p => cards.Any(c => c.Id == p.CardId));
    }

    private async Task<List<KpiPlanListItemDto>> ToItemsAsync(IReadOnlyList<AnnualPlan> plans, CancellationToken ct)
    {
        var ids = plans.Select(p => p.CardId).Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var planIds = plans.Select(p => p.Id).ToList();
        var counts = await db.Results.AsNoTracking().Where(r => planIds.Contains(r.PlanId)).GroupBy(r => r.PlanId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(plans.Select(p => p.ResponsibleUnitId), ct).ConfigureAwait(false);
        return plans.Select(p => ToItem(p, cards[p.CardId], units, counts.GetValueOrDefault(p.Id))).OrderBy(p => p.CardCode).ToList();
    }

    private static KpiPlanListItemDto ToItem(AnnualPlan p, KpiCard c, IReadOnlyDictionary<Guid, KpiUnitDto> units, int total) => new(
        p.Id, c.Id, c.Code, c.NameAr, c.NameEn, p.Year, p.MeasuringPeriod, p.UnitCode, p.Direction, p.TargetType, p.Target, p.RangeMin, p.RangeMax, p.Status,
        KpiReader.Unit(p.ResponsibleUnitId, units), p.AchievedTotal, p.ActualPerformance, p.CappedPerformance, p.Rag, p.ApprovedCount, total, p.Version);

    public async Task<Result<KpiPlanDto>> HandleAsync(GetKpiPlan query, CancellationToken cancellationToken)
    {
        var plan = await (await VisiblePlansAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken).ConfigureAwait(false);
        return plan is null ? Error.NotFound() : await ToDtoAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    private async Task<KpiPlanDto> ToDtoAsync(AnnualPlan plan, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == plan.CardId, ct).ConfigureAwait(false);
        var results = await db.Results.AsNoTracking().Where(r => r.PlanId == plan.Id).OrderBy(r => r.Sort).ToListAsync(ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([plan.ResponsibleUnitId], ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([plan.ResponsibleEmployeeId], ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var periods = results.Select(r =>
        {
            var approved = r.Status == ResultStatuses.Approved;
            return new KpiPlanPeriodDto(r.Id, r.PeriodCode, r.PeriodStart, r.PeriodEnd, r.Target, r.RangeMin, r.RangeMax, r.Status, r.OpensOn, r.DueOn,
                approved ? r.Achieve : null, approved ? r.ActualPerformance : null, approved ? r.CappedPerformance : null, r.NotApplicable, r.Late);
        }).ToList();
        return new KpiPlanDto(ToItem(plan, card, units, results.Count), plan.IsAccumulated, KpiReader.Person(plan.ResponsibleEmployeeId, people), periods,
            settings.RuleFor(card.MeasuringMethodCode), await CanManageAsync(ct).ConfigureAwait(false));
    }

    public async Task<Result<KpiPlanDto>> HandleAsync(UpdateKpiPlan command, CancellationToken cancellationToken)
    {
        var plan = await (await VisiblePlansAsync(cancellationToken).ConfigureAwait(false)).AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return Error.NotFound();
        }

        if (!await CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var input = command.Input;
        if (plan.Status == PlanStatuses.Confirmed
            && (input.TargetType != plan.TargetType || input.Target != plan.Target || input.RangeMin != plan.RangeMin || input.RangeMax != plan.RangeMax
                || input.UnitCode != plan.UnitCode || input.Direction != plan.Direction))
        {
            return Error.Rule("kpi.changeControlled", "kpi.errors.changeControlled");
        }

        var errors = new List<FieldError>();
        if (!TargetTypes.All.Contains(input.TargetType) || !Directions.All.Contains(input.Direction) || string.IsNullOrWhiteSpace(input.UnitCode))
        {
            errors.Add(new FieldError("targetType", "validation.invalidValue", "validation.invalidValue"));
        }
        else if (TargetErrors.Of(input.TargetType, input.Target, input.RangeMin, input.RangeMax) is { } targetError)
        {
            errors.Add(new FieldError("target", targetError, targetError));
        }

        var results = await db.Results.Where(r => r.PlanId == plan.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var period in input.Periods ?? [])
        {
            var result = results.FirstOrDefault(r => r.Id == period.ResultId);
            if (result is null || result.Status == ResultStatuses.Approved || TargetErrors.Of(input.TargetType, period.Target, period.RangeMin, period.RangeMax) is not null)
            {
                errors.Add(new FieldError($"periods.{period.ResultId}", "kpi.errors.target", "kpi.errors.target"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(plan).Property(p => p.Version).OriginalValue = input.Version;
        plan.SetTarget(input.TargetType, input.Target, input.RangeMin, input.RangeMax);
        plan.Edit(input.UnitCode.Trim(), input.Direction, input.IsAccumulated, input.ResponsibleEmployeeId ?? plan.ResponsibleEmployeeId);
        foreach (var period in input.Periods ?? [])
        {
            results.First(r => r.Id == period.ResultId).SetTarget(input.TargetType, period.Target, period.RangeMin, period.RangeMax);
        }

        if (input.Periods is null)
        {
            foreach (var result in results.Where(r => r.TargetType != input.TargetType && r.Status != ResultStatuses.Approved))
            {
                result.SetTarget(input.TargetType, input.Target, input.RangeMin, input.RangeMax);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await calculator.RecalculateAsync(plan.Id, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<int>> HandleAsync(ConfirmKpiPlans command, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var plans = await db.Plans.Where(p => command.PlanIds.Contains(p.Id) && p.Status == PlanStatuses.Draft).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        foreach (var plan in plans)
        {
            var periods = await db.Results.AsNoTracking().Where(r => r.PlanId == plan.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (TargetErrors.Of(plan.TargetType, plan.Target, plan.RangeMin, plan.RangeMax) is not null
                || periods.Any(r => TargetErrors.Of(r.TargetType, r.Target, r.RangeMin, r.RangeMax) is not null))
            {
                return Error.Rule("kpi.targetsMissing", "kpi.errors.targetsMissing");
            }

            plan.Confirm(now, context.UserId);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await opener.OpenDueAsync(plans.Select(p => p.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return plans.Count;
    }
}
