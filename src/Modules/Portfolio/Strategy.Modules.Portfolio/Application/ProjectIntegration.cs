using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Teams.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Moves the project with every project approval process, inside the engine's transaction (docs/design/portfolio.md §16, §18).</summary>
internal sealed class ProjectApprovalHook(
    PortfolioDbContext db, ProjectLoader loader, ProjectProgress progress, ProjectNotifier notifier, IRecordRoleService recordRoles, IRoleHolderQuery roles,
    IIntegrationEventPublisher events, IClock clock, IServiceProvider services) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = ProjectApprovalProcesses.All.Select(d => d.Key).ToList();

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        var key = approvalEvent.ProcessKey;
        var now = clock.UtcNow;
        var template = await loader.EnsureTemplateAsync(project, cancellationToken).ConfigureAwait(false);
        Result result = Result.Success();
        switch (approvalEvent.Kind)
        {
            case ApprovalEventKinds.StepEntered:
                {
                    var (ar, en) = await StepNamesAsync(approvalEvent, cancellationToken).ConfigureAwait(false);
                    project.EnterApproval(key, approvalEvent.StepCode, ar, en);
                    break;
                }

            case ApprovalEventKinds.Approved:
                project.DecideApproval(key, ApprovalInstanceStatuses.Approved);
                result = await ApprovedAsync(project, key, template, now, cancellationToken).ConfigureAwait(false);
                break;
            case ApprovalEventKinds.Returned:
            case ApprovalEventKinds.Rejected:
                project.DecideApproval(key, approvalEvent.Kind == ApprovalEventKinds.Returned ? ApprovalInstanceStatuses.Returned : ApprovalInstanceStatuses.Rejected);
                result = await NotApprovedAsync(project, key, approvalEvent.Kind, template, now, cancellationToken).ConfigureAwait(false);
                break;
            default:
                return Result.Success(); // Cancelled: the canceller already moved the project.
        }

        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ApprovedAsync(Project project, string key, StageTemplate template, DateTimeOffset now, CancellationToken ct)
    {
        var stageBefore = project.Stage;
        switch (key)
        {
            case ProjectApprovalProcesses.Charter:
                {
                    var applied = project.Apply(ProjectActions.CharterApproved, template, now);
                    if (applied.IsFailure)
                    {
                        return applied;
                    }

                    if (project.SubState == SubStates.Lpo)
                    {
                        var holders = await roles.GetHoldersAsync(PortfolioPermissions.AdministrationRoleKey, ct).ConfigureAwait(false);
                        foreach (var userId in holders.UserIds.Distinct())
                        {
                            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.User, userId,
                                PortfolioPermissions.ProcurementRole, ct).ConfigureAwait(false);
                        }

                        await notifier.ToUsersAsync(PortfolioNotifications.CharterApproved, project, holders.UserIds, db, ct).ConfigureAwait(false);
                    }
                    else if (project.SubState == SubStates.TeamFormation)
                    {
                        await notifier.ToRoleAsync(PortfolioNotifications.LpoUploaded, project, PortfolioPermissions.PmOfficeRoleKey, db, ct).ConfigureAwait(false);
                    }

                    break;
                }

            case ProjectApprovalProcesses.Planning:
                {
                    var applied = project.Apply(ProjectActions.PlanningApproved, template, now);
                    if (applied.IsFailure)
                    {
                        return applied;
                    }

                    await progress.RecalculateAsync(project, ct).ConfigureAwait(false);
                    break;
                }

            case ProjectApprovalProcesses.MonthlyUpdate:
                {
                    var window = await db.MonthlyUpdates.Where(m => m.ProjectId == project.Id && m.Status == MonthlyStatuses.Submitted).OrderByDescending(m => m.Period)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    window?.Approve(now);
                    foreach (var milestone in await db.Milestones.Where(m => m.ProjectId == project.Id && m.UpdateState == UpdateStates.PendingApproval).ToListAsync(ct)
                                 .ConfigureAwait(false))
                    {
                        milestone.ApproveUpdate();
                    }

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await progress.RecalculateAsync(project, ct).ConfigureAwait(false);
                    project.MarkMonthlyApproved(now);
                    if (project.Performance >= 1m && project.SubState == SubStates.Execution)
                    {
                        project.Apply(ProjectActions.AchievementComplete, template, now);
                    }

                    break;
                }

            case ProjectApprovalProcesses.Closure:
                {
                    var applied = project.Apply(ProjectActions.ClosureApproved, template, now);
                    if (applied.IsFailure)
                    {
                        return applied;
                    }

                    // ADR 0072: Teams opens the project-team evaluation; other subscribers get the Portfolio event.
                    await events.PublishAsync(new LinkedRecordClosed(PortfolioPermissions.ProjectResource, project.Id, "closure-approved", now), db, ct).ConfigureAwait(false);
                    await events.PublishAsync(new ProjectClosureApproved(project.Id, project.Number, project.NameAr, project.NameEn, project.UnitId, project.PmEmployeeId, now),
                        db, ct).ConfigureAwait(false);
                    break;
                }

            case ProjectApprovalProcesses.Impact:
                {
                    var applied = project.Apply(ProjectActions.ImpactApproved, template, now);
                    if (applied.IsFailure)
                    {
                        return applied;
                    }

                    break;
                }

            case ProjectApprovalProcesses.ChangeRequest:
                {
                    var cr = await db.ChangeRequests.Where(c => c.ProjectId == project.Id && c.Status == ChangeStatuses.UnderReview).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (cr is not null)
                    {
                        cr.Move(ChangeActions.Approve, now);
                        await notifier.ToEmployeesAsync(PortfolioNotifications.ChangeRequestDecided, project, [project.PmEmployeeId, project.OwnerEmployeeId], db, ct,
                            cr.Number, stage: new BilingualText("معتمد", "Approved")).ConfigureAwait(false);
                    }

                    break;
                }
        }

        if (project.Stage != stageBefore)
        {
            await notifier.ToEmployeesAsync(PortfolioNotifications.StageChanged, project, [project.PmEmployeeId], db, ct, stage: ProjectNotifier.StageText(project.Stage))
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async Task<Result> NotApprovedAsync(Project project, string key, string kind, StageTemplate template, DateTimeOffset now, CancellationToken ct)
    {
        switch (key)
        {
            case ProjectApprovalProcesses.MonthlyUpdate:
                {
                    var window = await db.MonthlyUpdates.Where(m => m.ProjectId == project.Id && m.Status == MonthlyStatuses.Submitted).OrderByDescending(m => m.Period)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    window?.Return();
                    foreach (var milestone in await db.Milestones.Where(m => m.ProjectId == project.Id && m.UpdateState == UpdateStates.PendingApproval).ToListAsync(ct)
                                 .ConfigureAwait(false))
                    {
                        milestone.ReturnUpdate();
                    }

                    return Result.Success();
                }

            case ProjectApprovalProcesses.ChangeRequest:
                {
                    var cr = await db.ChangeRequests.Where(c => c.ProjectId == project.Id && c.Status == ChangeStatuses.UnderReview).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (cr is not null)
                    {
                        cr.Move(kind == ApprovalEventKinds.Returned ? ChangeActions.Return : ChangeActions.Reject, now);
                        await notifier.ToEmployeesAsync(PortfolioNotifications.ChangeRequestDecided, project, [project.PmEmployeeId, project.OwnerEmployeeId], db, ct,
                            cr.Number, stage: kind == ApprovalEventKinds.Returned ? new BilingualText("معاد للتصحيح", "Returned") : new BilingualText("مرفوض", "Rejected"))
                            .ConfigureAwait(false);
                    }

                    return Result.Success();
                }

            default:
                return project.Apply(ProjectActions.ApprovalReturned, template, now);
        }
    }

    private async Task<(string? Ar, string? En)> StepNamesAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        var latest = await services.GetRequiredService<IApprovalEngine>().GetLatestAsync(approvalEvent.ProcessKey, approvalEvent.ResourceId, ct).ConfigureAwait(false);
        if (latest is not null && latest.CurrentStepCode == approvalEvent.StepCode && (latest.CurrentStepNameAr is not null || latest.CurrentStepNameEn is not null))
        {
            return (latest.CurrentStepNameAr, latest.CurrentStepNameEn);
        }

        var step = ProjectApprovalProcesses.Find(approvalEvent.ProcessKey)?.DefaultSteps.FirstOrDefault(s => s.Code == approvalEvent.StepCode);
        return step is null ? (approvalEvent.StepCode, approvalEvent.StepCode) : (step.NameAr, step.NameEn);
    }
}

/// <summary>"Next action required + who" for a project (SPEC §1.6, docs/design/portfolio.md §25).</summary>
internal sealed class ProjectCurrentStatus(
    PortfolioDbContext db, ProjectLoader loader, IRequestContext context, IApprovalEngine engine, IRoleHolderQuery roles, IUserDirectory users,
    Strategy.Platform.Organisation.Contracts.IEmployeeDirectory employees) : ICurrentStatusProvider
{
    public string ResourceType => PortfolioPermissions.ProjectResource;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var user = context.User;
        var p = await loader.FindAsync(resourceId, user, cancellationToken).ConfigureAwait(false);
        if (p is null)
        {
            return null;
        }

        var none = new Dictionary<string, BilingualText>();
        var pm = await PeopleAsync([p.PmEmployeeId], cancellationToken).ConfigureAwait(false);
        var canEdit = await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, p, cancellationToken).ConfigureAwait(false);
        var canManage = await loader.CanAsync(user, PortfolioPermissions.ProjectManage, p, cancellationToken).ConfigureAwait(false);
        CurrentStatus Status(string key, string tone, string? actorKey, IReadOnlyList<PersonName> actors, IReadOnlyList<CurrentStatusAction> actions,
            IReadOnlyDictionary<string, BilingualText>? parameters = null) =>
            new($"portfolio.currentStatus.project.{key}", parameters ?? none, tone, actorKey, actors, actions, p.Stage);

        switch (p.Status)
        {
            case ProjectStatuses.Cancelled:
                return Status("cancelled", "neutral", null, [], [], new Dictionary<string, BilingualText> { ["reason"] = new(p.CancelReason, p.CancelReason) });
            case ProjectStatuses.OnHold:
                return Status("onHold", "warning", "portfolio.currentStatus.pmOffice", [], canManage ? [new CurrentStatusAction("resume", "portfolio.actions.resume")] : [],
                    new Dictionary<string, BilingualText> { ["reason"] = new(p.HoldReason, p.HoldReason) });
            case ProjectStatuses.Completed:
                return Status("completed", "success", null, [], []);
        }

        if (p.ApprovalProcess is { } process && p.ApprovalResult is null)
        {
            var view = await engine.GetStatusViewAsync(process, p.Id, user, cancellationToken).ConfigureAwait(false);
            var actions = view?.MyTaskId is { } taskId
                ? view.MyActions.Select(a => new CurrentStatusAction(a, $"workflow.actions.{a}",
                    a == ApprovalActions.Approve ? "primary" : a == ApprovalActions.Reject ? "danger" : "secondary", taskId)).ToList()
                : [];
            var definition = ProjectApprovalProcesses.Find(process);
            var canRecall = canManage || canEdit || (process == ProjectApprovalProcesses.ChangeRequest && user.EmployeeIdOf() is { } me
                && await db.ChangeRequests.AnyAsync(c => c.ProjectId == p.Id && c.Status == ChangeStatuses.UnderReview && c.RequestedByEmployeeId == me, cancellationToken)
                    .ConfigureAwait(false));
            if (canRecall)
            {
                actions.Add(new CurrentStatusAction("recall", "portfolio.actions.recall", "secondary"));
            }

            return Status("waitingApproval", "info", "portfolio.currentStatus.approvers", view?.Approvers ?? [], actions, new Dictionary<string, BilingualText>
            {
                ["step"] = new(view?.Instance.CurrentStepNameAr ?? p.ApprovalStepNameAr, view?.Instance.CurrentStepNameEn ?? p.ApprovalStepNameEn),
                ["process"] = new(definition?.NameAr, definition?.NameEn),
            });
        }

        var returned = p.ApprovalResult is ApprovalInstanceStatuses.Returned or ApprovalInstanceStatuses.Rejected;
        switch (p.SubState)
        {
            case SubStates.Ownership:
                {
                    var canTake = user.EmployeeIdOf() is not null && (canManage || await loader.InSectionAsync(user, p, cancellationToken).ConfigureAwait(false));
                    return Status("takeOwnership", "info", "portfolio.currentStatus.sectionEmployees", [],
                        canTake ? [new CurrentStatusAction("take-ownership", "portfolio.actions.takeOwnership")] : []);
                }

            case SubStates.CharterDraft:
                {
                    var facts = await loader.FactsAsync(p, cancellationToken).ConfigureAwait(false);
                    var ready = ProjectGates.Charter(facts).All(g => g.Met);
                    return Status(returned ? "charterReturned" : ready ? "submitCharter" : "fillMinimums", returned ? "warning" : "info", "portfolio.currentStatus.pm", pm,
                        canEdit && ready ? [new CurrentStatusAction("submit-charter", "portfolio.actions.submitCharter")] : []);
                }

            case SubStates.Lpo:
                {
                    var canLpo = await loader.CanAsync(user, PortfolioPermissions.ProjectLpo, p, cancellationToken).ConfigureAwait(false);
                    return Status("waitingLpo", "info", "portfolio.currentStatus.administration", await RoleActorsAsync(PortfolioPermissions.AdministrationRoleKey, cancellationToken)
                        .ConfigureAwait(false), canLpo || canManage ? [new CurrentStatusAction("upload-lpo", "portfolio.actions.uploadLpo")] : []);
                }

            case SubStates.TeamFormation:
                return Status("waitingTeam", "info", "portfolio.currentStatus.pmOffice", await RoleActorsAsync(PortfolioPermissions.PmOfficeRoleKey, cancellationToken)
                    .ConfigureAwait(false), canManage ? [new CurrentStatusAction("request-team", "portfolio.actions.requestTeam")] : []);
            case SubStates.PlanningDraft:
                {
                    var facts = await loader.FactsAsync(p, cancellationToken).ConfigureAwait(false);
                    var ready = ProjectGates.Planning(facts).All(g => g.Met);
                    var key = returned ? "planningReturned" : ready ? "submitPlanning" : facts.DetailedWeight != 1m ? "addMilestones" : "updatePlanning";
                    return Status(key, returned ? "warning" : "info", "portfolio.currentStatus.pm", pm,
                        canEdit && ready ? [new CurrentStatusAction("submit-planning", "portfolio.actions.submitPlanning")] : []);
                }

            case SubStates.Execution:
                {
                    var window = await db.MonthlyUpdates.AsNoTracking().Where(m => m.ProjectId == p.Id && m.Status != MonthlyStatuses.Approved)
                        .OrderByDescending(m => m.Period).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    if (window is null)
                    {
                        return Status("noWindow", "neutral", null, [], canManage ? [new CurrentStatusAction("open-window", "portfolio.actions.openWindow", "secondary")] : []);
                    }

                    var parameters = new Dictionary<string, BilingualText> { ["period"] = new(window.Period, window.Period) };
                    return Status(window.Status == MonthlyStatuses.Returned ? "monthlyReturned" : "monthlyOpen", window.Status == MonthlyStatuses.Returned ? "warning" : "info",
                        "portfolio.currentStatus.pm", pm, canEdit ? [new CurrentStatusAction("monthly-update", "portfolio.actions.monthlyUpdate")] : [], parameters);
                }

            case SubStates.Completion:
                return Status("confirmCompletion", "info", "portfolio.currentStatus.pmOffice", await RoleActorsAsync(PortfolioPermissions.PmOfficeRoleKey, cancellationToken)
                    .ConfigureAwait(false), canManage ? [new CurrentStatusAction("confirm-completion", "portfolio.actions.confirmCompletion")] : []);
            case SubStates.ClosureDraft:
                return Status(returned ? "closureReturned" : "submitClosure", returned ? "warning" : "info", "portfolio.currentStatus.pm", pm,
                    canEdit ? [new CurrentStatusAction("closure", "portfolio.actions.closure")] : []);
            case SubStates.ImpactDraft:
                return Status(returned ? "impactReturned" : "enterImpact", returned ? "warning" : "info", "portfolio.currentStatus.pm", pm,
                    canEdit ? [new CurrentStatusAction("impact", "portfolio.actions.impact")] : []);
            default:
                return Status("completed", "success", null, [], []);
        }
    }

    private async Task<IReadOnlyList<PersonName>> PeopleAsync(IEnumerable<Guid?> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Where(i => i is not null).Select(i => i!.Value).ToList();
        var people = await employees.GetManyAsync(ids, ct).ConfigureAwait(false);
        return people.Values.Select(e => new PersonName(null, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr)).ToList();
    }

    private async Task<IReadOnlyList<PersonName>> RoleActorsAsync(string roleKey, CancellationToken ct)
    {
        var holders = await roles.GetHoldersAsync(roleKey, ct).ConfigureAwait(false);
        var found = await users.GetManyAsync(holders.UserIds, ct).ConfigureAwait(false);
        return await PeopleAsync(found.Values.Where(u => u.EmployeeId != null).Select(u => (Guid?)u.EmployeeId).Take(5), ct).ConfigureAwait(false);
    }
}

/// <summary>My Work: projects needing my action and monthly updates due (docs/design/portfolio.md §25).</summary>
internal sealed class ProjectMyWork(PortfolioDbContext db, ProjectLoader loader, IAccessService access) : IMyWorkSource
{
    public string SystemCode => PortfolioPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var items = new List<MyWorkItem>();
        var me = user.EmployeeIdOf();
        static string Link(Project p) => $"/portfolio/projects/{p.Id:D}";

        // Take ownership in my section.
        var units = await loader.UnitPathAsync(user, cancellationToken).ConfigureAwait(false);
        if (me is not null && units.Count > 0)
        {
            var unclaimed = await db.Projects.AsNoTracking()
                .Where(p => p.PmEmployeeId == null && p.Status == ProjectStatuses.Active && p.SubState == SubStates.Ownership
                    && units.Contains(p.SectionUnitId ?? p.UnitId ?? Guid.Empty))
                .OrderBy(p => p.CreatedAt).Take(20).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(unclaimed.Select(p => new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn,
                "portfolio.myWork.takeOwnership", Link(p), p.CreatedAt)));
        }

        // Projects I manage that wait for me.
        if (me is { } employee)
        {
            var mine = await db.Projects.AsNoTracking()
                .Where(p => p.PmEmployeeId == employee && p.Status == ProjectStatuses.Active
                    && (p.SubState == SubStates.CharterDraft || p.SubState == SubStates.PlanningDraft || p.SubState == SubStates.ClosureDraft
                        || p.SubState == SubStates.ImpactDraft || p.SubState == SubStates.Execution))
                .Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
            var ids = mine.Select(p => p.Id).ToList();
            var windows = await db.MonthlyUpdates.AsNoTracking()
                .Where(m => ids.Contains(m.ProjectId) && (m.Status == MonthlyStatuses.Open || m.Status == MonthlyStatuses.Returned))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var p in mine)
            {
                if (p.SubState == SubStates.Execution)
                {
                    if (windows.FirstOrDefault(w => w.ProjectId == p.Id) is { } w)
                    {
                        items.Add(new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn,
                            w.Status == MonthlyStatuses.Returned ? "portfolio.myWork.monthlyReturned" : "portfolio.myWork.monthlyDue", Link(p), w.OpenedAt,
                            w.DueAt is { } due ? DateOnly.FromDateTime(due.UtcDateTime) : null));
                    }

                    continue;
                }

                if (p.ApprovalProcess is not null && p.ApprovalResult is null)
                {
                    continue;
                }

                var key = p.SubState switch
                {
                    SubStates.CharterDraft => "portfolio.myWork.charter",
                    SubStates.PlanningDraft => "portfolio.myWork.planning",
                    SubStates.ClosureDraft => "portfolio.myWork.closure",
                    _ => "portfolio.myWork.impact",
                };
                items.Add(new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, key, Link(p), p.UpdatedAt));
            }

            var approvedChanges = await db.ChangeRequests.AsNoTracking()
                .Where(c => c.Status == ChangeStatuses.Approved && db.Projects.Any(p => p.Id == c.ProjectId && p.PmEmployeeId == employee))
                .Take(20).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(approvedChanges.Select(c => new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, c.ProjectId, c.Number, null, null,
                "portfolio.myWork.applyChange", $"/portfolio/projects/{c.ProjectId:D}?tab=changes", c.DecidedAt)));
        }

        // LPO to upload / team to form / initiative to confirm.
        var lpo = await access.ApplyScopeAsync(db.Projects.AsNoTracking(), user, PortfolioPermissions.ProjectLpo, ProjectScope.Map, cancellationToken).ConfigureAwait(false);
        items.AddRange((await lpo.Where(p => p.Status == ProjectStatuses.Active && p.SubState == SubStates.Lpo).Take(20).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, "portfolio.myWork.uploadLpo", Link(p), p.UpdatedAt)));
        var manage = await access.ApplyScopeAsync(db.Projects.AsNoTracking(), user, PortfolioPermissions.ProjectManage, ProjectScope.Map, cancellationToken)
            .ConfigureAwait(false);
        items.AddRange((await manage.Where(p => p.Status == ProjectStatuses.Active && (p.SubState == SubStates.TeamFormation || p.SubState == SubStates.Completion))
                .Take(20).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => new MyWorkItem(SystemCode, PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn,
                p.SubState == SubStates.TeamFormation ? "portfolio.myWork.formTeam" : "portfolio.myWork.confirmCompletion", Link(p), p.UpdatedAt)));
        return items.DistinctBy(i => (i.ResourceId, i.MessageKey, i.Number)).ToList();
    }
}

/// <summary>Projects are task-able and team-able; milestones are task-able (ADR 0072, SPEC §1.16).</summary>
public static class ProjectLinks
{
    public static readonly LinkableTypeDefinition Project = new(
        PortfolioPermissions.ProjectResource, PortfolioPermissions.SystemCode, "مشروع / مبادرة", "Project / initiative", [LinkCapabilities.TaskAble, LinkCapabilities.TeamAble],
        PortfolioPermissions.ProjectRead, "/portfolio/projects/{id}", FollowLinkedPermission: PortfolioPermissions.ProjectEdit);

    public static readonly LinkableTypeDefinition Milestone = new(
        PortfolioPermissions.MilestoneResource, PortfolioPermissions.SystemCode, "مرحلة / معلم مشروع", "Project milestone", [LinkCapabilities.TaskAble],
        PortfolioPermissions.ProjectRead, "/portfolio/milestones/{id}", FollowLinkedPermission: PortfolioPermissions.ProjectEdit);
}

internal sealed class ProjectLinkTargets(ProjectLoader loader) : ILinkTargetSource
{
    public string ResourceType => PortfolioPermissions.ProjectResource;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await loader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(p => p.Id, p => Target(p));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await loader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } project
        && await loader.CanAsync(user, permission, project, cancellationToken).ConfigureAwait(false);

    internal static LinkTarget Target(Project p)
    {
        var stage = ProjectNotifier.StageText(p.Status == ProjectStatuses.Active ? p.Stage : p.Status);
        return new LinkTarget(p.Id, p.Number, p.NameAr, p.NameEn, p.Status == ProjectStatuses.Active ? p.Stage : p.Status, stage.Ar ?? p.Stage, stage.En ?? p.Stage,
            p.Status switch
            {
                ProjectStatuses.Completed => "success",
                ProjectStatuses.Cancelled => "neutral",
                ProjectStatuses.OnHold => "warning",
                _ => p.ProgressStatus == ProgressCalculator.Delayed ? "danger" : "info",
            },
            p.Status is ProjectStatuses.Completed or ProjectStatuses.Cancelled);
    }
}

internal sealed class MilestoneLinkTargets(PortfolioDbContext db, ProjectLoader loader) : ILinkTargetSource
{
    public string ResourceType => PortfolioPermissions.MilestoneResource;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await loader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await db.Milestones.AsNoTracking().Where(m => ids.Contains(m.Id) && visible.Any(p => p.Id == m.ProjectId)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(m => m.Id, m => new LinkTarget(
            m.Id, m.Code, m.TitleAr, m.TitleEn, m.Status, m.Status, m.Status,
            m.Status == MilestoneStatuses.Completed ? "success" : m.Status == MilestoneStatuses.Cancelled ? "neutral" : "info",
            m.Status is MilestoneStatuses.Completed or MilestoneStatuses.Cancelled));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var projectId = await db.Milestones.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.ProjectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return projectId is { } p && await loader.FindAsync(p, user, cancellationToken).ConfigureAwait(false) is { } project
            && await loader.CanAsync(user, permission, project, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>ADR 0072: a formed team linked to projects → project waits no longer for its team; record role <c>team-member</c> follows the members.</summary>
internal sealed class ProjectTeamMembershipHandler(
    PortfolioDbContext db, ILinkStore links, IRecordRoleService recordRoles, ProjectNotifier notifier, IServiceProvider services, IClock clock)
    : IIntegrationEventHandler<TeamMembershipChanged>
{
    private const string TeamResource = "teams.team";

    public async Task HandleAsync(TeamMembershipChanged integrationEvent, CancellationToken cancellationToken)
    {
        var stored = await links.ListFromAsync(new LinkRef(TeamResource, integrationEvent.TeamId), LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false);
        var projectIds = stored.Where(l => l.ToType == PortfolioPermissions.ProjectResource).Select(l => l.ToId).Distinct().ToList();
        if (projectIds.Count == 0)
        {
            return;
        }

        var formed = integrationEvent.Status is "active" or "inactive" or "pending_evaluation" or "evaluation_waiting_approval";
        var members = formed ? integrationEvent.ActiveMemberEmployeeIds.ToHashSet() : [];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var project in await db.Projects.Where(p => projectIds.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var held = (await recordRoles.GetHoldersAsync(PortfolioPermissions.ProjectResource, project.Id, cancellationToken).ConfigureAwait(false))
                .Where(h => h.RoleCode == PortfolioPermissions.TeamMemberRole && h.PrincipalKind == RecordPrincipalKinds.Employee && h.ValidTo is null)
                .Select(h => h.PrincipalId).ToHashSet();

            // Other teams linked to the same project keep their members' access.
            var keep = await OtherTeamMembersAsync(project.Id, integrationEvent.TeamId, cancellationToken).ConfigureAwait(false);
            foreach (var employee in members.Where(m => !held.Contains(m)))
            {
                await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, employee,
                    PortfolioPermissions.TeamMemberRole, cancellationToken).ConfigureAwait(false);
            }

            foreach (var employee in held.Where(h => !members.Contains(h) && !keep.Contains(h)))
            {
                await recordRoles.RevokeInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, employee,
                    PortfolioPermissions.TeamMemberRole, cancellationToken).ConfigureAwait(false);
            }

            if (integrationEvent.Status == "active")
            {
                project.LinkTeam(integrationEvent.TeamId, clock.UtcNow);
                if (project.Status == ProjectStatuses.Active && project.SubState == SubStates.TeamFormation)
                {
                    var template = project.TemplateOr(StageTemplate.ProjectDefault);
                    if (project.Apply(ProjectActions.TeamFormed, template, clock.UtcNow).IsSuccess)
                    {
                        await notifier.ToEmployeesAsync(PortfolioNotifications.StageChanged, project, [project.PmEmployeeId], db, cancellationToken,
                            stage: ProjectNotifier.StageText(project.Stage)).ConfigureAwait(false);
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<Guid>> OtherTeamMembersAsync(Guid projectId, Guid teamId, CancellationToken ct)
    {
        if (services.GetService<ITeamsDirectory>() is not { } teams)
        {
            return [];
        }

        var others = (await links.ListToAsync(new LinkRef(PortfolioPermissions.ProjectResource, projectId), LinkCapabilities.TeamAble, ct).ConfigureAwait(false))
            .Where(l => l.FromType == TeamResource && l.FromId != teamId).Select(l => l.FromId).Distinct();
        var result = new HashSet<Guid>();
        foreach (var other in others)
        {
            if (await teams.GetTeamAsync(other, StrategyClaims.CreateSystemPrincipal(), ct).ConfigureAwait(false) is { } team)
            {
                result.UnionWith(team.ActiveMemberEmployeeIds);
            }
        }

        return result;
    }
}

/// <summary>The KPI card's "Linked records" lists projects whose active KPIs link it (ADR 0065 / 0074).</summary>
internal sealed class ProjectKpiLinks(PortfolioDbContext db) : IKpiLinkedRecordSource
{
    public string ResourceType => PortfolioPermissions.ProjectResource;

    public async Task<IReadOnlyList<Guid>> GetLinkedRecordIdsAsync(Guid cardId, CancellationToken cancellationToken) =>
        await db.Kpis.AsNoTracking().Where(k => k.KpiCardId == cardId && k.Status == ItemStatuses.Active).Select(k => k.ProjectId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Project risks for the Risk module's future "Project Risks" tab (ADR 0074).</summary>
internal sealed class ProjectRiskDirectory(PortfolioDbContext db, ProjectLoader loader) : IProjectRiskDirectory
{
    public async Task<IReadOnlyList<ProjectRiskSummary>> ListAsync(ClaimsPrincipal user, Guid? projectId = null, string? band = null, CancellationToken cancellationToken = default)
    {
        var visible = await loader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        if (projectId is { } p)
        {
            visible = visible.Where(x => x.Id == p);
        }

        var risks = db.Risks.AsNoTracking().Where(r => r.Status == ItemStatuses.Active);
        if (!string.IsNullOrWhiteSpace(band))
        {
            risks = risks.Where(r => r.ResidualBand == band);
        }

        var rows = await risks.Join(visible, r => r.ProjectId, x => x.Id, (r, x) => new { Risk = r, Project = x })
            .OrderByDescending(x => x.Risk.ResidualScore).Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new ProjectRiskSummary(
            x.Risk.Id, x.Project.Id, x.Project.Number, x.Project.NameAr, x.Project.NameEn, x.Risk.Title, x.Risk.CategoryCode, x.Risk.Probability, x.Risk.Impact,
            x.Risk.ControlEvaluation, x.Risk.InherentScore, x.Risk.InherentBand, x.Risk.ResidualScore, x.Risk.ResidualBand, x.Risk.Response, x.Risk.ImplementationPct,
            x.Risk.OwnerEmployeeId, x.Risk.Status, $"/portfolio/projects/{x.Project.Id:D}?tab=risks")).ToList();
    }
}

/// <summary>The monthly cycle (ADR 0071): opens due windows, reminds, sends the overdue notice. One transaction per project; stamps make it idempotent.</summary>
internal sealed class MonthlyCycleRunner(PortfolioDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public async Task<(int Opened, int Reminded, int Overdue)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        int opened = 0, reminded = 0, overdue = 0;
        var executing = await db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatuses.Active && p.SubState == SubStates.Execution).Select(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in executing)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>()
                .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
            var scoped = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var windows = scope.ServiceProvider.GetRequiredService<MonthlyWindows>();
            var notifier = scope.ServiceProvider.GetRequiredService<ProjectNotifier>();
            var settings = await scope.ServiceProvider.GetRequiredService<RequestReader>().SettingsAsync(ct).ConfigureAwait(false);
            var project = await scoped.Projects.FirstAsync(p => p.Id == id, ct).ConfigureAwait(false);
            var unit = project.SectionUnitId ?? project.UnitId;
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var changed = false;
            var current = await scoped.MonthlyUpdates.Where(m => m.ProjectId == id && m.Status != MonthlyStatuses.Approved).OrderByDescending(m => m.Period)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (current is null)
            {
                var today = await windows.LocalDateAsync(unit, now, ct).ConfigureAwait(false);
                var period = MonthlyUpdate.PeriodOf(today);
                var opening = await windows.OpeningAsync(unit, today, ct).ConfigureAwait(false);
                var exists = await scoped.MonthlyUpdates.AnyAsync(m => m.ProjectId == id && m.Period == period, ct).ConfigureAwait(false);
                if (!exists && now >= opening && MonthlyUpdate.PeriodOf(await windows.LocalDateAsync(unit, opening, ct).ConfigureAwait(false)) == period)
                {
                    await windows.OpenAsync(project, opening, "job", ct).ConfigureAwait(false);
                    opened++;
                    changed = true;
                }
            }
            else if (MonthlyStatuses.AcceptsUpdates(current.Status) && current.DueAt is { } due)
            {
                var dueText = await windows.DueTextAsync(unit, due, ct).ConfigureAwait(false);
                if (now <= due && current.RemindedAt is null
                    && now >= await windows.ReminderAtAsync(unit, due, settings.MonthlyReminderWorkingDays, ct).ConfigureAwait(false))
                {
                    current.MarkReminded(now);
                    await notifier.ToEmployeesAsync(PortfolioNotifications.MonthlyReminder, project, [project.PmEmployeeId], scoped, ct, current.Period, due: dueText)
                        .ConfigureAwait(false);
                    reminded++;
                    changed = true;
                }

                if (now > due && current.OverdueNotifiedAt is null)
                {
                    current.MarkOverdueNotified(now);
                    current.MarkReminded(current.RemindedAt ?? now);
                    await notifier.ToEmployeesAsync(PortfolioNotifications.MonthlyOverdue, project, [project.PmEmployeeId], scoped, ct, current.Period, due: dueText)
                        .ConfigureAwait(false);
                    overdue++;
                    changed = true;
                }
            }

            if (changed)
            {
                await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        return (opened, reminded, overdue);
    }
}

[DisallowConcurrentExecution]
internal sealed class MonthlyCycleJob(RequestContext requestContext, MonthlyCycleRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "portfolio.monthly-cycle", "دورة التحديث الشهري للمشاريع", "Project monthly update cycle", "0 10 * * * ?", typeof(MonthlyCycleJob),
        "يفتح نافذة التحديث الشهري في أول يوم عمل من الشهر ويرسل التذكيرات وإشعارات التأخر لمديري المشاريع.",
        "Opens the monthly update window on the first working moment of the month and sends reminders and overdue notices to project managers.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (opened, reminded, overdue) = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"opened={opened} reminded={reminded} overdue={overdue}";
    }
}

/// <summary>
/// Re-scores active project risks on the shared risk framework when a version is saved with "recalculate now" (and on the seeded version 1) —
/// ADR 0101. Tracked changes, so the audit trail records one entry per re-rated risk. Idempotent.
/// </summary>
internal sealed class ProjectRiskRescoreHandler(PortfolioDbContext db, IProjectRiskScoring scoring) : IIntegrationEventHandler<Risk.Contracts.RiskFrameworkChanged>
{
    public async Task HandleAsync(Risk.Contracts.RiskFrameworkChanged integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Recalculation != Risk.Contracts.RiskRecalculation.Now)
        {
            return;
        }

        var risks = await db.Risks.Where(r => r.Status == ItemStatuses.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var risk in risks)
        {
            var score = await scoring.ScoreAsync(risk.Probability, risk.Impact, risk.ControlEvaluation, cancellationToken).ConfigureAwait(false);
            if (score.Inherent != risk.InherentScore || score.InherentBand != risk.InherentBand || score.Residual != risk.ResidualScore || score.ResidualBand != risk.ResidualBand)
            {
                risk.Score(score);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
