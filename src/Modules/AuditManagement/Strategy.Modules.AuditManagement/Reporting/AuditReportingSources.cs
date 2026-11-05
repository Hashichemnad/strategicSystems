using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Reporting;

/// <summary>Dashboard lists: late findings (highest priority first) and overdue recommendations, live in the Audit reader's scope.</summary>
internal sealed class AuditListSource(AuditMgmtDbContext db, AuditReader reader, ILookupService lookups) : IReportingListSource
{
    public const string LateFindings = "auditmgmt.late-findings";
    public const string OverdueRecommendations = "auditmgmt.overdue-recommendations";

    private static readonly string[] FinalRecommendationStatuses = [.. RecommendationStatuses.Final];

    public string SystemCode => AuditPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(LateFindings, AuditPermissions.SystemCode, "ملاحظات التدقيق المتأخرة", "Late audit findings", AuditPermissions.Read),
        new(OverdueRecommendations, AuditPermissions.SystemCode, "التوصيات المتأخرة", "Overdue recommendations", AuditPermissions.Read),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var take = Math.Clamp(query.Take, 1, 200);
        var findings = await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false);
        if (query.UnitIds is { Count: > 0 } units)
        {
            findings = findings.Where(f => units.Contains(f.ResponsibleUnitId));
        }

        switch (code)
        {
            case LateFindings:
                {
                    // Priority order is the lookup order (configurable): rank the candidates in memory on their keys only, then load the page.
                    var priorities = (await lookups.GetValuesAsync(AuditPermissions.PriorityLookup, true, cancellationToken).ConfigureAwait(false)).Select(v => v.Code).ToList();
                    var keys = await findings.Where(f => f.Status == AuditFindingStatuses.Late).Select(f => new { f.Id, f.Priority, f.FindingDate })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var ids = keys.OrderByDescending(k => priorities.IndexOf(k.Priority)).ThenBy(k => k.FindingDate).Take(take).Select(k => k.Id).ToList();
                    var rows = await db.Findings.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken).ConfigureAwait(false);
                    return ids.Select(id => rows[id]).Select(f => new ReportingListItem(Finding.EntityType, f.Id, f.Number, f.TitleAr ?? f.TitleEn, f.TitleEn ?? f.TitleAr, f.Status,
                        "danger", Math.Max(0, query.Today.DayNumber - (f.ExpectedClosingOn ?? f.FindingDate).DayNumber), f.ExpectedClosingOn ?? f.FindingDate,
                        f.ResponsibleUnitId, AuditService.Link(f))).ToList();
                }

            case OverdueRecommendations:
                {
                    var today = query.Today;
                    var rows = await db.Recommendations.AsNoTracking()
                        .Where(r => r.Status == RecommendationStatuses.Late || (!FinalRecommendationStatuses.Contains(r.Status) && (r.RevisedDueOn ?? r.DueOn) < today))
                        .Join(findings, r => r.FindingId, f => f.Id, (r, f) => new { R = r, f.Number, f.ResponsibleUnitId })
                        .OrderBy(x => x.R.RevisedDueOn ?? x.R.DueOn).ThenBy(x => x.R.Number).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
                    return rows.Select(x => new ReportingListItem(Recommendation.EntityType, x.R.Id, x.R.Number, AuditNotifier.Short(x.R.ActionAr ?? x.R.ActionEn),
                        AuditNotifier.Short(x.R.ActionEn ?? x.R.ActionAr), x.R.Status, x.R.Status == RecommendationStatuses.Late ? "danger" : "warning",
                        Math.Max(0, today.DayNumber - x.R.EffectiveDue.DayNumber), x.R.EffectiveDue, x.ResponsibleUnitId,
                        $"/audit-management/recommendations/{x.R.Id:D}", x.Number, x.Number)).ToList();
                }

            default:
                return [];
        }
    }
}

/// <summary>A finding row for the report builder (the readable finding with its report).</summary>
internal sealed class AuditFindingRow
{
    public Guid Id { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Priority { get; init; } = string.Empty;

    public string TypeCode { get; init; } = string.Empty;

    public string ReportNumber { get; init; } = string.Empty;

    public string ReportTypeCode { get; init; } = string.Empty;

    public string ReportStatus { get; init; } = string.Empty;

    public DateOnly FindingDate { get; init; }

    public int Year { get; init; }

    public Guid ResponsibleUnitId { get; init; }

    public Guid DepartmentUnitId { get; init; }

    public Guid? AuditorEmployeeId { get; init; }

    public DateOnly? ExpectedClosingOn { get; init; }

    public DateOnly? ActualClosedOn { get; init; }

    public decimal Completion { get; init; }

    public bool Repeat { get; init; }

    public string? CapRemarkNumber { get; init; }
}

/// <summary>A recommendation row (with its finding).</summary>
internal sealed class AuditRecommendationRow
{
    public Guid Id { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? ActionAr { get; init; }

    public string? ActionEn { get; init; }

    public string FindingNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? ManagementResponseCode { get; init; }

    public Guid? ContactEmployeeId { get; init; }

    public DateOnly DueOn { get; init; }

    public DateOnly? RevisedDueOn { get; init; }

    public DateOnly DueDate { get; init; }

    public decimal LatestPct { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public DateOnly? LastFollowUpOn { get; init; }

    public DateOnly? NextFollowUpOn { get; init; }

    public Guid ResponsibleUnitId { get; init; }

    public string Priority { get; init; } = string.Empty;

    public bool Overdue { get; init; }
}

/// <summary>Report-builder objects: audit findings and recommendations, on the Audit reader's scoped queries (draft reports for the office only).</summary>
internal sealed class AuditDatasetSource(AuditMgmtDbContext db, AuditReader reader, AuditDeadlines deadlines) : IReportingDatasetSource
{
    public const string FindingsDataset = "auditmgmt.findings";
    public const string RecommendationsDataset = "auditmgmt.recommendations";

    private static readonly string[] FinalRecommendationStatuses = [.. RecommendationStatuses.Final];

    private static readonly DatasetQuery<AuditFindingRow> FindingQuery = new DatasetQuery<AuditFindingRow>(f => f.Id)
        .Field("number", f => f.Number)
        .Field("titleAr", f => f.TitleAr)
        .Field("titleEn", f => f.TitleEn)
        .Field("status", f => f.Status)
        .Field("priority", f => f.Priority)
        .Field("type", f => f.TypeCode)
        .Field("reportNumber", f => f.ReportNumber)
        .Field("reportType", f => f.ReportTypeCode)
        .Field("reportStatus", f => f.ReportStatus)
        .Field("findingDate", f => f.FindingDate)
        .Field("year", f => f.Year)
        .Field("responsibleUnit", f => f.ResponsibleUnitId)
        .Field("department", f => f.DepartmentUnitId)
        .Field("auditor", f => f.AuditorEmployeeId)
        .Field("expectedClosingOn", f => f.ExpectedClosingOn)
        .Field("actualClosedOn", f => f.ActualClosedOn)
        .Field("completion", f => f.Completion)
        .Field("repeat", f => f.Repeat)
        .Field("capRemarkNumber", f => f.CapRemarkNumber);

    private static readonly DatasetQuery<AuditRecommendationRow> RecommendationQuery = new DatasetQuery<AuditRecommendationRow>(r => r.Id)
        .Field("number", r => r.Number)
        .Field("actionAr", r => r.ActionAr)
        .Field("actionEn", r => r.ActionEn)
        .Field("findingNumber", r => r.FindingNumber)
        .Field("status", r => r.Status)
        .Field("managementResponse", r => r.ManagementResponseCode)
        .Field("contact", r => r.ContactEmployeeId)
        .Field("dueOn", r => r.DueOn)
        .Field("revisedDueOn", r => r.RevisedDueOn)
        .Field("dueDate", r => r.DueDate)
        .Field("latestPct", r => r.LatestPct)
        .Field("completedOn", r => r.CompletedOn)
        .Field("lastFollowUpOn", r => r.LastFollowUpOn)
        .Field("nextFollowUpOn", r => r.NextFollowUpOn)
        .Field("responsibleUnit", r => r.ResponsibleUnitId)
        .Field("priority", r => r.Priority)
        .Field("overdue", r => r.Overdue);

    public string SystemCode => AuditPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(FindingsDataset, AuditPermissions.SystemCode, "ملاحظات التدقيق", "Audit findings", AuditPermissions.Read,
        [
            new("number", "رقم الملاحظة", "Finding number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("priority", "الأولوية", "Priority", DatasetFieldType.Lookup, Groupable: true, LookupType: AuditPermissions.PriorityLookup),
            new("type", "نوع الملاحظة", "Finding type", DatasetFieldType.Lookup, Groupable: true, LookupType: AuditPermissions.FindingTypeLookup),
            new("reportNumber", "رقم التقرير", "Report number", DatasetFieldType.Text, Groupable: true),
            new("reportType", "نوع التقرير", "Report type", DatasetFieldType.Lookup, Groupable: true, LookupType: AuditPermissions.ReportTypeLookup),
            new("reportStatus", "حالة التقرير", "Report status", DatasetFieldType.Lookup, Groupable: true),
            new("findingDate", "تاريخ الملاحظة", "Finding date", DatasetFieldType.Date),
            new("year", "السنة", "Year", DatasetFieldType.Number, Groupable: true),
            new("responsibleUnit", "الجهة المسؤولة", "Responsible unit", DatasetFieldType.Unit, Groupable: true),
            new("department", "الإدارة", "Department", DatasetFieldType.Unit, Groupable: true),
            new("auditor", "المدقق", "Auditor", DatasetFieldType.Employee, Groupable: true),
            new("expectedClosingOn", "تاريخ الإغلاق المتوقع", "Expected closing", DatasetFieldType.Date),
            new("actualClosedOn", "تاريخ الإغلاق الفعلي", "Actual closing", DatasetFieldType.Date),
            new("completion", "نسبة الإنجاز", "Completion", DatasetFieldType.Ratio),
            new("repeat", "ملاحظة متكررة", "Repeat finding", DatasetFieldType.Bool, Groupable: true),
            new("capRemarkNumber", "رقم ملاحظة خطة العمل", "CAP remark number", DatasetFieldType.Text),
        ], "/audit-management/findings/{id}"),
        new(RecommendationsDataset, AuditPermissions.SystemCode, "توصيات التدقيق", "Audit recommendations", AuditPermissions.Read,
        [
            new("number", "رقم التوصية", "Recommendation number", DatasetFieldType.Text),
            new("actionAr", "التوصية (عربي)", "Recommendation (Arabic)", DatasetFieldType.Text),
            new("actionEn", "التوصية (إنجليزي)", "Recommendation (English)", DatasetFieldType.Text),
            new("findingNumber", "رقم الملاحظة", "Finding number", DatasetFieldType.Text, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("managementResponse", "رد الإدارة", "Management response", DatasetFieldType.Lookup, Groupable: true, LookupType: AuditPermissions.ResponseLookup),
            new("contact", "مسؤول التنفيذ", "Contact person", DatasetFieldType.Employee, Groupable: true),
            new("dueOn", "تاريخ الاستحقاق الأصلي", "Original due date", DatasetFieldType.Date),
            new("revisedDueOn", "تاريخ الاستحقاق المعدل", "Revised due date", DatasetFieldType.Date),
            new("dueDate", "تاريخ الاستحقاق", "Due date", DatasetFieldType.Date),
            new("latestPct", "نسبة الإنجاز", "Latest achievement", DatasetFieldType.Ratio),
            new("completedOn", "تاريخ التنفيذ", "Completed on", DatasetFieldType.Date),
            new("lastFollowUpOn", "آخر متابعة", "Last follow-up", DatasetFieldType.Date),
            new("nextFollowUpOn", "المتابعة القادمة", "Next follow-up", DatasetFieldType.Date),
            new("responsibleUnit", "الجهة المسؤولة", "Responsible unit", DatasetFieldType.Unit, Groupable: true),
            new("priority", "أولوية الملاحظة", "Finding priority", DatasetFieldType.Lookup, Groupable: true, LookupType: AuditPermissions.PriorityLookup),
            new("overdue", "متأخرة", "Overdue", DatasetFieldType.Bool, Groupable: true),
        ], "/audit-management/recommendations/{id}"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var findings = await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false);
        switch (query.Dataset)
        {
            case FindingsDataset:
                {
                    var rows = findings.Join(db.Reports.AsNoTracking(), f => f.ReportId, r => r.Id, (f, r) => new AuditFindingRow
                    {
                        Id = f.Id,
                        Number = f.Number,
                        TitleAr = f.TitleAr,
                        TitleEn = f.TitleEn,
                        Status = f.Status,
                        Priority = f.Priority,
                        TypeCode = f.TypeCode,
                        ReportNumber = r.Number,
                        ReportTypeCode = r.TypeCode,
                        ReportStatus = r.Status,
                        FindingDate = f.FindingDate,
                        Year = f.Year,
                        ResponsibleUnitId = f.ResponsibleUnitId,
                        DepartmentUnitId = f.DepartmentUnitId,
                        AuditorEmployeeId = f.AuditorEmployeeId,
                        ExpectedClosingOn = f.ExpectedClosingOn,
                        ActualClosedOn = f.ActualClosedOn,
                        Completion = f.Completion,
                        Repeat = f.Repeat,
                        CapRemarkNumber = f.CapRemarkNumber,
                    });
                    return await FindingQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
                }

            case RecommendationsDataset:
                {
                    var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
                    var rows = db.Recommendations.AsNoTracking().Join(findings, r => r.FindingId, f => f.Id, (r, f) => new AuditRecommendationRow
                    {
                        Id = r.Id,
                        Number = r.Number,
                        ActionAr = r.ActionAr,
                        ActionEn = r.ActionEn,
                        FindingNumber = f.Number,
                        Status = r.Status,
                        ManagementResponseCode = r.ManagementResponseCode,
                        ContactEmployeeId = r.ContactEmployeeId,
                        DueOn = r.DueOn,
                        RevisedDueOn = r.RevisedDueOn,
                        DueDate = r.RevisedDueOn ?? r.DueOn,
                        LatestPct = r.LatestPct,
                        CompletedOn = r.CompletedOn,
                        LastFollowUpOn = r.LastFollowUpOn,
                        NextFollowUpOn = r.NextFollowUpOn,
                        ResponsibleUnitId = f.ResponsibleUnitId,
                        Priority = f.Priority,
                        Overdue = r.Status == RecommendationStatuses.Late || (!FinalRecommendationStatuses.Contains(r.Status) && (r.RevisedDueOn ?? r.DueOn) < today),
                    });
                    return await RecommendationQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}

/// <summary>
/// My Work card "Audit follow-ups due" (ADR 0149) for the Audit Office: open recommendations of the findings the user audits whose follow-up
/// is due soon, and late ones — the same query as the Audit My Work items.
/// </summary>
internal sealed class AuditMyWorkCards(AuditMgmtDbContext db, AuditReader reader, AuditDeadlines deadlines) : IMyWorkCardSource
{
    public const string CardCode = "auditFollowUps";

    public string SystemCode => AuditPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me || !await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var due = AuditMyWork.FollowUpsDue(db, me, AuditMyWork.Limit(settings, today));
        var late = await due.CountAsync(x => x.R.Status == RecommendationStatuses.Late, cancellationToken).ConfigureAwait(false);
        var upcoming = await due.CountAsync(x => x.R.Status != RecommendationStatuses.Late, cancellationToken).ConfigureAwait(false);
        if (late + upcoming == 0)
        {
            return [];
        }

        var rows = await due.OrderByDescending(x => x.R.Status == RecommendationStatuses.Late).ThenBy(x => x.R.NextFollowUpOn ?? x.R.DueOn).Take(5)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var counts = new List<MyWorkCount>();
        if (upcoming > 0)
        {
            counts.Add(new MyWorkCount("auditFollowUpsDue", upcoming, "warning", "/audit-management/calendar"));
        }

        if (late > 0)
        {
            counts.Add(new MyWorkCount("auditRecommendationsLate", late, "danger", "/audit-management/findings?view=late"));
        }

        var items = rows.Select(x =>
        {
            var date = x.R.NextFollowUpOn ?? x.R.EffectiveDue;
            return new MyWorkCardItem(Recommendation.EntityType, x.R.Id, x.R.Number, AuditNotifier.Short(x.R.ActionAr ?? x.R.ActionEn),
                AuditNotifier.Short(x.R.ActionEn ?? x.R.ActionAr), x.R.Status == RecommendationStatuses.Late ? "auditmgmt.myWork.late" : "auditmgmt.myWork.followUpDue",
                $"{AuditService.Link(x.F)}?recommendation={x.R.Id:D}", date, x.R.Status == RecommendationStatuses.Late || date < today);
        }).ToList();
        return [new MyWorkCard(CardCode, AuditPermissions.SystemCode, $"myWork.cards.{CardCode}", counts, items, "/audit-management/calendar", 100)];
    }
}
