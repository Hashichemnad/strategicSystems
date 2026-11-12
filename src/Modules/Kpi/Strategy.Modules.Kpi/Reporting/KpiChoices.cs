using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>AR / EN labels of Performance Management's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/kpi.json</c> words them.</summary>
internal static class KpiChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> CardStatuses =
    [
        new(Domain.CardStatuses.Active, "فعّال", "Active"),
        new(Domain.CardStatuses.Inactive, "موقوف", "Inactive"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Levels =
    [
        new(CardLevels.Organisation, "المؤسسة", "Organisation"),
        new(CardLevels.Department, "الإدارة", "Department"),
        new(CardLevels.Section, "القسم", "Section"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Natures =
    [
        new(Domain.Natures.Quantitative, "كمي", "Quantitative"),
        new(Domain.Natures.Qualitative, "وصفي (سلّم)", "Qualitative (scale)"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> MeasuringPeriods =
    [
        new(Domain.MeasuringPeriods.Quarterly, "ربع سنوي", "Quarterly"),
        new(Domain.MeasuringPeriods.HalfYearly, "نصف سنوي", "Half-yearly"),
        new(Domain.MeasuringPeriods.Annual, "سنوي", "Annual"),
        new(Domain.MeasuringPeriods.TwoYear, "كل سنتين", "Two-year"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Directions =
    [
        new(Contracts.Directions.Increase, "الزيادة أفضل", "Increase is better"),
        new(Contracts.Directions.Decrease, "الانخفاض أفضل", "Decrease is better"),
        new(Contracts.Directions.Matching, "المطابقة أفضل", "Matching is better"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Periods =
    [
        new(PeriodCodes.Q1, "الربع الأول", "Q1"),
        new(PeriodCodes.Q2, "الربع الثاني", "Q2"),
        new(PeriodCodes.Q3, "الربع الثالث", "Q3"),
        new(PeriodCodes.Q4, "الربع الرابع", "Q4"),
        new(PeriodCodes.H1, "النصف الأول", "First half"),
        new(PeriodCodes.H2, "النصف الثاني", "Second half"),
        new(PeriodCodes.FullYear, "السنة كاملة", "Full year"),
        new(PeriodCodes.TwoYear, "سنتان", "Two years"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ResultStatuses =
    [
        new(Domain.ResultStatuses.NotOpen, "غير مفتوحة", "Not open"),
        new(Domain.ResultStatuses.Open, "مفتوحة للتحديث", "Open for update"),
        new(Domain.ResultStatuses.PendingApproval, "بانتظار الاعتماد", "Waiting for approval"),
        new(Domain.ResultStatuses.Returned, "معادة للتصحيح", "Returned for correction"),
        new(Domain.ResultStatuses.Approved, "معتمدة", "Approved"),
        new(Domain.ResultStatuses.Locked, "مقفلة", "Locked"),
    ];
}
