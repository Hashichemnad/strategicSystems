using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Cap.Application;

/// <summary>Starts and cancels CAP approvals (subject = the action; ADR 0121).</summary>
internal sealed class CapApprovals(IApprovalEngine engine)
{
    public Task<Result<ApprovalInstanceSummary>> SubmitAsync(string process, CapAction action, Remark home, string? comment, object transactionContext, CancellationToken ct) =>
        engine.SubmitAsync(new ApprovalSubmission(
            process,
            new ApprovalSubject(CapAction.EntityType, action.Id, action.Number, CapNotifier.Short(action.DetailsAr ?? action.DetailsEn),
                CapNotifier.Short(action.DetailsEn ?? action.DetailsAr), action.AssignedEmployeeId ?? home.OwnerEmployeeId, null, home.SectionUnitId ?? home.DepartmentUnitId),
            new Dictionary<string, object?>
            {
                ["priority"] = home.Priority,
                ["budgetRequired"] = action.BudgetRequired,
                ["budgetAvailable"] = action.BudgetAvailable ?? false,
                ["estimatedCost"] = action.EstimatedCost ?? 0m,
            },
            comment), transactionContext, ct);

    public Task<Result<ApprovalInstanceSummary>> CancelAsync(string process, Guid actionId, string reason, object transactionContext, CancellationToken ct) =>
        engine.CancelAsync(process, actionId, reason, transactionContext, ct);
}

/// <summary>
/// Moves actions and remarks with the PM Office decisions (inside the engine's transaction, ADR 0023 / 0121): validation (with budget routing,
/// ADR 0124), completion and extension.
/// </summary>
internal sealed class CapApprovalHook(
    CapDbContext db, CapService service, CapNotifier notifier, CapDeadlines deadlines, ILinkStore links, IClock clock, IServiceProvider services) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [CapProcesses.Validation, CapProcesses.Completion, CapProcesses.Extension];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned or ApprovalEventKinds.Cancelled))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var action = await db.Actions.FirstOrDefaultAsync(a => a.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        var result = approvalEvent.ProcessKey switch
        {
            CapProcesses.Validation => await ValidationAsync(action, approvalEvent, transactionContext, cancellationToken).ConfigureAwait(false),
            CapProcesses.Completion => await CompletionAsync(action, approvalEvent, cancellationToken).ConfigureAwait(false),
            _ => await ExtensionAsync(action, approvalEvent, cancellationToken).ConfigureAwait(false),
        };
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ValidationAsync(CapAction action, ApprovalEvent e, object transactionContext, CancellationToken ct)
    {
        if (e.Kind == ApprovalEventKinds.Cancelled)
        {
            if (action.Status != ActionStatuses.Cancelled)
            {
                action.ValidationCancelled();
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }

        var status = e.Kind switch
        {
            ApprovalEventKinds.Approved => ValidationStatuses.Approved,
            ApprovalEventKinds.Rejected => ValidationStatuses.Rejected,
            _ => ValidationStatuses.Returned,
        };
        action.Validated(status, e.Comment);
        var remarks = await service.RemarksOfAsync(action.Id, ct).ConfigureAwait(false);
        var home = remarks.FirstOrDefault(r => r.Id == action.HomeRemarkId) ?? remarks.FirstOrDefault();
        if (status == ValidationStatuses.Approved)
        {
            var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
            action.RefreshLate(today);
            if (action.BudgetRequired && action.BudgetAvailable == false && action.ProjectId is null && action.ProgramRequestId is null && home is not null)
            {
                await RouteBudgetAsync(action, home, transactionContext, ct).ConfigureAwait(false);
            }

            await notifier.SendAsync(CapNotifications.ActionAssigned, [action.AssignedEmployeeId], CapNotifier.Values(action), CapService.Link(action), db, ct)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.SendAsync(CapNotifications.ActionValidated, [home?.OwnerEmployeeId], CapNotifier.Values(action, text: DecisionText(status, e.Comment)),
            home is null ? CapService.Link(action) : CapService.Link(home), db, ct).ConfigureAwait(false);
        foreach (var remark in remarks)
        {
            await service.RecomputeAsync(remark, ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>Budget routing (ADR 0124): a draft Program Request pre-filled from the action and remark, source "Action Plan", linked back.</summary>
    private async Task RouteBudgetAsync(CapAction action, Remark home, object transactionContext, CancellationToken ct)
    {
        if (services.GetService(typeof(IProgramRequestDrafts)) is not IProgramRequestDrafts drafts || home.OwnerEmployeeId is not { } requester)
        {
            return;
        }

        var nameAr = CapNotifier.Short(action.DetailsAr ?? action.DetailsEn);
        var nameEn = CapNotifier.Short(action.DetailsEn ?? action.DetailsAr);
        var created = await drafts.CreateDraftAsync(new ProgramRequestDraft(
            nameAr, nameEn,
            $"{action.DetailsAr ?? action.DetailsEn}\n\n{home.Number}: {home.TextAr ?? home.TextEn}".Trim(),
            $"Corrective action {action.Number} of remark {home.Number} needs a budget that is not available this year.",
            requester, home.DepartmentUnitId, home.SectionUnitId, action.EstimatedCost, action.PlannedStart, action.PlannedEnd, PortfolioSuggestionSources.ActionPlan),
            transactionContext, ct).ConfigureAwait(false);
        action.LinkProgramRequest(created.Id, created.Number);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await links.AddAsync(LinkCapabilities.RemarkSource, new LinkRef(CapAction.EntityType, action.Id), new LinkRef("portfolio.request", created.Id), db, ct)
            .ConfigureAwait(false);
        await notifier.SendAsync(CapNotifications.BudgetRouted, [requester], CapNotifier.Values(action, text: created.Number), $"/portfolio/requests/{created.Id:D}", db, ct)
            .ConfigureAwait(false);
    }

    private async Task<Result> CompletionAsync(CapAction action, ApprovalEvent e, CancellationToken ct)
    {
        if (e.Kind == ApprovalEventKinds.Approved)
        {
            action.Complete(clock.UtcNow, await deadlines.TodayAsync(ct).ConfigureAwait(false));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await service.PublishActionCompletedAsync(action, ct).ConfigureAwait(false);
            foreach (var remark in await service.RemarksOfAsync(action.Id, ct).ConfigureAwait(false))
            {
                await service.RecomputeAsync(remark, ct).ConfigureAwait(false);
            }
        }
        else
        {
            action.SetPending(null);
        }

        if (e.Kind != ApprovalEventKinds.Cancelled)
        {
            await notifier.SendAsync(CapNotifications.CompletionDecided, [action.AssignedEmployeeId], CapNotifier.Values(action, text: DecisionText(e.Kind, e.Comment)),
                CapService.Link(action), db, ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async Task<Result> ExtensionAsync(CapAction action, ApprovalEvent e, CancellationToken ct)
    {
        var request = await db.Extensions.Where(x => x.ActionId == action.Id && x.Status == ExtensionStatuses.Pending).OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        action.SetPending(null);
        if (request is null)
        {
            return Result.Success();
        }

        var status = e.Kind switch
        {
            ApprovalEventKinds.Approved => ExtensionStatuses.Approved,
            ApprovalEventKinds.Cancelled => ExtensionStatuses.Cancelled,
            _ => ExtensionStatuses.Rejected,
        };
        request.Decide(status, clock.UtcNow, e.Comment);
        if (status == ExtensionStatuses.Approved)
        {
            action.Extend(request.RequestedEnd);
            action.RefreshLate(await deadlines.TodayAsync(ct).ConfigureAwait(false));
        }

        if (status != ExtensionStatuses.Cancelled)
        {
            await notifier.SendAsync(CapNotifications.ExtensionDecided, [action.AssignedEmployeeId], CapNotifier.Values(action, text: DecisionText(e.Kind, e.Comment)),
                CapService.Link(action), db, ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private static string DecisionText(string kind, string? comment)
    {
        var label = kind switch
        {
            ValidationStatuses.Approved or ApprovalEventKinds.Approved => "Approved / معتمد",
            ValidationStatuses.Rejected or ApprovalEventKinds.Rejected => "Rejected / مرفوض",
            _ => "Returned for correction / معاد للتصحيح",
        };
        return string.IsNullOrWhiteSpace(comment) ? label : $"{label} — {comment}";
    }
}

/// <summary>"Next action required + who" for remarks (SPEC §9.4 messages).</summary>
internal sealed class RemarkCurrentStatus(CapReader reader, CapViews views, CapService service, IRequestContext context) : ICurrentStatusProvider
{
    public string ResourceType => Remark.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return null;
        }

        var can = await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([remark.OwnerEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] owner = remark.OwnerEmployeeId is { } o && people.GetValueOrDefault(o) is { } p ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        PersonName[] office = [new PersonName(null, "مكتب إدارة المشاريع", "PM Office")];
        var none = new Dictionary<string, BilingualText>();
        var date = new BilingualText(CapDeadlines.Date(remark.DueOn), CapDeadlines.Date(remark.DueOn));
        var actions = await service.ActionsOfAsync(remark.Id, cancellationToken, tracked: false).ConfigureAwait(false);
        var live = actions.Where(a => a.Status != ActionStatuses.Cancelled).ToList();
        CurrentStatus S(string key, string tone, string? actorKey, PersonName[] actors, params CurrentStatusAction[] next) =>
            new(key, new Dictionary<string, BilingualText> { ["date"] = date }, tone, actorKey, actors, next, remark.Stage);
        switch (remark.Status)
        {
            case CapRemarkStatuses.Assigned:
                return can.Respond
                    ? S(remark.PmoDecision == "sent-back" ? "cap.currentStatus.sentBack" : "cap.currentStatus.waitingAccept", "info", "cap.currentStatus.owner", owner,
                        new CurrentStatusAction("accept", "cap.actions.accept"), new CurrentStatusAction("reject", "cap.actions.reject", "danger"))
                    : S("cap.currentStatus.waitingAccept", "info", "cap.currentStatus.owner", owner);
            case CapRemarkStatuses.RejectionPending:
                return can.ConfirmRejection
                    ? S("cap.currentStatus.waitingPmoConfirm", "warning", "cap.currentStatus.office", office,
                        new CurrentStatusAction("confirm-rejection", "cap.actions.confirmRejection", "danger"), new CurrentStatusAction("send-back", "cap.actions.sendBack", "secondary"))
                    : S("cap.currentStatus.waitingPmoConfirm", "warning", "cap.currentStatus.office", office);
            case CapRemarkStatuses.Planning:
            case CapRemarkStatuses.Correction:
                {
                    var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
                    var sourceType = (await reader.SourceAsync(remark.SourceId, context.User, cancellationToken).ConfigureAwait(false))?.SourceTypeCode ?? CapSourceTypes.Other;
                    var key = remark.Status == CapRemarkStatuses.Correction ? "cap.currentStatus.correct"
                        : remark.AllRejected ? "cap.currentStatus.actionsRejected"
                        : settings.RootCauseRequired(remark.Priority, remark.TypeCode, sourceType, remark.CriterionCode) && !remark.HasRootCause ? "cap.currentStatus.rootCauseRequired"
                        : "cap.currentStatus.waitingActions";
                    return can.Submit && live.Any(a => a.IsEditable)
                        ? S(key, remark.Status == CapRemarkStatuses.Correction ? "warning" : "info", "cap.currentStatus.owner", owner,
                            new CurrentStatusAction("submit", "cap.actions.submitActions"))
                        : S(key, remark.Status == CapRemarkStatuses.Correction ? "warning" : "info", "cap.currentStatus.owner", owner);
                }

            case CapRemarkStatuses.Validation:
                return can.Validate
                    ? S("cap.currentStatus.waitingPmo", "info", "cap.currentStatus.office", office, new CurrentStatusAction("validate", "cap.actions.validate"))
                    : S("cap.currentStatus.waitingPmo", "info", "cap.currentStatus.office", office);
            case CapRemarkStatuses.Implementation:
                {
                    var started = live.Any(a => a.ValidationStatus == ValidationStatuses.Approved && (a.AchievedPct > 0 || a.Status == ActionStatuses.Completed));
                    var parameters = new Dictionary<string, BilingualText>
                    {
                        ["done"] = Count(live.Count(a => a.Status == ActionStatuses.Completed)),
                        ["total"] = Count(live.Count(a => a.ValidationStatus == ValidationStatuses.Approved)),
                    };
                    var late = live.Any(a => a.Status == ActionStatuses.Late);
                    return new CurrentStatus(started ? "cap.currentStatus.inProgress" : "cap.currentStatus.actionsApproved", parameters, late ? "warning" : "success",
                        "cap.currentStatus.assignees", [], [], remark.Stage);
                }

            case CapRemarkStatuses.Verification:
                return can.Verify
                    ? S("cap.currentStatus.waitingVerification", "info", "cap.currentStatus.office", office, new CurrentStatusAction("verify", "cap.actions.verify"))
                    : S("cap.currentStatus.waitingVerification", "info", "cap.currentStatus.office", office);
            case CapRemarkStatuses.Completed:
                return new CurrentStatus("cap.currentStatus.completed", none, "success", null, [], [], remark.Stage);
            case CapRemarkStatuses.Rejected:
                return new CurrentStatus("cap.currentStatus.rejected", none, "neutral", null, [], [], remark.Stage);
            default:
                return new CurrentStatus("cap.currentStatus.cancelled", none, "neutral", null, [], [], remark.Stage);
        }
    }

    private static BilingualText Count(int n) => new(n.ToString(System.Globalization.CultureInfo.InvariantCulture), n.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>Current status of an action (waiting validation / returned / in progress / late / waiting completion / extension / completed).</summary>
internal sealed class ActionCurrentStatus(CapReader reader, CapService service, ActionViews views, IApprovalEngine engine, IRequestContext context) : ICurrentStatusProvider
{
    public string ResourceType => CapAction.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var action = await reader.ActionAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return null;
        }

        var remarks = await service.RemarksOfAsync(action.Id, cancellationToken).ConfigureAwait(false);
        var can = await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([action.AssignedEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] assignee = action.AssignedEmployeeId is { } a && people.GetValueOrDefault(a) is { } p ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        var date = new Dictionary<string, BilingualText> { ["date"] = new(CapDeadlines.Date(action.EffectiveEnd), CapDeadlines.Date(action.EffectiveEnd)) };
        if (action.PendingProcess is { } process)
        {
            var view = await engine.GetStatusViewAsync(process, action.Id, context.User, cancellationToken).ConfigureAwait(false);
            var key = process switch
            {
                CapProcesses.Completion => "cap.currentStatus.waitingCompletion",
                CapProcesses.Extension => "cap.currentStatus.waitingExtension",
                _ => "cap.currentStatus.waitingValidation",
            };
            var next = view?.MyTaskId is { } task ? new[] { new CurrentStatusAction("decide", "cap.actions.decide", "primary", task) } : [];
            return new CurrentStatus(key, date, "info", "workflow.currentStatus.approvers", view?.Approvers ?? [], next, action.Status);
        }

        return action.Status switch
        {
            ActionStatuses.Completed => new CurrentStatus("cap.currentStatus.actionCompleted", date, "success", null, [], [], action.Status),
            ActionStatuses.Cancelled => new CurrentStatus("cap.currentStatus.actionCancelled", date, "neutral", null, [], [], action.Status),
            _ when action.ValidationStatus == ValidationStatuses.Returned => new CurrentStatus("cap.currentStatus.actionReturned", date, "warning", "cap.currentStatus.owner", [], [],
                action.Status),
            _ when action.ValidationStatus == ValidationStatuses.Rejected => new CurrentStatus("cap.currentStatus.actionRejected", date, "neutral", null, [], [], action.Status),
            _ when action.ValidationStatus == ValidationStatuses.Draft => new CurrentStatus("cap.currentStatus.actionDraft", date, "info", "cap.currentStatus.owner", [], [],
                action.Status),
            ActionStatuses.Late => new CurrentStatus("cap.currentStatus.actionLate", date, "danger", "cap.currentStatus.assignee", assignee,
                Actions(can), action.Status),
            _ => new CurrentStatus(action.AchievedPct >= 1m ? "cap.currentStatus.actionReadyForCompletion" : "cap.currentStatus.actionInProgress", date, "info",
                "cap.currentStatus.assignee", assignee, Actions(can), action.Status),
        };
    }

    private static CurrentStatusAction[] Actions(CapActionPermissionsDto can)
    {
        var list = new List<CurrentStatusAction>();
        if (can.Progress)
        {
            list.Add(new CurrentStatusAction("progress", "cap.actions.updateAchievement"));
        }

        if (can.RequestCompletion)
        {
            list.Add(new CurrentStatusAction("request-completion", "cap.actions.requestCompletion"));
        }

        if (can.RequestExtension)
        {
            list.Add(new CurrentStatusAction("request-extension", "cap.actions.requestExtension", "secondary"));
        }

        return [.. list];
    }
}

/// <summary>My Work (SPEC §0.4): remarks to accept, actions to update, actions to validate (PMO), rejections to confirm.</summary>
internal sealed class CapMyWork(CapDbContext db, CapReader reader, CapDeadlines deadlines) : IMyWorkSource
{
    public string SystemCode => CapPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var items = new List<MyWorkItem>();
        var me = user.EmployeeIdOf();
        var remarks = await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false);
        if (me is { } employee)
        {
            var mine = await ToRespond(remarks, employee).OrderBy(r => r.DueOn).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(mine.Select(r => new MyWorkItem(CapPermissions.SystemCode, Remark.EntityType, r.Id, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn),
                CapNotifier.Short(r.TextEn ?? r.TextAr), r.Status switch
                {
                    CapRemarkStatuses.Assigned => "cap.myWork.toAccept",
                    CapRemarkStatuses.Correction => "cap.myWork.toCorrect",
                    _ => "cap.myWork.toPlan",
                }, CapService.Link(r), null, r.DueOn)));
            var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
            var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
            var actions = await ToUpdate(db.Actions.AsNoTracking(), employee, monthEnd).OrderBy(a => a.RevisedEnd ?? a.PlannedEnd).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(actions.Select(a => new MyWorkItem(CapPermissions.SystemCode, CapAction.EntityType, a.Id, a.Number, CapNotifier.Short(a.DetailsAr ?? a.DetailsEn),
                CapNotifier.Short(a.DetailsEn ?? a.DetailsAr), a.Status == ActionStatuses.Late ? "cap.myWork.actionLate" : "cap.myWork.actionToUpdate",
                $"/cap/my-actions?action={a.Id:D}", null, a.EffectiveEnd)));
        }

        if (await reader.CanAllAsync(user, CapPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            var pendingRemarks = await OfficePending(remarks).OrderBy(r => r.UpdatedAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(pendingRemarks.Select(r => new MyWorkItem(CapPermissions.SystemCode, Remark.EntityType, r.Id, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn),
                CapNotifier.Short(r.TextEn ?? r.TextAr), r.Status == CapRemarkStatuses.Validation ? "cap.myWork.toValidate" : "cap.myWork.toConfirm",
                r.Status == CapRemarkStatuses.Validation ? $"/cap/remarks/{r.Id:D}/validate" : CapService.Link(r), r.UpdatedAt, r.DueOn)));
        }

        return items;
    }

    /// <summary>Remarks waiting for their owner: to accept, to plan, to correct.</summary>
    internal static IQueryable<Remark> ToRespond(IQueryable<Remark> remarks, Guid employee) =>
        remarks.Where(r => r.OwnerEmployeeId == employee
            && (r.Status == CapRemarkStatuses.Assigned || r.Status == CapRemarkStatuses.Planning || r.Status == CapRemarkStatuses.Correction));

    /// <summary>Approved actions of the assignee that are late or end by <paramref name="monthEnd"/> (no pending approval).</summary>
    internal static IQueryable<CapAction> ToUpdate(IQueryable<CapAction> actions, Guid employee, DateOnly monthEnd) =>
        actions.Where(a => a.AssignedEmployeeId == employee && a.ValidationStatus == ValidationStatuses.Approved && a.PendingProcess == null
            && (a.Status == ActionStatuses.Late || ((a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress)
                && (a.RevisedEnd ?? a.PlannedEnd) <= monthEnd)));

    /// <summary>The PM Office's queue: remarks whose actions wait for validation, rejections to confirm.</summary>
    internal static IQueryable<Remark> OfficePending(IQueryable<Remark> remarks) =>
        remarks.Where(r => r.Status == CapRemarkStatuses.Validation || r.Status == CapRemarkStatuses.RejectionPending);
}
