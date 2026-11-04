using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>
/// Approval process <c>portfolio.request</c> (SPEC §1.5 W1, Appendix A Q-P4, ADR 0048): Section Manager when applicable → Department Director →
/// PM Office → DG Office when budget &gt; threshold → Budget Section when budget is required. Seeded once; the Approval Settings centre owns it.
/// </summary>
public static class RequestApprovalProcess
{
    public const string Key = "portfolio.request";
    public const string RequiresBudget = "requiresBudget";
    public const string EstimatedBudget = "estimatedBudget";
    public const string Threshold = "threshold";
    public const string Classification = "classification";
    public const string EvaluationScore = "evaluationScore";

    public const string SectionManagerStep = "section-manager";
    public const string DepartmentDirectorStep = "department-director";
    public const string PmOfficeStep = "pm-office";
    public const string DgOfficeStep = "dg-office";
    public const string BudgetStep = "budget-section";

    /// <summary>Q-P4: only when the section has a manager distinct from the director and from the submitter.</summary>
    public const string SectionManagerCondition = "sectionManager != null and sectionManager != departmentDirector and sectionManager != submitter";

    public const string DgOfficeCondition = "requiresBudget and estimatedBudget > threshold";

    public const string BudgetCondition = "requiresBudget";

    public static readonly ApprovalProcessDefinition Definition = new(
        Key, PortfolioPermissions.SystemCode, "اعتماد طلب برنامج", "Program request approval", PortfolioPermissions.RequestResource,
        PortfolioPermissions.RequestRead, "/portfolio/requests/{id}",
        [
            new(RequiresBudget, ExpressionValueType.Boolean, "يتطلب ميزانية", "Requires budget"),
            new(EstimatedBudget, ExpressionValueType.Number, "الميزانية التقديرية", "Estimated budget"),
            new(Threshold, ExpressionValueType.Number, "حد المبلغ (مشروع / مبادرة)", "Threshold amount (project / initiative)"),
            new(Classification, ExpressionValueType.Text, "التصنيف", "Classification"),
            new(EvaluationScore, ExpressionValueType.Number, "درجة التقييم (%)", "Evaluation score (%)"),
        ],
        [
            new(SectionManagerStep, "رئيس القسم", "Section manager", new ApproverRule(ApproverRuleKinds.SectionManager, Of: ApproverRuleSubjects.Record),
                SectionManagerCondition, SlaHours: 16),
            new(DepartmentDirectorStep, "مدير الإدارة", "Department director", new ApproverRule(ApproverRuleKinds.DepartmentDirector, Of: ApproverRuleSubjects.Record),
                SlaHours: 16),
            new(PmOfficeStep, "مكتب إدارة المشاريع", "PM Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.PmOfficeRoleKey), SlaHours: 24),
            new(DgOfficeStep, "مكتب المدير العام", "DG Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.DgOfficeRoleKey),
                DgOfficeCondition, SlaHours: 24),
            new(BudgetStep, "قسم الميزانية", "Budget Section", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.BudgetSectionRoleKey),
                BudgetCondition, SlaHours: 24),
        ],
        PortfolioPermissions.ApproverRole,
        "طلبات البرامج: رئيس القسم (عند الاقتضاء) ← مدير الإدارة ← مكتب إدارة المشاريع ← مكتب المدير العام (ميزانية أعلى من الحد) ← قسم الميزانية (عند طلب ميزانية).",
        "Program requests: section manager (when applicable) → department director → PM Office → DG Office (budget above the threshold) → Budget Section (budget required).");
}

/// <summary>Moves the request with the approval and creates the Project / Initiative on final approval (inside the engine's transaction).</summary>
internal sealed class RequestApprovalHook(
    PortfolioDbContext db, RequestReader reader, INumberingService numbering, IRecordRoleService recordRoles, ISystemState systems, IOrgDirectory org,
    PortfolioNotifier notifier, IClock clock, IServiceProvider services) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [RequestApprovalProcess.Key];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        switch (approvalEvent.Kind)
        {
            case ApprovalEventKinds.StepEntered:
                if (request.Status == RequestStatuses.InApproval)
                {
                    var (ar, en) = await StepNamesAsync(approvalEvent, cancellationToken).ConfigureAwait(false);
                    request.EnterStep(approvalEvent.StepCode, ar, en, now);
                }

                break;
            case ApprovalEventKinds.Returned:
            case ApprovalEventKinds.Rejected:
                {
                    var decided = request.Decide(approvalEvent.Kind == ApprovalEventKinds.Returned ? RequestActions.Return : RequestActions.Reject, now);
                    if (decided.IsFailure)
                    {
                        return decided.Error;
                    }

                    break;
                }

            case ApprovalEventKinds.Approved:
                {
                    var decided = request.Decide(RequestActions.Approve, now);
                    if (decided.IsFailure)
                    {
                        return decided.Error;
                    }

                    await CreateProjectAsync(request, cancellationToken).ConfigureAwait(false);
                    break;
                }

            default:
                return Result.Success(); // Cancelled: the request was cancelled first by its own handler.
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>ADR 0049: one project per request, numbered PROJ-{UNIT}-{YYYY}-NNN, owner = department director of the request's unit.</summary>
    private async Task CreateProjectAsync(ProgramRequest request, CancellationToken ct)
    {
        if (await db.Projects.AnyAsync(p => p.RequestId == request.Id, ct).ConfigureAwait(false))
        {
            return;
        }

        var owningUnit = await systems.GetOwningUnitAsync(PortfolioPermissions.SystemCode, ct).ConfigureAwait(false) ?? request.UnitId;
        var unitCode = owningUnit is { } unitId ? (await org.GetUnitAsync(unitId, ct).ConfigureAwait(false))?.Code : null;
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, PortfolioPermissions.ProjectNumbering, new NumberingContext(today, unitCode ?? PortfolioPermissions.SystemCode.ToUpperInvariant()),
            ct).ConfigureAwait(false);
        var owner = await reader.DepartmentDirectorOfAsync(request.UnitId, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var project = Project.FromRequest(number, request, owner, settings.TemplateFor(request.EffectiveClassification),
            settings.SuggestedMethodology(request.ProgramTypeCode));
        if (settings.SuggestedMethodology(request.ProgramTypeCode) != Methodologies.Waterfall)
        {
            project.SetMethodology(project.Methodology, settings.DefaultSprintLengthDays, null, null);
            project.SetBoard(BoardConfig.Default);
        }

        db.Projects.Add(project);
        request.LinkProject(project.Id);
        foreach (var benefit in await db.Benefits.Where(b => b.RequestId == request.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            benefit.AttachToProject(project.Id); // ADR 0095: benefits defined at request carry to the project
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (owner is { } ownerId)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, ownerId,
                PortfolioPermissions.OwnerRole, ct).ConfigureAwait(false);
        }

        if (request.RequesterEmployeeId is { } requester)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, requester,
                PortfolioPermissions.RequesterRole, ct).ConfigureAwait(false);
        }
        else if (request.RequesterUserId is { } requesterUser)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.User, requesterUser,
                PortfolioPermissions.RequesterRole, ct).ConfigureAwait(false);
        }

        if (request.RequesterUserId is { } user)
        {
            await notifier.SendAsync(PortfolioNotifications.RequestApproved, request, [user], project.Number, db, ct, $"/portfolio/projects/{project.Id:D}")
                .ConfigureAwait(false);
        }
    }

    private async Task<(string? Ar, string? En)> StepNamesAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        // Steps are configuration: the names come from the engine's version in progress (resolved lazily — the engine owns the hooks).
        var latest = await services.GetRequiredService<IApprovalEngine>().GetLatestAsync(approvalEvent.ProcessKey, approvalEvent.ResourceId, ct).ConfigureAwait(false);
        if (latest is not null && latest.CurrentStepCode == approvalEvent.StepCode && (latest.CurrentStepNameAr is not null || latest.CurrentStepNameEn is not null))
        {
            return (latest.CurrentStepNameAr, latest.CurrentStepNameEn);
        }

        var step = RequestApprovalProcess.Definition.DefaultSteps.FirstOrDefault(s => s.Code == approvalEvent.StepCode);
        return step is null ? (approvalEvent.StepCode, approvalEvent.StepCode) : (step.NameAr, step.NameEn);
    }
}

/// <summary>Sends Portfolio templates to users (outbox, in the caller's transaction).</summary>
internal sealed class PortfolioNotifier(INotificationService notifications)
{
    public Task SendAsync(
        NotificationTemplateDefinition template, ProgramRequest request, IReadOnlyCollection<Guid> userIds, string? text, object? transactionContext, CancellationToken ct,
        string? link = null, string? actor = null)
    {
        if (userIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return notifications.SendAsync(new NotificationRequest(
            template.Key,
            userIds.Distinct().Select(NotificationRecipient.User).ToList(),
            new Dictionary<string, string>
            {
                ["requestAr"] = request.NameAr ?? request.NameEn ?? string.Empty,
                ["requestEn"] = request.NameEn ?? request.NameAr ?? string.Empty,
                ["number"] = request.Number,
                ["actor"] = actor ?? string.Empty,
                ["text"] = text ?? string.Empty,
            },
            link ?? $"/portfolio/requests/{request.Id:D}"), transactionContext, ct);
    }
}

/// <summary>"Next action required + who" for a request (SPEC §1.6).</summary>
internal sealed class RequestCurrentStatus(
    RequestReader reader, RequestQueries queries, PortfolioDbContext db, IRequestContext context, IApprovalEngine engine, IRoleHolderQuery roles,
    IUserDirectory users, IEmployeeDirectory employees) : ICurrentStatusProvider
{
    public string ResourceType => PortfolioPermissions.RequestResource;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var r = await reader.FindAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (r is null)
        {
            return null;
        }

        var empty = new Dictionary<string, BilingualText>();
        var requester = await PeopleAsync([r.RequesterEmployeeId], cancellationToken).ConfigureAwait(false);
        switch (r.Status)
        {
            case RequestStatuses.InApproval:
                {
                    var view = await engine.GetStatusViewAsync(RequestApprovalProcess.Key, r.Id, context.User, cancellationToken).ConfigureAwait(false);
                    var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? r.ApprovalStepNameAr, view?.Instance.CurrentStepNameEn ?? r.ApprovalStepNameEn);
                    var actions = view?.MyTaskId is { } taskId
                        ? view.MyActions.Select(a => new CurrentStatusAction(a, $"workflow.actions.{a}",
                            a == ApprovalActions.Approve ? "primary" : a == ApprovalActions.Reject ? "danger" : "secondary", taskId)).ToList()
                        : [];
                    if (await queries.IsBudgetApproverAsync(r, cancellationToken).ConfigureAwait(false))
                    {
                        actions.Insert(0, new CurrentStatusAction("approved-budget", "portfolio.actions.setApprovedBudget", "secondary"));
                    }

                    return new CurrentStatus("portfolio.currentStatus.waitingStep", new Dictionary<string, BilingualText> { ["step"] = step }, "info",
                        "portfolio.currentStatus.approvers", view?.Approvers ?? [], actions, r.Status);
                }

            case RequestStatuses.Approved:
                {
                    var number = r.ProjectId is { } p
                        ? await db.Projects.AsNoTracking().Where(x => x.Id == p).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                        : null;
                    return new CurrentStatus("portfolio.currentStatus.approved", new Dictionary<string, BilingualText> { ["project"] = new(number, number) }, "success",
                        null, [], r.ProjectId is null ? [] : [new CurrentStatusAction("open-project", "portfolio.actions.openProject")], r.Status);
                }

            case RequestStatuses.Rejected:
                return new CurrentStatus("portfolio.currentStatus.rejected", empty, "danger", null, [], [], r.Status);
            case RequestStatuses.Cancelled:
                return new CurrentStatus("portfolio.currentStatus.cancelled", new Dictionary<string, BilingualText> { ["reason"] = new(r.CancelReason, r.CancelReason) },
                    "neutral", null, [], [], r.Status);
        }

        var canEdit = await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, r, cancellationToken).ConfigureAwait(false);
        if (r.RequiresBudget == true && r.CostPath == CostPaths.ProjectScope && r.ProcurementStatus == ProcurementStatuses.WaitingQuotations)
        {
            var canQuote = await reader.CanAsync(context.User, PortfolioPermissions.RequestQuotations, r, cancellationToken).ConfigureAwait(false);
            return new CurrentStatus("portfolio.currentStatus.waitingQuotations", empty, "info", "portfolio.currentStatus.administration",
                await AdministrationAsync(cancellationToken).ConfigureAwait(false),
                canQuote ? [new CurrentStatusAction("upload-quotations", "portfolio.actions.uploadQuotations")] : [], r.Status);
        }

        if (r.RequiresBudget == true && r.CostPath == CostPaths.ProjectScope && r.ProcurementStatus is null)
        {
            return new CurrentStatus(r.ProjectScopeUploaded ? "portfolio.currentStatus.sendScope" : "portfolio.currentStatus.uploadScope", empty, "info",
                "portfolio.currentStatus.requester", requester,
                canEdit ? [new CurrentStatusAction(r.ProjectScopeUploaded ? "hand-off" : "upload-scope",
                    r.ProjectScopeUploaded ? "portfolio.actions.handOff" : "portfolio.actions.uploadScope")] : [], r.Status);
        }

        if (r.RequiresBudget == true && r.CostPath == CostPaths.CostEstimation && !r.CostEstimationUploaded)
        {
            return new CurrentStatus("portfolio.currentStatus.uploadCostEstimation", empty, "info", "portfolio.currentStatus.requester", requester,
                canEdit ? [new CurrentStatusAction("upload-cost-estimation", "portfolio.actions.uploadCostEstimation")] : [], r.Status);
        }

        var returned = r.ApprovalStatus == RequestApprovalResults.Returned;
        return new CurrentStatus(returned ? "portfolio.currentStatus.returned" : "portfolio.currentStatus.draft", empty, returned ? "warning" : "info",
            "portfolio.currentStatus.requester", requester,
            canEdit ? [new CurrentStatusAction(returned ? "resubmit" : "submit", returned ? "portfolio.actions.resubmit" : "portfolio.actions.submit")] : [], r.Status);
    }

    private async Task<IReadOnlyList<PersonName>> PeopleAsync(IEnumerable<Guid?> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Where(i => i is not null).Select(i => i!.Value).ToList();
        var people = await employees.GetManyAsync(ids, ct).ConfigureAwait(false);
        return people.Values.Select(e => new PersonName(null, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr)).ToList();
    }

    private async Task<IReadOnlyList<PersonName>> AdministrationAsync(CancellationToken ct)
    {
        var holders = await roles.GetHoldersAsync(PortfolioPermissions.AdministrationRoleKey, ct).ConfigureAwait(false);
        var found = await users.GetManyAsync(holders.UserIds, ct).ConfigureAwait(false);
        var employeeIds = found.Values.Where(u => u.EmployeeId != null).Select(u => (Guid?)u.EmployeeId).Take(5);
        return await PeopleAsync(employeeIds, ct).ConfigureAwait(false);
    }
}

/// <summary>My Work: my returned requests, my drafts waiting for my upload, and requests waiting for quotations from me.</summary>
internal sealed class RequestMyWork(PortfolioDbContext db, IAccessService access) : IMyWorkSource
{
    public string SystemCode => PortfolioPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.UserId() is not { } userId)
        {
            return [];
        }

        var me = user.EmployeeIdOf();
        var items = new List<MyWorkItem>();
        var own = await access.ApplyScopeAsync(db.Requests.AsNoTracking(), user, PortfolioPermissions.RequestEdit, RequestScope.Map, cancellationToken).ConfigureAwait(false);
        var mine = await own
            .Where(r => r.Status == RequestStatuses.Draft && (r.RequesterUserId == userId || (me != null && r.RequesterEmployeeId == me))
                && (r.ApprovalStatus == RequestApprovalResults.Returned || r.ProcurementStatus == ProcurementStatuses.QuotationsUploaded))
            .OrderBy(r => r.UpdatedAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(mine.Select(r => new MyWorkItem(SystemCode, PortfolioPermissions.RequestResource, r.Id, r.Number, r.NameAr, r.NameEn,
            r.ApprovalStatus == RequestApprovalResults.Returned ? "portfolio.myWork.returned" : "portfolio.myWork.quotationsReady", $"/portfolio/requests/{r.Id:D}",
            r.LastActionAt ?? r.UpdatedAt)));
        var quoting = await access.ApplyScopeAsync(db.Requests.AsNoTracking(), user, PortfolioPermissions.RequestQuotations, RequestScope.Map, cancellationToken)
            .ConfigureAwait(false);
        var waiting = await quoting.Where(r => r.Status == RequestStatuses.Draft && r.ProcurementStatus == ProcurementStatuses.WaitingQuotations)
            .OrderBy(r => r.QuotationsHandedOffAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(waiting.Select(r => new MyWorkItem(SystemCode, PortfolioPermissions.RequestResource, r.Id, r.Number, r.NameAr, r.NameEn,
            "portfolio.myWork.uploadQuotations", $"/portfolio/requests/{r.Id:D}", r.QuotationsHandedOffAt)));
        return items;
    }
}
