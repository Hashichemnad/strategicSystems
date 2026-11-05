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
        ], "/portfolio/projects/{id}"),
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
        ], "/portfolio/projects/{id}?tab=milestones"),
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
        ], "/portfolio/requests/{id}"),
    ];

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
                        .Field("cost", m => m.Cost);

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
                        .Field("expectedEnd", r => r.ExpectedEndDate);
                    return await dataset.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }

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
