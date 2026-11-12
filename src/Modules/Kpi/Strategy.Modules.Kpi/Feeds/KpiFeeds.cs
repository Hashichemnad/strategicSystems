using System.Security.Claims;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Kpi.Feeds;

/// <summary>
/// Reportable record types of Performance Management (ADR 0190): KPI cards and their period results, read on the same scoped readers as the
/// report builder (<see cref="KpiReader.CardsAsync"/>, <see cref="KpiReader.ResultsAsync"/>; results not yet opened are excluded).
/// </summary>
internal sealed class KpiFeeds(KpiReader reader) : IReportableRecordSource
{
    public const string CardType = KpiCard.EntityType;
    public const string ResultType = KpiResult.EntityType;

    private static readonly RecordTypeQuery<KpiCard> Cards = new RecordTypeQuery<KpiCard>(c => c.Id, c => c.UpdatedAt)
        .Field("code", c => c.Code)
        .Field("nameAr", c => c.NameAr)
        .Field("nameEn", c => c.NameEn)
        .Field("status", c => c.Status)
        .Field("type", c => c.TypeCode)
        .Field("level", c => c.Level)
        .Field("nature", c => c.Nature)
        .Field("measuringPeriod", c => c.MeasuringPeriod)
        .Field("measuringMethod", c => c.MeasuringMethodCode)
        .Field("measureUnit", c => c.UnitCode)
        .Field("direction", c => c.Direction)
        .Field("department", c => c.DepartmentUnitId)
        .Field("unit", c => c.ResponsibleUnitId)
        .Field("owner", c => c.ResponsibleEmployeeId)
        .Field("parentCardId", c => c.ParentCardId)
        .Field("weight", c => c.Weight)
        .Field("baselineValue", c => c.BaselineValue)
        .Field("globalStar", c => c.IsGlobalStar)
        .Field("initiatedOn", c => c.InitiatedOn)
        .Field("closedOn", c => c.ClosedOn)
        .Deleted(c => c.Status == CardStatuses.Inactive);

    private static readonly RecordTypeQuery<KpiResult> Results = new RecordTypeQuery<KpiResult>(r => r.Id, r => r.UpdatedAt)
        .Field("cardId", r => r.CardId)
        .Field("year", r => r.Year)
        .Field("period", r => r.PeriodCode)
        .Field("periodStart", r => r.PeriodStart)
        .Field("periodEnd", r => r.PeriodEnd)
        .Field("status", r => r.Status)
        .Field("target", r => r.Target)
        .Field("rangeMin", r => r.RangeMin)
        .Field("rangeMax", r => r.RangeMax)
        .Field("achieve", r => r.Achieve)
        .Field("actualPerformance", r => r.ActualPerformance)
        .Field("cappedPerformance", r => r.CappedPerformance)
        .Field("notApplicable", r => r.NotApplicable)
        .Field("dueOn", r => r.DueOn)
        .Field("submittedAt", r => r.SubmittedAt)
        .Field("late", r => r.Late)
        .Field("daysLate", r => r.DaysLate)
        .Field("approvedAt", r => r.ApprovedAt)
        .Field("unit", r => r.OwnerUnitId)
        .Field("owner", r => r.OwnerEmployeeId);

    public string SystemCode => KpiPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(CardType, KpiPermissions.SystemCode, "KpiCards", "بطاقات مؤشرات الأداء", "KPI cards", KpiPermissions.CardRead,
        [
            new("code", "الرمز", "Code", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: KpiChoices.CardStatuses),
            new("type", "النوع", "Type", RecordFieldType.Lookup, LookupType: KpiPermissions.TypeLookup),
            new("level", "المستوى", "Level", RecordFieldType.Lookup, Choices: KpiChoices.Levels),
            new("nature", "الطبيعة", "Nature", RecordFieldType.Lookup, Choices: KpiChoices.Natures),
            new("measuringPeriod", "دورية القياس", "Measuring period", RecordFieldType.Lookup, Choices: KpiChoices.MeasuringPeriods),
            new("measuringMethod", "طريقة القياس", "Measuring method", RecordFieldType.Lookup, LookupType: KpiPermissions.MeasuringMethodLookup),
            new("measureUnit", "وحدة القياس", "Measuring unit", RecordFieldType.Lookup, LookupType: KpiPermissions.UnitLookup),
            new("direction", "الاتجاه", "Direction", RecordFieldType.Lookup, Choices: KpiChoices.Directions),
            new("department", "الإدارة", "Department", RecordFieldType.Unit),
            new("unit", "الوحدة المسؤولة", "Responsible unit", RecordFieldType.Unit),
            new("owner", "المسؤول", "Owner", RecordFieldType.Employee),
            new("parentCardId", "المؤشر الأب", "Parent KPI", RecordFieldType.Id),
            new("weight", "الوزن", "Weight", RecordFieldType.Number),
            new("baselineValue", "قيمة خط الأساس", "Baseline value", RecordFieldType.Number),
            new("globalStar", "مؤشر النجمة العالمية", "Global star", RecordFieldType.Bool),
            new("initiatedOn", "تاريخ البدء", "Initiated on", RecordFieldType.Date),
            new("closedOn", "تاريخ الإيقاف", "Closed on", RecordFieldType.Date),
        ],
        [
            new("results", "النتائج", "Results", ResultType, RecordFields.Id, "cardId", Many: true),
        ], "/kpi/cards/{id}"),
        new(ResultType, KpiPermissions.SystemCode, "KpiResults", "نتائج مؤشرات الأداء", "KPI results", KpiPermissions.ResultRead,
        [
            new("cardId", "المؤشر", "KPI card", RecordFieldType.Id),
            new("year", "السنة", "Year", RecordFieldType.Number),
            new("period", "الفترة", "Period", RecordFieldType.Lookup, Choices: KpiChoices.Periods),
            new("periodStart", "بداية الفترة", "Period start", RecordFieldType.Date),
            new("periodEnd", "نهاية الفترة", "Period end", RecordFieldType.Date),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: KpiChoices.ResultStatuses),
            new("target", "المستهدف", "Target", RecordFieldType.Number),
            new("rangeMin", "الحد الأدنى للنطاق", "Range minimum", RecordFieldType.Number),
            new("rangeMax", "الحد الأعلى للنطاق", "Range maximum", RecordFieldType.Number),
            new("achieve", "المحقق", "Achieved", RecordFieldType.Number),
            new("actualPerformance", "الأداء الفعلي", "Actual performance", RecordFieldType.Ratio),
            new("cappedPerformance", "الأداء المحدود", "Capped performance", RecordFieldType.Ratio),
            new("notApplicable", "غير قابل للتطبيق", "Not applicable", RecordFieldType.Bool),
            new("dueOn", "تاريخ الاستحقاق", "Due on", RecordFieldType.Date),
            new("submittedAt", "تاريخ التقديم", "Submitted on", RecordFieldType.DateTime),
            new("late", "متأخر", "Late", RecordFieldType.Bool),
            new("daysLate", "أيام التأخير", "Days late", RecordFieldType.Number),
            new("approvedAt", "تاريخ الاعتماد", "Approved on", RecordFieldType.DateTime),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("owner", "المسؤول", "Owner", RecordFieldType.Employee),
        ],
        [
            new("card", "المؤشر", "KPI card", CardType, "cardId", RecordFields.Id, Many: false),
        ], "/kpi/results/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (query.RecordType)
        {
            case CardType:
                {
                    var source = await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false);
                    return await Cards.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case ResultType:
                {
                    var source = (await reader.ResultsAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => r.Status != ResultStatuses.NotOpen);
                    return await Results.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }
    }
}
