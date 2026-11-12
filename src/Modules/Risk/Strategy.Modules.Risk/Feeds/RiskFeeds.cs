using System.Security.Claims;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Risk.Feeds;

/// <summary>
/// Reportable record types of Risk Management (ADR 0190): the risk register, read on the register's scoped query (<see cref="RiskReader"/>),
/// as the report builder. Rating bands are framework codes (configurable), hence text.
/// </summary>
internal sealed class RiskFeeds(RiskReader reader) : IReportableRecordSource
{
    public const string RiskType = RiskRecord.EntityType;

    private static readonly RecordTypeQuery<RiskRecord> Risks = new RecordTypeQuery<RiskRecord>(r => r.Id, r => r.UpdatedAt)
        .Field("number", r => r.Number)
        .Field("titleAr", r => r.TitleAr)
        .Field("titleEn", r => r.TitleEn)
        .Field("status", r => r.Status)
        .Field("type", r => r.RiskType)
        .Field("unit", r => r.UnitId)
        .Field("owner", r => r.OwnerEmployeeId)
        .Field("coordinator", r => r.CoordinatorEmployeeId)
        .Field("identifiedOn", r => r.IdentifiedOn)
        .Field("registerYear", r => r.RegisterYear)
        .Field("inherentScore", r => r.InherentScore)
        .Field("inherentBand", r => r.InherentBand)
        .Field("residualScore", r => r.ResidualScore)
        .Field("residualBand", r => r.ResidualBand)
        .Field("residualStatus", r => r.ResidualStatus)
        .Field("targetScore", r => r.TargetScore)
        .Field("targetBand", r => r.TargetBand)
        .Field("response", r => r.ResponseCode)
        .Field("withinAppetite", r => r.WithinAppetite)
        .Field("top10", r => r.Top10)
        .Field("escalated", r => r.Escalated)
        .Field("lastReviewOn", r => r.LastReviewOn)
        .Field("nextReviewOn", r => r.NextReviewOn)
        .Deleted(r => r.Status == RiskStatuses.Rejected);

    public string SystemCode => RiskPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(RiskType, RiskPermissions.SystemCode, "Risks", "المخاطر", "Risks", RiskPermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices:
            [
                new(RiskStatuses.Draft, "مسودة", "Draft"), new(RiskStatuses.UnderReview, "قيد الاعتماد", "Under review"),
                new(RiskStatuses.Active, "نشط", "Active"), new(RiskStatuses.Closed, "مغلق", "Closed"), new(RiskStatuses.Rejected, "مرفوض", "Rejected"),
            ]),
            new("type", "نوع الخطر", "Risk type", RecordFieldType.Lookup, Choices:
            [
                new(RiskTypes.Internal, "داخلي", "Internal"), new(RiskTypes.External, "خارجي", "External"),
            ]),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("owner", "مالك الخطر", "Risk owner", RecordFieldType.Employee),
            new("coordinator", "المنسق", "Coordinator", RecordFieldType.Employee),
            new("identifiedOn", "تاريخ التحديد", "Identified on", RecordFieldType.Date),
            new("registerYear", "سنة السجل", "Register year", RecordFieldType.Number),
            new("inherentScore", "درجة الخطر الكامن", "Inherent score", RecordFieldType.Number),
            new("inherentBand", "نطاق الخطر الكامن", "Inherent rating", RecordFieldType.Text),
            new("residualScore", "درجة الخطر المتبقي", "Residual score", RecordFieldType.Number),
            new("residualBand", "نطاق الخطر المتبقي", "Residual rating", RecordFieldType.Text),
            new("residualStatus", "حالة الخطر المتبقي", "Residual status", RecordFieldType.Lookup, Choices:
            [
                new(ResidualStatuses.NoIssue, "لا توجد مشكلة", "No issue"), new(ResidualStatuses.ContinuousReview, "مراجعة مستمرة", "Continuous review"),
                new(ResidualStatuses.ActiveManagement, "إدارة فعّالة", "Active management"),
            ]),
            new("targetScore", "الدرجة المستهدفة", "Target score", RecordFieldType.Number),
            new("targetBand", "النطاق المستهدف", "Target rating", RecordFieldType.Text),
            new("response", "استراتيجية الاستجابة", "Response strategy", RecordFieldType.Lookup, LookupType: RiskPermissions.ResponseLookup),
            new("withinAppetite", "ضمن الرغبة في المخاطر", "Within appetite", RecordFieldType.Bool),
            new("top10", "ضمن أهم المخاطر", "Top-10 risk", RecordFieldType.Bool),
            new("escalated", "مصعّد", "Escalated", RecordFieldType.Bool),
            new("lastReviewOn", "آخر مراجعة", "Last review", RecordFieldType.Date),
            new("nextReviewOn", "المراجعة القادمة", "Next review", RecordFieldType.Date),
        ], [], "/risk/risks/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != RiskType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var source = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        return await Risks.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
    }
}
