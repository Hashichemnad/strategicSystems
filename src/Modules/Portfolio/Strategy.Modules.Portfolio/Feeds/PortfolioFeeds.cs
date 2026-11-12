using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Portfolio.Reporting;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Portfolio.Feeds;

/// <summary>
/// Reportable record types of Portfolio (ADR 0190): projects, their milestones and program requests, read on the same scoped readers as the
/// report builder (non-draft projects). Cost fields need <c>portfolio.project.costs</c> and are masked row by row to the projects whose costs
/// the reader may see (ADR 0098), as in <see cref="Reporting.PortfolioDatasetSource"/>.
/// </summary>
internal sealed class PortfolioFeeds(PortfolioDbContext db, RequestReader reader, IAccessService access) : IReportableRecordSource
{
    public const string ProjectType = Project.EntityType;
    public const string MilestoneType = Milestone.EntityType;
    public const string RequestType = ProgramRequest.EntityType;

    private const string Costs = PortfolioPermissions.ProjectCosts;

    private static readonly RecordTypeQuery<ProgramRequest> Requests = new RecordTypeQuery<ProgramRequest>(r => r.Id, r => r.UpdatedAt)
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
        .Field("projectId", r => r.ProjectId)
        .Deleted(r => r.Status == RequestStatuses.Cancelled);

    public string SystemCode => PortfolioPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(ProjectType, PortfolioPermissions.SystemCode, "Projects", "المشاريع والمبادرات", "Projects and initiatives", PortfolioPermissions.ProjectRead,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("classification", "التصنيف", "Classification", RecordFieldType.Lookup, Choices: PortfolioChoices.Classifications),
            new("methodology", "المنهجية", "Methodology", RecordFieldType.Lookup, Choices: PortfolioChoices.Methodologies),
            new("stage", "المرحلة", "Stage", RecordFieldType.Lookup, Choices: PortfolioChoices.ProjectStages),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: PortfolioChoices.ProjectStatuses),
            new("progressStatus", "حالة التقدم", "Progress status", RecordFieldType.Lookup, Choices: PortfolioChoices.ProgressStatuses),
            new("unit", "الإدارة المالكة", "Owning unit", RecordFieldType.Unit),
            new("section", "القسم", "Section", RecordFieldType.Unit),
            new("owner", "مالك المشروع", "Project owner", RecordFieldType.Employee),
            new("pm", "مدير المشروع", "Project manager", RecordFieldType.Employee),
            new("strategicObjective", "الهدف الاستراتيجي", "Strategic objective", RecordFieldType.Text),
            new("plannedStart", "البداية المخططة", "Planned start", RecordFieldType.Date),
            new("plannedEnd", "النهاية المخططة", "Planned end", RecordFieldType.Date),
            new("performance", "نسبة الإنجاز", "Progress", RecordFieldType.Ratio),
            new("plannedProgress", "الإنجاز المخطط", "Planned progress", RecordFieldType.Ratio),
            new("completedAt", "تاريخ الاكتمال", "Completed on", RecordFieldType.DateTime),
            new("requestId", "طلب البرنامج", "Program request", RecordFieldType.Id),
            new("budget", "الميزانية", "Budget", RecordFieldType.Money, Permission: Costs),
            new("actualCost", "التكلفة الفعلية", "Actual cost", RecordFieldType.Money, Permission: Costs),
        ],
        [
            new("milestones", "المعالم", "Milestones", MilestoneType, RecordFields.Id, "projectId", Many: true),
            new("request", "طلب البرنامج", "Program request", RequestType, "requestId", RecordFields.Id, Many: false),
        ], "/portfolio/projects/{id}"),
        new(MilestoneType, PortfolioPermissions.SystemCode, "Milestones", "معالم المشاريع", "Project milestones", PortfolioPermissions.ProjectRead,
        [
            new("projectId", "المشروع", "Project", RecordFieldType.Id),
            new("parentId", "المعلم الأب", "Parent milestone", RecordFieldType.Id),
            new("code", "الرمز", "Code", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("isHeader", "عنوان مجموعة", "Header", RecordFieldType.Bool),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: PortfolioChoices.MilestoneStatuses),
            new("plannedStart", "البداية المخططة", "Planned start", RecordFieldType.Date),
            new("plannedEnd", "النهاية المخططة", "Planned end", RecordFieldType.Date),
            new("actualStart", "البداية الفعلية", "Actual start", RecordFieldType.Date),
            new("actualEnd", "النهاية الفعلية", "Actual end", RecordFieldType.Date),
            new("weight", "الوزن", "Weight", RecordFieldType.Ratio),
            new("completion", "نسبة الإكمال", "Completion", RecordFieldType.Ratio),
            new("isDeliverable", "مخرج رئيسي", "Deliverable", RecordFieldType.Bool),
            new("cost", "التكلفة", "Cost", RecordFieldType.Money, Permission: Costs),
        ],
        [
            new("project", "المشروع", "Project", ProjectType, "projectId", RecordFields.Id, Many: false),
        ], "/portfolio/milestones/{id}"),
        new(RequestType, PortfolioPermissions.SystemCode, "ProgramRequests", "طلبات البرامج", "Program requests", PortfolioPermissions.RequestRead,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: PortfolioChoices.RequestStatuses),
            new("approvalStep", "خطوة الاعتماد", "Approval step", RecordFieldType.Text),
            new("classification", "التصنيف", "Classification", RecordFieldType.Lookup, Choices: PortfolioChoices.Classifications),
            new("priorityBand", "فئة الأولوية", "Priority band", RecordFieldType.Text),
            new("evaluationScore", "درجة التقييم", "Evaluation score", RecordFieldType.Ratio),
            new("strategicObjective", "الهدف الاستراتيجي", "Strategic objective", RecordFieldType.Text),
            new("unit", "الإدارة الطالبة", "Requesting unit", RecordFieldType.Unit),
            new("requester", "مقدم الطلب", "Requester", RecordFieldType.Employee),
            new("submittedAt", "تاريخ التقديم", "Submitted on", RecordFieldType.DateTime),
            new("expectedStart", "البداية المتوقعة", "Expected start", RecordFieldType.Date),
            new("expectedEnd", "النهاية المتوقعة", "Expected end", RecordFieldType.Date),
            new("projectId", "المشروع", "Project", RecordFieldType.Id),
        ],
        [
            new("project", "المشروع", "Project", ProjectType, "projectId", RecordFields.Id, Many: false),
        ], "/portfolio/requests/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (query.RecordType)
        {
            case ProjectType:
                {
                    var (costQ, contribQ) = await CostVisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = await VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false);
                    var projects = new RecordTypeQuery<Project>(p => p.Id, p => p.UpdatedAt)
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
                        .Field("requestId", p => p.RequestId)
                        .Field("budget", p => costQ.Contains(p.Id) || contribQ.Contains(p.Id) ? p.Budget : null)
                        .Field("actualCost", p => costQ.Contains(p.Id) || contribQ.Contains(p.Id)
                            ? (decimal?)((p.Year1ActualCost ?? 0) + (p.Year2ActualCost ?? 0) + (p.Year3ActualCost ?? 0) + (p.Year4ActualCost ?? 0))
                            : null)
                        .Deleted(p => p.Status == ProjectStatuses.Cancelled);
                    return await projects.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case MilestoneType:
                {
                    var (costQ, contribQ) = await CostVisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    var visible = await VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = db.Milestones.AsNoTracking().Where(m => visible.Any(p => p.Id == m.ProjectId));
                    var milestones = new RecordTypeQuery<Milestone>(m => m.Id, m => m.UpdatedAt)
                        .Field("projectId", m => m.ProjectId)
                        .Field("parentId", m => m.ParentId)
                        .Field("code", m => m.Code)
                        .Field("titleAr", m => m.TitleAr)
                        .Field("titleEn", m => m.TitleEn)
                        .Field("isHeader", m => m.IsHeader)
                        .Field("status", m => m.Status)
                        .Field("plannedStart", m => m.PlannedStart)
                        .Field("plannedEnd", m => m.PlannedEnd)
                        .Field("actualStart", m => m.ActualStart)
                        .Field("actualEnd", m => m.ActualEnd)
                        .Field("weight", m => m.Weight)
                        .Field("completion", m => m.Weight <= 0 ? 0 : m.MaxAchieved / m.Weight)
                        .Field("isDeliverable", m => m.IsDeliverable)
                        .Field("cost", m => costQ.Contains(m.ProjectId) || contribQ.Contains(m.ProjectId) ? m.Cost : null)
                        .Deleted(m => m.Status == MilestoneStatuses.Cancelled);
                    return await milestones.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case RequestType:
                {
                    var source = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
                    return await Requests.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }
    }

    /// <summary>The reader's projects, drafts excluded (as the report builder).</summary>
    private async Task<IQueryable<Project>> VisibleProjectsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        (await reader.VisibleProjectsAsync(user, ct).ConfigureAwait(false)).Where(p => p.Status != ProjectStatuses.Draft);

    /// <summary>The ids of projects whose costs the reader may see (CostVisibility's rule, ADR 0098), as subqueries for the masks.</summary>
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
