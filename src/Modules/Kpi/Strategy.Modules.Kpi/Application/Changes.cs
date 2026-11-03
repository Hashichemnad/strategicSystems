using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Approval process <c>kpi.definition-change</c> (SPEC §2.7, ADR 0063): default single Strategy Office step.</summary>
public static class DefinitionChangeProcess
{
    public const string Key = "kpi.definition-change";

    public static readonly ApprovalProcessDefinition Definition = new(
        Key, KpiPermissions.SystemCode, "اعتماد تعديل تعريف مؤشر", "KPI definition change approval", KpiCard.EntityType, KpiPermissions.CardRead, "/kpi/cards/{id}?tab=versions",
        [],
        [
            new("strategy-office", "مكتب الاستراتيجية", "Strategy Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: KpiPermissions.StrategyOfficeRoleKey),
                SlaHours: 40),
        ],
        KpiPermissions.ApproverRole,
        "تعديل المستهدف أو المعادلة أو الوحدة أو الاتجاه بعد بدء سنة القياس.",
        "Changes to the target, equation, unit or direction after the measuring year has started.");
}

public sealed record KpiChangeDto(
    Guid Id, Guid CardId, string CardCode, string? CardNameAr, string? CardNameEn, int Year, ProposedChange Proposed, ProposedChange Current, string Reason, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? DecisionComment, uint Version);

public sealed record KpiChangeInput(ProposedChange Proposed, string Reason);

public sealed record RequestKpiDefinitionChange(Guid CardId, KpiChangeInput Input) : ICommand<KpiChangeDto>;

public sealed record WithdrawKpiDefinitionChange(Guid Id, string? Reason) : ICommand<KpiChangeDto>;

public sealed record ListKpiDefinitionChanges(Guid? CardId, string? Status) : IQuery<IReadOnlyList<KpiChangeDto>>;

internal sealed class ChangeHandlers(KpiDbContext db, KpiReader reader, IRequestContext context, IClock clock, IApprovalEngine engine, ILookupService lookups)
    : ICommandHandler<RequestKpiDefinitionChange, KpiChangeDto>, ICommandHandler<WithdrawKpiDefinitionChange, KpiChangeDto>,
        IQueryHandler<ListKpiDefinitionChanges, IReadOnlyList<KpiChangeDto>>
{
    public async Task<Result<KpiChangeDto>> HandleAsync(RequestKpiDefinitionChange command, CancellationToken cancellationToken)
    {
        var card = await reader.CardAsync(command.CardId, context.User, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var p = command.Input.Proposed;
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (string.IsNullOrWhiteSpace(command.Input.Reason))
        {
            Add("reason", "validation.notEmpty");
        }

        if (p.Direction is not null && !Directions.All.Contains(p.Direction))
        {
            Add("direction", "validation.invalid");
        }

        if (p.TargetType is not null && !TargetTypes.All.Contains(p.TargetType))
        {
            Add("targetType", "validation.invalid");
        }

        if (p.UnitCode is not null && !await lookups.IsActiveCodeAsync(KpiPermissions.UnitLookup, p.UnitCode, cancellationToken).ConfigureAwait(false))
        {
            Add("unitCode", "validation.invalid");
        }

        if (p.Formula is not null && !CompositeFormula.Compile(p.Formula, card.Elements).Success)
        {
            Add("formula", "kpi.errors.formulaInvalid");
        }

        var year = clock.Today.Year;
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(x => x.CardId == card.Id && x.Year == year, cancellationToken).ConfigureAwait(false);
        var targetType = p.TargetType ?? plan?.TargetType ?? card.TargetType;
        if ((p.Target ?? p.RangeMin ?? p.RangeMax) is not null && TargetErrors.Of(targetType, p.Target, p.RangeMin, p.RangeMax) is { } targetError)
        {
            Add("target", targetError);
        }

        var current = new ProposedChange(card.MeasuringEquation, card.Formula, plan?.UnitCode ?? card.UnitCode, plan?.Direction ?? card.Direction, targetType,
            plan?.Target, plan?.RangeMin, plan?.RangeMax);
        if (p == new ProposedChange(null, null, null, null, null, null, null, null))
        {
            Add("proposed", "kpi.errors.nothingChanged");
        }

        if (await db.Changes.AnyAsync(c => c.CardId == card.Id && c.Status == ChangeStatuses.Pending, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("kpi.changePending", "kpi.errors.changePending");
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var change = DefinitionChange.Create(card, year, p, current, command.Input.Reason, context.UserId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Changes.Add(change);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var started = await engine.SubmitAsync(new ApprovalSubmission(DefinitionChangeProcess.Key,
            new ApprovalSubject(KpiCard.EntityType, card.Id, card.Code, card.NameAr, card.NameEn, card.ResponsibleEmployeeId, context.UserId, card.ResponsibleUnitId),
            new Dictionary<string, object?>(), command.Input.Reason), db, cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(change, card);
    }

    public async Task<Result<KpiChangeDto>> HandleAsync(WithdrawKpiDefinitionChange command, CancellationToken cancellationToken)
    {
        var change = await db.Changes.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken).ConfigureAwait(false);
        var card = change is null ? null : await reader.CardAsync(change.CardId, context.User, cancellationToken).ConfigureAwait(false);
        if (change is null || card is null)
        {
            return Error.NotFound();
        }

        if (change.RequestedByUserId != context.UserId && !await reader.CanAsync(context.User, KpiPermissions.CardManage, card, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (change.Status != ChangeStatuses.Pending)
        {
            return Error.Rule("kpi.invalidTransition", "kpi.errors.invalidTransition");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var cancelled = await engine.CancelAsync(DefinitionChangeProcess.Key, card.Id, command.Reason ?? "withdrawn", db, cancellationToken).ConfigureAwait(false);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(change).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(change, card);
    }

    public async Task<Result<IReadOnlyList<KpiChangeDto>>> HandleAsync(ListKpiDefinitionChanges query, CancellationToken cancellationToken)
    {
        var cards = await reader.CardsAsync(context.User, cancellationToken).ConfigureAwait(false);
        var source = db.Changes.AsNoTracking().Where(c => cards.Any(card => card.Id == c.CardId));
        if (query.CardId is { } id)
        {
            source = source.Where(c => c.CardId == id);
        }

        if (query.Status is { Length: > 0 } status)
        {
            source = source.Where(c => c.Status == status);
        }

        var changes = await source.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = changes.Select(c => c.CardId).Distinct().ToList();
        var byId = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return changes.Select(c => ToDto(c, byId[c.CardId])).ToList();
    }

    internal static KpiChangeDto ToDto(DefinitionChange c, KpiCard card) => new(c.Id, c.CardId, card.Code, card.NameAr, card.NameEn, c.Year, c.Change,
        KpiCard.Read<ProposedChange>(c.Current) ?? new ProposedChange(null, null, null, null, null, null, null, null), c.Reason, c.Status, c.CreatedAt, c.DecidedAt,
        c.DecisionComment, c.Version);
}

/// <summary>Applies an approved definition change in the approval transaction (card, current plan, results not yet approved, new version).</summary>
internal sealed class DefinitionChangeHook(KpiDbContext db, IClock clock, PlanCalculator calculator, KpiNotifier notifier, ISettingsStore settings) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [DefinitionChangeProcess.Key];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        var status = approvalEvent.Kind switch
        {
            ApprovalEventKinds.Approved => ChangeStatuses.Approved,
            ApprovalEventKinds.Rejected => ChangeStatuses.Rejected,
            ApprovalEventKinds.Returned => ChangeStatuses.Returned,
            ApprovalEventKinds.Cancelled => ChangeStatuses.Withdrawn,
            _ => null,
        };
        if (status is null)
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var change = await db.Changes.Where(c => c.CardId == approvalEvent.ResourceId && c.Status == ChangeStatuses.Pending).OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (change is null || card is null)
        {
            return Error.NotFound();
        }

        change.Decide(status, clock.UtcNow, approvalEvent.Comment);
        if (status == ChangeStatuses.Approved)
        {
            var p = change.Change;
            card.ApplyControlled(new ControlledDefinition(p.MeasuringEquation ?? card.MeasuringEquation, p.Formula ?? card.Formula, p.UnitCode ?? card.UnitCode,
                p.Direction ?? card.Direction, p.TargetType ?? card.TargetType));
            card.NextVersion();
            db.CardVersions.Add(CardVersion.Of(card, change.Reason, change.Id));
            var plan = await db.Plans.FirstOrDefaultAsync(x => x.CardId == card.Id && x.Year == change.Year, cancellationToken).ConfigureAwait(false);
            if (plan is not null)
            {
                plan.Edit(p.UnitCode ?? plan.UnitCode, p.Direction ?? plan.Direction, plan.IsAccumulated, plan.ResponsibleEmployeeId);
                var targetType = p.TargetType ?? plan.TargetType;
                var targetChanged = p.TargetType is not null || p.Target is not null || p.RangeMin is not null || p.RangeMax is not null;
                if (targetChanged)
                {
                    plan.SetTarget(targetType, p.Target ?? plan.Target, p.RangeMin ?? plan.RangeMin, p.RangeMax ?? plan.RangeMax);
                    var results = await db.Results.Where(r => r.PlanId == plan.Id && r.Status != ResultStatuses.Approved).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var count = await db.Results.CountAsync(r => r.PlanId == plan.Id, cancellationToken).ConfigureAwait(false);
                    var configuration = await settings.GetAsync<KpiSettings>(cancellationToken).ConfigureAwait(false);
                    var defaults = PeriodSchedule.DefaultTargets(configuration.RuleFor(card.MeasuringMethodCode), targetType,
                        plan.Target, plan.RangeMin, plan.RangeMax, count);
                    foreach (var result in results)
                    {
                        var d = defaults[Math.Clamp(result.Sort - 1, 0, defaults.Count - 1)];
                        result.SetTarget(targetType, d.Target, d.RangeMin, d.RangeMax);
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (plan is not null)
            {
                await calculator.RecalculateAsync(plan.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (status != ChangeStatuses.Withdrawn && change.RequestedByUserId is { } requester)
        {
            await notifier.SendAsync(KpiNotifications.ChangeDecided, [], KpiNotifier.Values(card, null, text: status), $"/kpi/cards/{card.Id:D}?tab=versions", db,
                cancellationToken, [requester]).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
