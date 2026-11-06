using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>A KPI result with its card's identity, for the report builder.</summary>
internal sealed class KpiResultReportRow
{
    public Guid Id { get; init; }

    public string CardCode { get; init; } = string.Empty;

    public string? CardNameAr { get; init; }

    public string? CardNameEn { get; init; }

    public string? CardType { get; init; }

    public int Year { get; init; }

    public string PeriodCode { get; init; } = string.Empty;

    public DateOnly PeriodStart { get; init; }

    public DateOnly PeriodEnd { get; init; }

    public string Status { get; init; } = string.Empty;

    public decimal? Target { get; init; }

    public decimal? Achieve { get; init; }

    public decimal? ActualPerformance { get; init; }

    public decimal? CappedPerformance { get; init; }

    public bool NotApplicable { get; init; }

    public DateOnly? DueOn { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    public bool Late { get; init; }

    public int DaysLate { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public Guid? UnitId { get; init; }

    public Guid? OwnerEmployeeId { get; init; }
}

/// <summary>Report-builder objects of Performance Management (ADR 0144): KPI cards and KPI results, on the reader's scoped queries.</summary>
internal sealed class KpiDatasetSource(KpiDbContext db, KpiReader reader) : IReportingDatasetSource
{
    public const string CardsDataset = "kpi.cards";
    public const string ResultsDataset = "kpi.results";

    public string SystemCode => KpiPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(CardsDataset, KpiPermissions.SystemCode, "بطاقات مؤشرات الأداء", "KPI cards", KpiPermissions.CardRead,
        [
            new("code", "الرمز", "Code", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true, LookupType: KpiPermissions.TypeLookup),
            new("level", "المستوى", "Level", DatasetFieldType.Lookup, Groupable: true),
            new("nature", "الطبيعة", "Nature", DatasetFieldType.Lookup, Groupable: true),
            new("measuringPeriod", "دورية القياس", "Measuring period", DatasetFieldType.Lookup, Groupable: true),
            new("measuringMethod", "طريقة القياس", "Measuring method", DatasetFieldType.Lookup, Groupable: true, LookupType: KpiPermissions.MeasuringMethodLookup),
            new("measureUnit", "وحدة القياس", "Measuring unit", DatasetFieldType.Lookup, Groupable: true, LookupType: KpiPermissions.UnitLookup),
            new("direction", "الاتجاه", "Direction", DatasetFieldType.Lookup, Groupable: true),
            new("department", "الإدارة", "Department", DatasetFieldType.Unit, Groupable: true),
            new("unit", "الوحدة المسؤولة", "Responsible unit", DatasetFieldType.Unit, Groupable: true),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
            new("weight", "الوزن", "Weight", DatasetFieldType.Number),
            new("baselineValue", "قيمة خط الأساس", "Baseline value", DatasetFieldType.Number),
            new("globalStar", "مؤشر النجمة العالمية", "Global star", DatasetFieldType.Bool, Groupable: true),
            new("initiatedOn", "تاريخ البدء", "Initiated on", DatasetFieldType.Date),
        ], "/kpi/cards/{id}"),
        new(ResultsDataset, KpiPermissions.SystemCode, "نتائج مؤشرات الأداء", "KPI results", KpiPermissions.ResultRead,
        [
            new("cardCode", "رمز المؤشر", "KPI code", DatasetFieldType.Text, Groupable: true),
            new("cardNameAr", "المؤشر (عربي)", "KPI (Arabic)", DatasetFieldType.Text),
            new("cardNameEn", "المؤشر (إنجليزي)", "KPI (English)", DatasetFieldType.Text),
            new("cardType", "نوع المؤشر", "KPI type", DatasetFieldType.Lookup, Groupable: true, LookupType: KpiPermissions.TypeLookup),
            new("year", "السنة", "Year", DatasetFieldType.Number, Groupable: true),
            new("period", "الفترة", "Period", DatasetFieldType.Lookup, Groupable: true),
            new("periodStart", "بداية الفترة", "Period start", DatasetFieldType.Date),
            new("periodEnd", "نهاية الفترة", "Period end", DatasetFieldType.Date),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("target", "المستهدف", "Target", DatasetFieldType.Number),
            new("achieve", "المحقق", "Achieved", DatasetFieldType.Number),
            new("actualPerformance", "الأداء الفعلي", "Actual performance", DatasetFieldType.Ratio),
            new("cappedPerformance", "الأداء المحدود", "Capped performance", DatasetFieldType.Ratio),
            new("notApplicable", "غير قابل للتطبيق", "Not applicable", DatasetFieldType.Bool, Groupable: true),
            new("dueOn", "تاريخ الاستحقاق", "Due on", DatasetFieldType.Date),
            new("submittedAt", "تاريخ التقديم", "Submitted on", DatasetFieldType.DateTime),
            new("late", "متأخر", "Late", DatasetFieldType.Bool, Groupable: true),
            new("daysLate", "أيام التأخير", "Days late", DatasetFieldType.Number),
            new("approvedAt", "تاريخ الاعتماد", "Approved on", DatasetFieldType.DateTime),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
        ], "/kpi/results/{id}"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (query.Dataset)
        {
            case CardsDataset:
                {
                    var source = await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false);
                    var dataset = new DatasetQuery<KpiCard>(c => c.Id)
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
                        .Field("weight", c => c.Weight)
                        .Field("baselineValue", c => c.BaselineValue)
                        .Field("globalStar", c => c.IsGlobalStar)
                        .Field("initiatedOn", c => c.InitiatedOn);
                    return await dataset.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            case ResultsDataset:
                {
                    var results = await reader.ResultsAsync(user, cancellationToken).ConfigureAwait(false);
                    var source = from r in results
                                 join c in db.Cards.AsNoTracking() on r.CardId equals c.Id
                                 where r.Status != ResultStatuses.NotOpen
                                 select new KpiResultReportRow
                                 {
                                     Id = r.Id,
                                     CardCode = c.Code,
                                     CardNameAr = c.NameAr,
                                     CardNameEn = c.NameEn,
                                     CardType = c.TypeCode,
                                     Year = r.Year,
                                     PeriodCode = r.PeriodCode,
                                     PeriodStart = r.PeriodStart,
                                     PeriodEnd = r.PeriodEnd,
                                     Status = r.Status,
                                     Target = r.Target,
                                     Achieve = r.Achieve,
                                     ActualPerformance = r.ActualPerformance,
                                     CappedPerformance = r.CappedPerformance,
                                     NotApplicable = r.NotApplicable,
                                     DueOn = r.DueOn,
                                     SubmittedAt = r.SubmittedAt,
                                     Late = r.Late,
                                     DaysLate = r.DaysLate,
                                     ApprovedAt = r.ApprovedAt,
                                     UnitId = r.OwnerUnitId,
                                     OwnerEmployeeId = r.OwnerEmployeeId,
                                 };
                    var dataset = new DatasetQuery<KpiResultReportRow>(r => r.Id)
                        .Field("cardCode", r => r.CardCode)
                        .Field("cardNameAr", r => r.CardNameAr)
                        .Field("cardNameEn", r => r.CardNameEn)
                        .Field("cardType", r => r.CardType)
                        .Field("year", r => r.Year)
                        .Field("period", r => r.PeriodCode)
                        .Field("periodStart", r => r.PeriodStart)
                        .Field("periodEnd", r => r.PeriodEnd)
                        .Field("status", r => r.Status)
                        .Field("target", r => r.Target)
                        .Field("achieve", r => r.Achieve)
                        .Field("actualPerformance", r => r.ActualPerformance)
                        .Field("cappedPerformance", r => r.CappedPerformance)
                        .Field("notApplicable", r => r.NotApplicable)
                        .Field("dueOn", r => r.DueOn)
                        .Field("submittedAt", r => r.SubmittedAt)
                        .Field("late", r => r.Late)
                        .Field("daysLate", r => r.DaysLate)
                        .Field("approvedAt", r => r.ApprovedAt)
                        .Field("unit", r => r.UnitId)
                        .Field("owner", r => r.OwnerEmployeeId);
                    return await dataset.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}
