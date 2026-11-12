using System.Security.Claims;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.AuditManagement.Feeds;

/// <summary>
/// Reportable record types of Audit Management (ADR 0190): audit reports, findings and recommendations for analytics feeds and extracts, on the
/// audit reader's scoped queries (the same as the screens and the report builder; non-office readers never see draft reports' findings).
/// </summary>
internal sealed class AuditFeeds(AuditReader reader) : IReportableRecordSource
{
    public const string ReportType = AuditReport.EntityType;
    public const string FindingType = Finding.EntityType;
    public const string RecommendationType = Recommendation.EntityType;

    private static readonly RecordTypeQuery<AuditReport> Reports = new RecordTypeQuery<AuditReport>(r => r.Id, r => r.UpdatedAt)
        .Field("number", r => r.Number)
        .Field("titleAr", r => r.TitleAr)
        .Field("titleEn", r => r.TitleEn)
        .Field("type", r => r.TypeCode)
        .Field("auditorBody", r => r.AuditorBody)
        .Field("leadAuditor", r => r.LeadAuditorEmployeeId)
        .Field("periodFrom", r => r.PeriodFrom)
        .Field("periodTo", r => r.PeriodTo)
        .Field("reportDate", r => r.ReportDate)
        .Field("status", r => r.Status)
        .Field("capSourceId", r => r.CapSourceId)
        .Field("issuedAt", r => r.IssuedAt)
        .Field("closedAt", r => r.ClosedAt)
        .Deleted(r => r.Status == ReportStatuses.Cancelled);

    private static readonly RecordTypeQuery<Finding> Findings = new RecordTypeQuery<Finding>(f => f.Id, f => f.UpdatedAt)
        .Field("number", f => f.Number)
        .Field("reportId", f => f.ReportId)
        .Field("titleAr", f => f.TitleAr)
        .Field("titleEn", f => f.TitleEn)
        .Field("status", f => f.Status)
        .Field("priority", f => f.Priority)
        .Field("type", f => f.TypeCode)
        .Field("findingDate", f => f.FindingDate)
        .Field("year", f => f.Year)
        .Field("responsibleUnit", f => f.ResponsibleUnitId)
        .Field("department", f => f.DepartmentUnitId)
        .Field("auditor", f => f.AuditorEmployeeId)
        .Field("expectedClosingOn", f => f.ExpectedClosingOn)
        .Field("actualClosedOn", f => f.ActualClosedOn)
        .Field("completion", f => f.Completion)
        .Field("repeat", f => f.Repeat)
        .Field("repeatOfFindingId", f => f.RepeatOfFindingId)
        .Field("capRemarkId", f => f.CapRemarkId)
        .Field("capRemarkNumber", f => f.CapRemarkNumber)
        .Deleted(f => f.Status == AuditFindingStatuses.Cancelled);

    private static readonly RecordTypeQuery<Recommendation> Recommendations = new RecordTypeQuery<Recommendation>(r => r.Id, r => r.UpdatedAt)
        .Field("number", r => r.Number)
        .Field("findingId", r => r.FindingId)
        .Field("actionAr", r => r.ActionAr)
        .Field("actionEn", r => r.ActionEn)
        .Field("status", r => r.Status)
        .Field("managementResponse", r => r.ManagementResponseCode)
        .Field("contact", r => r.ContactEmployeeId)
        .Field("dueOn", r => r.DueOn)
        .Field("revisedDueOn", r => r.RevisedDueOn)
        .Field("latestPct", r => r.LatestPct)
        .Field("completedOn", r => r.CompletedOn)
        .Field("lastFollowUpOn", r => r.LastFollowUpOn)
        .Field("nextFollowUpOn", r => r.NextFollowUpOn)
        .Field("riskAcceptedOn", r => r.RiskAcceptedOn)
        .Deleted(r => r.Status == RecommendationStatuses.Cancelled);

    public string SystemCode => AuditPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(ReportType, AuditPermissions.SystemCode, "Reports", "تقارير التدقيق", "Audit reports", AuditPermissions.Read,
        [
            new("number", "رقم التقرير", "Report number", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("type", "نوع التقرير", "Report type", RecordFieldType.Lookup, LookupType: AuditPermissions.ReportTypeLookup),
            new("auditorBody", "جهة التدقيق", "Auditor body", RecordFieldType.Text),
            new("leadAuditor", "المدقق الرئيسي", "Lead auditor", RecordFieldType.Employee),
            new("periodFrom", "بداية فترة التدقيق", "Period from", RecordFieldType.Date),
            new("periodTo", "نهاية فترة التدقيق", "Period to", RecordFieldType.Date),
            new("reportDate", "تاريخ التقرير", "Report date", RecordFieldType.Date),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: AuditChoices.ReportStatuses),
            new("capSourceId", "مصدر خطة العمل التصحيحية", "CAP source", RecordFieldType.Id),
            new("issuedAt", "تاريخ الإصدار", "Issued at", RecordFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed at", RecordFieldType.DateTime),
        ],
        [
            new("findings", "الملاحظات", "Findings", FindingType, RecordFields.Id, "reportId", Many: true),
        ], "/audit-management/reports/{id}"),
        new(FindingType, AuditPermissions.SystemCode, "Findings", "ملاحظات التدقيق", "Audit findings", AuditPermissions.Read,
        [
            new("number", "رقم الملاحظة", "Finding number", RecordFieldType.Text),
            new("reportId", "التقرير", "Report", RecordFieldType.Id),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: AuditChoices.FindingStatuses),
            new("priority", "الأولوية", "Priority", RecordFieldType.Lookup, LookupType: AuditPermissions.PriorityLookup),
            new("type", "نوع الملاحظة", "Finding type", RecordFieldType.Lookup, LookupType: AuditPermissions.FindingTypeLookup),
            new("findingDate", "تاريخ الملاحظة", "Finding date", RecordFieldType.Date),
            new("year", "السنة", "Year", RecordFieldType.Number),
            new("responsibleUnit", "الجهة المسؤولة", "Responsible unit", RecordFieldType.Unit),
            new("department", "الإدارة", "Department", RecordFieldType.Unit),
            new("auditor", "المدقق", "Auditor", RecordFieldType.Employee),
            new("expectedClosingOn", "تاريخ الإغلاق المتوقع", "Expected closing", RecordFieldType.Date),
            new("actualClosedOn", "تاريخ الإغلاق الفعلي", "Actual closing", RecordFieldType.Date),
            new("completion", "نسبة الإنجاز", "Completion", RecordFieldType.Ratio),
            new("repeat", "ملاحظة متكررة", "Repeat finding", RecordFieldType.Bool),
            new("repeatOfFindingId", "الملاحظة الأصلية المتكررة", "Repeat of finding", RecordFieldType.Id),
            new("capRemarkId", "ملاحظة خطة العمل", "CAP remark", RecordFieldType.Id),
            new("capRemarkNumber", "رقم ملاحظة خطة العمل", "CAP remark number", RecordFieldType.Text),
        ],
        [
            new("report", "التقرير", "Report", ReportType, "reportId", RecordFields.Id, Many: false),
            new("recommendations", "التوصيات", "Recommendations", RecommendationType, RecordFields.Id, "findingId", Many: true),
        ], "/audit-management/findings/{id}"),
        new(RecommendationType, AuditPermissions.SystemCode, "Recommendations", "توصيات التدقيق", "Audit recommendations", AuditPermissions.Read,
        [
            new("number", "رقم التوصية", "Recommendation number", RecordFieldType.Text),
            new("findingId", "الملاحظة", "Finding", RecordFieldType.Id),
            new("actionAr", "التوصية (عربي)", "Recommendation (Arabic)", RecordFieldType.Text),
            new("actionEn", "التوصية (إنجليزي)", "Recommendation (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: AuditChoices.RecommendationStatuses),
            new("managementResponse", "رد الإدارة", "Management response", RecordFieldType.Lookup, LookupType: AuditPermissions.ResponseLookup),
            new("contact", "مسؤول التنفيذ", "Contact person", RecordFieldType.Employee),
            new("dueOn", "تاريخ الاستحقاق الأصلي", "Original due date", RecordFieldType.Date),
            new("revisedDueOn", "تاريخ الاستحقاق المعدل", "Revised due date", RecordFieldType.Date),
            new("latestPct", "نسبة الإنجاز", "Latest achievement", RecordFieldType.Ratio),
            new("completedOn", "تاريخ التنفيذ", "Completed on", RecordFieldType.Date),
            new("lastFollowUpOn", "آخر متابعة", "Last follow-up", RecordFieldType.Date),
            new("nextFollowUpOn", "المتابعة القادمة", "Next follow-up", RecordFieldType.Date),
            new("riskAcceptedOn", "تاريخ قبول المخاطر", "Risk accepted on", RecordFieldType.Date),
        ],
        [
            new("finding", "الملاحظة", "Finding", FindingType, "findingId", RecordFields.Id, Many: false),
        ], "/audit-management/recommendations/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.RecordType switch
        {
            ReportType => await Reports.RunAsync(await reader.ReportsAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false),
            FindingType => await Findings.RunAsync(await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false),
            RecommendationType => await Recommendations.RunAsync(await reader.RecommendationsAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query)),
        };
    }
}
