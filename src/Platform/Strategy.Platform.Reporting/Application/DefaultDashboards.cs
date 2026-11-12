using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.Application;

/// <summary>
/// Seeded dashboards (docs/design/reporting.md §4, §5): one system dashboard per system, the executive dashboard, and role defaults.
/// Only metric codes of the catalogue (§2.5) are used; a widget whose metric is not installed shows "not available". Re-seeding updates a
/// seeded dashboard only while nobody edited it (<see cref="Dashboard.SeedHash"/>); edited copies are never overwritten.
/// </summary>
public static class DefaultDashboards
{
    public sealed record Seed(string Code, string Level, string? System, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn,
        IReadOnlyList<WidgetConfig> Widgets, IReadOnlyList<string> Roles);

    private static WidgetConfig W(string id, string type, string? metric, string ar, string en, int x, int y, int w, int h,
        string? dimension = null, string? series = null, Dictionary<string, List<string>>? filters = null, int? top = null, string? list = null,
        string? variant = null, Thresholds? thresholds = null, List<string>? metrics = null) =>
        new()
        {
            Id = id,
            Type = type,
            Metric = metric,
            TitleAr = ar,
            TitleEn = en,
            X = x,
            Y = y,
            W = w,
            H = h,
            Dimension = dimension,
            Series = series,
            Filters = filters,
            Top = top,
            List = list,
            Variant = variant,
            Thresholds = thresholds,
            Metrics = metrics,
        };

    private static Dictionary<string, List<string>> F(string dim, params string[] values) => new() { [dim] = [.. values] };

    private static readonly Thresholds Score = new(0.9m, 0.7m);

    public static readonly IReadOnlyList<Seed> All =
    [
        new("executive", DashboardLevels.Executive, null, "اللوحة التنفيذية", "Executive dashboard",
            "الأداء الاستراتيجي والمحفظة والمخاطر والحوكمة وصحة المؤسسة ومقارنة الإدارات", "Strategy performance, portfolio, risk, governance, organisation health and department comparison",
            [
                W("org-score", "tile", "kpi.score", "أداء المؤسسة", "Organisation score", 0, 0, 3, 2, thresholds: Score),
                W("active-projects", "tile", "portfolio.projects", "المشاريع النشطة", "Active projects", 3, 0, 3, 2, filters: F("status", "active")),
                W("severe-risks", "tile", "risk.risks", "المخاطر الحرجة والعالية", "Critical and high risks", 6, 0, 3, 2, filters: F("severe", "true"), thresholds: new(1, 3, false)),
                W("late-findings", "tile", "auditmgmt.findings", "ملاحظات التدقيق المتأخرة", "Late audit findings", 9, 0, 3, 2, filters: F("status", "late"), thresholds: new(0, 2, false)),
                W("objectives", "bar", "kpi.score", "أداء الأهداف الاستراتيجية", "Strategic objective scores", 0, 2, 6, 4, dimension: "objective", variant: "horizontal"),
                W("strategy-map", "strategyMap", "kpi.score", "خريطة الاستراتيجية", "Strategy map", 6, 2, 6, 4),
                W("kpi-trend", "line", "kpi.score", "اتجاه أداء المؤشرات", "KPI performance trend", 0, 6, 6, 3),
                W("projects-stage", "bar", "portfolio.projects", "المشاريع حسب المرحلة والصحة", "Projects by stage and health", 6, 6, 6, 3, dimension: "stage", series: "health", variant: "stacked"),
                W("budget", "bar", "portfolio.budget", "الميزانية المخططة مقابل الفعلية", "Budget vs actual", 0, 9, 6, 3, dimension: "unit", series: "kind"),
                W("delayed", "list", null, "المشاريع المتأخرة", "Delayed projects", 6, 9, 3, 3, list: "portfolio.delayed-projects", top: 5),
                W("milestones", "list", null, "المعالم القادمة", "Upcoming milestones", 9, 9, 3, 3, list: "portfolio.upcoming-milestones", top: 5),
                W("top10", "list", null, "أعلى عشرة مخاطر مؤسسية", "Top-10 institutional risks", 0, 12, 4, 4, list: "risk.top10", top: 10),
                W("outside-appetite", "tile", "risk.risks", "خارج نطاق القبول", "Outside appetite", 4, 12, 2, 2, filters: F("appetite", "outside"), thresholds: new(0, 2, false)),
                W("movement", "bar", "risk.movement", "حركة المخاطر منذ الربع الماضي", "Risk movement since last quarter", 6, 12, 6, 2, dimension: "direction"),
                W("residual-heat", "heatmap", "risk.heat", "خريطة المخاطر المتبقية", "Residual risk heat-map", 4, 14, 8, 4, dimension: "likelihood", series: "impact", filters: F("kind", "residual")),
                W("findings-age", "heatmap", "auditmgmt.findings", "ملاحظات التدقيق المفتوحة حسب الأولوية والعمر", "Open audit findings by priority and age", 0, 18, 6, 3,
                    dimension: "priority", series: "age", filters: F("status", "open", "late")),
                W("overdue-recs", "list", null, "التوصيات المتأخرة", "Overdue recommendations", 6, 18, 6, 3, list: "auditmgmt.overdue-recommendations", top: 5),
                W("cap-source", "bar", "cap.remarks", "ملاحظات خطة العمل حسب المصدر", "CAP remarks by source", 0, 21, 6, 3, dimension: "source", series: "overdue", variant: "stacked"),
                W("kpi-on-time", "gauge", "kpi.submissions-on-time", "تسليم نتائج المؤشرات في الموعد", "KPI results submitted on time", 6, 21, 2, 3, thresholds: Score),
                W("cap-on-time", "gauge", "cap.actions-on-time", "إنجاز الإجراءات التصحيحية في الموعد", "CAP actions on time", 8, 21, 2, 3, thresholds: Score),
                W("tasks-on-time", "gauge", "tasks.on-time", "إنجاز المهام في الموعد", "Tasks on time", 10, 21, 2, 3, thresholds: Score),
                W("committees", "tile", "teams.teams", "اللجان النشطة", "Active committees", 0, 24, 3, 2, filters: new() { ["committee"] = ["true"], ["status"] = ["active"] }),
                W("docs-expiring", "list", null, "وثائق تنتهي صلاحيتها قريباً", "Documents expiring soon", 3, 24, 9, 2, list: "docreg.expiring", top: 5),
                W("scorecard", "scorecard", null, "مقارنة الإدارات", "Department comparison", 0, 26, 12, 5),
                W("brief", "brief", null, "الموجز التنفيذي", "Executive brief", 0, 31, 12, 4),
            ],
            ["reporting.executive-viewer"]),
        new("portfolio.overview", DashboardLevels.System, "portfolio", "صحة المحفظة", "Portfolio health",
            "المشاريع حسب المرحلة والصحة والميزانية والمعالم", "Projects by stage and health, budget, milestones",
            [
                W("projects", "tile", "portfolio.projects", "المشاريع", "Projects", 0, 0, 3, 2),
                W("health", "tile", "portfolio.health", "نسبة المشاريع الخضراء", "Share of green projects", 3, 0, 3, 2, thresholds: new(0.8m, 0.6m)),
                W("delayed-count", "tile", "portfolio.projects", "مشاريع متأخرة", "Delayed projects", 6, 0, 3, 2, filters: F("delayed", "true"), thresholds: new(0, 2, false)),
                W("requests", "tile", "portfolio.requests", "طلبات البرامج", "Program requests", 9, 0, 3, 2),
                W("stage", "bar", "portfolio.projects", "حسب المرحلة والصحة", "By stage and health", 0, 2, 6, 4, dimension: "stage", series: "health", variant: "stacked"),
                W("heat", "heatmap", "portfolio.projects", "الصحة حسب الإدارة", "Health by department", 6, 2, 6, 4, dimension: "unit", series: "health"),
                W("budget", "bar", "portfolio.budget", "الميزانية مقابل الفعلي حسب الإدارة", "Budget vs actual by department", 0, 6, 6, 4, dimension: "unit", series: "kind"),
                W("funnel", "bar", "portfolio.requests", "مسار الطلبات", "Request pipeline", 6, 6, 6, 4, dimension: "stage"),
                W("delayed", "list", null, "المشاريع المتأخرة", "Delayed projects", 0, 10, 6, 4, list: "portfolio.delayed-projects", top: 10),
                W("milestones", "list", null, "المعالم القادمة", "Upcoming milestones", 6, 10, 6, 4, list: "portfolio.upcoming-milestones", top: 10),
            ],
            ["portfolio.pm-office", "portfolio.viewer", "portfolio.dg-office"]),
        new("kpi.scorecard", DashboardLevels.System, "kpi", "بطاقة الأداء", "KPI scorecard",
            "الأداء حسب الهدف والإدارة وتوزيع الإنجاز", "Performance by objective and department, achievement distribution",
            [
                W("score", "tile", "kpi.score", "أداء المؤسسة", "Organisation score", 0, 0, 3, 2, thresholds: Score),
                W("cards", "tile", "kpi.cards", "المؤشرات", "KPIs", 3, 0, 3, 2),
                W("pending", "tile", "kpi.pending", "نتائج قيد الإجراء", "Results pending", 6, 0, 3, 2),
                W("on-time", "gauge", "kpi.submissions-on-time", "التسليم في الموعد", "Submitted on time", 9, 0, 3, 2, thresholds: Score),
                W("achievement", "bar", "kpi.results", "توزيع الإنجاز", "Achievement distribution", 0, 2, 6, 4, dimension: "rag"),
                W("by-dept", "bar", "kpi.score", "الأداء حسب الإدارة", "Score by department", 6, 2, 6, 4, dimension: "unit", variant: "horizontal"),
                W("map", "strategyMap", "kpi.score", "خريطة الاستراتيجية", "Strategy map", 0, 6, 12, 4),
                W("top", "list", null, "الأفضل أداءً", "Top performers", 0, 10, 6, 4, list: "kpi.top-performers", top: 5),
                W("bottom", "list", null, "الأضعف أداءً", "Bottom performers", 6, 10, 6, 4, list: "kpi.bottom-performers", top: 5),
            ],
            ["kpi.strategy-office", "kpi.executive"]),
        new("risk.profile", DashboardLevels.System, "risk", "ملف المخاطر", "Risk profile",
            "المخاطر حسب الفئة والتصنيف وخرائط المخاطر والحركة", "Risks by category and rating, heat-maps, movement",
            [
                W("risks", "tile", "risk.risks", "المخاطر", "Risks", 0, 0, 3, 2),
                W("severe", "tile", "risk.risks", "حرجة وعالية", "Critical and high", 3, 0, 3, 2, filters: F("severe", "true"), thresholds: new(1, 3, false)),
                W("outside", "tile", "risk.risks", "خارج نطاق القبول", "Outside appetite", 6, 0, 3, 2, filters: F("appetite", "outside"), thresholds: new(0, 2, false)),
                W("reviews", "tile", "risk.reviews-overdue", "مراجعات متأخرة", "Overdue reviews", 9, 0, 3, 2, thresholds: new(0, 3, false)),
                W("inherent", "heatmap", "risk.heat", "المخاطر الكامنة", "Inherent risk", 0, 2, 6, 4, dimension: "likelihood", series: "impact", filters: F("kind", "inherent")),
                W("residual", "heatmap", "risk.heat", "المخاطر المتبقية", "Residual risk", 6, 2, 6, 4, dimension: "likelihood", series: "impact", filters: F("kind", "residual")),
                W("category", "bar", "risk.risks", "حسب الفئة", "By category", 0, 6, 6, 4, dimension: "category", series: "band", variant: "stacked"),
                W("movement", "bar", "risk.movement", "الحركة", "Movement", 6, 6, 6, 4, dimension: "direction"),
                W("top10", "list", null, "أعلى عشرة مخاطر", "Top-10 risks", 0, 10, 6, 4, list: "risk.top10", top: 10),
                W("actions", "bar", "risk.actions", "إجراءات المعالجة", "Treatment actions", 6, 10, 6, 4, dimension: "status", series: "overdue", variant: "stacked"),
            ],
            ["risk.risk-office", "risk.risk-committee", "risk.viewer"]),
        new("teams.overview", DashboardLevels.System, "teams", "تقييم الفرق", "Team evaluations",
            "الفرق واللجان وتقدم التقييم والدرجات", "Teams and committees, evaluation progress and scores",
            [
                W("teams", "tile", "teams.teams", "الفرق", "Teams", 0, 0, 4, 2),
                W("committees", "tile", "teams.teams", "اللجان النشطة", "Active committees", 4, 0, 4, 2, filters: new() { ["committee"] = ["true"], ["status"] = ["active"] }),
                W("score", "tile", "teams.score", "متوسط درجة التقييم", "Average evaluation score", 8, 0, 4, 2),
                W("type", "bar", "teams.teams", "حسب النوع والحالة", "By type and status", 0, 2, 6, 4, dimension: "type", series: "status", variant: "stacked"),
                W("evaluations", "bar", "teams.evaluations", "تقدم التقييم", "Evaluation progress", 6, 2, 6, 4, dimension: "status"),
            ],
            ["teams.viewer", "teams.administrator"]),
        new("tasks.performance", DashboardLevels.System, "tasks", "أداء المهام", "Task performance",
            "المهام المفتوحة والمتأخرة والإنجاز في الموعد", "Open and overdue tasks, on-time completion",
            [
                W("open", "tile", "tasks.tasks", "مهام مفتوحة", "Open tasks", 0, 0, 3, 2),
                W("overdue", "tile", "tasks.tasks", "مهام متأخرة", "Overdue tasks", 3, 0, 3, 2, filters: F("overdue", "true"), thresholds: new(2, 5, false)),
                W("on-time", "gauge", "tasks.on-time", "الإنجاز في الموعد", "Completed on time", 6, 0, 3, 2, thresholds: Score),
                W("accept", "tile", "tasks.hours-to-accept", "متوسط ساعات القبول", "Average hours to accept", 9, 0, 3, 2),
                W("by-dept", "bar", "tasks.tasks", "حسب الإدارة", "By department", 0, 2, 6, 4, dimension: "unit", series: "overdue", variant: "stacked"),
                W("by-status", "bar", "tasks.tasks", "حسب الحالة والأولوية", "By status and priority", 6, 2, 6, 4, dimension: "status", series: "priority", variant: "stacked"),
                W("completed", "line", "tasks.completed", "المهام المنجزة", "Tasks completed", 0, 6, 6, 4),
                W("overdue-list", "list", null, "مهام متأخرة", "Overdue tasks", 6, 6, 6, 4, list: "tasks.overdue", top: 10),
            ],
            ["tasks.manager"]),
        new("docreg.library", DashboardLevels.System, "docreg", "حالة مكتبة الوثائق", "Document library status",
            "الوثائق حسب المستوى والنوع والوحدة والانتهاء", "Documents by level, type and unit, expiry",
            [
                W("docs", "tile", "docreg.documents", "الوثائق", "Documents", 0, 0, 4, 2),
                W("expiring", "tile", "docreg.documents", "تنتهي قريباً", "Expiring soon", 4, 0, 4, 2, filters: F("expiring", "true"), thresholds: new(0, 3, false)),
                W("level", "bar", "docreg.documents", "حسب المستوى والنوع", "By level and type", 0, 2, 6, 4, dimension: "level", series: "type", variant: "stacked"),
                W("status", "bar", "docreg.documents", "حسب الحالة", "By status", 6, 2, 6, 4, dimension: "status"),
                W("expiring-list", "list", null, "وثائق تنتهي قريباً", "Documents expiring soon", 0, 6, 12, 4, list: "docreg.expiring", top: 10),
            ],
            ["docreg.office", "docreg.leadership"]),
        new("ip.register", DashboardLevels.System, "ip", "سجل الملكية الفكرية", "IP register",
            "الملكية الفكرية حسب النوع والوحدة والحالة والانتهاء", "IP by type, unit and status, upcoming expiries",
            [
                W("records", "tile", "ip.records", "السجلات", "Records", 0, 0, 4, 2),
                W("expiring", "tile", "ip.records", "تنتهي قريباً", "Expiring soon", 4, 0, 4, 2, filters: F("expiring", "true")),
                W("type", "bar", "ip.records", "حسب النوع والحالة", "By type and status", 0, 2, 6, 4, dimension: "type", series: "status", variant: "stacked"),
                W("unit", "bar", "ip.records", "حسب الوحدة", "By unit", 6, 2, 6, 4, dimension: "unit"),
                W("expiring-list", "list", null, "تنتهي قريباً", "Upcoming expiries", 0, 6, 12, 4, list: "ip.expiring", top: 10),
            ],
            []),
        new("bench.plan", DashboardLevels.System, "bench", "خطة المقارنات المعيارية", "Benchmarking plan",
            "الدراسات حسب المرحلة والربع والإجراءات والممارسات المعتمدة", "Studies by stage and quarter, actions, practices adopted",
            [
                W("studies", "tile", "bench.studies", "الدراسات", "Studies", 0, 0, 4, 2),
                W("practices", "tile", "bench.practices-adopted", "ممارسات معتمدة", "Practices adopted", 4, 0, 4, 2),
                W("actions", "tile", "bench.actions", "الإجراءات", "Actions", 8, 0, 4, 2),
                W("stage", "bar", "bench.studies", "حسب المرحلة والربع", "By stage and quarter", 0, 2, 6, 4, dimension: "stage", series: "quarter", variant: "stacked"),
                W("actions-status", "bar", "bench.actions", "الإجراءات حسب الحالة", "Actions by status", 6, 2, 6, 4, dimension: "status"),
            ],
            ["bench.bench-office", "bench.viewer"]),
        new("cap.closure", DashboardLevels.System, "cap", "إغلاق خطة العمل التصحيحية", "CAP closure",
            "الملاحظات والإجراءات والتأخر ومعدل الإغلاق", "Remarks, actions, overdue and closure rate",
            [
                W("remarks", "tile", "cap.remarks", "الملاحظات", "Remarks", 0, 0, 3, 2),
                W("overdue", "tile", "cap.remarks", "ملاحظات متأخرة", "Overdue remarks", 3, 0, 3, 2, filters: F("overdue", "true"), thresholds: new(0, 3, false)),
                W("closure", "gauge", "cap.closure", "معدل الإغلاق", "Closure rate", 6, 0, 3, 2, thresholds: new(0.8m, 0.6m)),
                W("on-time", "gauge", "cap.actions-on-time", "في الموعد", "On time", 9, 0, 3, 2, thresholds: Score),
                W("source", "bar", "cap.remarks", "حسب المصدر والحالة", "By source and status", 0, 2, 6, 4, dimension: "source", series: "status", variant: "stacked"),
                W("actions", "bar", "cap.actions", "الإجراءات حسب الحالة", "Actions by status", 6, 2, 6, 4, dimension: "status"),
                W("dept", "bar", "cap.closure", "معدل الإغلاق حسب الوحدة", "Closure rate by unit", 0, 6, 6, 4, dimension: "unit", variant: "horizontal"),
                W("overdue-list", "list", null, "ملاحظات متأخرة", "Overdue remarks", 6, 6, 6, 4, list: "cap.overdue-remarks", top: 10),
            ],
            ["cap.cap-office", "cap.viewer"]),
        new("auditmgmt.closure", DashboardLevels.System, "auditmgmt", "إغلاق ملاحظات التدقيق", "Audit closure",
            "الملاحظات حسب الحالة والأولوية والعمر ومعدل الإغلاق", "Findings by status, priority and age, closure rate",
            [
                W("findings", "tile", "auditmgmt.findings", "الملاحظات", "Findings", 0, 0, 3, 2),
                W("late", "tile", "auditmgmt.findings", "متأخرة", "Late", 3, 0, 3, 2, filters: F("status", "late"), thresholds: new(0, 2, false)),
                W("closure", "gauge", "auditmgmt.closure", "معدل الإغلاق", "Closure rate", 6, 0, 3, 2, thresholds: new(0.8m, 0.6m)),
                W("recs", "tile", "auditmgmt.recommendations", "توصيات متأخرة", "Overdue recommendations", 9, 0, 3, 2, filters: F("overdue", "true"), thresholds: new(0, 2, false)),
                W("status", "bar", "auditmgmt.findings", "حسب الحالة والأولوية", "By status and priority", 0, 2, 6, 4, dimension: "status", series: "priority", variant: "stacked"),
                W("age", "heatmap", "auditmgmt.findings", "الملاحظات المفتوحة حسب العمر", "Open findings by age", 6, 2, 6, 4, dimension: "priority", series: "age", filters: F("status", "open", "late")),
                W("dept", "bar", "auditmgmt.closure", "معدل الإغلاق حسب الإدارة", "Closure rate by department", 0, 6, 6, 4, dimension: "unit", variant: "horizontal"),
                W("late-list", "list", null, "ملاحظات متأخرة", "Late findings", 6, 6, 6, 4, list: "auditmgmt.late-findings", top: 10),
            ],
            ["auditmgmt.audit-office", "auditmgmt.audit-committee"]),
        new("requests.overview", DashboardLevels.System, "requests", "الطلبات", "Requests",
            "الطلبات حسب النوع والحالة ومدة الإنجاز", "Requests by type and status, cycle time",
            [
                W("requests", "tile", "requests.requests", "الطلبات", "Requests", 0, 0, 6, 2),
                W("cycle", "tile", "requests.cycle-days", "متوسط أيام الإنجاز", "Average cycle days", 6, 0, 6, 2),
                W("type", "bar", "requests.requests", "حسب النوع والحالة", "By type and status", 0, 2, 12, 4, dimension: "type", series: "status", variant: "stacked"),
            ],
            []),
    ];

    public static async Task SeedAsync(ReportingDbContext db, IClock clock, CancellationToken ct)
    {
        foreach (var seed in All)
        {
            var json = JsonSerializer.Serialize(new DashboardDefinition { Widgets = [.. seed.Widgets] }, DashboardService.Json);
            var hash = DashboardService.Hash(json + seed.TitleAr + seed.TitleEn);
            var existing = await db.Dashboards.FirstOrDefaultAsync(d => d.Code == seed.Code, ct).ConfigureAwait(false);
            if (existing is null)
            {
                existing = new Dashboard(seed.Code, seed.TitleAr, seed.TitleEn, seed.Level, seed.System, null, json);
                existing.Describe(seed.TitleAr, seed.TitleEn, seed.DescriptionAr, seed.DescriptionEn);
                existing.Seeded(json, hash, clock.UtcNow);
                db.Dashboards.Add(existing);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                var sort = 0;
                foreach (var role in seed.Roles)
                {
                    if (!await db.DashboardRoles.AnyAsync(r => r.RoleKey == role && r.IsDefault, ct).ConfigureAwait(false))
                    {
                        db.DashboardRoles.Add(new DashboardRole(existing.Id, role, sort++, true));
                    }
                }
            }
            else if (existing.SeedHash is not null && existing.SeedHash != hash)
            {
                existing.Describe(seed.TitleAr, seed.TitleEn, seed.DescriptionAr, seed.DescriptionEn);
                existing.Seeded(json, hash, clock.UtcNow);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Seeds the default dashboards after migrations (idempotent).</summary>
internal sealed class ReportingSeeder : IDatabaseSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = (ReportingDbContext)services.GetService(typeof(ReportingDbContext))!;
        var clock = (IClock)services.GetService(typeof(IClock))!;
        await DefaultDashboards.SeedAsync(db, clock, cancellationToken).ConfigureAwait(false);
        var registry = (MetricRegistry)services.GetService(typeof(MetricRegistry))!;
        await ReportTypes.ReportTypeService.SeedAsync(db, registry, cancellationToken).ConfigureAwait(false);
        await Dossiers.DossierTemplateService.SeedAsync(db, registry, cancellationToken).ConfigureAwait(false);
    }
}
