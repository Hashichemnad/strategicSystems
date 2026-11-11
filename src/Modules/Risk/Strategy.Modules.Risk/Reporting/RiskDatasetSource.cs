using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>A treatment action with its risk's register unit and number (report-builder row).</summary>
internal sealed class RiskActionRow
{
    public Guid Id { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string Status { get; init; } = string.Empty;

    public Guid? OwnerEmployeeId { get; init; }

    public DateOnly? PlannedStart { get; init; }

    public DateOnly? PlannedEnd { get; init; }

    public DateOnly? ActualEnd { get; init; }

    public decimal CompletionPct { get; init; }

    public decimal? Cost { get; init; }

    public string RiskNumber { get; init; } = string.Empty;

    public Guid UnitId { get; init; }

    public Guid RiskId { get; init; }
}

/// <summary>A risk child (control / KRI) with its risk's number and unit.</summary>
internal sealed class RiskChildRow<T>
    where T : class
{
    public RiskRecord Risk { get; init; } = null!;

    public T Item { get; init; } = null!;
}

/// <summary>Report-builder objects "risks" and "treatment actions" on the register's scoped query (<see cref="RiskReader"/>).</summary>
internal sealed class RiskDatasetSource(RiskDbContext db, RiskReader reader) : IReportingDatasetSource
{
    public const string RisksDataset = "risk.risks";
    public const string ActionsDataset = "risk.actions";
    public const string ControlsDataset = "risk.controls";
    public const string KrisDataset = "risk.kris";

    private static readonly DatasetQuery<RiskRecord> RiskQuery = new DatasetQuery<RiskRecord>(r => r.Id)
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
        .Field("response", r => r.ResponseCode)
        .Field("withinAppetite", r => r.WithinAppetite)
        .Field("top10", r => r.Top10)
        .Field("escalated", r => r.Escalated)
        .Field("lastReviewOn", r => r.LastReviewOn)
        .Field("nextReviewOn", r => r.NextReviewOn)
        .Field("inherentLikelihood", r => r.InherentLikelihood)
        .Field("inherentImpact", r => r.InherentImpact)
        .Field("residualLikelihood", r => r.ResidualLikelihood)
        .Field("residualImpact", r => r.ResidualImpact)
        .Field("id", r => r.Id);

    private static readonly DatasetQuery<RiskActionRow> ActionQuery = new DatasetQuery<RiskActionRow>(a => a.Id)
        .Field("number", a => a.Number)
        .Field("titleAr", a => a.TitleAr)
        .Field("titleEn", a => a.TitleEn)
        .Field("status", a => a.Status)
        .Field("owner", a => a.OwnerEmployeeId)
        .Field("plannedStart", a => a.PlannedStart)
        .Field("plannedEnd", a => a.PlannedEnd)
        .Field("actualEnd", a => a.ActualEnd)
        .Field("completion", a => a.CompletionPct)
        .Field("cost", a => a.Cost)
        .Field("riskNumber", a => a.RiskNumber)
        .Field("unit", a => a.UnitId)
        .Field("id", a => a.Id)
        .Field("riskId", a => a.RiskId);

    private static readonly DatasetQuery<RiskChildRow<Control>> ControlQuery = new DatasetQuery<RiskChildRow<Control>>(c => c.Risk.Id)
        .Field("riskNumber", c => c.Risk.Number)
        .Field("unit", c => c.Risk.UnitId)
        .Field("nameAr", c => c.Item.NameAr)
        .Field("nameEn", c => c.Item.NameEn)
        .Field("type", c => c.Item.TypeCode)
        .Field("nature", c => c.Item.NatureCode)
        .Field("owner", c => c.Item.OwnerEmployeeId)
        .Field("frequency", c => c.Item.FrequencyCode)
        .Field("effectiveness", c => c.Item.EffectivenessCode)
        .Field("lastAssessedOn", c => c.Item.LastAssessedOn)
        .Field("status", c => c.Item.Status)
        .Field("riskId", c => c.Item.RiskId);

    private static readonly DatasetQuery<RiskChildRow<Kri>> KriQuery = new DatasetQuery<RiskChildRow<Kri>>(k => k.Risk.Id)
        .Field("riskNumber", k => k.Risk.Number)
        .Field("unit", k => k.Risk.UnitId)
        .Field("cardCode", k => k.Item.CardCode)
        .Field("cardNameAr", k => k.Item.CardNameAr)
        .Field("cardNameEn", k => k.Item.CardNameEn)
        .Field("warningThreshold", k => k.Item.WarningThreshold)
        .Field("breachThreshold", k => k.Item.BreachThreshold)
        .Field("lastValue", k => k.Item.LastValue)
        .Field("lastLevel", k => k.Item.LastLevel)
        .Field("lastPeriod", k => k.Item.LastPeriod)
        .Field("status", k => k.Item.Status)
        .Field("riskId", k => k.Item.RiskId)
        .Field("kpiCardId", k => k.Item.KpiCardId);

    public string SystemCode => RiskPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(RisksDataset, RiskPermissions.SystemCode, "المخاطر", "Risks", RiskPermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("type", "نوع الخطر", "Risk type", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("owner", "مالك الخطر", "Risk owner", DatasetFieldType.Employee, Groupable: true),
            new("coordinator", "المنسق", "Coordinator", DatasetFieldType.Employee, Groupable: true),
            new("identifiedOn", "تاريخ التحديد", "Identified on", DatasetFieldType.Date),
            new("registerYear", "سنة السجل", "Register year", DatasetFieldType.Number, Groupable: true),
            new("inherentScore", "درجة الخطر الكامن", "Inherent score", DatasetFieldType.Number),
            new("inherentBand", "نطاق الخطر الكامن", "Inherent rating", DatasetFieldType.Text, Groupable: true),
            new("residualScore", "درجة الخطر المتبقي", "Residual score", DatasetFieldType.Number),
            new("residualBand", "نطاق الخطر المتبقي", "Residual rating", DatasetFieldType.Text, Groupable: true),
            new("residualStatus", "حالة الخطر المتبقي", "Residual status", DatasetFieldType.Lookup, Groupable: true),
            new("targetScore", "الدرجة المستهدفة", "Target score", DatasetFieldType.Number),
            new("response", "استراتيجية الاستجابة", "Response strategy", DatasetFieldType.Lookup, Groupable: true, LookupType: RiskPermissions.ResponseLookup),
            new("withinAppetite", "ضمن الرغبة في المخاطر", "Within appetite", DatasetFieldType.Bool, Groupable: true),
            new("top10", "ضمن أهم المخاطر", "Top-10 risk", DatasetFieldType.Bool, Groupable: true),
            new("escalated", "مصعّد", "Escalated", DatasetFieldType.Bool, Groupable: true),
            new("lastReviewOn", "آخر مراجعة", "Last review", DatasetFieldType.Date),
            new("nextReviewOn", "المراجعة القادمة", "Next review", DatasetFieldType.Date),
            new("inherentLikelihood", "الاحتمالية الكامنة", "Inherent likelihood", DatasetFieldType.Number, Groupable: true),
            new("inherentImpact", "الأثر الكامن", "Inherent impact", DatasetFieldType.Number, Groupable: true),
            new("residualLikelihood", "الاحتمالية المتبقية", "Residual likelihood", DatasetFieldType.Number, Groupable: true),
            new("residualImpact", "الأثر المتبقي", "Residual impact", DatasetFieldType.Number, Groupable: true),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/risk/risks/{id}") { KeyField = "id" },
        new(ActionsDataset, RiskPermissions.SystemCode, "إجراءات المعالجة", "Treatment actions", RiskPermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
            new("plannedStart", "البداية المخططة", "Planned start", DatasetFieldType.Date),
            new("plannedEnd", "النهاية المخططة", "Planned end", DatasetFieldType.Date),
            new("actualEnd", "النهاية الفعلية", "Actual end", DatasetFieldType.Date),
            new("completion", "نسبة الإنجاز", "Completion", DatasetFieldType.Ratio),
            new("cost", "التكلفة", "Cost", DatasetFieldType.Money),
            new("riskNumber", "رقم الخطر", "Risk number", DatasetFieldType.Text, Groupable: true),
            new("unit", "وحدة الخطر", "Risk unit", DatasetFieldType.Unit, Groupable: true),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("riskId", "الخطر", "Risk", DatasetFieldType.Reference, Sortable: false),
        ], "/risk/actions/{id}") { KeyField = "id" },
        new(ControlsDataset, RiskPermissions.SystemCode, "الضوابط", "Controls", RiskPermissions.Read,
        [
            new("riskNumber", "رقم الخطر", "Risk number", DatasetFieldType.Text, Groupable: true),
            new("unit", "وحدة الخطر", "Risk unit", DatasetFieldType.Unit, Groupable: true),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true),
            new("nature", "الطبيعة", "Nature", DatasetFieldType.Lookup, Groupable: true),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
            new("frequency", "الدورية", "Frequency", DatasetFieldType.Lookup, Groupable: true),
            new("effectiveness", "الفاعلية", "Effectiveness", DatasetFieldType.Lookup, Groupable: true),
            new("lastAssessedOn", "آخر تقييم", "Last assessed", DatasetFieldType.Date),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("riskId", "الخطر", "Risk", DatasetFieldType.Reference, Sortable: false),
        ], "/risk/risks/{id}?tab=controls"),
        new(KrisDataset, RiskPermissions.SystemCode, "مؤشرات المخاطر الرئيسية", "Key risk indicators", RiskPermissions.Read,
        [
            new("riskNumber", "رقم الخطر", "Risk number", DatasetFieldType.Text, Groupable: true),
            new("unit", "وحدة الخطر", "Risk unit", DatasetFieldType.Unit, Groupable: true),
            new("cardCode", "رمز المؤشر", "KPI code", DatasetFieldType.Text, Groupable: true),
            new("cardNameAr", "المؤشر (عربي)", "KPI (Arabic)", DatasetFieldType.Text),
            new("cardNameEn", "المؤشر (إنجليزي)", "KPI (English)", DatasetFieldType.Text),
            new("warningThreshold", "حد التحذير", "Warning threshold", DatasetFieldType.Number),
            new("breachThreshold", "حد التجاوز", "Breach threshold", DatasetFieldType.Number),
            new("lastValue", "آخر قيمة", "Last value", DatasetFieldType.Number),
            new("lastLevel", "آخر مستوى", "Last level", DatasetFieldType.Lookup, Groupable: true),
            new("lastPeriod", "آخر فترة", "Last period", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("riskId", "الخطر", "Risk", DatasetFieldType.Reference, Sortable: false),
            new("kpiCardId", "بطاقة المؤشر", "KPI card", DatasetFieldType.Reference, Sortable: false),
        ], "/risk/risks/{id}?tab=kris"),
    ];

    public IReadOnlyList<DatasetRelationship> Relationships { get; } =
    [
        new("risk.risk-actions", RisksDataset, "id", ActionsDataset, "riskId", "إجراءات المعالجة", "Treatment actions"),
        new("risk.risk-controls", RisksDataset, "id", ControlsDataset, "riskId", "الضوابط", "Controls"),
        new("risk.risk-kris", RisksDataset, "id", KrisDataset, "riskId", "مؤشرات المخاطر الرئيسية", "Key risk indicators"),
        new("risk.kri-results", KrisDataset, "kpiCardId", "kpi.results", "cardId", "نتائج المؤشر", "KPI results"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var risks = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        switch (query.Dataset)
        {
            case RisksDataset:
                return await RiskQuery.RunAsync(risks, query, cancellationToken).ConfigureAwait(false);
            case ActionsDataset:
                var rows = from a in db.Actions.AsNoTracking()
                           join r in risks on a.RiskId equals r.Id
                           select new RiskActionRow
                           {
                               Id = a.Id,
                               Number = a.Number,
                               TitleAr = a.TitleAr,
                               TitleEn = a.TitleEn,
                               Status = a.Status,
                               OwnerEmployeeId = a.OwnerEmployeeId,
                               PlannedStart = a.PlannedStart,
                               PlannedEnd = a.PlannedEnd,
                               ActualEnd = a.ActualEnd,
                               CompletionPct = a.CompletionPct,
                               Cost = a.Cost,
                               RiskNumber = r.Number,
                               UnitId = r.UnitId,
                               RiskId = r.Id,
                           };
                return await ActionQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
            case ControlsDataset:
                var controls = from c in db.Controls.AsNoTracking()
                               join r in risks on c.RiskId equals r.Id
                               select new RiskChildRow<Control> { Risk = r, Item = c };
                return await ControlQuery.RunAsync(controls, query, cancellationToken).ConfigureAwait(false);
            case KrisDataset:
                var kris = from k in db.Kris.AsNoTracking()
                           join r in risks on k.RiskId equals r.Id
                           select new RiskChildRow<Kri> { Risk = r, Item = k };
                var stable = query.Sort.Count > 0 || query.GroupBy.Count > 0 ? query : query with { Sort = [new ReportSort("riskNumber", false), new ReportSort("cardCode", false)] };
                return await KriQuery.RunAsync(kris, stable, cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}
