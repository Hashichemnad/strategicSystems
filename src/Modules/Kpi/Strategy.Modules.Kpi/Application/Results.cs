using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Approval process <c>kpi.result</c> (SPEC K4.6): default single step — Strategy Office / PMO; editable in the Approval Settings centre.</summary>
public static class ResultApprovalProcess
{
    public const string Key = "kpi.result";

    public static readonly ApprovalProcessDefinition Definition = new(
        Key, KpiPermissions.SystemCode, "اعتماد نتيجة مؤشر أداء", "KPI result approval", KpiResult.EntityType, KpiPermissions.ResultRead, "/kpi/results/{id}",
        [
            new("capped", ExpressionValueType.Number, "الأداء المحدود", "Capped performance"),
            new("late", ExpressionValueType.Boolean, "متأخرة", "Late"),
            new("notApplicable", ExpressionValueType.Boolean, "غير منطبقة", "Not applicable"),
        ],
        [
            new("strategy-office", "مكتب الاستراتيجية", "Strategy Office / PMO",
                new ApproverRule(ApproverRuleKinds.Role, RoleKey: KpiPermissions.StrategyOfficeRoleKey), SlaHours: 40),
        ],
        KpiPermissions.ApproverRole,
        "التحقق من الدليل واعتماد نتيجة الفترة قبل نشرها في لوحات الأداء. يمكن إضافة خطوة مراجعة لمدير الإدارة قبل مكتب الاستراتيجية.",
        "Verify the evidence and approve the period result before it is published to dashboards. A department manager review step can be added before the Strategy Office.");
}

public sealed record KpiResultCardDto(
    Guid Id, string Code, string? NameAr, string? NameEn, string? MeasuringEquation, string Nature, string UnitCode, string Direction, string? Formula,
    IReadOnlyList<DataElement> DataElements, IReadOnlyList<ScaleLevel> Scale, bool AllowsNegative, string MeasuringPeriod);

public sealed record KpiResultDto(
    Guid Id,
    KpiResultCardDto Card,
    Guid PlanId,
    int Year,
    string PeriodCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string TargetType,
    decimal? Target,
    decimal? RangeMin,
    decimal? RangeMax,
    decimal? Achieve,
    IReadOnlyDictionary<string, decimal?> ElementValues,
    decimal? ScaleValue,
    decimal? Actual,
    decimal? Capped,
    string Rag,
    bool? Achieved,
    string? Causality,
    string? CorrectiveActions,
    string? Notes,
    bool NotApplicable,
    string? NaReason,
    string Status,
    DateOnly? OpensOn,
    DateOnly? DueOn,
    DateTimeOffset? DueAt,
    DateTimeOffset? SubmittedAt,
    KpiPersonDto? Owner,
    KpiUnitDto? OwnerUnit,
    bool Late,
    int DaysLate,
    DateTimeOffset? ApprovedAt,
    int ReturnCount,
    int ReopenedCount,
    string? StatusReason,
    IReadOnlyDictionary<string, bool> Checklist,
    IReadOnlyList<KpiPeriodPointDto> History,
    IReadOnlyList<string> Anomalies,
    int EvidenceCount,
    bool DraftFromMcp,
    KpiResultPermissionsDto Can,
    uint Version);

public sealed record KpiResultPermissionsDto(bool Submit, bool Recall, bool Review, bool Reopen, bool OpenNow, bool Lock);

public sealed record KpiResultListItemDto(
    Guid Id, Guid CardId, string CardCode, string? CardNameAr, string? CardNameEn, int Year, string PeriodCode, string TargetType, decimal? Target, decimal? RangeMin,
    decimal? RangeMax, string UnitCode, decimal? Achieve, decimal? Capped, string Rag, string Status, DateOnly? DueOn, bool Overdue, bool Late, int DaysLate,
    KpiUnitDto? OwnerUnit, KpiPersonDto? Owner, DateTimeOffset? SubmittedAt, bool NotApplicable, int EvidenceCount, IReadOnlyList<string> Anomalies, bool MyDecision,
    string? StepNameAr, string? StepNameEn);

public sealed record KpiEntryInput(
    decimal? Achieve, IReadOnlyDictionary<string, decimal?>? ElementValues, decimal? ScaleValue, string? Causality, string? CorrectiveActions, string? Notes,
    bool NotApplicable, string? NaReason)
{
    public ResultEntry ToEntry() => new(Achieve, ElementValues, ScaleValue, Causality, CorrectiveActions, Notes, NotApplicable, NaReason);
}

public sealed record KpiBulkOutcomeDto(Guid ResultId, bool Success, string? ErrorKey);

public sealed record KpiBulkResultDto(int Approved, int Failed, IReadOnlyList<KpiBulkOutcomeDto> Outcomes);

public sealed record GetKpiResult(Guid Id) : IQuery<KpiResultDto>;

public sealed record ListMyPendingKpiResults(string? View, int? Year) : IQuery<IReadOnlyList<KpiResultListItemDto>>;

public sealed record ListKpiApprovalQueue(int? Year, string? PeriodCode, Guid? UnitId, bool MineOnly) : IQuery<IReadOnlyList<KpiResultListItemDto>>;

public sealed record SaveKpiResultDraft(Guid Id, KpiEntryInput Entry, uint Version, bool FromMcp = false) : ICommand<KpiResultDto>;

public sealed record SubmitKpiResult(Guid Id, KpiEntryInput Entry, string? Comment, uint Version) : ICommand<KpiResultDto>;

public sealed record RecallKpiResult(Guid Id, uint Version) : ICommand<KpiResultDto>;

public sealed record DecideKpiResult(Guid Id, string Action, string? Comment, IReadOnlyDictionary<string, bool>? Checklist) : ICommand<KpiResultDto>;

public sealed record BulkApproveKpiResults(IReadOnlyList<Guid> ResultIds, string? Comment, bool ChecklistConfirmed) : ICommand<KpiBulkResultDto>;

public sealed record ReopenKpiResult(Guid Id, string Reason) : ICommand<KpiResultDto>;

public sealed record OpenKpiResultNow(Guid Id) : ICommand<KpiResultDto>;

/// <summary>Value computation and validation of an entry (SPEC §2.3 C validation, §2.7 composite / qualitative KPIs).</summary>
internal static class EntryRules
{
    public static (decimal? Achieve, List<FieldError> Errors) Evaluate(
        KpiCard card, KpiResult result, string direction, ResultEntry entry, bool submitting, KpiSettings settings, bool hasEvidence)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        foreach (var (field, value) in new[] { ("causality", entry.Causality), ("correctiveActions", entry.CorrectiveActions), ("notes", entry.Notes), ("naReason", entry.NaReason) })
        {
            if (value is { Length: > KpiResult.TextMaxLength })
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = KpiResult.TextMaxLength }));
            }
        }

        if (entry.NotApplicable)
        {
            if (submitting && string.IsNullOrWhiteSpace(entry.NaReason))
            {
                Add("naReason", "kpi.errors.naReasonRequired");
            }

            return (null, errors);
        }

        decimal? achieve = entry.Achieve;
        if (card.IsComposite)
        {
            var values = entry.ElementValues ?? new Dictionary<string, decimal?>();
            if (values.Values.Any(v => v is not null) || submitting)
            {
                var (value, error) = CompositeFormula.Evaluate(card.Formula!, card.Elements, values);
                if (error is not null)
                {
                    Add("elementValues", error);
                }

                achieve = value;
            }
            else
            {
                achieve = null;
            }
        }
        else if (card.IsQualitative)
        {
            achieve = entry.ScaleValue;
            if (achieve is { } level && card.ScaleLevels.All(s => s.Value != level))
            {
                Add("scaleValue", "kpi.errors.scaleLevel");
            }
        }

        if (achieve is < 0 && !card.AllowsNegative)
        {
            Add("achieve", "kpi.errors.negative");
        }

        if (!submitting)
        {
            return (achieve, errors);
        }

        if (achieve is null && !errors.Any(e => e.Field == "elementValues"))
        {
            Add(card.IsQualitative ? "scaleValue" : "achieve", "kpi.errors.valueRequired");
        }

        var performance = PerformanceCalculator.Calculate(result.TargetType, direction, result.Target, result.RangeMin, result.RangeMax, achieve);
        if (performance.Capped is < 1m)
        {
            if (string.IsNullOrWhiteSpace(entry.Causality))
            {
                Add("causality", "kpi.errors.causalityRequired");
            }

            if (string.IsNullOrWhiteSpace(entry.CorrectiveActions))
            {
                Add("correctiveActions", "kpi.errors.correctiveActionsRequired");
            }
        }

        if (settings.RequireEvidence && !hasEvidence)
        {
            Add("evidence", "kpi.errors.evidenceRequired");
        }

        return (achieve, errors);
    }
}

/// <summary>Builds result DTOs (history, anomalies, evidence count, the user's actions).</summary>
internal sealed class ResultViews(KpiDbContext db, KpiReader reader, IRequestContext context, IFileService files, IApprovalEngine engine)
{
    public async Task<KpiResultDto> ToDtoAsync(KpiResult r, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == r.CardId, ct).ConfigureAwait(false);
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == r.PlanId, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var green = card.GreenFrom ?? settings.GreenFrom;
        var amber = card.AmberFrom ?? settings.AmberFrom;
        var previous = await db.Results.AsNoTracking()
            .Where(x => x.CardId == r.CardId && x.Status == ResultStatuses.Approved && x.Id != r.Id && (x.Year < r.Year || (x.Year == r.Year && x.Sort < r.Sort)))
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Sort).Take(8).ToListAsync(ct).ConfigureAwait(false);
        previous.Reverse();
        var history = previous.Select(x => new KpiPeriodPointDto(x.Id, x.Year, x.PeriodCode, x.Target, x.RangeMin, x.RangeMax, x.Achieve, x.ActualPerformance,
            x.CappedPerformance, RagBands.Band(x.CappedPerformance, green, amber), x.Status, x.NotApplicable)).ToList();
        var anomalies = AnomalyRules.Flags(r.Achieve, previous.Where(x => x.Achieve is not null && !x.NotApplicable).Select(x => x.Achieve!.Value).ToList(), plan.UnitCode,
            card.AllowsNegative, r.Target ?? r.RangeMax, settings.AnomalyJumpRatio);
        var evidence = (await files.ListAsync(KpiResult.EntityType, r.Id.ToString("D"), KpiFilePurposes.Evidence.Key, ct).ConfigureAwait(false)).Count;
        var people = await reader.PeopleAsync([r.OwnerEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([r.OwnerUnitId], ct).ConfigureAwait(false);
        var user = context.User;
        var canSubmit = r.IsOpenForUpdate && await reader.CanAsync(user, KpiPermissions.ResultSubmit, r, ct).ConfigureAwait(false);
        var canRecall = r.Status == ResultStatuses.PendingApproval && await reader.CanAsync(user, KpiPermissions.ResultSubmit, r, ct).ConfigureAwait(false);
        var review = r.Status == ResultStatuses.PendingApproval
            && (await engine.GetStatusViewAsync(ResultApprovalProcess.Key, r.Id, user, ct).ConfigureAwait(false))?.MyTaskId is not null;
        var reopen = r.Status is ResultStatuses.Approved or ResultStatuses.Locked && await reader.CanAsync(user, KpiPermissions.ResultReopen, r, ct).ConfigureAwait(false);
        var openNow = r.Status == ResultStatuses.NotOpen && await reader.CanAsync(user, KpiPermissions.PlanManage, ct).ConfigureAwait(false);
        var canLock = r.Status is ResultStatuses.NotOpen or ResultStatuses.Open or ResultStatuses.Returned
            && await reader.CanAsync(user, KpiPermissions.ResultReopen, r, ct).ConfigureAwait(false);
        return new KpiResultDto(
            r.Id,
            new KpiResultCardDto(card.Id, card.Code, card.NameAr, card.NameEn, card.MeasuringEquation, card.Nature, plan.UnitCode, plan.Direction, card.Formula, card.Elements,
                card.ScaleLevels, card.AllowsNegative, plan.MeasuringPeriod),
            r.PlanId, r.Year, r.PeriodCode, r.PeriodStart, r.PeriodEnd, r.TargetType, r.Target, r.RangeMin, r.RangeMax, r.Achieve, r.Elements, r.ScaleValue,
            r.ActualPerformance, r.CappedPerformance, RagBands.Band(r.CappedPerformance, green, amber), r.Achieved, r.Causality, r.CorrectiveActions, r.Notes, r.NotApplicable,
            r.NaReason, r.Status, r.OpensOn, r.DueOn, r.DueAt, r.SubmittedAt, KpiReader.Person(r.OwnerEmployeeId, people), KpiReader.Unit(r.OwnerUnitId, units), r.Late,
            r.DaysLate, r.ApprovedAt, r.ReturnCount, r.ReopenedCount, r.StatusReason, r.ChecklistAnswers, history, anomalies, evidence, r.DraftFromMcp,
            new KpiResultPermissionsDto(canSubmit, canRecall, review, reopen, openNow, canLock), r.Version);
    }

    public async Task<IReadOnlyList<KpiResultListItemDto>> ToListAsync(IReadOnlyList<KpiResult> results, DateTimeOffset now, bool withDecisions, CancellationToken ct)
    {
        var cardIds = results.Select(r => r.CardId).Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var planIds = results.Select(r => r.PlanId).Distinct().ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(results.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(results.Select(r => r.OwnerUnitId), ct).ConfigureAwait(false);
        var withEvidence = await files.OwnersWithFilesAsync(KpiFilePurposes.Evidence.Key, results.Select(r => r.Id.ToString("D")).ToList(), ct).ConfigureAwait(false);
        var previous = await db.Results.AsNoTracking().Where(x => cardIds.Contains(x.CardId) && x.Status == ResultStatuses.Approved && !x.NotApplicable && x.Achieve != null)
            .Select(x => new { x.CardId, x.Year, x.Sort, x.Id, Achieve = x.Achieve!.Value }).ToListAsync(ct).ConfigureAwait(false);
        var items = new List<KpiResultListItemDto>();
        foreach (var r in results)
        {
            var card = cards[r.CardId];
            var plan = plans[r.PlanId];
            var before = previous.Where(x => x.CardId == r.CardId && x.Id != r.Id && (x.Year < r.Year || (x.Year == r.Year && x.Sort < r.Sort)))
                .OrderBy(x => x.Year).ThenBy(x => x.Sort).Select(x => x.Achieve).ToList();
            var anomalies = AnomalyRules.Flags(r.Achieve, before, plan.UnitCode, card.AllowsNegative, r.Target ?? r.RangeMax, settings.AnomalyJumpRatio);
            ApprovalStatusView? view = null;
            if (withDecisions && r.Status == ResultStatuses.PendingApproval)
            {
                view = await engine.GetStatusViewAsync(ResultApprovalProcess.Key, r.Id, context.User, ct).ConfigureAwait(false);
            }

            items.Add(new KpiResultListItemDto(r.Id, card.Id, card.Code, card.NameAr, card.NameEn, r.Year, r.PeriodCode, r.TargetType, r.Target, r.RangeMin, r.RangeMax,
                plan.UnitCode, r.Achieve, r.CappedPerformance, RagBands.Band(r.CappedPerformance, card.GreenFrom ?? settings.GreenFrom, card.AmberFrom ?? settings.AmberFrom),
                r.Status, r.DueOn, r.IsOpenForUpdate && r.DueAt is { } due && due < now, r.Late, r.DaysLate, KpiReader.Unit(r.OwnerUnitId, units),
                KpiReader.Person(r.OwnerEmployeeId, people), r.SubmittedAt, r.NotApplicable, withEvidence.Contains(r.Id.ToString("D")) ? 1 : 0, anomalies,
                view?.MyTaskId is not null, view?.Instance.CurrentStepNameAr, view?.Instance.CurrentStepNameEn));
        }

        return items;
    }
}

internal sealed class ResultQueries(KpiReader reader, ResultViews views, IRequestContext context, IClock clock)
    : IQueryHandler<GetKpiResult, KpiResultDto>, IQueryHandler<ListMyPendingKpiResults, IReadOnlyList<KpiResultListItemDto>>,
        IQueryHandler<ListKpiApprovalQueue, IReadOnlyList<KpiResultListItemDto>>
{
    public async Task<Result<KpiResultDto>> HandleAsync(GetKpiResult query, CancellationToken cancellationToken)
    {
        var result = await reader.ResultAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        return result is null ? Error.NotFound() : await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>My Pending Updates (SPEC §2.5): open / returned results I am responsible for (view <c>scope</c>: every one I may submit).</summary>
    public async Task<Result<IReadOnlyList<KpiResultListItemDto>>> HandleAsync(ListMyPendingKpiResults query, CancellationToken cancellationToken)
    {
        var source = await reader.ResultsAsync(context.User, cancellationToken, permission: KpiPermissions.ResultSubmit).ConfigureAwait(false);
        source = source.Where(r => r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned || r.Status == ResultStatuses.PendingApproval);
        if (query.View != "scope")
        {
            var me = context.User.EmployeeIdOf();
            source = source.Where(r => r.OwnerEmployeeId == me);
        }

        if (query.Year is { } year)
        {
            source = source.Where(r => r.Year == year);
        }

        var results = await source.OrderBy(r => r.DueOn).ThenBy(r => r.CardId).Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(await views.ToListAsync(results, clock.UtcNow, false, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The reviewer's queue (SPEC K4.1): pending results filtered by year, period and department.</summary>
    public async Task<Result<IReadOnlyList<KpiResultListItemDto>>> HandleAsync(ListKpiApprovalQueue query, CancellationToken cancellationToken)
    {
        var source = await reader.ResultsAsync(context.User, cancellationToken).ConfigureAwait(false);
        source = source.Where(r => r.Status == ResultStatuses.PendingApproval);
        if (query.Year is { } year)
        {
            source = source.Where(r => r.Year == year);
        }

        if (query.PeriodCode is { Length: > 0 } period)
        {
            source = source.Where(r => r.PeriodCode == period);
        }

        if (query.UnitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, cancellationToken).ConfigureAwait(false);
            source = source.Where(r => r.OwnerUnitId != null && units.Contains(r.OwnerUnitId.Value));
        }

        var results = await source.OrderBy(r => r.SubmittedAt).Take(PageLimit).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = await views.ToListAsync(results, clock.UtcNow, true, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<KpiResultListItemDto>>(query.MineOnly ? items.Where(i => i.MyDecision).ToList() : items);
    }

    private const int PageLimit = 200;
}

internal sealed class ResultCommands(
    KpiDbContext db, KpiReader reader, ResultViews views, IRequestContext context, IClock clock, IApprovalEngine engine, IFileService files, KpiCalendar calendar,
    PlanCalculator calculator, KpiNotifier notifier, IServiceScopeFactory scopes)
    : ICommandHandler<SaveKpiResultDraft, KpiResultDto>, ICommandHandler<SubmitKpiResult, KpiResultDto>, ICommandHandler<RecallKpiResult, KpiResultDto>,
        ICommandHandler<DecideKpiResult, KpiResultDto>, ICommandHandler<BulkApproveKpiResults, KpiBulkResultDto>, ICommandHandler<ReopenKpiResult, KpiResultDto>,
        ICommandHandler<OpenKpiResultNow, KpiResultDto>
{
    private async Task<(KpiResult? Result, KpiCard? Card, Error? Error)> LoadForOwnerAsync(Guid id, CancellationToken ct)
    {
        var result = await reader.ResultAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (result is null)
        {
            return (null, null, Error.NotFound());
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.ResultSubmit, result, ct).ConfigureAwait(false))
        {
            return (null, null, Error.Forbidden());
        }

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, ct).ConfigureAwait(false);
        return (result, card, null);
    }

    private async Task<Result<KpiResult>> SaveEntryAsync(KpiResult result, KpiCard card, KpiEntryInput input, bool submitting, uint version, bool fromMcp, CancellationToken ct)
    {
        if (!result.IsOpenForUpdate)
        {
            return Error.Rule("kpi.notOpen", "kpi.errors.notOpenForUpdate", new Dictionary<string, object?> { ["status"] = result.Status });
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var hasEvidence = (await files.ListAsync(KpiResult.EntityType, result.Id.ToString("D"), KpiFilePurposes.Evidence.Key, ct).ConfigureAwait(false)).Count > 0;
        var entry = input.ToEntry();
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == result.PlanId, ct).ConfigureAwait(false);
        var (achieve, errors) = EntryRules.Evaluate(card, result, plan.Direction, entry, submitting, settings, hasEvidence);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(result).Property(r => r.Version).OriginalValue = version;
        result.SaveEntry(entry, achieve, PerformanceCalculator.Calculate(result.TargetType, plan.Direction, result.Target, result.RangeMin, result.RangeMax, achieve), fromMcp);
        return result;
    }

    public async Task<Result<KpiResultDto>> HandleAsync(SaveKpiResultDraft command, CancellationToken cancellationToken)
    {
        var (result, card, error) = await LoadForOwnerAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var saved = await SaveEntryAsync(result!, card!, command.Entry, false, command.Version, command.FromMcp, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await views.ToDtoAsync(result!, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiResultDto>> HandleAsync(SubmitKpiResult command, CancellationToken cancellationToken)
    {
        var (result, card, error) = await LoadForOwnerAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var saved = await SaveEntryAsync(result!, card!, command.Entry, true, command.Version, false, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var now = clock.UtcNow;
        var today = await calendar.TodayAsync(cancellationToken).ConfigureAwait(false);
        var applied = result!.Apply(ResultActions.Submit, now, context.UserId);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        var (late, days) = WindowRules.Lateness(now, today, result.DueAt, result.DueOn);
        result.MarkLate(late, days);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var started = await engine.SubmitAsync(new ApprovalSubmission(ResultApprovalProcess.Key, Subject(result, card!),
            new Dictionary<string, object?> { ["capped"] = result.CappedPerformance, ["late"] = result.Late, ["notApplicable"] = result.NotApplicable }, command.Comment),
            db, cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(result).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }

    internal static ApprovalSubject Subject(KpiResult result, KpiCard card) => new(
        KpiResult.EntityType, result.Id, $"{card.Code} · {result.PeriodCode.ToUpperInvariant()} {result.Year}", card.NameAr, card.NameEn, result.OwnerEmployeeId,
        result.SubmittedBy, result.OwnerUnitId);

    public async Task<Result<KpiResultDto>> HandleAsync(RecallKpiResult command, CancellationToken cancellationToken)
    {
        var (result, _, error) = await LoadForOwnerAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        if (result!.Status != ResultStatuses.PendingApproval)
        {
            return Error.Rule("kpi.invalidTransition", "kpi.errors.invalidTransition",
                new Dictionary<string, object?> { ["status"] = result.Status, ["action"] = ResultActions.Recall });
        }

        if (result.Version != command.Version)
        {
            return Error.Conflict();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var cancelled = await engine.CancelAsync(ResultApprovalProcess.Key, result.Id, "recalled", db, cancellationToken).ConfigureAwait(false);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(result).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiResultDto>> HandleAsync(DecideKpiResult command, CancellationToken cancellationToken)
    {
        var result = await reader.ResultAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        var view = await engine.GetStatusViewAsync(ResultApprovalProcess.Key, result.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (result.Status != ResultStatuses.PendingApproval || view?.MyTaskId is null)
        {
            return Error.Forbidden("workflow.notAnApprover");
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var answers = (command.Checklist ?? new Dictionary<string, bool>()).Where(a => settings.Checklist.Any(c => c.Code == a.Key)).ToDictionary(a => a.Key, a => a.Value);
        if (command.Action == ApprovalActions.Approve && ChecklistMissing(settings, answers))
        {
            return Error.Validation([new FieldError("checklist", "kpi.errors.checklistRequired", "kpi.errors.checklistRequired")]);
        }

        result.SetChecklist(answers);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var decided = await engine.DecideAsync(ResultApprovalProcess.Key, result.Id, command.Action, command.Comment, cancellationToken).ConfigureAwait(false);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await db.Entry(result).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }

    internal static bool ChecklistMissing(KpiSettings settings, IReadOnlyDictionary<string, bool> answers) =>
        settings.Checklist.Where(c => c.Required).Any(c => !answers.TryGetValue(c.Code, out var ticked) || !ticked);

    /// <summary>
    /// Bulk approval (SPEC K4.5, ADR 0064): the reviewer confirms the checklist once for the selection; each result is decided in its own scope and
    /// transaction (a failure never leaks into the next), partial success reported.
    /// </summary>
    public async Task<Result<KpiBulkResultDto>> HandleAsync(BulkApproveKpiResults command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, KpiPermissions.ResultReview, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!command.ChecklistConfirmed)
        {
            return Error.Validation([new FieldError("checklistConfirmed", "kpi.errors.checklistRequired", "kpi.errors.checklistRequired")]);
        }

        if (command.ResultIds.Count is 0 or > 200)
        {
            return Error.Validation([new FieldError("resultIds", "kpi.errors.nothingSelected", "kpi.errors.nothingSelected")]);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var answers = settings.Checklist.ToDictionary(c => c.Code, _ => true);
        var outcomes = new List<KpiBulkOutcomeDto>();
        foreach (var id in command.ResultIds.Distinct())
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(context.CorrelationId, context.Channel, context.User, context.IpAddress, context.UserAgent);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var decided = await dispatcher.SendAsync(new DecideKpiResult(id, ApprovalActions.Approve, command.Comment, answers), cancellationToken).ConfigureAwait(false);
            outcomes.Add(new KpiBulkOutcomeDto(id, decided.IsSuccess, decided.IsSuccess ? null : decided.Error.MessageKey));
        }

        return new KpiBulkResultDto(outcomes.Count(o => o.Success), outcomes.Count(o => !o.Success), outcomes);
    }

    public async Task<Result<KpiResultDto>> HandleAsync(ReopenKpiResult command, CancellationToken cancellationToken)
    {
        var result = await reader.ResultAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.ResultReopen, result, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        var applied = result.Apply(ResultActions.Reopen, clock.UtcNow, context.UserId, command.Reason);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await calculator.RecalculateAsync(result.PlanId, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(KpiNotifications.Reopened, [result.OwnerEmployeeId], KpiNotifier.Values(card, result, KpiCalendar.Date(result.DueOn), null, command.Reason),
            $"/kpi/results/{result.Id:D}", db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<KpiResultDto>> HandleAsync(OpenKpiResultNow command, CancellationToken cancellationToken)
    {
        var result = await reader.ResultAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.PlanManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var applied = result.Apply(ResultActions.OpenWindow, clock.UtcNow, context.UserId);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        result.MarkOpenedNotified(clock.UtcNow);
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(KpiNotifications.WindowOpened, [result.OwnerEmployeeId], KpiNotifier.Values(card, result, KpiCalendar.Date(result.DueOn), count: 1),
            "/kpi/pending", db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(result, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Moves results with the approval (inside the engine's transaction, ADR 0023): approved → plan recalculated, owner notified, integration events;
/// returned / rejected → Returned; cancelled (recall) → Open. A final approval without the required checklist answers is refused (ADR 0064).
/// </summary>
internal sealed class ResultApprovalHook(
    KpiDbContext db, Platform.Settings.ISettingsStore settings, PlanCalculator calculator, KpiNotifier notifier, IIntegrationEventPublisher events, IClock clock,
    KpiReader reader, IUserDirectory users)
    : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [ResultApprovalProcess.Key];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        var action = approvalEvent.Kind switch
        {
            ApprovalEventKinds.Approved => ResultActions.Approved,
            ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned => ResultActions.Returned,
            ApprovalEventKinds.Cancelled => ResultActions.Recall,
            _ => null,
        };
        if (action is null)
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var result = await db.Results.FirstOrDefaultAsync(r => r.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        var configuration = await settings.GetAsync<KpiSettings>(cancellationToken).ConfigureAwait(false);
        if (action == ResultActions.Approved && ResultCommands.ChecklistMissing(configuration, result.ChecklistAnswers))
        {
            return Error.Rule("kpi.checklistRequired", "kpi.errors.checklistRequired");
        }

        var now = clock.UtcNow;
        var applied = result.Apply(action, now, approvalEvent.ActorUserId, approvalEvent.Comment);
        if (applied.IsFailure)
        {
            return applied;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, cancellationToken).ConfigureAwait(false);
        var link = $"/kpi/results/{result.Id:D}";
        var actor = await ActorAsync(approvalEvent.ActorUserId, cancellationToken).ConfigureAwait(false);
        if (action == ResultActions.Approved)
        {
            await calculator.RecalculateAsync(result.PlanId, cancellationToken).ConfigureAwait(false);
            await notifier.SendAsync(KpiNotifications.Approved, [result.OwnerEmployeeId], KpiNotifier.Values(card, result, actor: actor), link, db, cancellationToken)
                .ConfigureAwait(false);
            await PublishAsync(result, card, configuration, now, cancellationToken).ConfigureAwait(false);
        }
        else if (action == ResultActions.Returned)
        {
            await notifier.SendAsync(KpiNotifications.Returned, [result.OwnerEmployeeId], KpiNotifier.Values(card, result, actor: actor, text: approvalEvent.Comment), link,
                db, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async Task<string?> ActorAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is not { } id || (await users.GetManyAsync([id], ct).ConfigureAwait(false)).GetValueOrDefault(id) is not { } user)
        {
            return null;
        }

        if (user.EmployeeId is { } employee && (await reader.PeopleAsync([employee], ct).ConfigureAwait(false)).GetValueOrDefault(employee) is { } person)
        {
            return person.NameAr == person.NameEn ? person.NameEn : $"{person.NameAr} / {person.NameEn}";
        }

        return user.UserName;
    }

    private async Task PublishAsync(KpiResult result, KpiCard card, KpiSettings configuration, DateTimeOffset now, CancellationToken ct)
    {
        var green = card.GreenFrom ?? configuration.GreenFrom;
        var amber = card.AmberFrom ?? configuration.AmberFrom;
        var rag = RagBands.Band(result.CappedPerformance, green, amber);
        await events.PublishAsync(new KpiResultApproved(result.Id, card.Id, card.Code, result.Year, result.PeriodCode, result.Achieve, result.ActualPerformance,
            result.CappedPerformance, rag, result.NotApplicable, result.OwnerUnitId, now), db, ct).ConfigureAwait(false);
        bool Missed(string band) => band == RagCodes.Red || (configuration.MissedIncludesAmber && band == RagCodes.Amber);
        if (result.NotApplicable || !Missed(rag))
        {
            return;
        }

        var approved = await db.Results.AsNoTracking().Where(r => r.CardId == card.Id && r.Status == ResultStatuses.Approved && !r.NotApplicable)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Sort).Select(r => r.CappedPerformance).Take(24).ToListAsync(ct).ConfigureAwait(false);
        var consecutive = approved.TakeWhile(c => Missed(RagBands.Band(c, green, amber))).Count();
        await events.PublishAsync(new KpiTargetMissed(result.Id, card.Id, card.Code, card.NameAr, card.NameEn, result.Year, result.PeriodCode, result.Achieve,
            result.Target ?? result.RangeMin, result.CappedPerformance, rag, Math.Max(1, consecutive), result.OwnerUnitId, result.OwnerEmployeeId, now), db, ct)
            .ConfigureAwait(false);
    }
}

/// <summary>"Next action required + who" for a KPI result (SPEC §0.2 current-status engine).</summary>
internal sealed class ResultCurrentStatus(KpiReader reader, IRequestContext context, IApprovalEngine engine) : ICurrentStatusProvider
{
    public string ResourceType => KpiResult.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var result = await reader.ResultAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var people = await reader.PeopleAsync([result.OwnerEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] owners = result.OwnerEmployeeId is { } o && people.GetValueOrDefault(o) is { } p ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        var date = new BilingualText(KpiCalendar.Date(result.DueOn), KpiCalendar.Date(result.DueOn));
        var opens = new BilingualText(KpiCalendar.Date(result.OpensOn), KpiCalendar.Date(result.OpensOn));
        var canSubmit = await reader.CanAsync(context.User, KpiPermissions.ResultSubmit, result, cancellationToken).ConfigureAwait(false);
        switch (result.Status)
        {
            case ResultStatuses.NotOpen:
                return new CurrentStatus("kpi.currentStatus.notOpen", new Dictionary<string, BilingualText> { ["date"] = opens }, "neutral", null, [], [], result.Status);
            case ResultStatuses.Open:
            case ResultStatuses.Returned:
                {
                    var returned = result.Status == ResultStatuses.Returned;
                    return new CurrentStatus(returned ? "kpi.currentStatus.returned" : "kpi.currentStatus.open", new Dictionary<string, BilingualText> { ["date"] = date },
                        returned ? "warning" : "info", "kpi.currentStatus.owner", owners, canSubmit ? [new CurrentStatusAction("submit", "kpi.actions.submit")] : [],
                        result.Status);
                }

            case ResultStatuses.PendingApproval:
                {
                    var view = await engine.GetStatusViewAsync(ResultApprovalProcess.Key, result.Id, context.User, cancellationToken).ConfigureAwait(false);
                    var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? view?.Instance.CurrentStepNameEn, view?.Instance.CurrentStepNameEn ?? view?.Instance.CurrentStepNameAr);
                    var actions = new List<CurrentStatusAction>();
                    if (view?.MyTaskId is { } taskId)
                    {
                        actions.Add(new CurrentStatusAction("review", "kpi.actions.review", "primary", taskId));
                    }

                    if (canSubmit)
                    {
                        actions.Add(new CurrentStatusAction("recall", "kpi.actions.recall", "secondary"));
                    }

                    return new CurrentStatus("workflow.currentStatus.waitingStep", new Dictionary<string, BilingualText> { ["step"] = step }, "info",
                        "workflow.currentStatus.approvers", view?.Approvers ?? [], actions, result.Status);
                }

            case ResultStatuses.Approved:
                return new CurrentStatus("kpi.currentStatus.approved", new Dictionary<string, BilingualText>(), "success", null, [], [], result.Status);
            default:
                return new CurrentStatus("kpi.currentStatus.locked", new Dictionary<string, BilingualText>(), "neutral", null, [], [], result.Status);
        }
    }
}

/// <summary>My Work: my open / returned results with their due dates (approval tasks come from the engine's own source).</summary>
internal sealed class KpiMyWork(KpiDbContext db, IAccessService access, IClock clock) : IMyWorkSource
{
    public string SystemCode => KpiPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var query = await access.ApplyScopeAsync(db.Results.AsNoTracking(), user, KpiPermissions.ResultSubmit, KpiScope.Result, cancellationToken).ConfigureAwait(false);
        var results = await query.Where(r => r.OwnerEmployeeId == me && (r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned))
            .OrderBy(r => r.DueOn).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        var cardIds = results.Select(r => r.CardId).Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        return results.Select(r =>
        {
            var card = cards[r.CardId];
            var key = r.Status == ResultStatuses.Returned ? "kpi.myWork.returned" : r.DueAt is { } due && due < now ? "kpi.myWork.overdue" : "kpi.myWork.open";
            return new MyWorkItem(KpiPermissions.SystemCode, KpiResult.EntityType, r.Id, $"{card.Code} · {r.PeriodCode.ToUpperInvariant()} {r.Year}", card.NameAr, card.NameEn,
                key, $"/kpi/results/{r.Id:D}", r.OpensOn is { } o ? new DateTimeOffset(o.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null, r.DueOn);
        }).ToList();
    }
}
