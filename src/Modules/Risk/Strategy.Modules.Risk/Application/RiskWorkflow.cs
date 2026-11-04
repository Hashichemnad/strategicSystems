using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Application;

/// <summary>
/// Moves risks and reviews with their approvals (inside the engine's transaction, ADR 0023 / 0102): <c>risk.review</c> (R1),
/// <c>risk.periodic-review</c> (R3) and <c>risk.closure</c> (R5).
/// </summary>
internal sealed class RiskApprovalHook(
    RiskDbContext db, RiskRater rater, FrameworkStore frameworks, RiskReader reader, RiskDeadlines deadlines, RiskNotifier notifier, IIntegrationEventPublisher events,
    IClock clock)
    : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [RiskProcesses.Review, RiskProcesses.PeriodicReview, RiskProcesses.Closure];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned or ApprovalEventKinds.Cancelled))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var link = $"/risk/risks/{risk.Id:D}";
        Result result = approvalEvent.ProcessKey switch
        {
            RiskProcesses.Review => await RegistrationAsync(risk, approvalEvent, settings, now, link, cancellationToken).ConfigureAwait(false),
            RiskProcesses.PeriodicReview => await PeriodicAsync(risk, approvalEvent, settings, now, link, cancellationToken).ConfigureAwait(false),
            _ => await ClosureAsync(risk, approvalEvent, now, link, cancellationToken).ConfigureAwait(false),
        };
        if (result.IsFailure)
        {
            return result;
        }

        risk.SetPending(null);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> RegistrationAsync(RiskRecord risk, ApprovalEvent e, RiskSettings settings, DateTimeOffset now, string link, CancellationToken ct)
    {
        var action = e.Kind switch
        {
            ApprovalEventKinds.Approved => RiskActions.Approve,
            ApprovalEventKinds.Rejected => RiskActions.Reject,
            ApprovalEventKinds.Returned => RiskActions.Return,
            _ => RiskActions.Recall,
        };
        var applied = risk.Apply(action, now, e.ActorUserId, e.Comment);
        if (applied.IsFailure)
        {
            return applied;
        }

        if (action == RiskActions.Approve)
        {
            await rater.RateAsync(risk, settings, now, ct, moveToCurrent: true).ConfigureAwait(false);
            var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
            await ScheduleNextReviewAsync(risk, today, ct).ConfigureAwait(false);
            var current = await frameworks.CurrentAsync(ct).ConfigureAwait(false);
            db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Approval, current.VersionNo, e.Comment, now, e.ActorUserId));
            await notifier.SendAsync(RiskNotifications.Approved, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, RiskDeadlines.Date(risk.NextReviewOn)),
                link, db, ct).ConfigureAwait(false);
            await NotifyEscalationAsync(risk, link, ct).ConfigureAwait(false);
        }
        else if (action == RiskActions.Return)
        {
            await notifier.SendAsync(RiskNotifications.Returned, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, text: e.Comment), link, db, ct)
                .ConfigureAwait(false);
        }
        else if (action == RiskActions.Reject)
        {
            await notifier.SendAsync(RiskNotifications.Rejected, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, text: e.Comment), link, db, ct)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async Task<Result> PeriodicAsync(RiskRecord risk, ApprovalEvent e, RiskSettings settings, DateTimeOffset now, string link, CancellationToken ct)
    {
        var review = await db.Reviews.Where(r => r.RiskId == risk.Id && r.Status == ReviewStatuses.Submitted).OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (review is null)
        {
            return Result.Success();
        }

        if (e.Kind != ApprovalEventKinds.Approved)
        {
            review.Decide(e.Kind == ApprovalEventKinds.Cancelled ? ReviewStatuses.Draft : ReviewStatuses.Returned, e.Comment, now);
            if (e.Kind != ApprovalEventKinds.Cancelled)
            {
                await notifier.SendAsync(RiskNotifications.Returned, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, text: e.Comment), link, db,
                    ct).ConfigureAwait(false);
            }

            return Result.Success();
        }

        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var controls = await db.Controls.Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        foreach (var (controlId, rating) in review.ControlRatings)
        {
            if (controls.FirstOrDefault(c => c.Id == controlId) is { } control && rating != control.EffectivenessCode)
            {
                control.Rate(rating, today);
                if (rating is not null && rating == scorer.LowestEffectiveness)
                {
                    await events.PublishAsync(new RiskControlRatedIneffective(control.Id, risk.Id, risk.Number, control.NameAr, control.NameEn, risk.UnitId, control.OwnerEmployeeId,
                        now), db, ct).ConfigureAwait(false);
                }
            }
        }

        risk.SetResidual(review.ResidualLikelihood, review.ResidualImpact);
        await rater.RateAsync(risk, settings, now, ct, moveToCurrent: true, controlRatings: controls.Select(c => c.EffectivenessCode).ToList()).ConfigureAwait(false);
        await ScheduleNextReviewAsync(risk, today, ct).ConfigureAwait(false);
        review.Decide(ReviewStatuses.Approved, e.Comment, now);
        var current = await frameworks.CurrentAsync(ct).ConfigureAwait(false);
        db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Review, current.VersionNo, review.Commentary, now, e.ActorUserId));
        await notifier.SendAsync(RiskNotifications.Approved, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, RiskDeadlines.Date(risk.NextReviewOn)),
            link, db, ct).ConfigureAwait(false);
        await NotifyEscalationAsync(risk, link, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ClosureAsync(RiskRecord risk, ApprovalEvent e, DateTimeOffset now, string link, CancellationToken ct)
    {
        var action = e.Kind == ApprovalEventKinds.Approved ? RiskActions.ClosureApproved : RiskActions.ClosureRefused;
        var applied = risk.Apply(action, now, e.ActorUserId, e.Comment);
        if (applied.IsFailure)
        {
            return applied;
        }

        if (action == RiskActions.ClosureApproved)
        {
            risk.ClearEscalation();
            var (version, _) = await frameworks.ForAsync(risk.FrameworkVersionId, ct).ConfigureAwait(false);
            db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Closure, version.VersionNo, e.Comment, now, e.ActorUserId));
            await notifier.SendAsync(RiskNotifications.Closed, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk), link, db, ct).ConfigureAwait(false);
        }
        else if (e.Kind != ApprovalEventKinds.Cancelled)
        {
            await notifier.SendAsync(RiskNotifications.Returned, [risk.OwnerEmployeeId], RiskNotifier.Values(risk, text: e.Comment), link, db, ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async Task ScheduleNextReviewAsync(RiskRecord risk, DateOnly from, CancellationToken ct)
    {
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        var (date, due) = await deadlines.NextReviewAsync(risk.UnitId, from, scorer.ReviewMonths(risk.ResidualBand), ct).ConfigureAwait(false);
        risk.SetReview(from, date, due);
    }

    private async Task NotifyEscalationAsync(RiskRecord risk, string link, CancellationToken ct)
    {
        if (risk.Escalated)
        {
            await notifier.SendAsync(RiskNotifications.Escalated, [risk.OwnerEmployeeId], RiskNotifier.Values(risk, text: risk.EscalationReason), link, db, ct,
                [RiskPermissions.RiskCommitteeRoleKey, RiskPermissions.RiskOfficeRoleKey]).ConfigureAwait(false);
        }
    }
}

/// <summary>"Next action required + who" for a risk (SPEC §0.2 current-status engine).</summary>
internal sealed class RiskCurrentStatus(RiskReader reader, RiskViews views, IRequestContext context, IApprovalEngine engine) : ICurrentStatusProvider
{
    public string ResourceType => RiskRecord.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var risk = await reader.RiskAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return null;
        }

        var can = await views.PermissionsAsync(risk, context.User, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([risk.OwnerEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] owners = risk.OwnerEmployeeId is { } o && people.GetValueOrDefault(o) is { } p ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        var date = new BilingualText(RiskDeadlines.Date(risk.NextReviewOn), RiskDeadlines.Date(risk.NextReviewOn));
        var none = new Dictionary<string, BilingualText>();
        if (risk.PendingProcess is { } process)
        {
            var view = await engine.GetStatusViewAsync(process, risk.Id, context.User, cancellationToken).ConfigureAwait(false);
            var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? view?.Instance.CurrentStepNameEn, view?.Instance.CurrentStepNameEn ?? view?.Instance.CurrentStepNameAr);
            var actions = new List<CurrentStatusAction>();
            if (view?.MyTaskId is { } taskId)
            {
                actions.Add(new CurrentStatusAction("review", "risk.actions.decide", "primary", taskId));
            }

            if (can.Recall)
            {
                actions.Add(new CurrentStatusAction("recall", "risk.actions.recall", "secondary"));
            }

            var key = process switch
            {
                RiskProcesses.Closure => "risk.currentStatus.closurePending",
                RiskProcesses.PeriodicReview => "risk.currentStatus.reviewPending",
                _ => "workflow.currentStatus.waitingStep",
            };
            return new CurrentStatus(key, new Dictionary<string, BilingualText> { ["step"] = step }, "info", "workflow.currentStatus.approvers", view?.Approvers ?? [], actions,
                risk.Status);
        }

        switch (risk.Status)
        {
            case RiskStatuses.Draft:
                return new CurrentStatus(risk.Returned ? "risk.currentStatus.returned" : "risk.currentStatus.draft", none, risk.Returned ? "warning" : "info",
                    "risk.currentStatus.owner", owners, can.Submit ? [new CurrentStatusAction("submit", "risk.actions.submit")] : [], risk.Status);
            case RiskStatuses.Active:
                {
                    var actions = new List<CurrentStatusAction>();
                    if (can.StartReview)
                    {
                        actions.Add(new CurrentStatusAction("start-review", "risk.actions.startReview"));
                    }

                    if (risk.ReassessmentRequired)
                    {
                        return new CurrentStatus("risk.currentStatus.reassessment", new Dictionary<string, BilingualText>
                        {
                            ["reason"] = ReasonText(risk.ReassessmentReason),
                        }, "warning", "risk.currentStatus.owner", owners, actions, risk.Status);
                    }

                    return views.ReviewOverdue(risk)
                        ? new CurrentStatus("risk.currentStatus.reviewOverdue", new Dictionary<string, BilingualText> { ["date"] = date }, "danger", "risk.currentStatus.owner",
                            owners, actions, risk.Status)
                        : new CurrentStatus("risk.currentStatus.active", new Dictionary<string, BilingualText> { ["date"] = date }, "success", "risk.currentStatus.owner", owners,
                            [], risk.Status);
                }

            case RiskStatuses.Closed:
                return new CurrentStatus("risk.currentStatus.closed", none, "neutral", null, [], can.Reopen ? [new CurrentStatusAction("reopen", "risk.actions.reopen", "secondary")] : [],
                    risk.Status);
            default:
                return new CurrentStatus("risk.currentStatus.rejected", none, "neutral", null, [], can.Reopen ? [new CurrentStatusAction("reopen", "risk.actions.reopen", "secondary")] : [],
                    risk.Status);
        }
    }

    public static BilingualText ReasonText(string? reason) => reason switch
    {
        ReassessmentReasons.KriBreach => new("تجاوز مؤشر خطر رئيسي", "a key risk indicator breached its threshold"),
        ReassessmentReasons.Event => new("تسجيل حدث خطر", "a risk event was logged"),
        ReassessmentReasons.Appetite => new("تجاوز الرغبة في المخاطر", "the risk is outside appetite"),
        ReassessmentReasons.AuditFinding => new("ربط ملاحظة تدقيق", "an audit finding was linked"),
        ReassessmentReasons.Framework => new("تغيير إطار المخاطر", "the risk framework changed"),
        ReassessmentReasons.ControlAdded => new("إضافة ضابط", "a control was added"),
        _ => new(reason, reason),
    };
}

/// <summary>My Work (SPEC §0.4): my risks due for review / reassessment, my returned drafts, my overdue actions.</summary>
internal sealed class RiskMyWork(RiskDbContext db, IAccessService access, IClock clock, RiskDeadlines deadlines) : IMyWorkSource
{
    public string SystemCode => RiskPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var items = new List<MyWorkItem>();
        var now = clock.UtcNow;
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var soon = today.AddDays(30);
        var risks = await (await access.ApplyScopeAsync(db.Risks.AsNoTracking(), user, RiskPermissions.Read, RiskScope.Risk, cancellationToken).ConfigureAwait(false))
            .Where(r => (r.OwnerEmployeeId == me || r.CoordinatorEmployeeId == me)
                && ((r.Status == RiskStatuses.Active && r.PendingProcess == null && (r.ReassessmentRequired || (r.NextReviewOn != null && r.NextReviewOn <= soon)))
                    || (r.Status == RiskStatuses.Draft && r.Returned)))
            .OrderBy(r => r.NextReviewOn).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var r in risks)
        {
            var key = r.Status == RiskStatuses.Draft ? "risk.myWork.returned"
                : r.ReassessmentRequired ? "risk.myWork.reassessment"
                : r.NextReviewDueAt is { } due && due < now ? "risk.myWork.reviewOverdue" : "risk.myWork.reviewDue";
            items.Add(new MyWorkItem(RiskPermissions.SystemCode, RiskRecord.EntityType, r.Id, r.Number, r.TitleAr, r.TitleEn, key, $"/risk/risks/{r.Id:D}", null, r.NextReviewOn));
        }

        var actions = await db.Actions.AsNoTracking()
            .Where(a => a.OwnerEmployeeId == me && (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress) && a.PlannedEnd != null && a.PlannedEnd < today)
            .OrderBy(a => a.PlannedEnd).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(actions.Select(a => new MyWorkItem(RiskPermissions.SystemCode, TreatmentAction.EntityType, a.Id, a.Number, a.TitleAr, a.TitleEn, "risk.myWork.actionOverdue",
            $"/risk/my-actions?action={a.Id:D}", null, a.PlannedEnd)));
        return items;
    }
}
