using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

/// <summary>One column of the department comparison scorecard (ADR 0146): a metric with optional dimension filters and RAG limits.</summary>
public sealed record ScorecardColumn
{
    public string Code { get; init; } = string.Empty;

    public string LabelAr { get; init; } = string.Empty;

    public string LabelEn { get; init; } = string.Empty;

    public string Metric { get; init; } = string.Empty;

    public Dictionary<string, List<string>> Filters { get; init; } = [];

    /// <summary>Green from this value (higher is better) or up to it (lower is better).</summary>
    public decimal Green { get; init; }

    public decimal Amber { get; init; }

    public bool HigherIsBetter { get; init; } = true;

    /// <summary>Drill target: the system dashboard code.</summary>
    public string? Dashboard { get; init; }
}

/// <summary>Settings → Reporting (docs/design/reporting.md §2–§9). Every value is a seeded default the organisation can change.</summary>
public sealed record ReportingSettings : ISettingsArea
{
    public static string Key => "reporting";

    /// <summary>Organisation level whose units are the "departments" of the comparison scorecard; lower units roll up.</summary>
    public string DepartmentLevelCode { get; init; } = "department";

    /// <summary>First month of the fiscal year (quarters and years).</summary>
    public int FiscalStartMonth { get; init; } = 1;

    /// <summary>Full refresh of the current month every N minutes (dirty systems refresh within about a minute).</summary>
    public int RefreshMinutes { get; init; } = 10;

    /// <summary><c>attachment</c> or <c>link</c> (installations that forbid attachments).</summary>
    public string DeliveryMode { get; init; } = "attachment";

    public int ExportRowLimit { get; init; } = 50_000;

    /// <summary>Attachments larger than this are sent as a link to the authorised download.</summary>
    public int AttachmentLimitMb { get; init; } = 10;

    /// <summary>Generated files kept for download (days).</summary>
    public int DeliveryRetentionDays { get; init; } = 30;

    public List<ScorecardColumn> Scorecard { get; init; } = DefaultScorecard();

    public static List<ScorecardColumn> DefaultScorecard() =>
    [
        new() { Code = "kpi-score", LabelAr = "أداء المؤشرات", LabelEn = "KPI score", Metric = "kpi.score", Green = 0.9m, Amber = 0.7m, Dashboard = "kpi.scorecard" },
        new() { Code = "project-health", LabelAr = "صحة المشاريع", LabelEn = "Project health", Metric = "portfolio.health", Green = 0.8m, Amber = 0.6m, Dashboard = "portfolio.overview" },
        new()
        {
            Code = "projects-red", LabelAr = "مشاريع حمراء", LabelEn = "Red projects", Metric = "portfolio.projects",
            Filters = new() { ["health"] = ["red"] }, Green = 0, Amber = 1, HigherIsBetter = false, Dashboard = "portfolio.overview",
        },
        new()
        {
            Code = "severe-risks", LabelAr = "مخاطر عالية وحرجة", LabelEn = "High and critical risks", Metric = "risk.risks",
            Filters = new() { ["severe"] = ["true"] }, Green = 1, Amber = 3, HigherIsBetter = false, Dashboard = "risk.profile",
        },
        new()
        {
            Code = "outside-appetite", LabelAr = "خارج نطاق القبول", LabelEn = "Outside appetite", Metric = "risk.risks",
            Filters = new() { ["appetite"] = ["outside"] }, Green = 0, Amber = 2, HigherIsBetter = false, Dashboard = "risk.profile",
        },
        new()
        {
            Code = "open-findings", LabelAr = "ملاحظات تدقيق مفتوحة", LabelEn = "Open audit findings", Metric = "auditmgmt.findings",
            Filters = new() { ["status"] = ["open", "late"] }, Green = 2, Amber = 5, HigherIsBetter = false, Dashboard = "auditmgmt.closure",
        },
        new()
        {
            Code = "late-findings", LabelAr = "ملاحظات متأخرة", LabelEn = "Late findings", Metric = "auditmgmt.findings",
            Filters = new() { ["status"] = ["late"] }, Green = 0, Amber = 1, HigherIsBetter = false, Dashboard = "auditmgmt.closure",
        },
        new() { Code = "cap-closure", LabelAr = "إغلاق الإجراءات التصحيحية", LabelEn = "CAP closure", Metric = "cap.closure", Green = 0.8m, Amber = 0.6m, Dashboard = "cap.closure" },
        new()
        {
            Code = "overdue-tasks", LabelAr = "مهام متأخرة", LabelEn = "Overdue tasks", Metric = "tasks.tasks",
            Filters = new() { ["overdue"] = ["true"] }, Green = 2, Amber = 5, HigherIsBetter = false, Dashboard = "tasks.performance",
        },
    ];
}
