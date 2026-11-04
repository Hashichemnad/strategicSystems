using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.Portfolio.Application;

// ---------------------------------------------------------------- templates (ADR 0095)

public sealed record ProjectTemplateDto(Guid Id, string Code, string? NameAr, string? NameEn, string Methodology, ProjectTemplateContent Content, string Status, uint Version);

public sealed record ProjectTemplateInput(string Code, string? NameAr, string? NameEn, string Methodology, ProjectTemplateContent Content, uint? Version = null);

public sealed record ApplyTemplateInput(Guid TemplateId);

public sealed record SaveAsTemplateInput(string Code, string? NameAr, string? NameEn);

public sealed record ApplyTemplateResult(int Milestones, int PlanningItems, int Risks, int Epics);

internal sealed class TemplateService(
    PortfolioDbContext db, ProjectGuard guard, ProjectProgress progress, IProjectRiskScoring scoring, IAccessService access, IRequestContext context)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<ProjectTemplateDto>>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = db.Templates.AsNoTracking();
        if (!includeInactive || !await access.CanAsync(context.User, PortfolioPermissions.SettingsManage, cancellationToken: ct).ConfigureAwait(false))
        {
            query = query.Where(t => t.Status == ItemStatuses.Active);
        }

        return (await query.OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto).ToList();
    }

    public async Task<Result<ProjectTemplateDto>> SaveAsync(Guid? id, ProjectTemplateInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 60 || !input.Code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
        {
            errors.Add(new FieldError("code", "validation.code", "validation.code"));
        }

        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (!Methodologies.All.Contains(input.Methodology))
        {
            errors.Add(new FieldError("methodology", "validation.unknownCode", "validation.unknownCode"));
        }

        errors.AddRange(ContentErrors(input.Content));
        if (await db.Templates.AnyAsync(t => t.Code == input.Code && t.Id != id, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("code", "validation.duplicate", "validation.duplicate"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        var content = JsonSerializer.Serialize(input.Content, Json);
        ProjectTemplate template;
        if (id is { } templateId)
        {
            template = (await db.Templates.FirstOrDefaultAsync(t => t.Id == templateId, ct).ConfigureAwait(false))!;
            if (template is null)
            {
                return Error.NotFound();
            }

            if (input.Version is { } v)
            {
                db.Entry(template).Property(x => x.Version).OriginalValue = v;
            }

            template.Edit(input.NameAr, input.NameEn, input.Methodology, content);
        }
        else
        {
            template = ProjectTemplate.Create(input.Code.Trim(), input.NameAr, input.NameEn, input.Methodology, content);
            db.Templates.Add(template);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(template);
    }

    public async Task<Result<ProjectTemplateDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound();
        }

        template.SetActive(active);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(template);
    }

    /// <summary>Creates the template's milestones (dates offset from the project start), planning items, risks and epics on a project being planned.</summary>
    public async Task<Result<ApplyTemplateResult>> ApplyAsync(Guid projectId, ApplyTemplateInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!ProjectLifecycle.CanPlanMilestones(project.Status, project.SubState))
        {
            return ProjectService.NotInStage(project);
        }

        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.TemplateId && t.Status == ItemStatuses.Active, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound();
        }

        var content = JsonSerializer.Deserialize<ProjectTemplateContent>(template.Content, Json) ?? new ProjectTemplateContent();
        var existing = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false);
        var existingWeight = existing.Where(m => m.IsActive && !m.IsHeader && !m.HighLevel).Sum(m => m.Weight);
        var leafWeight = (content.Milestones ?? []).Where(m => !m.IsHeader).Sum(m => m.Weight);
        if (existingWeight + leafWeight > 1)
        {
            return Error.Validation([new FieldError("templateId", "portfolio.errors.weightsOver100", "portfolio.errors.weightsOver100",
                new Dictionary<string, object?> { ["total"] = ProjectGates.Percent(existingWeight + leafWeight) })]);
        }

        var start = project.PlannedStart ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var created = new List<Milestone>();
        var top = existing.Count(m => m.ParentId is null);
        var milestones = content.Milestones ?? [];
        for (var i = 0; i < milestones.Count; i++)
        {
            var t = milestones[i];
            var parent = t.ParentIndex is { } pi && pi >= 0 && pi < i && milestones[pi].IsHeader ? created.ElementAtOrDefault(pi) : null;
            string code;
            if (parent is not null)
            {
                code = $"{parent.Code}.{created.Count(m => m.ParentId == parent.Id) + 1}";
            }
            else
            {
                code = $"{project.Number}-{++top}";
            }

            var from = start.AddDays(Math.Max(0, t.OffsetDays));
            var to = from.AddDays(Math.Max(0, t.DurationDays - 1));
            var milestone = Milestone.Create(projectId, code, new MilestoneValues(t.TitleAr, t.TitleEn, parent?.Id, t.IsHeader, false, from, to, t.IsHeader ? 0 : t.Weight,
                null, false, null, null, null, false, null, i));
            created.Add(milestone);
            db.Milestones.Add(milestone);
        }

        var items = content.PlanningItems ?? [];
        foreach (var item in items.Where(x => PlanningCategories.All.Contains(x.Category)))
        {
            db.PlanningItems.Add(PlanningItem.Create(projectId, project.Stage, new PlanningItemValues(item.Category, item.Description)));
        }

        var risks = content.Risks ?? [];
        foreach (var r in risks)
        {
            var values = new ProjectRiskValues(r.Title, r.CategoryCode, Math.Clamp(r.Probability, 1, 5), Math.Clamp(r.Impact, 1, 5), null, null, "mitigate", null, null, null, null,
                project.PmEmployeeId, null);
            db.Risks.Add(ProjectRisk.Create(projectId, project.Stage, values, await scoring.ScoreAsync(values.Probability, values.Impact, null, ct).ConfigureAwait(false)));
        }

        var epics = 0;
        if (project.Methodology == Methodologies.Agile)
        {
            var count = await db.Epics.CountAsync(e => e.ProjectId == projectId, ct).ConfigureAwait(false);
            var weight = await db.Epics.Where(e => e.ProjectId == projectId && e.Status != EpicStatuses.Cancelled && e.MilestoneId == null).SumAsync(e => e.Weight, ct)
                .ConfigureAwait(false);
            foreach (var e in (content.Epics ?? []).Where(e => e.Weight > 0))
            {
                if (weight + e.Weight > 1)
                {
                    break;
                }

                weight += e.Weight;
                db.Epics.Add(Epic.Create(projectId, $"{project.Number}-E{++count}", new EpicValues(e.TitleAr, e.TitleEn, e.Weight, null, null, null, epics)));
                epics++;
            }
        }

        await progress.RecalculateAsync(project, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ApplyTemplateResult(created.Count, items.Count, risks.Count, epics);
    }

    /// <summary>PM Office saves a project's plan as a template (offsets from the project start).</summary>
    public async Task<Result<ProjectTemplateDto>> SaveAsAsync(Guid projectId, SaveAsTemplateInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectManage, ct, live: false, tracked: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var start = project.PlannedStart ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId && m.Status != MilestoneStatuses.Cancelled && !m.HighLevel)
            .OrderBy(m => m.Code).ToListAsync(ct).ConfigureAwait(false);
        var ordered = milestones.Where(m => m.ParentId is null).SelectMany(m => new[] { m }.Concat(milestones.Where(c => c.ParentId == m.Id))).ToList();
        var rows = ordered.Select(m => new TemplateMilestone(m.TitleAr, m.TitleEn, m.IsHeader ? 0 : m.Weight,
            m.PlannedStart is { } s ? Math.Max(0, s.DayNumber - start.DayNumber) : 0,
            m.PlannedStart is { } a && m.PlannedEnd is { } b ? b.DayNumber - a.DayNumber + 1 : 30, m.IsHeader,
            m.ParentId is { } p ? ordered.FindIndex(x => x.Id == p) : null)).ToList();
        var items = await db.PlanningItems.AsNoTracking().Where(x => x.ProjectId == projectId && x.Status == ItemStatuses.Active)
            .Select(x => new TemplatePlanningItem(x.Category, x.Description ?? string.Empty)).ToListAsync(ct).ConfigureAwait(false);
        var risks = await db.Risks.AsNoTracking().Where(x => x.ProjectId == projectId && x.Status == ItemStatuses.Active)
            .Select(x => new TemplateRisk(x.Title ?? string.Empty, x.CategoryCode, x.Probability, x.Impact)).ToListAsync(ct).ConfigureAwait(false);
        var epics = await db.Epics.AsNoTracking().Where(x => x.ProjectId == projectId && x.Status != EpicStatuses.Cancelled && x.MilestoneId == null)
            .Select(x => new TemplateEpic(x.TitleAr, x.TitleEn, x.Weight)).ToListAsync(ct).ConfigureAwait(false);
        return await SaveAsync(null, new ProjectTemplateInput(input.Code, input.NameAr, input.NameEn, project.Methodology, new ProjectTemplateContent(rows, items, risks, epics)), ct)
            .ConfigureAwait(false);
    }

    private static List<FieldError> ContentErrors(ProjectTemplateContent? content)
    {
        var errors = new List<FieldError>();
        if (content is null)
        {
            errors.Add(new FieldError("content", "validation.notEmpty", "validation.notEmpty"));
            return errors;
        }

        var milestones = content.Milestones ?? [];
        if (milestones.Count > 200)
        {
            errors.Add(new FieldError("content.milestones", "validation.invalid", "validation.invalid"));
        }

        if (milestones.Where(m => !m.IsHeader).Sum(m => m.Weight) > 1 || milestones.Any(m => !m.IsHeader && m.Weight is <= 0 or > 1))
        {
            errors.Add(new FieldError("content.milestones", "portfolio.errors.weightsOver100", "portfolio.errors.weightsOver100"));
        }

        if ((content.Epics ?? []).Sum(e => e.Weight) > 1)
        {
            errors.Add(new FieldError("content.epics", "portfolio.errors.weightsOver100", "portfolio.errors.weightsOver100"));
        }

        if ((content.PlanningItems ?? []).Any(p => !PlanningCategories.All.Contains(p.Category)))
        {
            errors.Add(new FieldError("content.planningItems", "validation.unknownCode", "validation.unknownCode"));
        }

        return errors;
    }

    private static ProjectTemplateDto ToDto(ProjectTemplate t) =>
        new(t.Id, t.Code, t.NameAr, t.NameEn, t.Methodology, JsonSerializer.Deserialize<ProjectTemplateContent>(t.Content, Json) ?? new ProjectTemplateContent(), t.Status, t.Version);
}

// ---------------------------------------------------------------- portfolios and programmes

public sealed record PortfolioGroupDto(
    Guid Id, string Kind, string Code, string? NameAr, string? NameEn, string? Description, Guid? ParentId, PortfolioPersonDto? Owner, string Status, int Projects,
    decimal AverageProgress, int Delayed, uint Version);

public sealed record PortfolioGroupInput(PortfolioGroupValues Values, uint? Version = null);

public sealed record GroupingInput(Guid? PortfolioGroupId, Guid? ProgrammeId, uint Version);

internal sealed class GroupService(PortfolioDbContext db, ProjectLoader loader, RequestReader reader, IAccessService access, IRequestContext context)
{
    public async Task<Result<IReadOnlyList<PortfolioGroupDto>>> ListAsync(CancellationToken ct)
    {
        var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Kind).ThenBy(g => g.Code).ToListAsync(ct).ConfigureAwait(false);
        var visible = await loader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var projects = await visible.Where(p => p.PortfolioGroupId != null || p.ProgrammeId != null)
            .Select(p => new { p.PortfolioGroupId, p.ProgrammeId, p.Performance, p.ProgressStatus }).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(groups.Select(g => g.OwnerEmployeeId), ct).ConfigureAwait(false);
        return groups.Select(g =>
        {
            var programmes = groups.Where(x => x.ParentId == g.Id).Select(x => x.Id).ToHashSet();
            var mine = projects.Where(p => p.ProgrammeId == g.Id || p.PortfolioGroupId == g.Id || (p.ProgrammeId is { } pr && programmes.Contains(pr))).ToList();
            return new PortfolioGroupDto(g.Id, g.Kind, g.Code, g.NameAr, g.NameEn, g.Description, g.ParentId, g.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null,
                g.Status, mine.Count, mine.Count == 0 ? 0 : decimal.Round(mine.Average(p => p.Performance), 4), mine.Count(p => p.ProgressStatus == ProgressCalculator.Delayed),
                g.Version);
        }).ToList();
    }

    public async Task<Result<IReadOnlyList<PortfolioGroupDto>>> SaveAsync(Guid? id, PortfolioGroupInput input, CancellationToken ct)
    {
        if (!await access.CanAsync(context.User, PortfolioPermissions.PortfolioManage, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (!GroupKinds.All.Contains(v.Kind ?? string.Empty))
        {
            errors.Add(new FieldError("kind", "validation.unknownCode", "validation.unknownCode"));
        }

        if (string.IsNullOrWhiteSpace(v.Code) || v.Code.Length > 60)
        {
            errors.Add(new FieldError("code", "validation.code", "validation.code"));
        }

        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        ProjectGuard.Length(errors, ("nameAr", v.NameAr, RequestRules.NameMaxLength), ("nameEn", v.NameEn, RequestRules.NameMaxLength),
            ("description", v.Description, RequestRules.TextMaxLength));
        if (v.Kind == GroupKinds.Programme && v.ParentId is { } parent
            && !await db.Groups.AnyAsync(g => g.Id == parent && g.Kind == GroupKinds.Portfolio, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("parentId", "validation.unknownCode", "validation.unknownCode"));
        }

        var code = v.Code?.Trim();
        if (await db.Groups.AnyAsync(g => g.Code == code && g.Id != id, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("code", "validation.duplicate", "validation.duplicate"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        if (id is { } groupId)
        {
            var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct).ConfigureAwait(false);
            if (group is null)
            {
                return Error.NotFound();
            }

            if (input.Version is { } version)
            {
                db.Entry(group).Property(x => x.Version).OriginalValue = version;
            }

            group.Edit(v);
        }
        else
        {
            db.Groups.Add(PortfolioGroup.Create(v));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<GroupingInput>> AssignAsync(Guid projectId, GroupingInput input, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await access.CanAsync(context.User, PortfolioPermissions.PortfolioManage, cancellationToken: ct).ConfigureAwait(false)
            && !await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = new List<FieldError>();
        if (input.PortfolioGroupId is { } pg && !await db.Groups.AnyAsync(g => g.Id == pg && g.Kind == GroupKinds.Portfolio, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("portfolioGroupId", "validation.unknownCode", "validation.unknownCode"));
        }

        Guid? portfolio = input.PortfolioGroupId;
        if (input.ProgrammeId is { } pr)
        {
            var programme = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == pr && g.Kind == GroupKinds.Programme, ct).ConfigureAwait(false);
            if (programme is null)
            {
                errors.Add(new FieldError("programmeId", "validation.unknownCode", "validation.unknownCode"));
            }
            else
            {
                portfolio ??= programme.ParentId;
            }
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetGrouping(portfolio, input.ProgrammeId);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return new GroupingInput(project.PortfolioGroupId, project.ProgrammeId, project.Version);
    }
}

// ---------------------------------------------------------------- communication reminders (daily job)

internal sealed class CommunicationReminderRunner(PortfolioDbContext db, RequestReader reader, ProjectNotifier notifier)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (!settings.Features.CommunicationPlan)
        {
            return 0;
        }

        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var due = await db.Communications.Where(c => c.Status == ItemStatuses.Active && c.NextDue != null && c.NextDue <= today
            && (c.RemindedFor == null || c.RemindedFor != c.NextDue)).ToListAsync(ct).ConfigureAwait(false);
        var sent = 0;
        foreach (var group in due.GroupBy(c => c.ProjectId))
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == group.Key && p.Status == ProjectStatuses.Active, ct).ConfigureAwait(false);
            if (project is null)
            {
                continue;
            }

            foreach (var item in group)
            {
                await notifier.ToEmployeesAsync(PortfolioNotifications.CommunicationDue, project, [item.OwnerEmployeeId ?? project.PmEmployeeId], db, ct,
                    item.Audience ?? string.Empty, due: item.NextDue!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
                item.MarkReminded(item.NextDue!.Value);
                sent++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sent;
    }
}

internal sealed class CommunicationReminderJob(RequestContext requestContext, CommunicationReminderRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "portfolio.communication-reminders", "تذكيرات خطة التواصل", "Communication plan reminders", "0 15 6 * * ?", typeof(CommunicationReminderJob),
        "يذكّر مسؤول كل بند في خطة التواصل مرة واحدة عند حلول موعده.", "Reminds the owner of each communication plan item once when it falls due.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"sent={await runner.RunAsync(cancellationToken).ConfigureAwait(false)}";
}

// ---------------------------------------------------------------- Gantt and dashboard (docs/design/portfolio.md §36, §38)

public sealed record GanttRowDto(
    Guid Id, string Kind, Guid? ParentId, string Code, string? TitleAr, string? TitleEn, DateOnly? PlannedStart, DateOnly? PlannedEnd, DateOnly? ActualStart,
    DateOnly? ActualEnd, DateOnly? BaselineStart, DateOnly? BaselineEnd, decimal Weight, decimal Achieved, string Status, bool Delayed, bool HighLevel, string? Rag = null);

public sealed record GanttDependencyDto(Guid From, Guid To);

public sealed record GanttDto(
    Guid? ProjectId, string? Number, string? NameAr, string? NameEn, DateOnly? Start, DateOnly? End, DateOnly Today, IReadOnlyList<GanttRowDto> Rows,
    IReadOnlyList<GanttDependencyDto> Dependencies, bool BaselineShown);

public sealed record DashboardProjectDto(
    Guid Id, string Number, string? NameAr, string? NameEn, string Classification, string Methodology, string Stage, string Status, string ProgressStatus, decimal Performance,
    decimal Planned, PortfolioUnitDto? Unit, decimal? Budget, decimal? Actual, ProjectHealth? Health, DateOnly? PlannedEnd, decimal? Value, int? Complexity);

public sealed record PortfolioHeatCellDto(Guid? UnitId, string? UnitAr, string? UnitEn, string Dimension, int Green, int Amber, int Red, int Grey);

public sealed record BudgetRowDto(Guid? UnitId, string? UnitAr, string? UnitEn, decimal Budget, decimal Actual, int Projects);

public sealed record StageCountDto(string Stage, int Count);

public sealed record UpcomingMilestoneDto(
    Guid ProjectId, string ProjectNumber, string? ProjectNameAr, string? ProjectNameEn, Guid MilestoneId, string Code, string? TitleAr, string? TitleEn, DateOnly PlannedEnd,
    decimal Completion);

public sealed record FunnelDto(int Submitted, int InApproval, int Approved, int Active, int Completed);

public sealed record PortfolioDashboardDto(
    int Total, int Active, int Delayed, decimal AverageProgress, IReadOnlyList<StageCountDto> ByStage, IReadOnlyList<PortfolioHeatCellDto> Heat, IReadOnlyList<BudgetRowDto> Budget,
    IReadOnlyList<DashboardProjectDto> Projects, IReadOnlyList<DashboardProjectDto> DelayedProjects, IReadOnlyList<UpcomingMilestoneDto> Upcoming, FunnelDto Funnel,
    bool HealthOn, int CostsHidden);

public sealed record DashboardFilter(Guid? PortfolioGroupId, Guid? ProgrammeId, Guid? UnitId);

internal sealed class PortfolioViewService(
    PortfolioDbContext db, ProjectLoader loader, RequestReader reader, HealthService health, CostVisibility costs, IRequestContext context)
{
    private const int MaxProjects = 1000;

    public async Task<Result<GanttDto>> ProjectGanttAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId && m.Status != MilestoneStatuses.Cancelled).ToListAsync(ct)
            .ConfigureAwait(false);
        var baseline = settings.Features.Baselines
            ? await db.Baselines.AsNoTracking().Where(b => b.ProjectId == projectId).OrderByDescending(b => b.VersionNo).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        var baseItems = baseline is null ? [] : BaselineService.Items(baseline).ToDictionary(i => i.Id);
        var rows = Order(milestones).Select(m =>
        {
            var b = baseItems.GetValueOrDefault(m.Id);
            var current = m.PendingAchieve ?? m.MaxAchieved;
            return new GanttRowDto(m.Id, m.IsHeader ? "phase" : "milestone", m.ParentId, m.Code, m.TitleAr, m.TitleEn, m.PlannedStart, m.PlannedEnd, m.ActualStart, m.ActualEnd,
                b?.Start, b?.End, m.Weight, ProgressCalculator.CompletionRate(m.MaxAchieved, m.Weight), m.Status,
                !m.IsHeader && MilestoneRules.IsDelayed(current, m.Weight, m.PlannedEnd, today), m.HighLevel);
        }).ToList();
        if (Methodologies.UsesSprints(project.Methodology))
        {
            var releases = await db.Releases.AsNoTracking().Where(r => r.ProjectId == projectId && r.Status != ReleaseStatuses.Cancelled).OrderBy(r => r.TargetDate)
                .ToListAsync(ct).ConfigureAwait(false);
            var sprints = await db.Sprints.AsNoTracking().Where(s => s.ProjectId == projectId && s.Status != SprintStatuses.Cancelled).OrderBy(s => s.Number)
                .ToListAsync(ct).ConfigureAwait(false);
            rows.AddRange(releases.Select(r => new GanttRowDto(r.Id, "release", null, r.NameEn ?? r.NameAr ?? string.Empty, r.NameAr, r.NameEn, r.TargetDate, r.TargetDate, null,
                r.Status == ReleaseStatuses.Released ? r.TargetDate : null, null, null, 0, r.Status == ReleaseStatuses.Released ? 1 : 0, r.Status, false, false)));
            rows.AddRange(sprints.Select(s => new GanttRowDto(s.Id, "sprint", null, $"S{s.Number}", s.Name, s.Name, s.StartDate, s.EndDate, s.StartedAt is { } st ? s.StartDate : null,
                s.ClosedAt is not null ? s.EndDate : null, null, null, 0, s.CommittedPoints == 0 ? 0 : decimal.Round((decimal)s.CompletedPoints / s.CommittedPoints, 4), s.Status,
                false, false)));
        }

        var dependencies = settings.Features.Raid
            ? await db.RaidItems.AsNoTracking()
                .Where(r => r.ProjectId == projectId && r.Type == RaidTypes.Dependency && r.MilestoneId != null && r.DependsOnMilestoneId != null && r.Status != RaidStatuses.Closed)
                .Select(r => new GanttDependencyDto(r.DependsOnMilestoneId!.Value, r.MilestoneId!.Value)).ToListAsync(ct).ConfigureAwait(false)
            : [];
        var ids = rows.Select(r => r.Id).ToHashSet();
        return new GanttDto(project.Id, project.Number, project.NameAr, project.NameEn, project.PlannedStart, project.PlannedEnd, today, rows,
            dependencies.Where(d => ids.Contains(d.From) && ids.Contains(d.To)).ToList(), baseline is not null);
    }

    /// <summary>Portfolio-wide plan: one row per project with its phases / top-level milestones (high-level plan before the detailed one exists).</summary>
    public async Task<Result<GanttDto>> PortfolioGanttAsync(DashboardFilter filter, CancellationToken ct)
    {
        var projects = await ProjectsAsync(filter, ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var ids = projects.Select(p => p.Id).ToList();
        var milestones = await db.Milestones.AsNoTracking().Where(m => ids.Contains(m.ProjectId) && m.Status != MilestoneStatuses.Cancelled && m.ParentId == null)
            .ToListAsync(ct).ConfigureAwait(false);
        var healthOn = (await reader.SettingsAsync(ct).ConfigureAwait(false)).Features.Health;
        var healths = healthOn ? await health.BulkAsync(projects, ct).ConfigureAwait(false) : new Dictionary<Guid, ProjectHealth>();
        var rows = new List<GanttRowDto>();
        foreach (var p in projects.OrderBy(p => p.PlannedStart).ThenBy(p => p.Number))
        {
            rows.Add(new GanttRowDto(p.Id, "project", null, p.Number, p.NameAr, p.NameEn, p.PlannedStart, p.PlannedEnd, p.OwnershipTakenAt is { } o ? DateOnly.FromDateTime(o.UtcDateTime) : null,
                p.CompletedAt is { } c ? DateOnly.FromDateTime(c.UtcDateTime) : null, null, null, 1, p.Performance, p.Status, p.ProgressStatus == ProgressCalculator.Delayed,
                false, healths.GetValueOrDefault(p.Id)?.Overall.Effective));
            var mine = milestones.Where(m => m.ProjectId == p.Id).ToList();
            var detailed = mine.Where(m => !m.HighLevel).ToList();
            foreach (var m in (detailed.Count > 0 ? detailed : mine).OrderBy(m => m.PlannedStart).ThenBy(m => m.Code))
            {
                rows.Add(new GanttRowDto(m.Id, m.IsHeader ? "phase" : "milestone", p.Id, m.Code, m.TitleAr, m.TitleEn, m.PlannedStart, m.PlannedEnd, m.ActualStart, m.ActualEnd,
                    null, null, m.Weight, ProgressCalculator.CompletionRate(m.MaxAchieved, m.Weight), m.Status,
                    !m.IsHeader && MilestoneRules.IsDelayed(m.MaxAchieved, m.Weight, m.PlannedEnd, today), m.HighLevel));
            }
        }

        return new GanttDto(null, null, null, null, projects.Min(p => p.PlannedStart), projects.Max(p => p.PlannedEnd), today, rows, [], false);
    }

    public async Task<Result<PortfolioDashboardDto>> DashboardAsync(DashboardFilter filter, CancellationToken ct)
    {
        var user = context.User;
        var projects = await ProjectsAsync(filter, ct).ConfigureAwait(false);
        var ids = projects.Select(p => p.Id).ToList();
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var visibleCosts = await costs.VisibleSetAsync(user, ids, ct).ConfigureAwait(false);
        var healths = settings.Features.Health ? await health.BulkAsync(projects, ct).ConfigureAwait(false) : new Dictionary<Guid, ProjectHealth>();
        var facts = settings.Features.Health ? null : await health.FactsAsync(projects, ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(projects.Select(p => p.UnitId), ct).ConfigureAwait(false);
        var requestIds = projects.Select(p => p.RequestId).ToList();
        var requests = await db.Requests.AsNoTracking().Where(r => requestIds.Contains(r.Id)).Select(r => new { r.Id, r.EvaluationScore, r.PriorityScores })
            .ToListAsync(ct).ConfigureAwait(false);
        var rows = projects.Select(p =>
        {
            var show = visibleCosts.Contains(p.Id);
            var request = requests.FirstOrDefault(r => r.Id == p.RequestId);
            int? complexity = null;
            if (request?.PriorityScores is { } json && JsonSerializer.Deserialize<Dictionary<string, int>>(json) is { } scores && scores.TryGetValue("complexity", out var cx))
            {
                complexity = cx;
            }

            var actuals = new[] { p.Year1ActualCost, p.Year2ActualCost, p.Year3ActualCost, p.Year4ActualCost };
            var planned = facts?.GetValueOrDefault(p.Id)?.Planned ?? p.PlannedProgress;
            return new DashboardProjectDto(p.Id, p.Number, p.NameAr, p.NameEn, p.Classification, p.Methodology, p.Stage, p.Status, p.ProgressStatus, p.Performance, planned,
                p.UnitId is { } u ? units.GetValueOrDefault(u) : null, show ? p.Budget : null, show ? actuals.Sum(a => a ?? 0) : null, healths.GetValueOrDefault(p.Id),
                p.PlannedEnd, request?.EvaluationScore, complexity);
        }).ToList();
        var active = rows.Where(r => r.Status == ProjectStatuses.Active).ToList();
        var heat = new List<PortfolioHeatCellDto>();
        if (settings.Features.Health)
        {
            foreach (var unit in active.GroupBy(r => r.Unit?.Id))
            {
                foreach (var dimension in HealthDimensions.All)
                {
                    var colours = unit.Select(r => Colour(r.Health!, dimension)).ToList();
                    var first = unit.First().Unit;
                    heat.Add(new PortfolioHeatCellDto(unit.Key, first?.NameAr, first?.NameEn, dimension, colours.Count(c => c == Rag.Green), colours.Count(c => c == Rag.Amber),
                        colours.Count(c => c == Rag.Red), colours.Count(c => c == Rag.Grey)));
                }
            }
        }

        var budget = rows.Where(r => r.Budget is not null).GroupBy(r => r.Unit?.Id)
            .Select(g => new BudgetRowDto(g.Key, g.First().Unit?.NameAr, g.First().Unit?.NameEn, g.Sum(r => r.Budget ?? 0), g.Sum(r => r.Actual ?? 0), g.Count())).ToList();
        var activeIds = active.Select(a => a.Id).ToList();
        var until = today.AddDays(60);
        var upcoming = await db.Milestones.AsNoTracking()
            .Where(m => activeIds.Contains(m.ProjectId) && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled && m.Status != MilestoneStatuses.Completed
                && m.PlannedEnd >= today && m.PlannedEnd <= until)
            .OrderBy(m => m.PlannedEnd).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var requestsVisible = await reader.VisibleAsync(user, ct).ConfigureAwait(false);
        var funnel = new FunnelDto(
            await requestsVisible.CountAsync(r => r.Status != RequestStatuses.Draft, ct).ConfigureAwait(false),
            await requestsVisible.CountAsync(r => r.Status == RequestStatuses.InApproval, ct).ConfigureAwait(false),
            await requestsVisible.CountAsync(r => r.Status == RequestStatuses.Approved, ct).ConfigureAwait(false),
            rows.Count(r => r.Status == ProjectStatuses.Active), rows.Count(r => r.Status == ProjectStatuses.Completed));
        return new PortfolioDashboardDto(
            rows.Count, active.Count, active.Count(r => r.ProgressStatus == ProgressCalculator.Delayed),
            active.Count == 0 ? 0 : decimal.Round(active.Average(r => r.Performance), 4),
            rows.GroupBy(r => r.Status == ProjectStatuses.Active ? r.Stage : r.Status).Select(g => new StageCountDto(g.Key, g.Count())).ToList(),
            heat, budget, rows.OrderBy(r => r.Number).ToList(),
            active.Where(r => r.ProgressStatus == ProgressCalculator.Delayed).OrderBy(r => r.Performance - r.Planned).Take(20).ToList(),
            upcoming.Select(m =>
            {
                var p = projects.First(x => x.Id == m.ProjectId);
                return new UpcomingMilestoneDto(p.Id, p.Number, p.NameAr, p.NameEn, m.Id, m.Code, m.TitleAr, m.TitleEn, m.PlannedEnd!.Value,
                    ProgressCalculator.CompletionRate(m.MaxAchieved, m.Weight));
            }).ToList(),
            funnel, settings.Features.Health, rows.Count(r => !visibleCosts.Contains(r.Id)));
    }

    private static string Colour(ProjectHealth h, string dimension) => dimension switch
    {
        HealthDimensions.Schedule => h.Schedule.Effective,
        HealthDimensions.Cost => h.Cost.Effective,
        HealthDimensions.Scope => h.Scope.Effective,
        HealthDimensions.Risk => h.Risk.Effective,
        _ => h.Overall.Effective,
    };

    public async Task<List<Project>> ProjectsAsync(DashboardFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = await reader.VisibleProjectsAsync(context.User, ct).ConfigureAwait(false);
        if (filter.PortfolioGroupId is { } pg)
        {
            var programmes = await db.Groups.AsNoTracking().Where(g => g.ParentId == pg).Select(g => (Guid?)g.Id).ToListAsync(ct).ConfigureAwait(false);
            query = query.Where(p => p.PortfolioGroupId == pg || programmes.Contains(p.ProgrammeId));
        }

        if (filter.ProgrammeId is { } pr)
        {
            query = query.Where(p => p.ProgrammeId == pr);
        }

        if (filter.UnitId is { } unit)
        {
            query = query.Where(p => p.UnitId == unit || p.SectionUnitId == unit);
        }

        return await query.Where(p => p.Status != ProjectStatuses.Draft).OrderBy(p => p.Number).Take(MaxProjects).ToListAsync(ct).ConfigureAwait(false);
    }

    private static List<Milestone> Order(List<Milestone> milestones)
    {
        var result = new List<Milestone>();
        foreach (var top in milestones.Where(m => m.ParentId is null).OrderBy(m => m.HighLevel ? 0 : 1).ThenBy(m => m.Sort).ThenBy(m => m.Code, StringComparer.Ordinal))
        {
            result.Add(top);
            result.AddRange(milestones.Where(m => m.ParentId == top.Id).OrderBy(m => m.Sort).ThenBy(m => m.Code, StringComparer.Ordinal));
        }

        return result;
    }
}
