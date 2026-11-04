using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>The project approval processes (docs/design/portfolio.md §18). Subject = the project; one in progress at a time (ADR 0070).</summary>
public static class ProjectApprovalProcesses
{
    public const string Charter = "portfolio.charter";
    public const string Planning = "portfolio.planning";
    public const string MonthlyUpdate = "portfolio.monthly-update";
    public const string Closure = "portfolio.closure";
    public const string Impact = "portfolio.impact";
    public const string ChangeRequest = "portfolio.change-request";

    public const string ClassificationVariable = "classification";
    public const string BudgetVariable = "budget";
    public const string PeriodVariable = "period";
    public const string PerformanceVariable = "performance";
    public const string ChangeTypeVariable = "changeType";
    public const string CostAffectedVariable = "costAffected";
    public const string CostChangeVariable = "costChange";
    public const string GmThresholdVariable = "gmThreshold";

    public const string FinanceCondition = "costAffected";
    public const string GmCondition = "changeType == 'cancellation' or changeType == 'merge' or costChange > gmThreshold";

    public static readonly IReadOnlyList<string> StageProcesses = [Charter, Planning, Closure, Impact];

    private static readonly ExpressionVariable[] Common =
    [
        new(ClassificationVariable, ExpressionValueType.Text, "التصنيف", "Classification"),
        new(BudgetVariable, ExpressionValueType.Number, "ميزانية المشروع", "Project budget"),
    ];

    private static ApprovalStepDefinition SectionManager() =>
        new(RequestApprovalProcess.SectionManagerStep, "رئيس القسم", "Section manager", new ApproverRule(ApproverRuleKinds.SectionManager, Of: ApproverRuleSubjects.Record),
            RequestApprovalProcess.SectionManagerCondition, SlaHours: 16);

    private static ApprovalStepDefinition Director() =>
        new(RequestApprovalProcess.DepartmentDirectorStep, "مدير الإدارة", "Department director",
            new ApproverRule(ApproverRuleKinds.DepartmentDirector, Of: ApproverRuleSubjects.Record), SlaHours: 16);

    private static ApprovalStepDefinition PmOffice() =>
        new(RequestApprovalProcess.PmOfficeStep, "مكتب إدارة المشاريع", "PM Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.PmOfficeRoleKey),
            SlaHours: 24);

    private static ApprovalProcessDefinition Define(
        string key, string nameAr, string nameEn, IReadOnlyList<ApprovalStepDefinition> steps, string descriptionAr, string descriptionEn, params ExpressionVariable[] extra) =>
        new(key, PortfolioPermissions.SystemCode, nameAr, nameEn, PortfolioPermissions.ProjectResource, PortfolioPermissions.ProjectRead, "/portfolio/projects/{id}",
            [.. Common, .. extra], steps, PortfolioPermissions.ApproverRole, descriptionAr, descriptionEn);

    public static readonly ApprovalProcessDefinition CharterDefinition = Define(
        Charter, "اعتماد ميثاق المشروع", "Project charter approval", [SectionManager(), Director(), PmOffice()],
        "ميثاق المشروع: رئيس القسم (عند الاقتضاء) ← مدير الإدارة ← مكتب إدارة المشاريع.", "Charter: section manager (when applicable) → department director → PM Office.");

    public static readonly ApprovalProcessDefinition PlanningDefinition = Define(
        Planning, "اعتماد تخطيط المشروع", "Project planning approval", [SectionManager(), Director(), PmOffice()],
        "التخطيط: رئيس القسم (عند الاقتضاء) ← مدير الإدارة ← مكتب إدارة المشاريع.", "Planning: section manager (when applicable) → department director → PM Office.");

    public static readonly ApprovalProcessDefinition MonthlyDefinition = Define(
        MonthlyUpdate, "اعتماد التحديث الشهري", "Monthly update approval", [Director(), PmOffice()],
        "التحديث الشهري (Q-P1): مدير الإدارة ← مكتب إدارة المشاريع.", "Monthly update (Q-P1): department director → PM Office.",
        new ExpressionVariable(PeriodVariable, ExpressionValueType.Text, "الفترة", "Period"),
        new ExpressionVariable(PerformanceVariable, ExpressionValueType.Number, "نسبة الإنجاز (%)", "Performance (%)"));

    public static readonly ApprovalProcessDefinition ClosureDefinition = Define(
        Closure, "اعتماد إغلاق المشروع", "Project closure approval", [SectionManager(), Director(), PmOffice()],
        "الإغلاق: رئيس القسم (عند الاقتضاء) ← مدير الإدارة ← مكتب إدارة المشاريع.", "Closure: section manager (when applicable) → department director → PM Office.");

    public static readonly ApprovalProcessDefinition ImpactDefinition = Define(
        Impact, "اعتماد أثر المشروع", "Project impact approval", [PmOffice()], "الأثر: مكتب إدارة المشاريع.", "Impact: PM Office.");

    public static readonly ApprovalProcessDefinition ChangeRequestDefinition = Define(
        ChangeRequest, "اعتماد طلب تغيير", "Change request approval",
        [
            Director(), PmOffice(),
            new("finance-section", "قسم الخدمات المالية", "Finance Service Section", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.FinanceSectionRoleKey),
                FinanceCondition, SlaHours: 24),
            new("gm-office", "مكتب المدير العام", "GM Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: PortfolioPermissions.GmOfficeRoleKey), GmCondition, SlaHours: 24),
        ],
        "طلبات التغيير (Q-P4): مدير الإدارة ← مكتب إدارة المشاريع ← الخدمات المالية (عند تأثر التكلفة) ← مكتب المدير العام (إلغاء / دمج أو تغيير تكلفة أعلى من الحد).",
        "Change requests (Q-P4): department director → PM Office → Finance Service Section (cost affected) → GM Office (cancellation / merge or a cost change above the threshold).",
        new ExpressionVariable(ChangeTypeVariable, ExpressionValueType.Text, "نوع التغيير", "Change type"),
        new ExpressionVariable(CostAffectedVariable, ExpressionValueType.Boolean, "يؤثر على التكلفة", "Cost affected"),
        new ExpressionVariable(CostChangeVariable, ExpressionValueType.Number, "مقدار تغيير التكلفة", "Cost change"),
        new ExpressionVariable(GmThresholdVariable, ExpressionValueType.Number, "حد مكتب المدير العام", "GM Office threshold"));

    public static readonly IReadOnlyList<ApprovalProcessDefinition> All =
        [CharterDefinition, PlanningDefinition, MonthlyDefinition, ClosureDefinition, ImpactDefinition, ChangeRequestDefinition];

    public static ApprovalProcessDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}

/// <summary>
/// Projects within the caller's read scope plus — so they can take ownership — projects without a PM whose section is the caller's unit or an
/// ancestor of it (ADR 0070). Every project handler starts here; 404 outside.
/// </summary>
internal sealed class ProjectLoader(PortfolioDbContext db, RequestReader reader, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org)
{
    public async Task<IQueryable<Project>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Projects : db.Projects.AsNoTracking();
        var scoped = await access.ApplyScopeAsync(source, user, PortfolioPermissions.ProjectRead, ProjectScope.Map, ct).ConfigureAwait(false);
        var units = await UnitPathAsync(user, ct).ConfigureAwait(false);
        if (units.Count == 0)
        {
            return scoped;
        }

        var claimable = source.Where(p => p.PmEmployeeId == null && p.Status == ProjectStatuses.Active && p.SubState == SubStates.Ownership
            && units.Contains(p.SectionUnitId ?? p.UnitId ?? Guid.Empty));
        return scoped.Union(claimable);
    }

    public async Task<Project?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, Project project, CancellationToken ct) =>
        access.CanAsync(user, permission, ProjectScope.Map.RefOf(project), ct);

    /// <summary>The caller's unit and its ancestors (the sections whose unclaimed projects they may take).</summary>
    public async Task<IReadOnlyList<Guid>> UnitPathAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.EmployeeIdOf() is not { } employeeId || (await employees.GetAsync(employeeId, ct).ConfigureAwait(false))?.UnitId is not { } unit)
        {
            return [];
        }

        var path = await org.GetPathAsync(unit, cancellationToken: ct).ConfigureAwait(false);
        return path.Select(n => n.Id).Append(unit).Distinct().ToList();
    }

    public async Task<bool> InSectionAsync(ClaimsPrincipal user, Project project, CancellationToken ct) =>
        (await UnitPathAsync(user, ct).ConfigureAwait(false)).Contains(project.SectionUnitId ?? project.UnitId ?? Guid.Empty);

    public async Task<StageTemplate> TemplateAsync(Project project, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        return project.TemplateOr(settings.TemplateFor(project.Classification));
    }

    /// <summary>Snapshots the template on a project created before Phase 9 (first lifecycle action).</summary>
    public async Task<StageTemplate> EnsureTemplateAsync(Project project, CancellationToken ct)
    {
        var template = await TemplateAsync(project, ct).ConfigureAwait(false);
        if (project.Template is null)
        {
            project.UseTemplate(template);
        }

        return template;
    }

    public async Task<GateFacts> FactsAsync(Project project, CancellationToken ct)
    {
        var id = project.Id;
        var milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == id && m.Status != MilestoneStatuses.Cancelled).ToListAsync(ct).ConfigureAwait(false);
        var highLevel = milestones.Where(m => m.HighLevel && !m.IsHeader).ToList();
        var leaves = milestones.Where(m => !m.HighLevel && !m.IsHeader).ToList();
        return new GateFacts(
            project.PmEmployeeId is not null,
            !string.IsNullOrWhiteSpace(project.ScopeOfWork),
            await db.Kpis.CountAsync(k => k.ProjectId == id && k.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Risks.CountAsync(k => k.ProjectId == id && k.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Stakeholders.CountAsync(k => k.ProjectId == id && k.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Members.CountAsync(k => k.ProjectId == id && k.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            highLevel.Count,
            highLevel.Sum(m => m.Weight),
            await db.PlanningItems.CountAsync(k => k.ProjectId == id && k.Status == ItemStatuses.Active && k.Category == PlanningCategories.InScope, ct).ConfigureAwait(false),
            leaves.Count,
            leaves.Sum(m => m.Weight),
            leaves.Count(m => m.PlannedStart is null || m.PlannedEnd is null || m.PlannedEnd < m.PlannedStart),
            leaves.Count(m => m.IsDeliverable && (string.IsNullOrWhiteSpace(m.MainDeliverable) || string.IsNullOrWhiteSpace(m.AcceptanceCriteria))));
    }
}

/// <summary>Progress recalculation (Q-P6, ADR 0071) and header aggregation, on the tracked project and milestones.</summary>
internal sealed class ProjectProgress(PortfolioDbContext db, RequestReader reader)
{
    public async Task RecalculateAsync(Project project, CancellationToken ct)
    {
        var milestones = await db.Milestones.Where(m => m.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var header in milestones.Where(m => m.IsHeader))
        {
            var children = milestones.Where(m => m.ParentId == header.Id && m.IsActive).ToList();
            header.Aggregate(children.Sum(c => c.Weight), children.Sum(c => c.MaxAchieved), children.Min(c => c.PlannedStart), children.Max(c => c.PlannedEnd));
        }

        var leaves = milestones.Where(m => !m.IsHeader && !m.HighLevel)
            .Select(m => new MilestoneProgressInput(m.Weight, m.MaxAchieved, m.PlannedStart, m.PlannedEnd, !m.IsActive)).ToList();
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        project.SetProgress(ProgressCalculator.Performance(leaves), ProgressCalculator.PlannedToDate(leaves, today),
            ProgressCalculator.Status(leaves, today, settings.ProgressTolerance));
    }
}

/// <summary>Sends Portfolio project templates (outbox, in the caller's transaction).</summary>
internal sealed class ProjectNotifier(INotificationService notifications, RequestReader reader, IRoleHolderQuery roles)
{
    public async Task ToEmployeesAsync(
        NotificationTemplateDefinition template, Project project, IEnumerable<Guid?> employeeIds, object? transactionContext, CancellationToken ct, string? text = null,
        string? actor = null, BilingualText? stage = null, string? due = null)
    {
        var ids = employeeIds.Where(e => e is not null).Select(e => e!.Value).Distinct().ToList();
        var users = ids.Count == 0 ? [] : await reader.UsersOfEmployeesAsync(ids, ct).ConfigureAwait(false);
        await ToUsersAsync(template, project, users, transactionContext, ct, text, actor, stage, due).ConfigureAwait(false);
    }

    public async Task ToRoleAsync(NotificationTemplateDefinition template, Project project, string roleKey, object? transactionContext, CancellationToken ct)
    {
        var holders = await roles.GetHoldersAsync(roleKey, ct).ConfigureAwait(false);
        await ToUsersAsync(template, project, holders.UserIds, transactionContext, ct).ConfigureAwait(false);
    }

    public Task ToUsersAsync(
        NotificationTemplateDefinition template, Project project, IReadOnlyCollection<Guid> userIds, object? transactionContext, CancellationToken ct, string? text = null,
        string? actor = null, BilingualText? stage = null, string? due = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (userIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return notifications.SendAsync(new NotificationRequest(
            template.Key,
            userIds.Distinct().Select(NotificationRecipient.User).ToList(),
            new Dictionary<string, string>
            {
                ["projectAr"] = project.NameAr ?? project.NameEn ?? string.Empty,
                ["projectEn"] = project.NameEn ?? project.NameAr ?? string.Empty,
                ["number"] = project.Number,
                ["actor"] = actor ?? string.Empty,
                ["text"] = text ?? string.Empty,
                ["stageAr"] = stage?.Ar ?? string.Empty,
                ["stageEn"] = stage?.En ?? string.Empty,
                ["due"] = due ?? string.Empty,
            },
            $"/portfolio/projects/{project.Id:D}"), transactionContext, ct);
    }

    /// <summary>Stage names for the templates (code → AR / EN).</summary>
    public static BilingualText StageText(string stage) => stage switch
    {
        ProjectStages.Initiation => new("البدء", "Initiation"),
        ProjectStages.Planning => new("التخطيط", "Planning"),
        ProjectStages.Execution => new("التنفيذ", "Execution"),
        ProjectStages.Closure => new("الإغلاق", "Closure"),
        ProjectStages.Impact => new("الأثر", "Impact"),
        ProjectStages.Completed => new("مكتمل", "Completed"),
        _ => new(stage, stage),
    };
}

/// <summary>Project files: readers of the project read; writers edit it (PM / PM Office) or upload the LPO (procurement).</summary>
internal sealed class ProjectFiles(ProjectLoader loader) : IFileOwnerAccess
{
    public string OwnerType => PortfolioPermissions.ProjectResource;

    public string AttachmentPurpose => ProjectFilePurposes.Document.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await loader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await loader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } project
        && project.Status == ProjectStatuses.Active
        && (await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, cancellationToken).ConfigureAwait(false)
            || await loader.CanAsync(user, PortfolioPermissions.ProjectLpo, project, cancellationToken).ConfigureAwait(false));
}

/// <summary>Milestone evidence: read with the project, written by its editors.</summary>
internal sealed class MilestoneFiles(PortfolioDbContext db, ProjectLoader loader) : IFileOwnerAccess
{
    public string OwnerType => PortfolioPermissions.MilestoneResource;

    public string AttachmentPurpose => ProjectFilePurposes.Evidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await ProjectOfAsync(ownerId, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await ProjectOfAsync(ownerId, user, cancellationToken).ConfigureAwait(false) is { } project
        && await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, cancellationToken).ConfigureAwait(false);

    private async Task<Project?> ProjectOfAsync(string ownerId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(ownerId, out var id))
        {
            return null;
        }

        var projectId = await db.Milestones.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return projectId is { } p ? await loader.FindAsync(p, user, ct).ConfigureAwait(false) : null;
    }
}
