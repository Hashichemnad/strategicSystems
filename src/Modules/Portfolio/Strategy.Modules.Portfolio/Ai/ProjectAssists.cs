using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Modules.Portfolio.Ai;

/// <summary>Phase 10 AI assists (SPEC §1.15 B, ADR 0099). Drafts only: nothing is saved by these calls.</summary>
public static class ProjectAssistFeatures
{
    public const string CharterAssistant = "portfolio-charter-assistant";
    public const string PlanGenerator = "portfolio-plan-generator";
    public const string UpdateAssistant = "portfolio-update-assistant";
    public const string StatusReport = "portfolio-status-report";
    public const string ChangeImpact = "portfolio-change-impact";
    public const string Insights = "portfolio-insights";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(CharterAssistant, "مساعد ميثاق المشروع", "Charter assistant", PortfolioPermissions.AiAssist),
        new(PlanGenerator, "مولّد خطة المشروع", "Plan generator", PortfolioPermissions.AiAssist),
        new(UpdateAssistant, "مساعد التحديث الشهري / السبرنت", "Monthly / sprint update assistant", PortfolioPermissions.AiAssist),
        new(StatusReport, "كاتب تقرير الحالة", "Status report writer", PortfolioPermissions.AiAssist),
        new(ChangeImpact, "تحليل أثر طلب التغيير", "Change impact analysis", PortfolioPermissions.AiAssist),
        new(Insights, "رؤى المحفظة", "Portfolio insights", PortfolioPermissions.AiAssist),
    ];
}

public sealed record LessonHitDto(Guid Id, Guid ProjectId, string ProjectNumber, string? ProjectNameAr, string? ProjectNameEn, string? Lesson, string? TypeCode, string? Recommendation);

public sealed record SuggestedKpiDto(string? NameAr, string? NameEn, string? Equation, decimal? Target);

public sealed record CharterRiskSuggestionDto(string Title, int Probability, int Impact, string? Mitigation);

public sealed record SuggestedStakeholderDto(string Name, string Classification, string Interest, string Impact);

public sealed record SuggestedPlanningItemDto(string Category, string Description);

public sealed record CharterSuggestionsDto(
    IReadOnlyList<SuggestedKpiDto> Kpis, IReadOnlyList<CharterRiskSuggestionDto> Risks, IReadOnlyList<SuggestedStakeholderDto> Stakeholders,
    IReadOnlyList<SuggestedPlanningItemDto> PlanningItems, IReadOnlyList<LessonHitDto> Lessons);

public sealed record DraftMilestoneDto(string? Phase, string? TitleAr, string? TitleEn, decimal Weight, DateOnly PlannedStart, DateOnly PlannedEnd);

public sealed record DraftStoryDto(string Title, string? AsA, string? IWant, string? SoThat, int Points);

public sealed record DraftEpicDto(string? TitleAr, string? TitleEn, decimal Weight, IReadOnlyList<DraftStoryDto> Stories);

public sealed record PlanDraftDto(string Methodology, IReadOnlyList<DraftMilestoneDto> Milestones, IReadOnlyList<DraftEpicDto> Epics);

public sealed record AtRiskDto(Guid Id, string Code, string? TitleAr, string? TitleEn, string Reason);

public sealed record UpdateDraftDto(string? Narrative, IReadOnlyList<string> CorrectiveActions, IReadOnlyList<AtRiskDto> AtRisk);

public sealed record StatusNarrativeDto(string? NarrativeAr, string? NarrativeEn);

public sealed record DependentProjectDto(Guid Id, string Number, string? NameAr, string? NameEn);

public sealed record ChangeImpactDto(
    string? Summary, string? Dates, string? Cost, string? Weights, string? Dependents, IReadOnlyList<string> Recommendation, IReadOnlyList<DependentProjectDto> DependentProjects);

public sealed record InsightsDto(string? Narrative, IReadOnlyList<string> Recommendations);

internal sealed class ProjectAssistService(
    AiGateway gateway, PortfolioDbContext db, ProjectGuard guard, ProjectLoader loader, RequestReader reader, CostVisibility costs, PortfolioViewService views,
    IRequestContext context)
{
    private static readonly JsonElement CharterSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["kpis","risks","stakeholders","planningItems"],"properties":{
          "kpis":{"type":"array","maxItems":5,"items":{"type":"object","properties":{"nameAr":{"type":"string"},"nameEn":{"type":"string"},"equation":{"type":"string"},"target":{"type":["number","null"]}}}},
          "risks":{"type":"array","maxItems":6,"items":{"type":"object","properties":{"title":{"type":"string"},"probability":{"type":"integer"},"impact":{"type":"integer"},"mitigation":{"type":"string"}}}},
          "stakeholders":{"type":"array","maxItems":6,"items":{"type":"object","properties":{"name":{"type":"string"},"classification":{"type":"string"},"interest":{"type":"string"},"impact":{"type":"string"}}}},
          "planningItems":{"type":"array","maxItems":8,"items":{"type":"object","properties":{"category":{"type":"string"},"description":{"type":"string"}}}}}}
        """);

    private static readonly JsonElement PlanSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["milestones","epics"],"properties":{
          "milestones":{"type":"array","maxItems":12,"items":{"type":"object","properties":{"phase":{"type":"string"},"titleAr":{"type":"string"},"titleEn":{"type":"string"},"weight":{"type":"number"},"durationDays":{"type":"integer"}}}},
          "epics":{"type":"array","maxItems":8,"items":{"type":"object","properties":{"titleAr":{"type":"string"},"titleEn":{"type":"string"},"weight":{"type":"number"},
            "stories":{"type":"array","maxItems":6,"items":{"type":"object","properties":{"title":{"type":"string"},"asA":{"type":"string"},"iWant":{"type":"string"},"soThat":{"type":"string"},"points":{"type":"integer"}}}}}}}}}
        """);

    private static readonly JsonElement UpdateSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["narrative","correctiveActions"],"properties":{"narrative":{"type":"string","maxLength":3000},
          "correctiveActions":{"type":"array","maxItems":8,"items":{"type":"string","maxLength":500}}}}
        """);

    private static readonly JsonElement StatusSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["narrativeAr","narrativeEn"],"properties":{"narrativeAr":{"type":"string","maxLength":3000},"narrativeEn":{"type":"string","maxLength":3000}}}
        """);

    private static readonly JsonElement ImpactSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summary"],"properties":{"summary":{"type":"string"},"dates":{"type":"string"},"cost":{"type":"string"},"weights":{"type":"string"},
          "dependents":{"type":"string"},"recommendation":{"type":"array","maxItems":6,"items":{"type":"string"}}}}
        """);

    private static readonly JsonElement InsightsSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["narrative","recommendations"],"properties":{"narrative":{"type":"string","maxLength":4000},
          "recommendations":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":500}}}}
        """);

    // ---------- lessons learned search (no model) ----------

    public async Task<Result<IReadOnlyList<LessonHitDto>>> SearchLessonsAsync(string? text, Guid? excludeProjectId, CancellationToken ct)
    {
        var visible = await loader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var lessons = db.Lessons.AsNoTracking().Where(l => l.Status == ItemStatuses.Active && l.ProjectId != excludeProjectId);
        var words = (text ?? string.Empty).Split([' ', '\n', '\t', '،', ','], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).Take(6).ToList();
        if (words.Count > 0)
        {
            var patterns = words.Select(Like.Pattern).ToList();
            lessons = lessons.Where(l => patterns.Any(p => EF.Functions.ILike(l.Text ?? string.Empty, p, "\\") || EF.Functions.ILike(l.Recommendation ?? string.Empty, p, "\\")));
        }

        var rows = await lessons.Join(visible, l => l.ProjectId, p => p.Id, (l, p) => new { Lesson = l, Project = p }).OrderByDescending(x => x.Lesson.CreatedAt).Take(10)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(x => new LessonHitDto(x.Lesson.Id, x.Project.Id, x.Project.Number, x.Project.NameAr, x.Project.NameEn, x.Lesson.Text, x.Lesson.TypeCode,
            x.Lesson.Recommendation)).ToList();
    }

    // ---------- charter assistant ----------

    public async Task<Result<CharterSuggestionsDto>> CharterAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct, tracked: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == project.RequestId, ct).ConfigureAwait(false);
        var existing = new StringBuilder();
        existing.AppendLine("Existing KPIs: " + string.Join("; ", await db.Kpis.AsNoTracking().Where(k => k.ProjectId == projectId && k.Status == ItemStatuses.Active)
            .Select(k => k.NameEn ?? k.NameAr).ToListAsync(ct).ConfigureAwait(false)));
        existing.AppendLine("Existing risks: " + string.Join("; ", await db.Risks.AsNoTracking().Where(k => k.ProjectId == projectId && k.Status == ItemStatuses.Active)
            .Select(k => k.Title).ToListAsync(ct).ConfigureAwait(false)));
        existing.AppendLine("Existing stakeholders: " + string.Join("; ", await db.Stakeholders.AsNoTracking()
            .Where(k => k.ProjectId == projectId && k.Status == ItemStatuses.Active).Select(k => k.Name).ToListAsync(ct).ConfigureAwait(false)));
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.CharterAssistant, new Dictionary<string, string>(),
            [new AiDocument("project", Describe(project) + $"\nJustifications: {RichText.ToPlainText(request?.Justifications)}\n" + existing)], CharterSchema, PortfolioPermissions.SystemCode,
            ResourceType: PortfolioPermissions.ProjectResource, ResourceId: projectId.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var lessons = await SearchLessonsAsync($"{project.NameEn} {project.NameAr} {project.ScopeOfWork}", projectId, ct).ConfigureAwait(false);
        return new CharterSuggestionsDto(
            Items(o, "kpis").Select(k => new SuggestedKpiDto(Str(k, "nameAr"), Str(k, "nameEn"), Str(k, "equation"), Num(k, "target"))).Where(k => k.NameAr is not null || k.NameEn is not null)
                .ToList(),
            Items(o, "risks").Where(r => Str(r, "title") is not null)
                .Select(r => new CharterRiskSuggestionDto(Str(r, "title")!, Clamp(Int(r, "probability")), Clamp(Int(r, "impact")), Str(r, "mitigation"))).ToList(),
            Items(o, "stakeholders").Where(s => Str(s, "name") is not null)
                .Select(s => new SuggestedStakeholderDto(Str(s, "name")!, OneOf(Str(s, "classification"), ["internal", "external", "government", "contractor", "customer"], "internal"),
                    OneOf(Str(s, "interest"), Levels.All, "medium"), OneOf(Str(s, "impact"), Levels.All, "medium"))).ToList(),
            Items(o, "planningItems").Where(p => Str(p, "description") is not null && PlanningCategories.All.Contains(Str(p, "category") ?? string.Empty))
                .Select(p => new SuggestedPlanningItemDto(Str(p, "category")!, Str(p, "description")!)).ToList(),
            lessons.IsSuccess ? lessons.Value : []);
    }

    // ---------- plan generator ----------

    public async Task<Result<PlanDraftDto>> PlanAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct, tracked: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var start = project.PlannedStart ?? await reader.TodayAsync(ct).ConfigureAwait(false);
        var end = project.PlannedEnd is { } e && e > start ? e : start.AddDays(179);
        var duration = end.DayNumber - start.DayNumber + 1;
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.PlanGenerator,
            new Dictionary<string, string> { ["methodology"] = project.Methodology, ["duration"] = duration.ToString(CultureInfo.InvariantCulture) },
            [new AiDocument("project", Describe(project))], PlanSchema, PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.ProjectResource,
            ResourceId: projectId.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var milestones = new List<DraftMilestoneDto>();
        if (Methodologies.UsesMilestones(project.Methodology))
        {
            var raw = Items(o, "milestones").Select(m => (Phase: Str(m, "phase"), Ar: Str(m, "titleAr"), En: Str(m, "titleEn"), Weight: Num(m, "weight") ?? 0,
                Days: Math.Max(1, Int(m, "durationDays")))).Where(m => m.Ar is not null || m.En is not null).ToList();
            var weights = Normalise(raw.Select(m => m.Weight).ToList());
            var totalDays = Math.Max(1, raw.Sum(m => m.Days));
            var cursor = start;
            for (var i = 0; i < raw.Count; i++)
            {
                var days = Math.Max(1, (int)Math.Round((double)raw[i].Days * duration / totalDays));
                var to = i == raw.Count - 1 ? end : Min(cursor.AddDays(days - 1), end);
                milestones.Add(new DraftMilestoneDto(raw[i].Phase, raw[i].Ar, raw[i].En, weights[i], cursor, to));
                cursor = to < end ? to.AddDays(1) : end;
            }
        }

        var epics = new List<DraftEpicDto>();
        if (project.Methodology == Methodologies.Agile)
        {
            var raw = Items(o, "epics").Where(x => Str(x, "titleAr") is not null || Str(x, "titleEn") is not null).ToList();
            var weights = Normalise(raw.Select(x => Num(x, "weight") ?? 0).ToList());
            for (var i = 0; i < raw.Count; i++)
            {
                var stories = Items(raw[i], "stories").Where(s => Str(s, "title") is not null)
                    .Select(s => new DraftStoryDto(Str(s, "title")!, Str(s, "asA"), Str(s, "iWant"), Str(s, "soThat"), Math.Clamp(Int(s, "points"), 0, UserStory.MaxPoints))).ToList();
                epics.Add(new DraftEpicDto(Str(raw[i], "titleAr"), Str(raw[i], "titleEn"), weights[i], stories));
            }
        }

        return new PlanDraftDto(project.Methodology, milestones, epics);
    }

    /// <summary>Weights from percentages (or anything) to ratios that total exactly 1.0000 (the rounding goes to the largest).</summary>
    public static List<decimal> Normalise(IReadOnlyList<decimal> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count == 0)
        {
            return [];
        }

        var values = raw.Select(v => v <= 0 ? 1m : v).ToList();
        var total = values.Sum();
        var result = values.Select(v => decimal.Round(v / total, 4)).ToList();
        var diff = 1m - result.Sum();
        var largest = result.IndexOf(result.Max());
        result[largest] += diff;
        return result;
    }

    // ---------- monthly / sprint update assistant ----------

    public async Task<Result<UpdateDraftDto>> UpdateAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct, tracked: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var atRisk = await AtRiskAsync(project, ct).ConfigureAwait(false);
        var data = new StringBuilder(Describe(project));
        data.AppendLine("Milestones:");
        foreach (var m in await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled)
                     .OrderBy(m => m.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            data.Append(CultureInfo.InvariantCulture, $"- {m.Code} {m.TitleEn ?? m.TitleAr}: weight {m.Weight * 100:0.##}%, approved {m.MaxAchieved * 100:0.##}%, ")
                .AppendLine(CultureInfo.InvariantCulture, $"this period {(m.PendingAchieve ?? m.MaxAchieved) * 100:0.##}%, planned end {m.PlannedEnd:yyyy-MM-dd}");
        }

        foreach (var s in await db.Sprints.AsNoTracking().Where(s => s.ProjectId == projectId && s.Status != SprintStatuses.Planned && s.Status != SprintStatuses.Cancelled)
                     .OrderByDescending(s => s.Number).Take(3).ToListAsync(ct).ConfigureAwait(false))
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Sprint {s.Number} ({s.Status}): committed {s.CommittedPoints}, completed {s.CompletedPoints}. {s.DemoOutcome}");
        }

        data.AppendLine("At risk (computed by the system): " + string.Join("; ", atRisk.Select(a => $"{a.Code} {a.TitleEn ?? a.TitleAr} ({a.Reason})")));
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.UpdateAssistant, new Dictionary<string, string>(),
            [new AiDocument("project", data.ToString())], UpdateSchema, PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.ProjectResource,
            ResourceId: projectId.ToString("D")), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new UpdateDraftDto(Str(call.Value.Output, "narrative"), Strings(call.Value.Output, "correctiveActions"), atRisk);
    }

    /// <summary>Behind plan (planned end passed, or less achieved than the elapsed share of its dates minus the tolerance).</summary>
    public async Task<List<AtRiskDto>> AtRiskAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var tolerance = (await reader.SettingsAsync(ct).ConfigureAwait(false)).ProgressTolerance;
        var result = new List<AtRiskDto>();
        foreach (var m in await db.Milestones.AsNoTracking().Where(m => m.ProjectId == project.Id && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled
                         && m.Status != MilestoneStatuses.Completed).ToListAsync(ct).ConfigureAwait(false))
        {
            var current = m.PendingAchieve ?? m.MaxAchieved;
            if (MilestoneRules.IsDelayed(current, m.Weight, m.PlannedEnd, today))
            {
                result.Add(new AtRiskDto(m.Id, m.Code, m.TitleAr, m.TitleEn, "past-end"));
                continue;
            }

            var planned = ProgressCalculator.PlannedToDate([new MilestoneProgressInput(m.Weight, 0, m.PlannedStart, m.PlannedEnd)], today);
            if (current < planned - (tolerance * m.Weight))
            {
                result.Add(new AtRiskDto(m.Id, m.Code, m.TitleAr, m.TitleEn, "behind-plan"));
            }
        }

        return result;
    }

    // ---------- status report writer ----------

    public async Task<Result<StatusNarrativeDto>> StatusNarrativeAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct, tracked: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var showCosts = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var data = new StringBuilder(Describe(project));
        var atRisk = await AtRiskAsync(project, ct).ConfigureAwait(false);
        data.AppendLine("At risk: " + string.Join("; ", atRisk.Select(a => $"{a.Code} {a.TitleEn ?? a.TitleAr}")));
        foreach (var r in await db.Risks.AsNoTracking().Where(r => r.ProjectId == projectId && r.Status == ItemStatuses.Active).OrderByDescending(r => r.ResidualScore).Take(3)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Risk: {r.Title} ({r.ResidualBand})");
        }

        if (showCosts)
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Budget: {project.Budget:0.##}");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.StatusReport, new Dictionary<string, string>(),
            [new AiDocument("project", data.ToString())], StatusSchema, PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.ProjectResource,
            ResourceId: projectId.ToString("D")), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new StatusNarrativeDto(Str(call.Value.Output, "narrativeAr"), Str(call.Value.Output, "narrativeEn"));
    }

    // ---------- change impact ----------

    public async Task<Result<ChangeImpactDto>> ChangeImpactAsync(Guid changeRequestId, CancellationToken ct)
    {
        var cr = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == changeRequestId, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await guard.ReadAsync(cr.ProjectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var showCosts = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var visible = await loader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var dependentIds = await db.RaidItems.AsNoTracking().Where(r => r.Type == RaidTypes.Dependency && r.DependsOnProjectId == project.Id && r.Status != RaidStatuses.Closed)
            .Select(r => r.ProjectId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var dependents = await visible.Where(p => dependentIds.Contains(p.Id)).Select(p => new DependentProjectDto(p.Id, p.Number, p.NameAr, p.NameEn)).ToListAsync(ct)
            .ConfigureAwait(false);
        var data = new StringBuilder(Describe(project));
        data.AppendLine(CultureInfo.InvariantCulture, $"Change request {cr.Number}: type {cr.Type}, areas {cr.Areas}, reason: {cr.Reason}");
        data.AppendLine(CultureInfo.InvariantCulture, $"Proposed: start {cr.ProposedStart:yyyy-MM-dd}, end {cr.ProposedEnd:yyyy-MM-dd}, methodology {cr.ProposedMethodology}");
        data.AppendLine(CultureInfo.InvariantCulture, $"Proposed scope: {cr.ProposedScope}");
        if (showCosts)
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Current budget {project.Budget:0.##}; proposed budget {cr.ProposedBudget:0.##}; cost affected {cr.CostAffected}");
        }

        data.AppendLine("Dependent projects (from the RAID log): " + string.Join("; ", dependents.Select(d => $"{d.Number} {d.NameEn ?? d.NameAr}")));
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.ChangeImpact, new Dictionary<string, string>(),
            [new AiDocument("change", data.ToString())], ImpactSchema, PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.ProjectResource,
            ResourceId: project.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new ChangeImpactDto(Str(o, "summary"), Str(o, "dates"), showCosts ? Str(o, "cost") : null, Str(o, "weights"), Str(o, "dependents"), Strings(o, "recommendation"),
            dependents);
    }

    // ---------- portfolio insights ----------

    public async Task<Result<InsightsDto>> InsightsAsync(DashboardFilter filter, CancellationToken ct)
    {
        var dashboard = await views.DashboardAsync(filter, ct).ConfigureAwait(false);
        if (dashboard.IsFailure)
        {
            return dashboard.Error;
        }

        var d = dashboard.Value;
        var data = new StringBuilder();
        data.AppendLine(CultureInfo.InvariantCulture, $"Projects: {d.Total}, active {d.Active}, delayed {d.Delayed}, average progress {d.AverageProgress * 100:0.#}%");
        foreach (var p in d.DelayedProjects.Take(10))
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Delayed: {p.Number} {p.NameEn ?? p.NameAr} progress {p.Performance * 100:0.#}% vs planned {p.Planned * 100:0.#}%");
        }

        foreach (var p in d.Projects.Where(p => p.Health?.Overall.Effective == Rag.Red).Take(10))
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Red health: {p.Number} {p.NameEn ?? p.NameAr}");
        }

        foreach (var b in d.Budget)
        {
            data.AppendLine(CultureInfo.InvariantCulture, $"Budget {b.UnitEn ?? b.UnitAr}: budget {b.Budget:0}, actual {b.Actual:0}");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(ProjectAssistFeatures.Insights, new Dictionary<string, string>(),
            [new AiDocument("portfolio", data.ToString())], InsightsSchema, PortfolioPermissions.SystemCode), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new InsightsDto(Str(call.Value.Output, "narrative"), Strings(call.Value.Output, "recommendations"));
    }

    // ---------- helpers ----------

    private static string Describe(Project p) =>
        $"Project {p.Number}\nName (Arabic): {p.NameAr}\nName (English): {p.NameEn}\nClassification: {p.Classification}\nMethodology: {p.Methodology}\n"
        + $"Description: {RichText.ToPlainText(p.Description)}\nScope of work: {p.ScopeOfWork}\nPlanned dates: {p.PlannedStart:yyyy-MM-dd} → {p.PlannedEnd:yyyy-MM-dd}\n"
        + $"Progress: {p.Performance * 100:0.#}% (planned {p.PlannedProgress * 100:0.#}%, {p.ProgressStatus})\nStage: {p.Stage}\n";

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static int Clamp(int value) => Math.Clamp(value == 0 ? 3 : value, 1, 5);

    private static string OneOf(string? value, IReadOnlyList<string> allowed, string fallback) => value is not null && allowed.Contains(value) ? value : fallback;

    private static List<JsonElement> Items(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? [.. a.EnumerateArray()] : [];

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static decimal? Num(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static int Int(JsonElement o, string name) => (int)Math.Round(Num(o, name) ?? 0);

    private static List<string> Strings(JsonElement o, string name) =>
        Items(o, name).Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())).Select(x => x.GetString()!.Trim()).ToList();
}
