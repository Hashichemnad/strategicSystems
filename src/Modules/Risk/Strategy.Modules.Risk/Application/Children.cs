using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Application;

public sealed record ControlDto(
    Guid Id, Guid RiskId, ControlValues Values, RiskPersonDto? Owner, Guid? SourceActionId, string Status, bool CanEdit, bool CanRate, uint Version);

public sealed record ActionDto(
    Guid Id, string Number, Guid RiskId, string RiskNumber, string? RiskTitleAr, string? RiskTitleEn, ActionValues Values, RiskPersonDto? Owner,
    IReadOnlyList<RiskUnitDto> ResponsibleUnits, DateOnly? ActualStart, DateOnly? ActualEnd, decimal CompletionPct, string Status, bool Overdue, Guid? ConvertedControlId,
    bool CanEdit, bool CanProgress, bool CanConvert, int DraftUpdates, uint Version);

public sealed record ActionUpdateDto(Guid Id, string? Note, decimal CompletionPct, DateTimeOffset At, RiskPersonDto? By, bool Draft);

public sealed record KriDto(
    Guid Id, Guid RiskId, Guid KpiCardId, string CardCode, string? CardNameAr, string? CardNameEn, string Basis, string Direction, decimal? WarningThreshold,
    decimal BreachThreshold, string Status, decimal? LastValue, string LastLevel, string? LastPeriod, DateTimeOffset? LastAt, KpiYearPerformance? Performance, bool CardVisible,
    uint Version);

public sealed record EventDto(
    Guid Id, string Number, EventValues Values, IReadOnlyList<RiskListRefDto> Risks, RiskUnitDto? Unit, Guid? CapRemarkId, string Status, DateTimeOffset CreatedAt,
    bool CanEdit, uint Version);

public sealed record RiskListRefDto(Guid Id, string Number, string? TitleAr, string? TitleEn);

public sealed record ReviewDto(
    Guid Id, Guid RiskId, string Status, ReviewValues Values, string? DecisionComment, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt,
    decimal PreviewResidualScore, string PreviewResidualBand, bool CanEdit, uint Version);

public sealed record ProgressInput(decimal CompletionPct, string? Note, DateOnly? ActualStart, DateOnly? ActualEnd, uint Version, bool FromMcp = false);

public sealed record ConvertInput(string EffectivenessCode, string? NameAr, string? NameEn, string? TypeCode, string? NatureCode, string? FrequencyCode);

public sealed record ListControls(Guid RiskId) : IQuery<IReadOnlyList<ControlDto>>;

public sealed record SaveControl(Guid RiskId, Guid? Id, ControlValues Values, uint? Version) : ICommand<ControlDto>;

public sealed record RateControl(Guid Id, string? EffectivenessCode, uint Version) : ICommand<ControlDto>;

public sealed record RetireControl(Guid Id) : ICommand<ControlDto>;

public sealed record ListActions(Guid RiskId) : IQuery<IReadOnlyList<ActionDto>>;

public sealed record ListMyActions(bool IncludeCompleted) : IQuery<IReadOnlyList<ActionDto>>;

public sealed record SaveAction(Guid RiskId, Guid? Id, ActionValues Values, uint? Version) : ICommand<ActionDto>;

public sealed record CancelAction(Guid Id) : ICommand<ActionDto>;

public sealed record UpdateActionProgress(Guid Id, ProgressInput Input) : ICommand<ActionDto>;

public sealed record ConvertActionToControl(Guid Id, ConvertInput Input) : ICommand<ControlDto>;

public sealed record ListActionUpdates(Guid Id) : IQuery<IReadOnlyList<ActionUpdateDto>>;

public sealed record ListKris(Guid RiskId) : IQuery<IReadOnlyList<KriDto>>;

public sealed record SaveKri(Guid RiskId, Guid? Id, KriValues Values, uint? Version) : ICommand<KriDto>;

public sealed record RemoveKri(Guid Id) : ICommand<KriDto>;

public sealed record ListRiskEvents(Guid? RiskId) : IQuery<IReadOnlyList<EventDto>>;

public sealed record LogRiskEvent(Guid? Id, EventValues Values, uint? Version) : ICommand<EventDto>;

public sealed record ListReviews(Guid RiskId) : IQuery<IReadOnlyList<ReviewDto>>;

public sealed record StartReview(Guid RiskId) : ICommand<ReviewDto>;

public sealed record SaveReview(Guid Id, ReviewValues Values, uint Version) : ICommand<ReviewDto>;

public sealed record SubmitReview(Guid Id, string? Comment) : ICommand<ReviewDto>;

/// <summary>Controls, treatment actions, KRIs, events and periodic reviews (design §3.2, §5.2, §5.3, §9).</summary>
internal sealed class ChildHandlers(
    RiskDbContext db, RiskReader reader, RiskViews views, RiskRater rater, FrameworkStore frameworks, RiskRoleSync roles, RiskNotifier notifier, INumberingService numbering,
    ILookupService lookups, IApprovalEngine engine, IIntegrationEventPublisher events, IRequestContext context, IClock clock, RiskDeadlines deadlines,
    IServiceProvider services)
    : IQueryHandler<ListControls, IReadOnlyList<ControlDto>>, ICommandHandler<SaveControl, ControlDto>, ICommandHandler<RateControl, ControlDto>,
        ICommandHandler<RetireControl, ControlDto>, IQueryHandler<ListActions, IReadOnlyList<ActionDto>>, IQueryHandler<ListMyActions, IReadOnlyList<ActionDto>>,
        ICommandHandler<SaveAction, ActionDto>, ICommandHandler<CancelAction, ActionDto>, ICommandHandler<UpdateActionProgress, ActionDto>,
        ICommandHandler<ConvertActionToControl, ControlDto>, IQueryHandler<ListActionUpdates, IReadOnlyList<ActionUpdateDto>>, IQueryHandler<ListKris, IReadOnlyList<KriDto>>,
        ICommandHandler<SaveKri, KriDto>, ICommandHandler<RemoveKri, KriDto>, IQueryHandler<ListRiskEvents, IReadOnlyList<EventDto>>, ICommandHandler<LogRiskEvent, EventDto>,
        IQueryHandler<ListReviews, IReadOnlyList<ReviewDto>>, ICommandHandler<StartReview, ReviewDto>, ICommandHandler<SaveReview, ReviewDto>,
        ICommandHandler<SubmitReview, ReviewDto>
{
    // ---- helpers ------------------------------------------------------------------------------------------------------------------------

    private async Task<(RiskRecord? Risk, RiskPermissionsDto? Can)> RiskAsync(Guid riskId, CancellationToken ct, bool tracked = false)
    {
        var risk = await reader.RiskAsync(riskId, context.User, ct, tracked).ConfigureAwait(false);
        return risk is null ? (null, null) : (risk, await views.PermissionsAsync(risk, context.User, ct).ConfigureAwait(false));
    }

    private bool IsMe(Guid? employee) => employee is { } e && context.User.EmployeeIdOf() == e;

    private async Task ReRateAsync(Guid riskId, CancellationToken ct)
    {
        var risk = await db.Risks.FirstAsync(r => r.Id == riskId, ct).ConfigureAwait(false);
        await rater.RateAsync(risk, await reader.SettingsAsync(ct).ConfigureAwait(false), clock.UtcNow, ct).ConfigureAwait(false);
    }

    private async Task<List<FieldError>> ControlErrorsAsync(ControlValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        foreach (var (field, type, code) in new[]
                 {
                     ("typeCode", RiskPermissions.ControlTypeLookup, v.TypeCode), ("natureCode", RiskPermissions.ControlNatureLookup, v.NatureCode),
                     ("frequencyCode", RiskPermissions.ControlFrequencyLookup, v.FrequencyCode),
                 })
        {
            if (code is not null && !await lookups.IsActiveCodeAsync(type, code, ct).ConfigureAwait(false))
            {
                errors.Add(new FieldError(field, "validation.invalid", "validation.invalid"));
            }
        }

        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        if (v.EffectivenessCode is { } e && scorer.Content.ControlEffectiveness.All(x => x.Code != e))
        {
            errors.Add(new FieldError("effectivenessCode", "validation.invalid", "validation.invalid"));
        }

        return errors;
    }

    private async Task PublishIneffectiveAsync(Control control, RiskRecord risk, string? before, CancellationToken ct)
    {
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        if (control.EffectivenessCode is { } code && code != before && code == scorer.LowestEffectiveness)
        {
            await events.PublishAsync(new RiskControlRatedIneffective(control.Id, risk.Id, risk.Number, control.NameAr, control.NameEn, risk.UnitId, control.OwnerEmployeeId,
                clock.UtcNow), db, ct).ConfigureAwait(false);
        }
    }

    // ---- controls -----------------------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ControlDto>>> HandleAsync(ListControls query, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(query.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var controls = await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id).OrderBy(c => c.Status).ThenBy(c => c.CreatedAt).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var people = await reader.PeopleAsync(controls.Select(c => c.OwnerEmployeeId), cancellationToken).ConfigureAwait(false);
        var rate = await reader.CanAsync(context.User, RiskPermissions.ControlUpdate, risk, cancellationToken).ConfigureAwait(false);
        return controls.Select(c => ToDto(c, people, can!.ManageChildren, risk.IsOpen && (can.ManageChildren || (rate && IsMe(c.OwnerEmployeeId))))).ToList();
    }

    private static ControlDto ToDto(Control c, IReadOnlyDictionary<Guid, RiskPersonDto> people, bool edit, bool rate) =>
        new(c.Id, c.RiskId, new ControlValues(c.NameAr, c.NameEn, c.Description, c.TypeCode, c.NatureCode, c.OwnerEmployeeId, c.FrequencyCode, c.EffectivenessCode,
            c.LastAssessedOn, c.Evidence), RiskReader.Person(c.OwnerEmployeeId, people), c.SourceActionId, c.Status, edit && c.Status == ControlStatuses.Active,
            rate && c.Status == ControlStatuses.Active, c.Version);

    private async Task<ControlDto> ControlDtoAsync(Control c, CancellationToken ct) =>
        ToDto(c, await reader.PeopleAsync([c.OwnerEmployeeId], ct).ConfigureAwait(false), true, true);

    public async Task<Result<ControlDto>> HandleAsync(SaveControl command, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(command.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        var errors = await ControlErrorsAsync(command.Values, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Control control;
        string? before = null;
        if (command.Id is { } id)
        {
            control = await db.Controls.FirstOrDefaultAsync(c => c.Id == id && c.RiskId == risk.Id && c.Status == ControlStatuses.Active, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException();
            db.Entry(control).Property(c => c.Version).OriginalValue = command.Version ?? control.Version;
            before = control.EffectivenessCode;
            control.Update(command.Values);
        }
        else
        {
            control = Control.Create(risk.Id, command.Values);
            db.Controls.Add(control);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishIneffectiveAsync(control, risk, before, cancellationToken).ConfigureAwait(false);
        await ReRateAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await roles.SyncAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ControlDtoAsync(control, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ControlDto>> HandleAsync(RateControl command, CancellationToken cancellationToken)
    {
        var control = await db.Controls.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (control is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(control.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var rate = await reader.CanAsync(context.User, RiskPermissions.ControlUpdate, risk, cancellationToken).ConfigureAwait(false);
        if (!risk.IsOpen || control.Status != ControlStatuses.Active || !(can!.ManageChildren || (rate && IsMe(control.OwnerEmployeeId))))
        {
            return Error.Forbidden();
        }

        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        if (command.EffectivenessCode is { } e && scorer.Content.ControlEffectiveness.All(x => x.Code != e))
        {
            return Error.Validation([new FieldError("effectivenessCode", "validation.invalid", "validation.invalid")]);
        }

        db.Entry(control).Property(c => c.Version).OriginalValue = command.Version;
        var before = control.EffectivenessCode;
        control.Rate(command.EffectivenessCode, await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishIneffectiveAsync(control, risk, before, cancellationToken).ConfigureAwait(false);
        await ReRateAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ControlDtoAsync(control, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ControlDto>> HandleAsync(RetireControl command, CancellationToken cancellationToken)
    {
        var control = await db.Controls.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (control is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(control.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        control.Retire();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ReRateAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await roles.SyncAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ControlDtoAsync(control, cancellationToken).ConfigureAwait(false);
    }

    // ---- treatment actions --------------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ActionDto>>> HandleAsync(ListActions query, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(query.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var actions = await db.Actions.AsNoTracking().Where(a => a.RiskId == risk.Id).OrderBy(a => a.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var progress = await reader.CanAsync(context.User, RiskPermissions.ActionUpdate, risk, cancellationToken).ConfigureAwait(false);
        return Result.Success(await ActionDtosAsync(actions, new Dictionary<Guid, RiskRecord> { [risk.Id] = risk },
            a => (can!.ManageChildren, risk.IsOpen && (can.ManageChildren || (progress && IsMe(a.OwnerEmployeeId)))), cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<ActionDto>>> HandleAsync(ListMyActions query, CancellationToken cancellationToken)
    {
        if (context.User.EmployeeIdOf() is not { } me)
        {
            return Array.Empty<ActionDto>();
        }

        var visible = await reader.RisksAsync(context.User, cancellationToken).ConfigureAwait(false);
        var actions = await db.Actions.AsNoTracking()
            .Where(a => a.OwnerEmployeeId == me && a.Status != ActionStatuses.Cancelled && (query.IncludeCompleted || a.Status != ActionStatuses.Completed))
            .Join(visible, a => a.RiskId, r => r.Id, (a, r) => a).OrderBy(a => a.PlannedEnd).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        var riskIds = actions.Select(a => a.RiskId).Distinct().ToList();
        var risks = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(await ActionDtosAsync(actions, risks, a => (false, risks[a.RiskId].IsOpen), cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<ActionDto>> ActionDtosAsync(
        IReadOnlyList<TreatmentAction> actions, Dictionary<Guid, RiskRecord> risks, Func<TreatmentAction, (bool Edit, bool Progress)> can, CancellationToken ct)
    {
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(actions.Select(a => a.OwnerEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(actions.SelectMany(a => a.ResponsibleUnitIds).Select(u => (Guid?)u), ct).ConfigureAwait(false);
        var ids = actions.Select(a => a.Id).ToList();
        var drafts = await db.ActionUpdates.AsNoTracking().Where(u => ids.Contains(u.ActionId) && u.Draft).GroupBy(u => u.ActionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        return actions.Select(a =>
        {
            var risk = risks[a.RiskId];
            var (edit, progress) = can(a);
            return new ActionDto(a.Id, a.Number, a.RiskId, risk.Number, risk.TitleAr, risk.TitleEn,
                new ActionValues(a.TitleAr, a.TitleEn, a.OwnerEmployeeId, a.ResponsibleUnitIds, a.PlannedStart, a.PlannedEnd, a.Cost), RiskReader.Person(a.OwnerEmployeeId, people),
                a.ResponsibleUnitIds.Select(u => RiskReader.Unit(u, units)!).ToList(), a.ActualStart, a.ActualEnd, a.CompletionPct, a.Status, a.IsOverdue(today),
                a.ConvertedControlId, edit && a.Status is ActionStatuses.NotStarted or ActionStatuses.InProgress,
                progress && a.Status is ActionStatuses.NotStarted or ActionStatuses.InProgress or ActionStatuses.Completed && a.ConvertedControlId is null,
                progress && a.Status == ActionStatuses.Completed && a.ConvertedControlId is null, drafts.GetValueOrDefault(a.Id), a.Version);
        }).ToList();
    }

    private async Task<ActionDto> ActionDtoAsync(TreatmentAction a, RiskRecord risk, CancellationToken ct) =>
        (await ActionDtosAsync([a], new Dictionary<Guid, RiskRecord> { [risk.Id] = risk }, _ => (true, true), ct).ConfigureAwait(false))[0];

    public async Task<Result<ActionDto>> HandleAsync(SaveAction command, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(command.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        var v = command.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (v.OwnerEmployeeId is null)
        {
            errors.Add(new FieldError("ownerEmployeeId", "validation.required", "validation.required"));
        }

        if (v.PlannedEnd is null)
        {
            errors.Add(new FieldError("plannedEnd", "validation.required", "validation.required"));
        }

        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            errors.Add(new FieldError("plannedEnd", "risk.errors.endBeforeStart", "risk.errors.endBeforeStart"));
        }

        if (v.Cost is < 0)
        {
            errors.Add(new FieldError("cost", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        TreatmentAction action;
        if (command.Id is { } id)
        {
            action = await db.Actions.FirstOrDefaultAsync(a => a.Id == id && a.RiskId == risk.Id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
            if (action.Status is ActionStatuses.Completed or ActionStatuses.Cancelled)
            {
                return Error.Rule("risk.actionClosed", "risk.errors.actionClosed");
            }

            db.Entry(action).Property(a => a.Version).OriginalValue = command.Version ?? action.Version;
            action.Update(v);
        }
        else
        {
            var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
            var number = await numbering.NextAsync(db, RiskPermissions.ActionNumbering, new NumberingContext(today), cancellationToken).ConfigureAwait(false);
            action = TreatmentAction.Create(number, risk.Id, v);
            db.Actions.Add(action);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await roles.SyncAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ActionDtoAsync(action, risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(CancelAction command, CancellationToken cancellationToken)
    {
        var action = await db.Actions.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(action.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        action.Cancel();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await roles.SyncAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ActionDtoAsync(action, risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(UpdateActionProgress command, CancellationToken cancellationToken)
    {
        var action = await db.Actions.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(action.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var progress = await reader.CanAsync(context.User, RiskPermissions.ActionUpdate, risk, cancellationToken).ConfigureAwait(false);
        if (!risk.IsOpen || !(can!.ManageChildren || (progress && IsMe(action.OwnerEmployeeId))))
        {
            return Error.Forbidden();
        }

        if (action.Status == ActionStatuses.Cancelled || action.ConvertedControlId is not null)
        {
            return Error.Rule("risk.actionClosed", "risk.errors.actionClosed");
        }

        var input = command.Input;
        if (input.CompletionPct is < 0 or > 1)
        {
            return Error.Validation([new FieldError("completionPct", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 0, ["to"] = 100 })]);
        }

        if (string.IsNullOrWhiteSpace(input.Note))
        {
            return Error.Validation([new FieldError("note", "validation.required", "validation.required")]);
        }

        var now = clock.UtcNow;
        db.Entry(action).Property(a => a.Version).OriginalValue = input.Version;
        if (!input.FromMcp)
        {
            action.Progress(input.CompletionPct, input.ActualStart, input.ActualEnd, await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false));
            foreach (var draft in await db.ActionUpdates.Where(u => u.ActionId == action.Id && u.Draft).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                draft.Confirm();
            }
        }

        db.ActionUpdates.Add(ActionUpdate.Create(action.Id, input.Note.Trim(), input.CompletionPct, now, context.User.UserId(), draft: input.FromMcp));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ActionDtoAsync(action, risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ControlDto>> HandleAsync(ConvertActionToControl command, CancellationToken cancellationToken)
    {
        var action = await db.Actions.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(action.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var progress = await reader.CanAsync(context.User, RiskPermissions.ActionUpdate, risk, cancellationToken).ConfigureAwait(false);
        if (!risk.IsOpen || !(can!.ManageChildren || (progress && IsMe(action.OwnerEmployeeId))))
        {
            return Error.Forbidden();
        }

        if (action.Status != ActionStatuses.Completed || action.ConvertedControlId is not null)
        {
            return Error.Rule("risk.actionNotCompleted", "risk.errors.actionNotCompleted");
        }

        var input = command.Input;
        var values = new ControlValues(input.NameAr ?? action.TitleAr, input.NameEn ?? action.TitleEn, null, input.TypeCode, input.NatureCode, action.OwnerEmployeeId,
            input.FrequencyCode, input.EffectivenessCode, await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false), $"{action.Number}");
        var errors = await ControlErrorsAsync(values, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(input.EffectivenessCode))
        {
            errors.Add(new FieldError("effectivenessCode", "validation.required", "validation.required"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var control = Control.Create(risk.Id, values, action.Id);
        db.Controls.Add(control);
        action.MarkConverted(control.Id);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishIneffectiveAsync(control, risk, null, cancellationToken).ConfigureAwait(false);
        var tracked = await db.Risks.FirstAsync(r => r.Id == risk.Id, cancellationToken).ConfigureAwait(false);
        await rater.RateAsync(tracked, await reader.SettingsAsync(cancellationToken).ConfigureAwait(false), clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (tracked.Status == RiskStatuses.Active)
        {
            tracked.FlagReassessment(ReassessmentReasons.ControlAdded);
        }

        await roles.SyncAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ControlDtoAsync(control, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ActionUpdateDto>>> HandleAsync(ListActionUpdates query, CancellationToken cancellationToken)
    {
        var action = await db.Actions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == query.Id, cancellationToken).ConfigureAwait(false);
        if (action is null || (await RiskAsync(action.RiskId, cancellationToken).ConfigureAwait(false)).Risk is null)
        {
            return Error.NotFound();
        }

        var updates = await db.ActionUpdates.AsNoTracking().Where(u => u.ActionId == action.Id).OrderByDescending(u => u.At).ToListAsync(cancellationToken).ConfigureAwait(false);
        return updates.Select(u => new ActionUpdateDto(u.Id, u.Note, u.CompletionPct, u.At, u.By is { } b ? new RiskPersonDto(b, null, null) : null, u.Draft)).ToList();
    }

    // ---- KRIs ---------------------------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<KriDto>>> HandleAsync(ListKris query, CancellationToken cancellationToken)
    {
        var (risk, _) = await RiskAsync(query.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var kris = await db.Kris.AsNoTracking().Where(k => k.RiskId == risk.Id && k.Status == ControlStatuses.Active).OrderBy(k => k.CardCode).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var catalog = services.GetService(typeof(IKpiCatalog)) as IKpiCatalog;
        var year = (await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false)).Year;
        var result = new List<KriDto>();
        foreach (var k in kris)
        {
            var visible = catalog is not null && await catalog.GetAsync(k.KpiCardId, context.User, cancellationToken).ConfigureAwait(false) is not null;
            var performance = visible ? await catalog!.GetPerformanceAsync(k.KpiCardId, year, context.User, cancellationToken).ConfigureAwait(false) : null;
            result.Add(ToDto(k, performance, visible));
        }

        return result;
    }

    private static KriDto ToDto(Kri k, KpiYearPerformance? performance, bool visible) => new(
        k.Id, k.RiskId, k.KpiCardId, k.CardCode, k.CardNameAr, k.CardNameEn, k.Basis, k.Direction, k.WarningThreshold, k.BreachThreshold, k.Status, k.LastValue, k.LastLevel,
        k.LastPeriod, k.LastAt, performance, visible, k.Version);

    public async Task<Result<KriDto>> HandleAsync(SaveKri command, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(command.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        var v = command.Values;
        var errors = new List<FieldError>();
        if (!KriBases.All.Contains(v.Basis))
        {
            errors.Add(new FieldError("basis", "validation.invalid", "validation.invalid"));
        }

        if (!KriDirections.All.Contains(v.Direction))
        {
            errors.Add(new FieldError("direction", "validation.invalid", "validation.invalid"));
        }

        if (v.WarningThreshold is { } w && (v.Direction == KriDirections.Above ? w >= v.BreachThreshold : w <= v.BreachThreshold))
        {
            errors.Add(new FieldError("warningThreshold", "risk.errors.warningBeforeBreach", "risk.errors.warningBeforeBreach"));
        }

        var card = services.GetService(typeof(IKpiCatalog)) is IKpiCatalog catalog
            ? await catalog.GetAsync(v.KpiCardId, context.User, cancellationToken).ConfigureAwait(false)
            : null;
        if (card is null)
        {
            errors.Add(new FieldError("kpiCardId", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Kri kri;
        if (command.Id is { } id)
        {
            kri = await db.Kris.FirstOrDefaultAsync(k => k.Id == id && k.RiskId == risk.Id && k.Status == ControlStatuses.Active, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException();
            db.Entry(kri).Property(k => k.Version).OriginalValue = command.Version ?? kri.Version;
            kri.Update(v);
        }
        else
        {
            if (await db.Kris.AnyAsync(k => k.RiskId == risk.Id && k.KpiCardId == v.KpiCardId && k.Status == ControlStatuses.Active, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("kpiCardId", "validation.duplicate", "validation.duplicate")]);
            }

            kri = Kri.Create(risk.Id, v, card!.Code, card.NameAr, card.NameEn);
            db.Kris.Add(kri);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(kri, null, true);
    }

    public async Task<Result<KriDto>> HandleAsync(RemoveKri command, CancellationToken cancellationToken)
    {
        var kri = await db.Kris.FirstOrDefaultAsync(k => k.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (kri is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(kri.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.ManageChildren)
        {
            return Error.Forbidden();
        }

        kri.Remove();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(kri, null, true);
    }

    // ---- events -------------------------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<EventDto>>> HandleAsync(ListRiskEvents query, CancellationToken cancellationToken)
    {
        var visible = await reader.RisksAsync(context.User, cancellationToken).ConfigureAwait(false);
        IQueryable<Guid> eventIds;
        if (query.RiskId is { } riskId)
        {
            if (!await visible.AnyAsync(r => r.Id == riskId, cancellationToken).ConfigureAwait(false))
            {
                return Error.NotFound();
            }

            eventIds = db.EventRisks.Where(e => e.RiskId == riskId).Select(e => e.EventId);
        }
        else
        {
            eventIds = db.EventRisks.Join(visible, e => e.RiskId, r => r.Id, (e, r) => e.EventId);
        }

        var rows = await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id)).OrderByDescending(e => e.OccurredOn).Take(200).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(await EventDtosAsync(rows, cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<EventDto>> EventDtosAsync(IReadOnlyList<RiskEvent> rows, CancellationToken ct)
    {
        var ids = rows.Select(e => e.Id).ToList();
        var links = await db.EventRisks.AsNoTracking().Where(l => ids.Contains(l.EventId)).ToListAsync(ct).ConfigureAwait(false);
        var visible = await reader.RisksAsync(context.User, ct).ConfigureAwait(false);
        var riskIds = links.Select(l => l.RiskId).Distinct().ToList();
        var risks = await visible.Where(r => riskIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(rows.Select(e => e.UnitId), ct).ConfigureAwait(false);
        var manage = await reader.CanAsync(context.User, RiskPermissions.EventManage, ct).ConfigureAwait(false);
        return rows.Select(e =>
        {
            var linked = links.Where(l => l.EventId == e.Id).Select(l => l.RiskId).ToList();
            return new EventDto(e.Id, e.Number, new EventValues(e.OccurredOn, e.Description, e.FinancialImpact, e.Currency, e.OtherImpact, e.NearMiss, e.RootCause, e.Lessons,
                    e.UnitId, linked),
                linked.Where(risks.ContainsKey).Select(id => new RiskListRefDto(id, risks[id].Number, risks[id].TitleAr, risks[id].TitleEn)).ToList(),
                RiskReader.Unit(e.UnitId, units), e.CapRemarkId, e.Status, e.CreatedAt, manage && linked.All(risks.ContainsKey), e.Version);
        }).ToList();
    }

    public async Task<Result<EventDto>> HandleAsync(LogRiskEvent command, CancellationToken cancellationToken)
    {
        if (command.Values is null)
        {
            return Error.Validation([new FieldError("values", "validation.required", "validation.required")]);
        }

        var v = command.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Description))
        {
            errors.Add(new FieldError("description", "validation.required", "validation.required"));
        }

        if (v.RiskIds is not { Count: > 0 })
        {
            errors.Add(new FieldError("riskIds", "validation.required", "validation.required"));
        }

        if (v.FinancialImpact is < 0)
        {
            errors.Add(new FieldError("financialImpact", "validation.invalid", "validation.invalid"));
        }

        var risks = new List<RiskRecord>();
        foreach (var id in (v.RiskIds ?? []).Distinct())
        {
            var risk = await reader.RiskAsync(id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
            if (risk is null)
            {
                return Error.NotFound();
            }

            if (!await reader.CanAsync(context.User, RiskPermissions.EventManage, risk, cancellationToken).ConfigureAwait(false))
            {
                return Error.Forbidden();
            }

            if (risk.Status != RiskStatuses.Active)
            {
                errors.Add(new FieldError("riskIds", "risk.errors.notActive", "risk.errors.notActive"));
            }

            risks.Add(risk);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        RiskEvent e;
        var values = v with { UnitId = v.UnitId ?? risks[0].UnitId };
        if (command.Id is { } eventId)
        {
            e = await db.Events.FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
            db.Entry(e).Property(x => x.Version).OriginalValue = command.Version ?? e.Version;
            e.Update(values);
            db.EventRisks.RemoveRange(await db.EventRisks.Where(l => l.EventId == e.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        }
        else
        {
            var number = await numbering.NextAsync(db, RiskPermissions.EventNumbering, new NumberingContext(v.OccurredOn), cancellationToken).ConfigureAwait(false);
            e = RiskEvent.Create(number, values);
            db.Events.Add(e);
        }

        foreach (var risk in risks)
        {
            db.EventRisks.Add(new EventRisk { EventId = e.Id, RiskId = risk.Id });
            if (command.Id is null && !v.NearMiss)
            {
                risk.FlagReassessment(ReassessmentReasons.Event);
                await notifier.SendAsync(RiskNotifications.Reassessment, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId],
                    RiskNotifier.Values(risk, text: $"{e.Number}"), $"/risk/risks/{risk.Id:D}?tab=events", db, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (command.Id is null)
        {
            await events.PublishAsync(new RiskEventLogged(e.Id, e.Number, e.OccurredOn, e.Description, e.NearMiss, e.FinancialImpact, e.UnitId, risks.Select(r => r.Id).ToList(),
                clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await EventDtosAsync([e], cancellationToken).ConfigureAwait(false))[0];
    }

    // ---- periodic reviews (R3) ----------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ReviewDto>>> HandleAsync(ListReviews query, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(query.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var reviews = await db.Reviews.AsNoTracking().Where(r => r.RiskId == risk.Id).OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ReviewDto>();
        foreach (var r in reviews)
        {
            list.Add(await ReviewDtoAsync(r, risk, can!, cancellationToken).ConfigureAwait(false));
        }

        return list;
    }

    private async Task<ReviewDto> ReviewDtoAsync(Review r, RiskRecord risk, RiskPermissionsDto can, CancellationToken ct)
    {
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        var controls = await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var ratings = controls.Select(c => r.ControlRatings.TryGetValue(c.Id, out var code) ? code : c.EffectivenessCode).ToList();
        var factor = scorer.Factor(risk.ControlEffectivenessOverride ?? scorer.OverallEffectiveness(ratings)) ?? 0m;
        var score = scorer.Score(r.ResidualLikelihood, r.ResidualImpact, factor);
        var editable = r.Status is ReviewStatuses.Draft or ReviewStatuses.Returned && (can.StartReview || can.Office);
        return new ReviewDto(r.Id, r.RiskId, r.Status, r.Values(), r.DecisionComment, r.SubmittedAt, r.DecidedAt, r.CreatedAt, score, scorer.Band(score).Code, editable, r.Version);
    }

    public async Task<Result<ReviewDto>> HandleAsync(StartReview command, CancellationToken cancellationToken)
    {
        var (risk, can) = await RiskAsync(command.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!can!.StartReview)
        {
            return risk.Status == RiskStatuses.Active && risk.PendingProcess is null ? Error.Forbidden() : Error.Rule("risk.reviewNotAllowed", "risk.errors.reviewNotAllowed");
        }

        var open = await db.Reviews.FirstOrDefaultAsync(r => r.RiskId == risk.Id && (r.Status == ReviewStatuses.Draft || r.Status == ReviewStatuses.Returned), cancellationToken)
            .ConfigureAwait(false);
        if (open is null)
        {
            var controls = await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
            open = Review.Create(risk.Id, new ReviewValues(risk.ResidualLikelihood ?? risk.InherentLikelihood, risk.ResidualImpact ?? risk.InherentImpact,
                controls.ToDictionary(c => c.Id, c => c.EffectivenessCode), null, null));
            db.Reviews.Add(open);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReviewDtoAsync(open, risk, can, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ReviewDto>> HandleAsync(SaveReview command, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Error.NotFound();
        }

        var (risk, can) = await RiskAsync(review.RiskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!(can!.StartReview || can.Office) || review.Status is not (ReviewStatuses.Draft or ReviewStatuses.Returned))
        {
            return Error.Forbidden();
        }

        var errors = await ReviewErrorsAsync(command.Values, risk.Id, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(review).Property(r => r.Version).OriginalValue = command.Version;
        review.Update(command.Values);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ReviewDtoAsync(review, risk, can, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<FieldError>> ReviewErrorsAsync(ReviewValues v, Guid riskId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        if (!scorer.IsLikelihood(v.ResidualLikelihood))
        {
            errors.Add(new FieldError("residualLikelihood", "validation.invalid", "validation.invalid"));
        }

        if (!scorer.IsImpact(v.ResidualImpact))
        {
            errors.Add(new FieldError("residualImpact", "validation.invalid", "validation.invalid"));
        }

        var controls = await db.Controls.AsNoTracking().Where(c => c.RiskId == riskId && c.Status == ControlStatuses.Active).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var (id, code) in v.ControlRatings ?? new Dictionary<Guid, string?>())
        {
            if (!controls.Contains(id) || (code is not null && scorer.Content.ControlEffectiveness.All(e => e.Code != code)))
            {
                errors.Add(new FieldError($"controlRatings.{id:D}", "validation.invalid", "validation.invalid"));
            }
        }

        return errors;
    }

    public async Task<Result<ReviewDto>> HandleAsync(SubmitReview command, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Error.NotFound();
        }

        var risk = await reader.RiskAsync(review.RiskId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var can = await views.PermissionsAsync(risk, context.User, cancellationToken).ConfigureAwait(false);
        if (!can.StartReview || review.Status is not (ReviewStatuses.Draft or ReviewStatuses.Returned))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(review.Commentary))
        {
            return Error.Validation([new FieldError("commentary", "validation.required", "validation.required")]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        review.Submit(clock.UtcNow);
        risk.SetPending(RiskProcesses.PeriodicReview);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var submission = RiskCommands.Submission(RiskProcesses.PeriodicReview, risk, command.Comment ?? review.Commentary);
        var submitted = await engine.SubmitAsync(submission, db, cancellationToken).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ReviewDtoAsync(review, risk, can, cancellationToken).ConfigureAwait(false);
    }
}
