using Strategy.Modules.Benchmarking.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Benchmarking.Reporting;

/// <summary>AR / EN labels of Benchmarking's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/bench.json</c> words them.</summary>
internal static class BenchChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> Stages =
    [
        new(StudyStages.Plan, "التخطيط", "Plan"),
        new(StudyStages.Search, "البحث", "Search"),
        new(StudyStages.Acquire, "الاكتساب", "Acquire"),
        new(StudyStages.Analysis, "التحليل", "Analysis"),
        new(StudyStages.Completed, "مكتملة", "Completed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(StudyStatuses.Active, "نشطة", "Active"),
        new(StudyStatuses.Completed, "مكتملة", "Completed"),
        new(StudyStatuses.Cancelled, "ملغاة", "Cancelled"),
    ];
}
