using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

public sealed record StatusReportFile(byte[] Content, string FileName);

/// <summary>
/// Bilingual monthly status report (SPEC §1.12, ADR 0097): rendered on demand through Gotenberg from the approved monthly update's snapshot.
/// Cost figures only for callers who may see costs (ADR 0098).
/// </summary>
internal sealed class StatusReportService(
    PortfolioDbContext db, ProjectGuard guard, RequestReader reader, HealthService health, CostVisibility costs, IPdfRenderer pdf, ISettingsStore settings, IAuditLog audit,
    IRequestContext context, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record SnapshotItem(string? Code, string? TitleAr, string? TitleEn, decimal Weight, decimal Approved, decimal? Achieve, DateOnly? ActualEnd, string? Causality,
        string? CorrectiveActions);

    private sealed record SprintItem(int Number, string? Name, DateOnly? StartDate, DateOnly? EndDate, int CommittedPoints, int CompletedPoints, string? DemoOutcome);

    public async Task<Result<StatusReportFile>> RenderAsync(Guid projectId, string? period, string? language, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var query = db.MonthlyUpdates.AsNoTracking().Where(m => m.ProjectId == projectId && m.Status == MonthlyStatuses.Approved);
        if (!string.IsNullOrWhiteSpace(period))
        {
            query = query.Where(m => m.Period == period);
        }

        var update = await query.OrderByDescending(m => m.Period).ThenByDescending(m => m.Sequence).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (update is null)
        {
            return Error.Rule("portfolio.statusReport.none", "portfolio.errors.noApprovedUpdate");
        }

        var lang = language == "en" ? "en" : "ar";
        var html = await HtmlAsync(project, update, lang, ct).ConfigureAwait(false);
        byte[] bytes;
        try
        {
            bytes = await pdf.RenderAsync(html, ct).ConfigureAwait(false);
        }
        catch (PdfRenderingException ex)
        {
            return Error.Rule("portfolio.statusReport.failed", ex.MessageKey);
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, PortfolioPermissions.SystemCode, Project.EntityType, project.Id.ToString("D"),
            new { report = "status", period = update.Period, language = lang }), ct).ConfigureAwait(false);
        return new StatusReportFile(bytes, $"{project.Number}-status-{update.Period}-{lang}.pdf");
    }

    /// <summary>The report's HTML (public for tests). Everything is encoded; no script, no external resources.</summary>
    public async Task<string> HtmlAsync(Project project, MonthlyUpdate update, string lang, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(update);
        var ar = lang == "ar";
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var showCosts = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var portfolioSettings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([project.PmEmployeeId, project.OwnerEmployeeId], ct).ConfigureAwait(false);
        var items = JsonSerializer.Deserialize<List<SnapshotItem>>(update.Items, Json) ?? [];
        var sprints = JsonSerializer.Deserialize<List<SprintItem>>(update.Sprints, Json) ?? [];
        var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == project.Id && r.Status == ItemStatuses.Active).OrderByDescending(r => r.ResidualScore).Take(5)
            .ToListAsync(ct).ConfigureAwait(false);
        var issues = portfolioSettings.Features.Raid
            ? await db.RaidItems.AsNoTracking().Where(r => r.ProjectId == project.Id && r.Type == RaidTypes.Issue && r.Status == RaidStatuses.Open).Take(10).ToListAsync(ct)
                .ConfigureAwait(false)
            : [];
        var projectHealth = portfolioSettings.Features.Health ? (await health.BulkAsync([project], ct).ConfigureAwait(false))[project.Id] : null;
        var color = System.Text.RegularExpressions.Regex.IsMatch(branding.PrimaryColor ?? string.Empty, "^#[0-9a-fA-F]{6}$") ? branding.PrimaryColor! : "#1f3a68";
        string T(string a, string e) => ar ? a : e;
        string N(string? a, string? e) => (ar ? a ?? e : e ?? a) ?? string.Empty;
        string P(decimal ratio) => (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        string M(decimal? m) => m?.ToString("#,0.00", CultureInfo.InvariantCulture) ?? "—";
        string Rag(string? rag) => rag switch
        {
            "green" => T("أخضر", "Green"),
            "amber" => T("كهرماني", "Amber"),
            "red" => T("أحمر", "Red"),
            _ => T("لا بيانات", "No data"),
        };

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(lang).Append("\" dir=\"").Append(ar ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(project.Number)).Append("</title><style>").Append(PdfFonts.FontFaceCss())
            .Append("@page{size:A4;margin:14mm 12mm}body{font-family:'").Append(PdfFonts.Family).Append("',sans-serif;font-size:9.5pt;color:#1b1f24;margin:0}")
            .Append(".band{display:flex;justify-content:space-between;border-bottom:3px solid ").Append(color).Append(";padding-bottom:6px;margin-bottom:10px}")
            .Append(".ar{direction:rtl;text-align:right}.en{direction:ltr;text-align:left}h1{font-size:14pt;margin:0;color:").Append(color).Append('}')
            .Append("h2{font-size:11pt;color:").Append(color).Append(";margin:12px 0 4px}.org{font-size:8.5pt;color:#57606a}")
            .Append(".kpis{display:flex;gap:8px;margin:6px 0}.kpi{flex:1;border:1px solid #d0d7de;border-radius:4px;padding:6px}.kpi b{display:block;font-size:12pt}")
            .Append(".rag{display:inline-block;padding:1px 6px;border-radius:8px;color:#fff;font-size:8pt}.green{background:#1a7f37}.amber{background:#bf8700}.red{background:#cf222e}.grey{background:#6e7781}")
            .Append("table{width:100%;border-collapse:collapse;font-size:8.5pt}th{background:").Append(color).Append(";color:#fff}")
            .Append("th,td{border:1px solid #d0d7de;padding:3px 5px;text-align:start;vertical-align:top}p{margin:0 0 6px;line-height:1.6;white-space:pre-wrap}")
            .Append("</style></head><body>");
        html.Append("<div class=\"band\" dir=\"ltr\"><div class=\"en\"><h1>Monthly status report</h1><div class=\"org\">").Append(E(branding.OrganisationNameEn))
            .Append("</div></div><div class=\"ar\"><h1>تقرير حالة المشروع الشهري</h1><div class=\"org\">").Append(E(branding.OrganisationNameAr)).Append("</div></div></div>");
        html.Append("<table><tr><th>").Append(T("المشروع", "Project")).Append("</th><td>").Append(E(project.Number)).Append(" — ").Append(E(N(project.NameAr, project.NameEn)))
            .Append("</td><th>").Append(T("الفترة", "Period")).Append("</th><td>").Append(E(update.Period)).Append("</td></tr><tr><th>").Append(T("مدير المشروع", "Project manager"))
            .Append("</th><td>").Append(E(project.PmEmployeeId is { } pm && people.TryGetValue(pm, out var pmp) ? N(pmp.NameAr, pmp.NameEn) : "—")).Append("</td><th>")
            .Append(T("المالك", "Owner")).Append("</th><td>")
            .Append(E(project.OwnerEmployeeId is { } o && people.TryGetValue(o, out var op) ? N(op.NameAr, op.NameEn) : "—")).Append("</td></tr><tr><th>")
            .Append(T("المنهجية", "Methodology")).Append("</th><td>").Append(E(Method(project.Methodology, ar))).Append("</td><th>").Append(T("تاريخ الاعتماد", "Approved on"))
            .Append("</th><td>").Append(E(update.ApprovedAt?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</td></tr></table>");

        html.Append("<div class=\"kpis\"><div class=\"kpi\">").Append(T("الإنجاز المعتمد", "Approved progress")).Append("<b>").Append(P(project.Performance))
            .Append("</b></div><div class=\"kpi\">").Append(T("المخطط حتى تاريخه", "Planned to date")).Append("<b>").Append(P(project.PlannedProgress))
            .Append("</b></div><div class=\"kpi\">").Append(T("حالة التقدم", "Progress status")).Append("<b>")
            .Append(project.ProgressStatus == ProgressCalculator.Delayed ? T("متأخر", "Delayed") : T("في المسار", "On track")).Append("</b></div>");
        if (projectHealth is not null)
        {
            html.Append("<div class=\"kpi\">").Append(T("الصحة العامة", "Overall health")).Append("<b><span class=\"rag ").Append(projectHealth.Overall.Effective).Append("\">")
                .Append(Rag(projectHealth.Overall.Effective)).Append("</span></b></div>");
        }

        html.Append("</div>");
        if (projectHealth is not null)
        {
            html.Append("<table><tr>");
            foreach (var (label, value) in new[]
                     {
                         (T("الجدول الزمني", "Schedule"), projectHealth.Schedule), (T("التكلفة", "Cost"), projectHealth.Cost), (T("النطاق", "Scope"), projectHealth.Scope),
                         (T("المخاطر", "Risk"), projectHealth.Risk),
                     })
            {
                html.Append("<td>").Append(label).Append(": <span class=\"rag ").Append(value.Effective).Append("\">").Append(Rag(value.Effective)).Append("</span></td>");
            }

            html.Append("</tr></table>");
        }

        html.Append("<h2>").Append(T("ملخص الفترة", "Summary of the period")).Append("</h2><p>")
            .Append(E((ar ? update.NarrativeAr ?? update.NarrativeEn : update.NarrativeEn ?? update.NarrativeAr) ?? update.SubmitComment ?? T("لا يوجد ملخص.", "No summary.")))
            .Append("</p>");
        if (items.Count > 0)
        {
            html.Append("<h2>").Append(T("المراحل المحدّثة", "Milestones updated")).Append("</h2><table><thead><tr><th>").Append(T("الرمز", "Code")).Append("</th><th>")
                .Append(T("المرحلة", "Milestone")).Append("</th><th>").Append(T("الوزن", "Weight")).Append("</th><th>").Append(T("الإنجاز", "Achieved")).Append("</th><th>")
                .Append(T("تاريخ الانتهاء الفعلي", "Actual end")).Append("</th><th>").Append(T("أسباب التأخر / الإجراءات", "Delay causes / actions")).Append("</th></tr></thead><tbody>");
            foreach (var i in items)
            {
                html.Append("<tr><td>").Append(E(i.Code)).Append("</td><td dir=\"auto\">").Append(E(N(i.TitleAr, i.TitleEn))).Append("</td><td>").Append(P(i.Weight)).Append("</td><td>")
                    .Append(P(i.Achieve ?? i.Approved)).Append("</td><td>").Append(D(i.ActualEnd)).Append("</td><td dir=\"auto\">")
                    .Append(E(string.Join(" / ", new[] { i.Causality, i.CorrectiveActions }.Where(x => !string.IsNullOrWhiteSpace(x))))).Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (sprints.Count > 0)
        {
            html.Append("<h2>").Append(T("السبرنتات", "Sprints")).Append("</h2><table><thead><tr><th>#</th><th>").Append(T("السبرنت", "Sprint")).Append("</th><th>")
                .Append(T("التواريخ", "Dates")).Append("</th><th>").Append(T("النقاط الملتزم بها", "Committed")).Append("</th><th>").Append(T("النقاط المنجزة", "Completed"))
                .Append("</th><th>").Append(T("نتيجة العرض", "Demo outcome")).Append("</th></tr></thead><tbody>");
            foreach (var s in sprints)
            {
                html.Append("<tr><td>").Append(s.Number).Append("</td><td>").Append(E(s.Name)).Append("</td><td>").Append(D(s.StartDate)).Append(" → ").Append(D(s.EndDate))
                    .Append("</td><td>").Append(s.CommittedPoints).Append("</td><td>").Append(s.CompletedPoints).Append("</td><td dir=\"auto\">").Append(E(s.DemoOutcome))
                    .Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (risks.Count > 0)
        {
            html.Append("<h2>").Append(T("أبرز المخاطر", "Top risks")).Append("</h2><table><thead><tr><th>").Append(T("الخطر", "Risk")).Append("</th><th>")
                .Append(T("الاحتمالية × الأثر", "Probability × impact")).Append("</th><th>").Append(T("التصنيف المتبقي", "Residual band")).Append("</th><th>")
                .Append(T("إجراءات التخفيف", "Mitigation")).Append("</th></tr></thead><tbody>");
            foreach (var r in risks)
            {
                html.Append("<tr><td dir=\"auto\">").Append(E(r.Title)).Append("</td><td>").Append(r.Probability).Append(" × ").Append(r.Impact).Append("</td><td>")
                    .Append(E(r.ResidualBand)).Append("</td><td dir=\"auto\">").Append(E(r.MitigationActions)).Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (issues.Count > 0)
        {
            html.Append("<h2>").Append(T("القضايا المفتوحة", "Open issues")).Append("</h2><table><tbody>");
            foreach (var i in issues)
            {
                html.Append("<tr><td>").Append(E(i.Code)).Append("</td><td dir=\"auto\">").Append(E(i.Title)).Append("</td><td>").Append(D(i.DueDate)).Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (showCosts)
        {
            var actual = new[] { project.Year1ActualCost, project.Year2ActualCost, project.Year3ActualCost, project.Year4ActualCost }.Sum(a => a ?? 0);
            html.Append("<h2>").Append(T("الميزانية", "Budget")).Append("</h2><table><tr><th>").Append(T("الميزانية المعتمدة", "Approved budget")).Append("</th><td>")
                .Append(M(project.Budget)).Append("</td><th>").Append(T("التكلفة الفعلية", "Actual cost")).Append("</th><td>").Append(M(actual)).Append("</td></tr></table>");
        }

        html.Append("<p class=\"org\" style=\"margin-top:10px\">").Append(T("أُنشئ تلقائياً من التحديث الشهري المعتمد", "Generated from the approved monthly update")).Append(" · ")
            .Append(clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append("</p></body></html>");
        return html.ToString();
    }

    private static string Method(string methodology, bool ar) => methodology switch
    {
        Methodologies.Agile => ar ? "رشيقة (Agile)" : "Agile",
        Methodologies.Hybrid => ar ? "هجينة" : "Hybrid",
        _ => ar ? "تقليدية (شلالية)" : "Waterfall",
    };

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
