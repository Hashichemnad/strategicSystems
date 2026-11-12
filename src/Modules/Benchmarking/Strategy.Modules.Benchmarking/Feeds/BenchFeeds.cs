using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Benchmarking.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Benchmarking.Feeds;

/// <summary>
/// Reportable record types of Benchmarking (ADR 0190): studies on <see cref="BenchReader.StudiesAsync"/> (as the report builder) and their
/// topics, readable with the study.
/// </summary>
internal sealed class BenchFeeds(BenchDbContext db, BenchReader reader) : IReportableRecordSource
{
    public const string StudyType = Study.EntityType;
    public const string TopicType = Topic.EntityType;

    private static readonly RecordTypeQuery<Study> Studies = new RecordTypeQuery<Study>(s => s.Id, s => s.UpdatedAt)
        .Field("number", s => s.Number)
        .Field("titleAr", s => s.TitleAr)
        .Field("titleEn", s => s.TitleEn)
        .Field("stage", s => s.Stage)
        .Field("status", s => s.Status)
        .Field("targetYear", s => s.TargetYear)
        .Field("targetQuarter", s => s.TargetQuarter)
        .Field("unit", s => s.ResponsibleUnitId)
        .Field("section", s => s.SectionUnitId)
        .Field("coordinator", s => s.ResponsibleEmployeeId)
        .Field("requester", s => s.RequesterEmployeeId)
        .Field("comparisonType", s => s.ComparisonTypeCode)
        .Field("purpose", s => s.PurposeCode)
        .Field("requestNumber", s => s.RequestNumber)
        .Field("planApprovedAt", s => s.PlanApprovedAt)
        .Field("analysisApprovedAt", s => s.AnalysisApprovedAt)
        .Field("closedAt", s => s.ClosedAt)
        .Field("impactDueOn", s => s.ImpactDueOn)
        .Field("createdAt", s => s.CreatedAt)
        .Deleted(s => s.Status == StudyStatuses.Cancelled);

    private static readonly RecordTypeQuery<Topic> Topics = new RecordTypeQuery<Topic>(t => t.Id, t => t.UpdatedAt)
        .Field("studyId", t => t.StudyId)
        .Field("title", t => t.Title)
        .Field("owner", t => t.OwnerEmployeeId)
        .Field("description", t => t.Description)
        .Field("sort", t => t.Sort)
        .Deleted(t => t.Removed);

    public string SystemCode => BenchPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(StudyType, BenchPermissions.SystemCode, "Studies", "دراسات المقارنة المعيارية", "Benchmarking studies", BenchPermissions.Read,
        [
            new("number", "رقم الدراسة", "Study number", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("stage", "المرحلة", "Stage", RecordFieldType.Lookup, Choices: BenchChoices.Stages),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: BenchChoices.Statuses),
            new("targetYear", "السنة المستهدفة", "Target year", RecordFieldType.Number),
            new("targetQuarter", "الربع المستهدف", "Target quarter", RecordFieldType.Number),
            new("unit", "الجهة المسؤولة", "Responsible unit", RecordFieldType.Unit),
            new("section", "القسم", "Section", RecordFieldType.Unit),
            new("coordinator", "منسق الدراسة", "Coordinator", RecordFieldType.Employee),
            new("requester", "مقدم الطلب", "Requester", RecordFieldType.Employee),
            new("comparisonType", "نوع المقارنة", "Comparison type", RecordFieldType.Lookup, LookupType: BenchPermissions.ComparisonTypeLookup),
            new("purpose", "الغرض من المقارنة", "Purpose", RecordFieldType.Lookup, LookupType: BenchPermissions.PurposeLookup),
            new("requestNumber", "رقم الطلب", "Request number", RecordFieldType.Text),
            new("planApprovedAt", "تاريخ اعتماد الخطة", "Plan approved at", RecordFieldType.DateTime),
            new("analysisApprovedAt", "تاريخ اعتماد التحليل", "Analysis approved at", RecordFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed at", RecordFieldType.DateTime),
            new("impactDueOn", "موعد قياس الأثر", "Impact review due", RecordFieldType.Date),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ],
        [
            new("topics", "المواضيع", "Topics", TopicType, RecordFields.Id, "studyId", Many: true),
        ], "/bench/studies/{id}"),
        new(TopicType, BenchPermissions.SystemCode, "StudyTopics", "مواضيع الدراسات", "Study topics", BenchPermissions.Read,
        [
            new("studyId", "الدراسة", "Study", RecordFieldType.Id),
            new("title", "الموضوع", "Topic", RecordFieldType.Text),
            new("owner", "المسؤول", "Owner", RecordFieldType.Employee),
            new("description", "الوصف", "Description", RecordFieldType.Text),
            new("sort", "الترتيب", "Order", RecordFieldType.Number),
        ],
        [
            new("study", "الدراسة", "Study", StudyType, "studyId", RecordFields.Id, Many: false),
        ]),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var studies = await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false);
        return query.RecordType switch
        {
            StudyType => await Studies.RunAsync(studies, query, cancellationToken).ConfigureAwait(false),

            // A topic is readable with its study (the study page shows them).
            TopicType => await Topics.RunAsync(db.Topics.AsNoTracking().Where(t => studies.Any(s => s.Id == t.StudyId)), query, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query)),
        };
    }
}
