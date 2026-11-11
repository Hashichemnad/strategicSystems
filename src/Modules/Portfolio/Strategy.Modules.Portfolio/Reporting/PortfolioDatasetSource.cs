using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>A milestone with its project's identity, for the report builder (row id = the project: the SPA opens milestones on the project page).</summary>
internal sealed class MilestoneReportRow
{
    public Guid ProjectId { get; init; }

    public Guid Id { get; init; }

    public string ProjectNumber { get; init; } = string.Empty;

    public string? ProjectNameAr { get; init; }

    public string? ProjectNameEn { get; init; }

    public Guid? UnitId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateOnly? PlannedStart { get; init; }

    public DateOnly? PlannedEnd { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? ActualEnd { get; init; }

    public decimal Weight { get; init; }

    public decimal Completion { get; init; }

    public bool IsDeliverable { get; init; }

    public decimal? Cost { get; init; }
}

/// <summary>
/// Report-builder objects of Portfolio (ADR 0144): projects, milestones and program requests, each run on the reader's scoped query. Cost
/// fields need <c>portfolio.project.costs</c> to be offered and are, in addition, masked row by row to the projects whose costs the runner may
/// see (ADR 0098: the cost scope can be narrower than the read scope, and team members see costs only with the setting on).
/// </summary>
internal sealed class PortfolioDatasetSource(PortfolioDbContext db, RequestReader reader, IAccessService access) : IReportingDatasetSource
{
    public const string ProjectsDataset = "portfolio.projects";
    public const string MilestonesDataset = "portfolio.milestones";
    public const string RequestsDataset = "portfolio.requests";
    public const string ChangeRequestsDataset = "portfolio.change-requests";
    public const string ProjectRisksDataset = "portfolio.project-risks";
    public const string RaidDataset = "portfolio.raid";
    public const string StakeholdersDataset = "portfolio.stakeholders";
    public const string MeetingsDataset = "portfolio.meetings";
    public const string LessonsDataset = "portfolio.lessons";
    public const string ProjectKpisDataset = "portfolio.project-kpis";

    private const string ProjectLink = "/portfolio/projects/{id}";

    private const string Costs = PortfolioPermissions.ProjectCosts;

    public string SystemCode => PortfolioPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(ProjectsDataset, PortfolioPermissions.SystemCode, "المشاريع والمبادرات", "Projects and initiatives", PortfolioPermissions.ProjectRead,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("classification", "التصنيف", "Classification", DatasetFieldType.Lookup, Groupable: true),
            new("methodology", "المنهجية", "Methodology", DatasetFieldType.Lookup, Groupable: true),
            new("stage", "المرحلة", "Stage", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("progressStatus", "حالة التقدم", "Progress status", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الإدارة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            new("section", "القسم", "Section", DatasetFieldType.Unit, Groupable: true),
            new("owner", "مالك المشروع", "Project owner", DatasetFieldType.Employee, Groupable: true),
            new("pm", "مدير المشروع", "Project manager", DatasetFieldType.Employee, Groupable: true),
            new("strategicObjective", "الهدف الاستراتيجي", "Strategic objective", DatasetFieldType.Text, Groupable: true),
            new("plannedStart", "البداية المخططة", "Planned start", DatasetFieldType.Date),
            new("plannedEnd", "النهاية المخططة", "Planned end", DatasetFieldType.Date),
            new("performance", "نسبة الإنجاز", "Progress", DatasetFieldType.Ratio),
            new("plannedProgress", "الإنجاز المخطط", "Planned progress", DatasetFieldType.Ratio),
            new("completedAt", "تاريخ الاكتمال", "Completed on", DatasetFieldType.DateTime),
            new("budget", "الميزانية", "Budget", DatasetFieldType.Money, Permission: Costs),
            new("actualCost", "التكلفة الفعلية", "Actual cost", DatasetFieldType.Money, Permission: Costs),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("requestId", "طلب البرنامج", "Program request", DatasetFieldType.Reference, Sortable: false),
            new("teamId", "فريق المشروع", "Project team", DatasetFieldType.Reference, Sortable: false),
            new("description", "الوصف", "Description", DatasetFieldType.RichText, Filterable: false, Sortable: false),
            new("scopeOfWork", "نطاق العمل", "Scope of work", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("resourcesHuman", "الموارد البشرية", "Human resources", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("resourcesTechnical", "الموارد التقنية", "Technical resources", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("summaryOfAchievement", "ملخص الإنجاز", "Summary of achievement", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("mainDeliverables", "المخرجات الرئيسية", "Main deliverables", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("communitySatisfaction", "رضا المجتمع", "Community satisfaction", DatasetFieldType.Number),
            new("employeeSatisfaction", "رضا الموظفين", "Employee satisfaction", DatasetFieldType.Number),
            new("impactNotes", "ملاحظات الأثر", "Impact notes", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("costPerformance", "أداء التكلفة", "Cost performance", DatasetFieldType.Ratio, Permission: Costs),
        ], ProjectLink) { KeyField = "id" },
        new(MilestonesDataset, PortfolioPermissions.SystemCode, "معالم المشاريع", "Project milestones", PortfolioPermissions.ProjectRead,
        [
            new("projectNumber", "رقم المشروع", "Project number", DatasetFieldType.Text, Groupable: true),
            new("projectNameAr", "المشروع (عربي)", "Project (Arabic)", DatasetFieldType.Text),
            new("projectNameEn", "المشروع (إنجليزي)", "Project (English)", DatasetFieldType.Text),
            new("unit", "الإدارة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            new("code", "الرمز", "Code", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("plannedStart", "البداية المخططة", "Planned start", DatasetFieldType.Date),
            new("plannedEnd", "النهاية المخططة", "Planned end", DatasetFieldType.Date),
            new("actualStart", "البداية الفعلية", "Actual start", DatasetFieldType.Date),
            new("actualEnd", "النهاية الفعلية", "Actual end", DatasetFieldType.Date),
            new("weight", "الوزن", "Weight", DatasetFieldType.Ratio),
            new("completion", "نسبة الإكمال", "Completion", DatasetFieldType.Ratio),
            new("isDeliverable", "مخرج رئيسي", "Deliverable", DatasetFieldType.Bool, Groupable: true),
            new("cost", "التكلفة", "Cost", DatasetFieldType.Money, Permission: Costs),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("projectId", "المشروع", "Project", DatasetFieldType.Reference, Sortable: false),
        ], "/portfolio/projects/{id}?tab=milestones") { KeyField = "id" },
        new(RequestsDataset, PortfolioPermissions.SystemCode, "طلبات البرامج", "Program requests", PortfolioPermissions.RequestRead,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("approvalStep", "خطوة الاعتماد", "Approval step", DatasetFieldType.Text, Groupable: true),
            new("classification", "التصنيف", "Classification", DatasetFieldType.Lookup, Groupable: true),
            new("priorityBand", "فئة الأولوية", "Priority band", DatasetFieldType.Lookup, Groupable: true),
            new("evaluationScore", "درجة التقييم", "Evaluation score", DatasetFieldType.Ratio),
            new("strategicObjective", "الهدف الاستراتيجي", "Strategic objective", DatasetFieldType.Text, Groupable: true),
            new("unit", "الإدارة الطالبة", "Requesting unit", DatasetFieldType.Unit, Groupable: true),
            new("requester", "مقدم الطلب", "Requester", DatasetFieldType.Employee, Groupable: true),
            new("submittedAt", "تاريخ التقديم", "Submitted on", DatasetFieldType.DateTime),
            new("expectedStart", "البداية المتوقعة", "Expected start", DatasetFieldType.Date),
            new("expectedEnd", "النهاية المتوقعة", "Expected end", DatasetFieldType.Date),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/portfolio/requests/{id}") { KeyField = "id" },
        Child(ChangeRequestsDataset, "طلبات التغيير", "Change requests", "change-requests",
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("reason", "السبب", "Reason", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("costAffected", "يؤثر على التكلفة", "Affects cost", DatasetFieldType.Bool, Groupable: true),
            new("proposedBudget", "الميزانية المقترحة", "Proposed budget", DatasetFieldType.Money, Permission: Costs),
            new("submittedAt", "تاريخ التقديم", "Submitted on", DatasetFieldType.DateTime),
            new("decidedAt", "تاريخ القرار", "Decided on", DatasetFieldType.DateTime),
            new("implementedAt", "تاريخ التنفيذ", "Implemented on", DatasetFieldType.DateTime),
        ]),
        Child(ProjectRisksDataset, "مخاطر المشاريع", "Project risks", "risks",
        [
            new("title", "العنوان", "Title", DatasetFieldType.Text),
            new("category", "الفئة", "Category", DatasetFieldType.Lookup, Groupable: true),
            new("probability", "الاحتمالية", "Likelihood", DatasetFieldType.Number, Groupable: true),
            new("impact", "الأثر", "Impact", DatasetFieldType.Number, Groupable: true),
            new("inherentScore", "الدرجة الكامنة", "Inherent score", DatasetFieldType.Number),
            new("inherentBand", "التصنيف الكامن", "Inherent rating", DatasetFieldType.Lookup, Groupable: true),
            new("residualScore", "الدرجة المتبقية", "Residual score", DatasetFieldType.Number),
            new("residualBand", "التصنيف المتبقي", "Residual rating", DatasetFieldType.Lookup, Groupable: true),
            new("response", "الاستجابة", "Response", DatasetFieldType.Lookup, Groupable: true),
            new("owner", "مالك الخطر", "Risk owner", DatasetFieldType.Employee, Groupable: true),
            new("reviewDate", "تاريخ المراجعة", "Review date", DatasetFieldType.Date),
        ]),
        Child(RaidDataset, "القضايا والقرارات والافتراضات والاعتماديات", "Issues, decisions, assumptions and dependencies", "raid",
        [
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true),
            new("code", "الرمز", "Code", DatasetFieldType.Text),
            new("title", "العنوان", "Title", DatasetFieldType.Text),
            new("priority", "الأولوية", "Priority", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
            new("dueDate", "تاريخ الاستحقاق", "Due date", DatasetFieldType.Date),
            new("decidedOn", "تاريخ القرار", "Decided on", DatasetFieldType.Date),
            new("decidedBy", "صاحب القرار", "Decided by", DatasetFieldType.Text),
            new("resolution", "الحل / القرار", "Resolution / decision", DatasetFieldType.Text, Filterable: false, Sortable: false),
        ]),
        Child(StakeholdersDataset, "أصحاب المصلحة", "Stakeholders", "stakeholders",
        [
            new("name", "الاسم", "Name", DatasetFieldType.Text),
            new("classification", "التصنيف", "Classification", DatasetFieldType.Lookup, Groupable: true),
            new("interest", "مستوى الاهتمام", "Interest", DatasetFieldType.Lookup, Groupable: true),
            new("influence", "مستوى التأثير", "Influence", DatasetFieldType.Lookup, Groupable: true),
            new("communication", "طريقة التواصل", "Communication", DatasetFieldType.Text),
            new("frequency", "دورية التواصل", "Frequency", DatasetFieldType.Text),
        ]),
        Child(MeetingsDataset, "اجتماعات المشاريع", "Project meetings", "meetings",
        [
            new("meetingAt", "موعد الاجتماع", "Meeting time", DatasetFieldType.DateTime),
            new("title", "العنوان", "Title", DatasetFieldType.Text),
            new("notes", "المحضر", "Minutes", DatasetFieldType.RichText, Filterable: false, Sortable: false),
        ]),
        Child(LessonsDataset, "الدروس المستفادة", "Lessons learned", "lessons",
        [
            new("stage", "المرحلة", "Stage", DatasetFieldType.Lookup, Groupable: true),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true),
            new("text", "الدرس", "Lesson", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("recommendation", "التوصية", "Recommendation", DatasetFieldType.Text, Filterable: false, Sortable: false),
        ]),
        Child(ProjectKpisDataset, "مؤشرات المشاريع", "Project KPIs", "kpis",
        [
            new("nature", "طبيعة المؤشر", "Nature", DatasetFieldType.Lookup, Groupable: true),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("target", "المستهدف", "Target", DatasetFieldType.Number),
            new("targetYear", "سنة المستهدف", "Target year", DatasetFieldType.Number, Groupable: true),
            new("currentResult", "النتيجة الحالية", "Current result", DatasetFieldType.Number),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("kpiCardId", "بطاقة المؤشر", "KPI card", DatasetFieldType.Reference, Sortable: false),
        ], key: "id"),
    ];

    /// <summary>Relationships (ADR 0180), incl. Portfolio's references into Teams, Tasks, KPI and the approval history.</summary>
    public IReadOnlyList<DatasetRelationship> Relationships { get; } =
    [
        new("portfolio.project-milestones", ProjectsDataset, "id", MilestonesDataset, "projectId", "معالم المشروع", "Milestones"),
        new("portfolio.project-change-requests", ProjectsDataset, "id", ChangeRequestsDataset, "projectId", "طلبات التغيير", "Change requests"),
        new("portfolio.project-project-risks", ProjectsDataset, "id", ProjectRisksDataset, "projectId", "مخاطر المشروع", "Risks"),
        new("portfolio.project-raid", ProjectsDataset, "id", RaidDataset, "projectId", "القضايا والقرارات", "Issues and decisions"),
        new("portfolio.project-stakeholders", ProjectsDataset, "id", StakeholdersDataset, "projectId", "أصحاب المصلحة", "Stakeholders"),
        new("portfolio.project-meetings", ProjectsDataset, "id", MeetingsDataset, "projectId", "الاجتماعات", "Meetings"),
        new("portfolio.project-lessons", ProjectsDataset, "id", LessonsDataset, "projectId", "الدروس المستفادة", "Lessons learned"),
        new("portfolio.project-kpis", ProjectsDataset, "id", ProjectKpisDataset, "projectId", "مؤشرات المشروع", "KPIs"),
        new("portfolio.request-projects", RequestsDataset, "id", ProjectsDataset, "requestId", "المشاريع", "Projects"),
        new("portfolio.project-team-members", ProjectsDataset, "teamId", "teams.members", "teamId", "أعضاء فريق المشروع", "Project team members"),
        new("portfolio.project-tasks", ProjectsDataset, "id", "tasks.tasks", "linkedId", "مهام المشروع", "Project tasks"),
        new("portfolio.milestone-tasks", MilestonesDataset, "id", "tasks.tasks", "linkedId", "مهام المعلم", "Milestone tasks"),
        new("portfolio.project-kpi-cards", ProjectKpisDataset, "kpiCardId", "kpi.cards", "id", "بطاقة المؤشر", "KPI card"),
        new("portfolio.project-kpi-results", ProjectKpisDataset, "kpiCardId", "kpi.results", "cardId", "نتائج المؤشر", "KPI results"),
        new("portfolio.request-approvals", RequestsDataset, "id", "workflow.approvals", "resourceId", "سجل الاعتماد", "Approval history"),
        new("portfolio.project-approvals", ProjectsDataset, "id", "workflow.approvals", "resourceId", "سجل الاعتماد", "Approval history"),
    ];

    /// <summary>A project child dataset: the project's number / names / unit, then <paramref name="fields"/>, then the project key.</summary>
    private static DatasetDefinition Child(string code, string labelAr, string labelEn, string tab, IReadOnlyList<DatasetField> fields, string? key = null) =>
        new(code, PortfolioPermissions.SystemCode, labelAr, labelEn, PortfolioPermissions.ProjectRead,
        [
            new("projectNumber", "رقم المشروع", "Project number", DatasetFieldType.Text, Groupable: true),
            new("projectNameAr", "المشروع (عربي)", "Project (Arabic)", DatasetFieldType.Text),
            new("projectNameEn", "المشروع (إنجليزي)", "Project (English)", DatasetFieldType.Text),
            new("unit", "الإدارة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            .. fields,
            new("projectId", "المشروع", "Project", DatasetFieldType.Reference, Sortable: false),
        ], ProjectLink + "?tab=" + tab) { KeyField = key };

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (query.Dataset)
        {
            case ProjectsDataset:
                {
                    var (costQ, contribQ) = await CostVisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = (await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false)).Where(p => p.Status != ProjectStatuses.Draft);
                    var dataset = new DatasetQuery<Project>(p => p.Id)
                        .Field("number", p => p.Number)
                        .Field("nameAr", p => p.NameAr)
                        .Field("nameEn", p => p.NameEn)
                        .Field("classification", p => p.Classification)
                        .Field("methodology", p => p.Methodology)
                        .Field("stage", p => p.Stage)
                        .Field("status", p => p.Status)
                        .Field("progressStatus", p => p.ProgressStatus)
                        .Field("unit", p => p.UnitId)
                        .Field("section", p => p.SectionUnitId)
                        .Field("owner", p => p.OwnerEmployeeId)
                        .Field("pm", p => p.PmEmployeeId)
                        .Field("strategicObjective", p => p.StrategicObjectiveCode)
                        .Field("plannedStart", p => p.PlannedStart)
                        .Field("plannedEnd", p => p.PlannedEnd)
                        .Field("performance", p => p.Performance)
                        .Field("plannedProgress", p => p.PlannedProgress)
                        .Field("completedAt", p => p.CompletedAt)
                        .Field("id", p => p.Id)
                        .Field("requestId", p => p.RequestId)
                        .Field("teamId", p => p.TeamId)
                        .Field("description", p => p.Description)
                        .Field("scopeOfWork", p => p.ScopeOfWork)
                        .Field("resourcesHuman", p => p.ResourcesHuman)
                        .Field("resourcesTechnical", p => p.ResourcesTechnical)
                        .Field("summaryOfAchievement", p => p.SummaryOfAchievement)
                        .Field("mainDeliverables", p => p.MainDeliverables)
                        .Field("communitySatisfaction", p => p.CommunitySatisfaction)
                        .Field("employeeSatisfaction", p => p.EmployeeSatisfaction)
                        .Field("impactNotes", p => p.ImpactNotes)
                        .Field("costPerformance", p => costQ.Contains(p.Id) || contribQ.Contains(p.Id) ? p.CostPerformance : null)
                        .Field("budget", p => costQ.Contains(p.Id) || contribQ.Contains(p.Id) ? p.Budget : null)
                        .Field("actualCost", p => costQ.Contains(p.Id) || contribQ.Contains(p.Id)
                            ? (decimal?)((p.Year1ActualCost ?? 0) + (p.Year2ActualCost ?? 0) + (p.Year3ActualCost ?? 0) + (p.Year4ActualCost ?? 0))
                            : null);
                    return await dataset.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case MilestonesDataset:
                {
                    var (costQ, contribQ) = await CostVisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var projects = (await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false)).Where(p => p.Status != ProjectStatuses.Draft);
                    var source = from m in db.Milestones.AsNoTracking()
                                 join p in projects on m.ProjectId equals p.Id
                                 where !m.IsHeader && m.Status != MilestoneStatuses.Cancelled
                                 select new MilestoneReportRow
                                 {
                                     ProjectId = p.Id,
                                     Id = m.Id,
                                     ProjectNumber = p.Number,
                                     ProjectNameAr = p.NameAr,
                                     ProjectNameEn = p.NameEn,
                                     UnitId = p.UnitId,
                                     Code = m.Code,
                                     TitleAr = m.TitleAr,
                                     TitleEn = m.TitleEn,
                                     Status = m.Status,
                                     PlannedStart = m.PlannedStart,
                                     PlannedEnd = m.PlannedEnd,
                                     ActualStart = m.ActualStart,
                                     ActualEnd = m.ActualEnd,
                                     Weight = m.Weight,
                                     Completion = m.Weight <= 0 ? 0 : m.MaxAchieved / m.Weight,
                                     IsDeliverable = m.IsDeliverable,
                                     Cost = costQ.Contains(p.Id) || contribQ.Contains(p.Id) ? m.Cost : null,
                                 };
                    var dataset = new DatasetQuery<MilestoneReportRow>(m => m.ProjectId)
                        .Field("projectNumber", m => m.ProjectNumber)
                        .Field("projectNameAr", m => m.ProjectNameAr)
                        .Field("projectNameEn", m => m.ProjectNameEn)
                        .Field("unit", m => m.UnitId)
                        .Field("code", m => m.Code)
                        .Field("titleAr", m => m.TitleAr)
                        .Field("titleEn", m => m.TitleEn)
                        .Field("status", m => m.Status)
                        .Field("plannedStart", m => m.PlannedStart)
                        .Field("plannedEnd", m => m.PlannedEnd)
                        .Field("actualStart", m => m.ActualStart)
                        .Field("actualEnd", m => m.ActualEnd)
                        .Field("weight", m => m.Weight)
                        .Field("completion", m => m.Completion)
                        .Field("isDeliverable", m => m.IsDeliverable)
                        .Field("cost", m => m.Cost)
                        .Field("id", m => m.Id)
                        .Field("projectId", m => m.ProjectId);

                    // Several milestones share a project id: page in a stable order unless the runner sorts.
                    var stable = query.Sort.Count > 0 || query.GroupBy.Count > 0
                        ? query
                        : query with { Sort = [new ReportSort("projectNumber", false), new ReportSort("code", false)] };
                    return await dataset.RunAsync(source, stable, cancellationToken).ConfigureAwait(false);
                }

            case RequestsDataset:
                {
                    var source = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var dataset = new DatasetQuery<ProgramRequest>(r => r.Id)
                        .Field("number", r => r.Number)
                        .Field("nameAr", r => r.NameAr)
                        .Field("nameEn", r => r.NameEn)
                        .Field("status", r => r.Status)
                        .Field("approvalStep", r => r.ApprovalStepCode)
                        .Field("classification", r => r.ClassificationOverride ?? r.Classification)
                        .Field("priorityBand", r => r.PriorityBand)
                        .Field("evaluationScore", r => r.EvaluationScore)
                        .Field("strategicObjective", r => r.StrategicObjectiveCode)
                        .Field("unit", r => r.UnitId)
                        .Field("requester", r => r.RequesterEmployeeId)
                        .Field("submittedAt", r => r.SubmittedAt)
                        .Field("expectedStart", r => r.ExpectedStartDate)
                        .Field("expectedEnd", r => r.ExpectedEndDate)
                        .Field("id", r => r.Id);
                    return await dataset.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case ChangeRequestsDataset:
                {
                    var (costQ, contribQ) = await CostVisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var projects = await ProjectsAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = from c in db.ChangeRequests.AsNoTracking()
                                 join p in projects on c.ProjectId equals p.Id
                                 select new ProjectChildRow<ChangeRequest> { Project = p, Item = c, Costs = costQ.Contains(p.Id) || contribQ.Contains(p.Id) };
                    var dataset = ChildQuery<ChangeRequest>()
                        .Field("number", r => r.Item.Number)
                        .Field("type", r => r.Item.Type)
                        .Field("status", r => r.Item.Status)
                        .Field("reason", r => r.Item.Reason)
                        .Field("costAffected", r => r.Item.CostAffected)
                        .Field("proposedBudget", r => r.Costs ? r.Item.ProposedBudget : null)
                        .Field("submittedAt", r => r.Item.SubmittedAt)
                        .Field("decidedAt", r => r.Item.DecidedAt)
                        .Field("implementedAt", r => r.Item.ImplementedAt);
                    return await dataset.RunAsync(source, Stable(query, "number"), cancellationToken).ConfigureAwait(false);
                }

            case ProjectRisksDataset:
                {
                    var source = ChildSource(db.Risks.AsNoTracking().Where(r => r.Status == ItemStatuses.Active), await ProjectsAsync(user, cancellationToken).ConfigureAwait(false));
                    var dataset = ChildQuery<ProjectRisk>()
                        .Field("title", r => r.Item.Title)
                        .Field("category", r => r.Item.CategoryCode)
                        .Field("probability", r => r.Item.Probability)
                        .Field("impact", r => r.Item.Impact)
                        .Field("inherentScore", r => r.Item.InherentScore)
                        .Field("inherentBand", r => r.Item.InherentBand)
                        .Field("residualScore", r => r.Item.ResidualScore)
                        .Field("residualBand", r => r.Item.ResidualBand)
                        .Field("response", r => r.Item.Response)
                        .Field("owner", r => r.Item.OwnerEmployeeId)
                        .Field("reviewDate", r => r.Item.ReviewDate);
                    return await dataset.RunAsync(source, Stable(query, "title"), cancellationToken).ConfigureAwait(false);
                }

            case RaidDataset:
                {
                    var projects = await ProjectsAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = from i in db.RaidItems.AsNoTracking()
                                 join p in projects on i.ProjectId equals p.Id
                                 select new ProjectChildRow<RaidItem> { Project = p, Item = i };
                    var dataset = ChildQuery<RaidItem>()
                        .Field("type", r => r.Item.Type)
                        .Field("code", r => r.Item.Code)
                        .Field("title", r => r.Item.Title)
                        .Field("priority", r => r.Item.Priority)
                        .Field("status", r => r.Item.Status)
                        .Field("owner", r => r.Item.OwnerEmployeeId)
                        .Field("dueDate", r => r.Item.DueDate)
                        .Field("decidedOn", r => r.Item.DecidedOn)
                        .Field("decidedBy", r => r.Item.DecidedBy)
                        .Field("resolution", r => r.Item.Resolution);
                    return await dataset.RunAsync(source, Stable(query, "code"), cancellationToken).ConfigureAwait(false);
                }

            case StakeholdersDataset:
                {
                    var source = ChildSource(db.Stakeholders.AsNoTracking().Where(r => r.Status == ItemStatuses.Active), await ProjectsAsync(user, cancellationToken).ConfigureAwait(false));
                    var dataset = ChildQuery<Stakeholder>()
                        .Field("name", r => r.Item.Name)
                        .Field("classification", r => r.Item.ClassificationCode)
                        .Field("interest", r => r.Item.InterestLevel)
                        .Field("influence", r => r.Item.ImpactLevel)
                        .Field("communication", r => r.Item.CommunicationMethod)
                        .Field("frequency", r => r.Item.CommunicationFrequency);
                    return await dataset.RunAsync(source, Stable(query, "name"), cancellationToken).ConfigureAwait(false);
                }

            case MeetingsDataset:
                {
                    var source = ChildSource(db.Meetings.AsNoTracking().Where(r => r.Status == ItemStatuses.Active), await ProjectsAsync(user, cancellationToken).ConfigureAwait(false));
                    var dataset = ChildQuery<Meeting>()
                        .Field("meetingAt", r => r.Item.MeetingAt)
                        .Field("title", r => r.Item.Title)
                        .Field("notes", r => r.Item.Notes);
                    return await dataset.RunAsync(source, Stable(query, "meetingAt"), cancellationToken).ConfigureAwait(false);
                }

            case LessonsDataset:
                {
                    var source = ChildSource(db.Lessons.AsNoTracking().Where(r => r.Status == ItemStatuses.Active), await ProjectsAsync(user, cancellationToken).ConfigureAwait(false));
                    var dataset = ChildQuery<Lesson>()
                        .Field("stage", r => r.Item.Stage)
                        .Field("type", r => r.Item.TypeCode)
                        .Field("text", r => r.Item.Text)
                        .Field("recommendation", r => r.Item.Recommendation);
                    return await dataset.RunAsync(source, Stable(query, "stage"), cancellationToken).ConfigureAwait(false);
                }

            case ProjectKpisDataset:
                {
                    var source = ChildSource(db.Kpis.AsNoTracking().Where(r => r.Status == ItemStatuses.Active), await ProjectsAsync(user, cancellationToken).ConfigureAwait(false));
                    var dataset = ChildQuery<ProjectKpi>()
                        .Field("nature", r => r.Item.Nature)
                        .Field("nameAr", r => r.Item.NameAr)
                        .Field("nameEn", r => r.Item.NameEn)
                        .Field("target", r => r.Item.Target)
                        .Field("targetYear", r => r.Item.TargetYear)
                        .Field("currentResult", r => r.Item.CurrentResult)
                        .Field("id", r => r.Item.Id)
                        .Field("kpiCardId", r => r.Item.KpiCardId);
                    return await dataset.RunAsync(source, Stable(query, "nameEn"), cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }

    private async Task<IQueryable<Project>> ProjectsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        (await reader.VisibleProjectsAsync(user, ct).ConfigureAwait(false)).Where(p => p.Status != ProjectStatuses.Draft);

    private static IQueryable<ProjectChildRow<T>> ChildSource<T>(IQueryable<T> items, IQueryable<Project> projects)
        where T : ProjectChild =>
        from i in items
        join p in projects on i.ProjectId equals p.Id
        select new ProjectChildRow<T> { Project = p, Item = i };

    /// <summary>The shared project columns of a child dataset; the row id is the project (the SPA opens children on the project page).</summary>
    private static DatasetQuery<ProjectChildRow<T>> ChildQuery<T>()
        where T : class =>
        new DatasetQuery<ProjectChildRow<T>>(r => r.Project.Id)
            .Field("projectNumber", r => r.Project.Number)
            .Field("projectNameAr", r => r.Project.NameAr)
            .Field("projectNameEn", r => r.Project.NameEn)
            .Field("unit", r => r.Project.UnitId)
            .Field("projectId", r => r.Project.Id);

    /// <summary>Several children share a project id: page in a stable order unless the runner sorts.</summary>
    private static ReportQuery Stable(ReportQuery query, string field) =>
        query.Sort.Count > 0 || query.GroupBy.Count > 0 ? query : query with { Sort = [new ReportSort("projectNumber", false), new ReportSort(field, false)] };

    /// <summary>The ids of projects whose costs the runner may see (CostVisibility's rule, ADR 0098), as subqueries for the masks.</summary>
    private async Task<(IQueryable<Guid> Costs, IQueryable<Guid> Members)> CostVisibleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var costs = (await access.ApplyScopeAsync(db.Projects.AsNoTracking(), user, PortfolioPermissions.ProjectCosts, ProjectScope.Map, ct).ConfigureAwait(false))
            .Select(p => p.Id);
        var members = (await reader.SettingsAsync(ct).ConfigureAwait(false)).TeamMembersSeeCosts
            ? (await access.ApplyScopeAsync(db.Projects.AsNoTracking(), user, PortfolioPermissions.ProjectContribute, ProjectScope.Map, ct).ConfigureAwait(false))
                .Select(p => p.Id)
            : db.Projects.AsNoTracking().Where(p => false).Select(p => p.Id);
        return (costs, members);
    }
}

/// <summary>A project child with its (visible) project, for the child datasets.</summary>
internal sealed class ProjectChildRow<T>
    where T : class
{
    public Project Project { get; init; } = null!;

    public T Item { get; init; } = null!;

    public bool Costs { get; init; }
}
