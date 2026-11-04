using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Tasks.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Loads a project for a Phase 10 command: 404 outside the read scope, 403 without the permission, rule error when not live.</summary>
internal sealed class ProjectGuard(ProjectLoader loader, IRequestContext context)
{
    public async Task<(Project? Project, Error? Error)> ReadAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked).ConfigureAwait(false);
        return project is null ? (null, Error.NotFound()) : (project, null);
    }

    public async Task<(Project? Project, Error? Error)> CanAsync(Guid id, string permission, CancellationToken ct, bool live = true, bool tracked = true)
    {
        var (project, error) = await ReadAsync(id, ct, tracked).ConfigureAwait(false);
        if (project is null)
        {
            return (null, error);
        }

        if (!await loader.CanAsync(context.User, permission, project, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        if (live && (project.Status is ProjectStatuses.Cancelled or ProjectStatuses.Completed))
        {
            return (null, Error.Rule("portfolio.project.closed", "portfolio.errors.projectClosed"));
        }

        return (project, null);
    }

    public Task<bool> HasAsync(Project project, string permission, CancellationToken ct) => loader.CanAsync(context.User, permission, project, ct);

    public static void Length(List<FieldError> errors, params (string Field, string? Text, int Max)[] fields)
    {
        foreach (var (field, text, max) in fields)
        {
            if (text is { } t && t.Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max }));
            }
        }
    }

    public static Error? Validation(List<FieldError> errors) => errors.Count == 0 ? null : Error.Validation(errors);
}

// ---------------------------------------------------------------- health (ADR 0093)

public sealed record HealthDto(ProjectHealth Health, decimal? CostVariance, decimal Progress, decimal Planned, int MaxDaysPastDue, int OverdueTasks, bool CanOverride, uint Version);

public sealed record HealthOverrideInput(string? Value, string? Justification, uint Version);

internal sealed class HealthService(
    PortfolioDbContext db, ProjectGuard guard, RequestReader reader, CostVisibility costs, IRequestContext context, IClock clock, IServiceProvider services)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<HealthDto>> GetAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var facts = (await FactsAsync([project], ct).ConfigureAwait(false))[project.Id];
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var health = HealthCalculator.Calculate(facts, settings.Health, Overrides(project));
        var showCosts = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var canOverride = project.Status == ProjectStatuses.Active
            && (await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false)
                || await guard.HasAsync(project, PortfolioPermissions.ProjectManage, ct).ConfigureAwait(false));
        return new HealthDto(health, showCosts ? HealthCalculator.CostVariance(facts) : null, facts.Progress, facts.Planned, facts.MaxDaysPastDue, facts.OverdueTasks,
            canOverride, project.Version);
    }

    public async Task<Result<HealthDto>> OverrideAsync(Guid projectId, string dimension, HealthOverrideInput input, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false)
            && !await guard.HasAsync(project, PortfolioPermissions.ProjectManage, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (project.Status != ProjectStatuses.Active)
        {
            return ProjectService.NotInStage(project);
        }

        var errors = new List<FieldError>();
        if (!HealthDimensions.All.Contains(dimension))
        {
            errors.Add(new FieldError("dimension", "validation.unknownCode", "validation.unknownCode"));
        }

        if (input.Value is { } v && !Rag.Colours.Contains(v))
        {
            errors.Add(new FieldError("value", "validation.unknownCode", "validation.unknownCode"));
        }

        if (input.Value is not null && string.IsNullOrWhiteSpace(input.Justification))
        {
            errors.Add(new FieldError("justification", "portfolio.errors.justificationRequired", "portfolio.errors.justificationRequired"));
        }

        ProjectGuard.Length(errors, ("justification", input.Justification, RequestRules.TextMaxLength));
        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        var overrides = new Dictionary<string, HealthOverride>(Overrides(project));
        if (input.Value is { } value)
        {
            overrides[dimension] = new HealthOverride(value, input.Justification!.Trim(), context.User.UserId(), clock.UtcNow);
        }
        else
        {
            overrides.Remove(dimension);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetHealthOverrides(JsonSerializer.Serialize(overrides, Json));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await GetAsync(projectId, ct).ConfigureAwait(false);
    }

    public static IReadOnlyDictionary<string, HealthOverride> Overrides(Project project) =>
        JsonSerializer.Deserialize<Dictionary<string, HealthOverride>>(project.HealthOverrides, Json) ?? [];

    /// <summary>Health of many projects (dashboards) with one query per fact type.</summary>
    public async Task<IReadOnlyDictionary<Guid, ProjectHealth>> BulkAsync(IReadOnlyList<Project> projects, CancellationToken ct)
    {
        var facts = await FactsAsync(projects, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        return projects.ToDictionary(p => p.Id, p => HealthCalculator.Calculate(facts[p.Id], settings.Health, Overrides(p)));
    }

    public async Task<Dictionary<Guid, HealthFacts>> FactsAsync(IReadOnlyList<Project> projects, CancellationToken ct)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var since = clock.UtcNow.AddDays(-settings.Health.ScopeWindowDays);
        var milestones = await db.Milestones.AsNoTracking().Where(m => ids.Contains(m.ProjectId) && m.Status != MilestoneStatuses.Cancelled && !m.IsHeader && !m.HighLevel)
            .Select(m => new { m.ProjectId, m.Weight, m.MaxAchieved, m.PlannedStart, m.PlannedEnd }).ToListAsync(ct).ConfigureAwait(false);
        var risks = await db.Risks.AsNoTracking().Where(r => ids.Contains(r.ProjectId) && r.Status == ItemStatuses.Active)
            .Select(r => new { r.ProjectId, r.ResidualBand }).ToListAsync(ct).ConfigureAwait(false);
        var changes = await db.ChangeRequests.AsNoTracking()
            .Where(c => ids.Contains(c.ProjectId) && c.Type == ChangeTypes.Details
                && (c.Status == ChangeStatuses.UnderReview || ((c.Status == ChangeStatuses.Approved || c.Status == ChangeStatuses.Completed) && c.DecidedAt >= since)))
            .Select(c => new { c.ProjectId, c.Status, c.Areas }).ToListAsync(ct).ConfigureAwait(false);
        var epics = await db.Epics.AsNoTracking().Where(e => ids.Contains(e.ProjectId) && e.Status != EpicStatuses.Cancelled).Select(e => e.ProjectId).Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        var tasks = services.GetService<ITaskDirectory>();
        var result = new Dictionary<Guid, HealthFacts>();
        foreach (var p in projects)
        {
            var leaves = milestones.Where(m => m.ProjectId == p.Id).Select(m => new MilestoneProgressInput(m.Weight, m.MaxAchieved, m.PlannedStart, m.PlannedEnd)).ToList();
            bool hasPlan;
            decimal planned;
            if (p.Methodology == Methodologies.Agile)
            {
                hasPlan = epics.Contains(p.Id) && p.PlannedStart is not null && p.PlannedEnd is not null;
                planned = ProgressCalculator.PlannedToDate([new MilestoneProgressInput(1m, 0, p.PlannedStart, p.PlannedEnd)], today);
            }
            else
            {
                hasPlan = leaves.Count > 0;
                planned = ProgressCalculator.PlannedToDate(leaves, today);
            }

            var pastDue = leaves.Where(l => l.PlannedEnd is { } e && e < today && l.Approved < l.Weight).Select(l => today.DayNumber - l.PlannedEnd!.Value.DayNumber)
                .DefaultIfEmpty(0).Max();
            var overdue = tasks is not null && p.Status == ProjectStatuses.Active && p.SubState == SubStates.Execution
                ? (await tasks.CountLinkedAsync(PortfolioPermissions.ProjectResource, p.Id, ct).ConfigureAwait(false)).Overdue
                : 0;
            var actuals = new[] { p.Year1ActualCost, p.Year2ActualCost, p.Year3ActualCost, p.Year4ActualCost };
            decimal? actual = actuals.Any(a => a is not null) ? actuals.Sum(a => a ?? 0) : null;
            var mine = changes.Where(c => c.ProjectId == p.Id && IsScopeChange(c.Areas)).ToList();
            var bands = risks.Where(r => r.ProjectId == p.Id).Select(r => r.ResidualBand).ToList();
            var highest = bands.OrderByDescending(b => RiskBands.All.ToList().IndexOf(b)).FirstOrDefault();
            result[p.Id] = new HealthFacts(hasPlan, p.Performance, planned, pastDue, overdue, p.Budget, actual,
                mine.Count(c => c.Status != ChangeStatuses.UnderReview), mine.Any(c => c.Status == ChangeStatuses.UnderReview), highest, bands.Count > 0);
        }

        return result;
    }

    private static bool IsScopeChange(string areas)
    {
        var list = JsonSerializer.Deserialize<List<string>>(areas) ?? [];
        return list.Contains("scope") || list.Contains("plan");
    }
}

// ---------------------------------------------------------------- baselines and EVM (ADR 0094)

public sealed record BaselineDto(
    Guid Id, int VersionNo, DateTimeOffset TakenAt, string? Reason, Guid? ChangeRequestId, string? ChangeRequestNumber, decimal? Budget, DateOnly? PlannedStart,
    DateOnly? PlannedEnd, IReadOnlyList<BaselineItem> Items);

public sealed record VarianceDto(
    int VersionNo, IReadOnlyList<VarianceRow> Rows, decimal? BaselineBudget, decimal? CurrentBudget, DateOnly? BaselineEnd, DateOnly? CurrentEnd, int? EndVarianceDays,
    bool CostsHidden);

public sealed record EvmDto(EarnedValue Values, int? BaselineVersion, DateOnly AsOf);

internal sealed class BaselineService(PortfolioDbContext db, ProjectGuard guard, RequestReader reader, CostVisibility costs, IRequestContext context, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<BaselineDto>>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var show = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var rows = await db.Baselines.AsNoTracking().Where(b => b.ProjectId == projectId).OrderByDescending(b => b.VersionNo).ToListAsync(ct).ConfigureAwait(false);
        var crIds = rows.Where(r => r.ChangeRequestId is not null).Select(r => r.ChangeRequestId!.Value).ToList();
        var numbers = await db.ChangeRequests.AsNoTracking().Where(c => crIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct).ConfigureAwait(false);
        return rows.Select(b => new BaselineDto(b.Id, b.VersionNo, b.TakenAt, b.Reason, b.ChangeRequestId,
            b.ChangeRequestId is { } c ? numbers.GetValueOrDefault(c) : null, show ? b.Budget : null, b.PlannedStart, b.PlannedEnd,
            Items(b).Select(i => show ? i : i with { Cost = null }).ToList())).ToList();
    }

    public async Task<Result<VarianceDto>> VarianceAsync(Guid projectId, int? version, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var baseline = await db.Baselines.AsNoTracking().Where(b => b.ProjectId == projectId && (version == null || b.VersionNo == version))
            .OrderByDescending(b => b.VersionNo).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (baseline is null)
        {
            return Error.Rule("portfolio.baseline.none", "portfolio.errors.noBaseline");
        }

        var show = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var current = await CurrentAsync(project, ct).ConfigureAwait(false);
        var rows = BaselineVariance.Compare(Items(baseline), current)
            .Select(r => show ? r : r with { BaselineCost = null, CurrentCost = null }).ToList();
        return new VarianceDto(baseline.VersionNo, rows, show ? baseline.Budget : null, show ? project.Budget : null, baseline.PlannedEnd, project.PlannedEnd,
            baseline.PlannedEnd is { } be && project.PlannedEnd is { } pe ? pe.DayNumber - be.DayNumber : null, !show);
    }

    /// <summary>Earned value as of today (needs cost visibility — 403 otherwise).</summary>
    public async Task<Result<EvmDto>> EvmAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden("portfolio.costsHidden");
        }

        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var baseline = await db.Baselines.AsNoTracking().Where(b => b.ProjectId == projectId).OrderByDescending(b => b.VersionNo).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var bac = baseline?.Budget ?? project.Budget ?? 0;
        decimal planned;
        if (baseline is not null && Items(baseline).Any(i => i.Kind == "milestone"))
        {
            planned = BaselineVariance.PlannedToDate(Items(baseline), today);
        }
        else
        {
            var start = baseline?.PlannedStart ?? project.PlannedStart;
            var end = baseline?.PlannedEnd ?? project.PlannedEnd;
            planned = ProgressCalculator.PlannedToDate([new MilestoneProgressInput(1m, 0, start, end)], today);
        }

        var actual = new[] { project.Year1ActualCost, project.Year2ActualCost, project.Year3ActualCost, project.Year4ActualCost }.Sum(a => a ?? 0);
        return new EvmDto(EarnedValueCalculator.Calculate(bac, planned, project.Performance, actual), baseline?.VersionNo, today);
    }

    /// <summary>Snapshot of the current plan (planning approval: v1; an applied change request with re-baseline: next version).</summary>
    public async Task<Baseline> TakeAsync(Project project, string reason, Guid? changeRequestId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        var items = (await CurrentAsync(project, ct).ConfigureAwait(false)).Select(c => c.Item).ToList();
        var next = (await db.Baselines.Where(b => b.ProjectId == project.Id).MaxAsync(b => (int?)b.VersionNo, ct).ConfigureAwait(false) ?? 0) + 1;
        var baseline = Baseline.Take(project.Id, next, clock.UtcNow, context.User.UserId(), reason, changeRequestId, project.Budget, project.PlannedStart, project.PlannedEnd,
            JsonSerializer.Serialize(items, Json));
        db.Baselines.Add(baseline);
        return baseline;
    }

    private async Task<List<CurrentPlanItem>> CurrentAsync(Project project, CancellationToken ct)
    {
        var milestones = (await db.Milestones.Where(m => m.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false))
            .Concat(db.Milestones.Local.Where(m => m.ProjectId == project.Id)).DistinctBy(m => m.Id)
            .Where(m => m.IsActive && !m.IsHeader && !m.HighLevel).OrderBy(m => m.Code, StringComparer.Ordinal).ToList();
        var epics = (await db.Epics.Where(e => e.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false))
            .Concat(db.Epics.Local.Where(e => e.ProjectId == project.Id)).DistinctBy(e => e.Id).Where(e => e.IsActive).OrderBy(e => e.Code, StringComparer.Ordinal).ToList();
        var result = milestones.Select(m => new CurrentPlanItem(new BaselineItem(m.Id, "milestone", m.Code, m.TitleAr, m.TitleEn, m.PlannedStart, m.PlannedEnd, m.Weight, m.Cost),
            m.ActualEnd)).ToList();
        result.AddRange(epics.Select(e => new CurrentPlanItem(new BaselineItem(e.Id, "epic", e.Code, e.TitleAr, e.TitleEn, null, null, e.Weight, null), null)));
        return result;
    }

    public static IReadOnlyList<BaselineItem> Items(Baseline b) => JsonSerializer.Deserialize<List<BaselineItem>>(b.Items, Json) ?? [];
}

// ---------------------------------------------------------------- RAID (ADR 0095)

public sealed record RaidItemDto(
    Guid Id, string Type, string Code, string? Title, string? Description, PortfolioPersonDto? Owner, string Priority, DateOnly? DueDate, string Status, string? Resolution,
    Guid? MilestoneId, Guid? DependsOnProjectId, string? DependsOnProjectNumber, Guid? DependsOnMilestoneId, DateOnly? DecidedOn, string? DecidedBy, string Source, Guid? SourceId,
    uint Version);

public sealed record RaidRiskDto(Guid Id, string? Title, int Probability, int Impact, string ResidualBand, PortfolioPersonDto? Owner, DateOnly? ReviewDate);

public sealed record RaidLogDto(IReadOnlyList<RaidItemDto> Items, IReadOnlyList<RaidRiskDto> Risks, bool CanEdit, bool CanPropose);

public sealed record RaidInput(RaidValues Values, uint? Version = null);

internal sealed class RaidService(PortfolioDbContext db, ProjectGuard guard, ProjectLoader loader, RequestReader reader, IRequestContext context)
{
    public async Task<Result<RaidLogDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var items = await db.RaidItems.AsNoTracking().Where(r => r.ProjectId == projectId).OrderBy(r => r.Type).ThenBy(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == projectId && r.Status == ItemStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(items.Select(i => i.OwnerEmployeeId).Concat(risks.Select(r => r.OwnerEmployeeId)), ct).ConfigureAwait(false);
        var otherIds = items.Where(i => i.DependsOnProjectId is not null).Select(i => i.DependsOnProjectId!.Value).ToList();
        var visible = await loader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var numbers = await visible.Where(p => otherIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct).ConfigureAwait(false);
        var live = project.Status is ProjectStatuses.Active or ProjectStatuses.OnHold;
        return new RaidLogDto(
            items.Select(i => ToDto(i, people, numbers)).ToList(),
            risks.Select(r => new RaidRiskDto(r.Id, r.Title, r.Probability, r.Impact, r.ResidualBand, r.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null,
                r.ReviewDate)).ToList(),
            live && await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false),
            live && await guard.HasAsync(project, PortfolioPermissions.ProjectContribute, ct).ConfigureAwait(false));
    }

    public async Task<Result<RaidItemDto>> SaveAsync(Guid projectId, Guid? id, RaidInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectContribute, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var canEdit = await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        var v = input.Values;
        var errors = new List<FieldError>();
        if (!RaidTypes.All.Contains(v.Type ?? string.Empty))
        {
            errors.Add(new FieldError("type", "validation.unknownCode", "validation.unknownCode"));
        }

        if (string.IsNullOrWhiteSpace(v.Title))
        {
            errors.Add(new FieldError("title", "validation.notEmpty", "validation.notEmpty"));
        }

        ProjectGuard.Length(errors, ("title", v.Title, RequestRules.NameMaxLength), ("description", v.Description, RequestRules.TextMaxLength),
            ("resolution", v.Resolution, RequestRules.TextMaxLength), ("decidedBy", v.DecidedBy, RequestRules.NameMaxLength));
        if (v.Type == RaidTypes.Dependency)
        {
            if (v.DependsOnProjectId is null && v.DependsOnMilestoneId is null)
            {
                errors.Add(new FieldError("dependsOnProjectId", "validation.notEmpty", "validation.notEmpty"));
            }

            if (v.DependsOnProjectId is { } other && (other == projectId || await loader.FindAsync(other, context.User, ct).ConfigureAwait(false) is null))
            {
                errors.Add(new FieldError("dependsOnProjectId", "validation.unknownCode", "validation.unknownCode"));
            }

            var target = v.DependsOnProjectId ?? projectId;
            if (v.DependsOnMilestoneId is { } dm && !await db.Milestones.AnyAsync(m => m.Id == dm && m.ProjectId == target, ct).ConfigureAwait(false))
            {
                errors.Add(new FieldError("dependsOnMilestoneId", "validation.unknownCode", "validation.unknownCode"));
            }

            if (v.DependsOnMilestoneId is not null && v.DependsOnMilestoneId == v.MilestoneId)
            {
                errors.Add(new FieldError("dependsOnMilestoneId", "portfolio.errors.dependencySelf", "portfolio.errors.dependencySelf"));
            }
        }

        if (v.MilestoneId is { } mid && !await db.Milestones.AnyAsync(m => m.Id == mid && m.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("milestoneId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        RaidItem item;
        if (id is { } itemId)
        {
            item = (await db.RaidItems.FirstOrDefaultAsync(r => r.Id == itemId && r.ProjectId == projectId, ct).ConfigureAwait(false))!;
            if (item is null)
            {
                return Error.NotFound();
            }

            if (!canEdit && item.CreatedBy != context.User.UserId())
            {
                return Error.Forbidden();
            }

            if (input.Version is { } version)
            {
                db.Entry(item).Property(x => x.Version).OriginalValue = version;
            }

            item.Edit(v);
        }
        else
        {
            item = RaidItem.Create(projectId, await RaidCodes.NextAsync(db, project, v.Type!, ct).ConfigureAwait(false), v, proposed: !canEdit);
            db.RaidItems.Add(item);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var people = await reader.PeopleAsync([item.OwnerEmployeeId], ct).ConfigureAwait(false);
        return ToDto(item, people, []);
    }

    /// <summary>The PM accepts an entry a team member proposed (SPEC §1.9 "draft for PM to accept").</summary>
    public async Task<Result<RaidItemDto>> AcceptAsync(Guid projectId, Guid id, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var item = await db.RaidItems.FirstOrDefaultAsync(r => r.Id == id && r.ProjectId == projectId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        item.Accept();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(item, await reader.PeopleAsync([item.OwnerEmployeeId], ct).ConfigureAwait(false), []);
    }

    private static RaidItemDto ToDto(RaidItem i, Dictionary<Guid, PortfolioPersonDto> people, Dictionary<Guid, string> numbers) =>
        new(i.Id, i.Type, i.Code, i.Title, i.Description, i.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, i.Priority, i.DueDate, i.Status, i.Resolution,
            i.MilestoneId, i.DependsOnProjectId, i.DependsOnProjectId is { } p ? numbers.GetValueOrDefault(p) : null, i.DependsOnMilestoneId, i.DecidedOn, i.DecidedBy, i.Source,
            i.SourceId, i.Version);
}

// ---------------------------------------------------------------- RACI

public sealed record RaciEntryDto(Guid Id, string Area, Guid? MilestoneId, PortfolioPersonDto? Employee, Guid EmployeeId, string Role, uint Version);

public sealed record RaciMatrixDto(IReadOnlyList<RaciEntryDto> Entries, IReadOnlyList<string> Areas, IReadOnlyList<PortfolioPersonDto> People, bool CanEdit);

public sealed record RaciInput(RaciValues Values);

internal sealed class RaciService(PortfolioDbContext db, ProjectGuard guard, RequestReader reader)
{
    public async Task<Result<RaciMatrixDto>> MatrixAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var rows = await db.RaciEntries.AsNoTracking().Where(r => r.ProjectId == projectId && r.Status == ItemStatuses.Active).OrderBy(r => r.Area).ToListAsync(ct)
            .ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(r => (Guid?)r.EmployeeId), ct).ConfigureAwait(false);
        return new RaciMatrixDto(
            rows.Select(r => new RaciEntryDto(r.Id, r.Area, r.MilestoneId, people.GetValueOrDefault(r.EmployeeId), r.EmployeeId, r.Role, r.Version)).ToList(),
            rows.Select(r => r.Area).Distinct().ToList(), people.Values.OrderBy(p => p.NameEn).ToList(),
            project.Status == ProjectStatuses.Active && await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false));
    }

    public async Task<Result<RaciMatrixDto>> SaveAsync(Guid projectId, Guid? id, RaciInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Area))
        {
            errors.Add(new FieldError("area", "validation.notEmpty", "validation.notEmpty"));
        }

        ProjectGuard.Length(errors, ("area", v.Area, RequestRules.NameMaxLength));
        if (!RaciRoles.All.Contains(v.Role ?? string.Empty))
        {
            errors.Add(new FieldError("role", "validation.unknownCode", "validation.unknownCode"));
        }

        if ((await reader.PeopleAsync([v.EmployeeId], ct).ConfigureAwait(false)).Count == 0)
        {
            errors.Add(new FieldError("employeeId", "validation.unknownCode", "validation.unknownCode"));
        }

        var area = v.Area?.Trim();
        var others = await db.RaciEntries.Where(r => r.ProjectId == projectId && r.Status == ItemStatuses.Active && r.Area == area && r.Id != id).ToListAsync(ct)
            .ConfigureAwait(false);
        if (v.Role == RaciRoles.Accountable && others.Any(r => r.Role == RaciRoles.Accountable))
        {
            errors.Add(new FieldError("role", "portfolio.errors.raciOneAccountable", "portfolio.errors.raciOneAccountable"));
        }

        if (others.Any(r => r.EmployeeId == v.EmployeeId))
        {
            errors.Add(new FieldError("employeeId", "validation.duplicate", "validation.duplicate"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        if (id is { } entryId)
        {
            var entry = await db.RaciEntries.FirstOrDefaultAsync(r => r.Id == entryId && r.ProjectId == projectId, ct).ConfigureAwait(false);
            if (entry is null)
            {
                return Error.NotFound();
            }

            entry.Edit(v);
        }
        else
        {
            db.RaciEntries.Add(RaciEntry.Create(projectId, v));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await MatrixAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<RaciMatrixDto>> RemoveAsync(Guid projectId, Guid id, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var entry = await db.RaciEntries.FirstOrDefaultAsync(r => r.Id == id && r.ProjectId == projectId, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return Error.NotFound();
        }

        entry.Deactivate("removed");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await MatrixAsync(projectId, ct).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------- communication plan

public sealed record CommunicationDto(
    Guid Id, Guid? StakeholderId, string? StakeholderName, string? Audience, string? Message, string? Method, string Frequency, PortfolioPersonDto? Owner, DateOnly? NextDue,
    DateOnly? LastDone, bool Due, string Status, uint Version);

public sealed record CommunicationInput(CommunicationValues Values);

internal sealed class CommunicationService(PortfolioDbContext db, ProjectGuard guard, RequestReader reader)
{
    public async Task<Result<IReadOnlyList<CommunicationDto>>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        return project is null ? error! : await DtosAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<CommunicationDto>>> SaveAsync(Guid projectId, Guid? id, CommunicationInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Audience) && v.StakeholderId is null)
        {
            errors.Add(new FieldError("audience", "validation.notEmpty", "validation.notEmpty"));
        }

        ProjectGuard.Length(errors, ("audience", v.Audience, RequestRules.NameMaxLength), ("method", v.Method, RequestRules.NameMaxLength),
            ("message", v.Message, RequestRules.TextMaxLength));
        if (v.Frequency is { } f && !CommunicationFrequencies.All.Contains(f))
        {
            errors.Add(new FieldError("frequency", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v.StakeholderId is { } sid && !await db.Stakeholders.AnyAsync(s => s.Id == sid && s.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("stakeholderId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        if (id is { } itemId)
        {
            var item = await db.Communications.FirstOrDefaultAsync(c => c.Id == itemId && c.ProjectId == projectId, ct).ConfigureAwait(false);
            if (item is null)
            {
                return Error.NotFound();
            }

            item.Edit(v);
        }
        else
        {
            db.Communications.Add(CommunicationItem.Create(projectId, v with { NextDue = v.NextDue ?? await reader.TodayAsync(ct).ConfigureAwait(false) }));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtosAsync(projectId, ct).ConfigureAwait(false);
    }

    /// <summary>One item per active stakeholder without one, with its method and frequency; owner = the PM.</summary>
    public async Task<Result<IReadOnlyList<CommunicationDto>>> GenerateAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var existing = await db.Communications.Where(c => c.ProjectId == projectId && c.Status == ItemStatuses.Active && c.StakeholderId != null)
            .Select(c => c.StakeholderId!.Value).ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in await db.Stakeholders.AsNoTracking().Where(s => s.ProjectId == projectId && s.Status == ItemStatuses.Active && !existing.Contains(s.Id))
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            db.Communications.Add(CommunicationItem.Create(projectId, new CommunicationValues(s.Id, s.Name, null, s.CommunicationMethod,
                CommunicationFrequencies.FromText(s.CommunicationFrequency), project.PmEmployeeId, today)));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtosAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<CommunicationDto>>> MarkDoneAsync(Guid projectId, Guid id, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectContribute, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var item = await db.Communications.FirstOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        item.MarkDone(await reader.TodayAsync(ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtosAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<CommunicationDto>>> RemoveAsync(Guid projectId, Guid id, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var item = await db.Communications.FirstOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        item.Deactivate("removed");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtosAsync(projectId, ct).ConfigureAwait(false);
    }

    private async Task<List<CommunicationDto>> DtosAsync(Guid projectId, CancellationToken ct)
    {
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var rows = await db.Communications.AsNoTracking().Where(c => c.ProjectId == projectId && c.Status == ItemStatuses.Active).OrderBy(c => c.NextDue).ToListAsync(ct)
            .ConfigureAwait(false);
        var stakeholders = await db.Stakeholders.AsNoTracking().Where(s => s.ProjectId == projectId).ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        return rows.Select(r => new CommunicationDto(r.Id, r.StakeholderId, r.StakeholderId is { } s ? stakeholders.GetValueOrDefault(s) : null, r.Audience, r.Message, r.Method,
            r.Frequency, r.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, r.NextDue, r.LastDone, r.NextDue is { } d && d <= today, r.Status, r.Version)).ToList();
    }
}

// ---------------------------------------------------------------- benefits

public sealed record BenefitDto(
    Guid Id, Guid? RequestId, Guid? ProjectId, string? TitleAr, string? TitleEn, string? Measure, decimal? BaselineValue, decimal? TargetValue, string? Unit,
    PortfolioPersonDto? Owner, DateOnly? MeasurementDate, decimal? ActualValue, DateTimeOffset? MeasuredAt, string Status, string? Note, uint Version);

public sealed record BenefitInput(BenefitValues Values, uint? Version = null);

public sealed record BenefitMeasureInput(BenefitMeasurement Values, uint Version);

internal sealed class BenefitService(PortfolioDbContext db, ProjectGuard guard, RequestReader reader, IRequestContext context, IClock clock)
{
    public async Task<Result<IReadOnlyList<BenefitDto>>> ForRequestAsync(Guid requestId, CancellationToken ct) =>
        await reader.FindAsync(requestId, context.User, ct).ConfigureAwait(false) is null
            ? Error.NotFound()
            : await DtosAsync(db.Benefits.AsNoTracking().Where(b => b.RequestId == requestId && b.ItemStatus == ItemStatuses.Active), ct).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<BenefitDto>>> ForProjectAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        return project is null
            ? error!
            : await DtosAsync(db.Benefits.AsNoTracking().Where(b => b.ProjectId == projectId && b.ItemStatus == ItemStatuses.Active), ct).ConfigureAwait(false);
    }

    /// <summary>Expected benefits are defined on the request while it is a draft (SPEC §1.12).</summary>
    public async Task<Result<IReadOnlyList<BenefitDto>>> SaveOnRequestAsync(Guid requestId, Guid? id, BenefitInput input, CancellationToken ct)
    {
        var request = await reader.FindAsync(requestId, context.User, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (request.Status != RequestStatuses.Draft)
        {
            return Error.Rule("portfolio.request.notDraft", "portfolio.errors.notDraft");
        }

        if (Validate(input.Values) is { } invalid)
        {
            return invalid;
        }

        if (id is { } benefitId)
        {
            var benefit = await db.Benefits.FirstOrDefaultAsync(b => b.Id == benefitId && b.RequestId == requestId, ct).ConfigureAwait(false);
            if (benefit is null)
            {
                return Error.NotFound();
            }

            benefit.Edit(input.Values);
        }
        else
        {
            db.Benefits.Add(Benefit.ForRequest(requestId, input.Values));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ForRequestAsync(requestId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<BenefitDto>>> SaveOnProjectAsync(Guid projectId, Guid? id, BenefitInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (Validate(input.Values) is { } invalid)
        {
            return invalid;
        }

        if (id is { } benefitId)
        {
            var benefit = await db.Benefits.FirstOrDefaultAsync(b => b.Id == benefitId && b.ProjectId == projectId, ct).ConfigureAwait(false);
            if (benefit is null)
            {
                return Error.NotFound();
            }

            benefit.Edit(input.Values);
        }
        else
        {
            db.Benefits.Add(Benefit.ForProject(projectId, input.Values));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ForProjectAsync(projectId, ct).ConfigureAwait(false);
    }

    /// <summary>Measured in the Impact stage (or at closure for projects without Impact).</summary>
    public async Task<Result<IReadOnlyList<BenefitDto>>> MeasureAsync(Guid projectId, Guid id, BenefitMeasureInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (project.Stage is not (ProjectStages.Impact or ProjectStages.Closure))
        {
            return ProjectService.NotInStage(project);
        }

        var errors = new List<FieldError>();
        if (input.Values.Status is not { } s || !BenefitStatuses.Measured.Contains(s))
        {
            errors.Add(new FieldError("status", "validation.unknownCode", "validation.unknownCode"));
        }

        ProjectGuard.Length(errors, ("note", input.Values.Note, RequestRules.TextMaxLength));
        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        var benefit = await db.Benefits.FirstOrDefaultAsync(b => b.Id == id && b.ProjectId == projectId, ct).ConfigureAwait(false);
        if (benefit is null)
        {
            return Error.NotFound();
        }

        db.Entry(benefit).Property(x => x.Version).OriginalValue = input.Version;
        benefit.RecordMeasurement(input.Values, context.User.UserId(), clock.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ForProjectAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<BenefitDto>>> RemoveAsync(Guid? requestId, Guid? projectId, Guid id, CancellationToken ct)
    {
        Benefit? benefit;
        if (projectId is { } pid)
        {
            var (project, error) = await guard.CanAsync(pid, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
            if (project is null)
            {
                return error!;
            }

            benefit = await db.Benefits.FirstOrDefaultAsync(b => b.Id == id && b.ProjectId == pid, ct).ConfigureAwait(false);
        }
        else
        {
            var request = await reader.FindAsync(requestId!.Value, context.User, ct).ConfigureAwait(false);
            if (request is null)
            {
                return Error.NotFound();
            }

            if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, ct).ConfigureAwait(false) || request.Status != RequestStatuses.Draft)
            {
                return Error.Forbidden();
            }

            benefit = await db.Benefits.FirstOrDefaultAsync(b => b.Id == id && b.RequestId == requestId, ct).ConfigureAwait(false);
        }

        if (benefit is null)
        {
            return Error.NotFound();
        }

        benefit.Deactivate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return projectId is { } p2 ? await ForProjectAsync(p2, ct).ConfigureAwait(false) : await ForRequestAsync(requestId!.Value, ct).ConfigureAwait(false);
    }

    private static Error? Validate(BenefitValues v)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        ProjectGuard.Length(errors, ("titleAr", v.TitleAr, RequestRules.NameMaxLength), ("titleEn", v.TitleEn, RequestRules.NameMaxLength),
            ("measure", v.Measure, RequestRules.TextMaxLength), ("unit", v.Unit, 60));
        return ProjectGuard.Validation(errors);
    }

    private async Task<List<BenefitDto>> DtosAsync(IQueryable<Benefit> query, CancellationToken ct)
    {
        var rows = await query.OrderBy(b => b.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        return rows.Select(b => new BenefitDto(b.Id, b.RequestId, b.ProjectId, b.TitleAr, b.TitleEn, b.Measure, b.BaselineValue, b.TargetValue, b.Unit,
            b.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, b.MeasurementDate, b.ActualValue, b.MeasuredAt, b.Status, b.Note, b.Version)).ToList();
    }
}

// ---------------------------------------------------------------- quality sign-off, payments, supplier rating, risk matrix, checklists

public sealed record SignOffInput(string Decision, string? Comment, uint Version);

public sealed record PaymentDto(Guid Id, Guid ContractId, string? Title, DateOnly? DueDate, decimal? Amount, Guid? MilestoneId, string Status, DateOnly? PaidOn, uint Version);

public sealed record PaymentInput(PaymentValues Values, uint? Version = null);

public sealed record RatingInput(int Quality, int Timeliness, int Communication, string? Comment);

public sealed record RatingDto(int? Quality, int? Timeliness, int? Communication, decimal? Overall, string? Comment, DateTimeOffset? RatedAt);

public sealed record ContractExtrasDto(Guid ContractId, IReadOnlyList<PaymentDto> Payments, decimal? PaidTotal, decimal? PlannedTotal, RatingDto Rating, bool CostsHidden);

public sealed record RiskMatrixCell(int Probability, int Impact, int Count, IReadOnlyList<Guid> RiskIds);

public sealed record RiskMatrixDto(string Basis, IReadOnlyList<RiskMatrixCell> Cells, IReadOnlyList<RaidRiskDto> Risks);

public sealed record ChecklistItemDto(string Gate, string Code, string? LabelAr, string? LabelEn, bool Required, bool Checked, DateTimeOffset? At, string? Note);

public sealed record ChecklistDto(IReadOnlyList<ChecklistItemDto> Items, bool CanTick, uint Version);

public sealed record ChecklistTickInput(bool Checked, string? Note, uint Version);

internal sealed class ProjectExtrasService(
    PortfolioDbContext db, ProjectGuard guard, RequestReader reader, CostVisibility costs, ProjectLoader loader, IRequestContext context, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Owner or PM Office signs off a deliverable milestone (accepted / rejected with comment).</summary>
    public async Task<Result<Guid>> SignOffAsync(Guid milestoneId, SignOffInput input, CancellationToken ct)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await guard.ReadAsync(milestone.ProjectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var me = context.User.EmployeeIdOf();
        if (!(me is not null && me == project.OwnerEmployeeId) && !await guard.HasAsync(project, PortfolioPermissions.ProjectManage, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = new List<FieldError>();
        if (!milestone.IsDeliverable)
        {
            errors.Add(new FieldError("milestone", "portfolio.errors.notDeliverable", "portfolio.errors.notDeliverable"));
        }

        if (input.Decision is not ("accepted" or "rejected"))
        {
            errors.Add(new FieldError("decision", "validation.unknownCode", "validation.unknownCode"));
        }

        if (input.Decision == "rejected" && string.IsNullOrWhiteSpace(input.Comment))
        {
            errors.Add(new FieldError("comment", "validation.notEmpty", "validation.notEmpty"));
        }

        ProjectGuard.Length(errors, ("comment", input.Comment, RequestRules.TextMaxLength));
        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        db.Entry(milestone).Property(x => x.Version).OriginalValue = input.Version;
        milestone.SignOff(input.Decision, input.Comment, me, clock.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return milestone.Id;
    }

    public async Task<Result<ContractExtrasDto>> ContractAsync(Guid projectId, Guid contractId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && c.ProjectId == projectId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound();
        }

        var show = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var payments = await db.Payments.AsNoTracking().Where(p => p.ContractId == contractId).OrderBy(p => p.DueDate).ToListAsync(ct).ConfigureAwait(false);
        return new ContractExtrasDto(contractId,
            payments.Select(p => new PaymentDto(p.Id, p.ContractId, p.Title, p.DueDate, show ? p.Amount : null, p.MilestoneId, p.Status, p.PaidOn, p.Version)).ToList(),
            show ? payments.Where(p => p.Status == PaymentStatuses.Paid).Sum(p => p.Amount) : null,
            show ? payments.Where(p => p.Status != PaymentStatuses.Cancelled).Sum(p => p.Amount) : null,
            new RatingDto(contract.RatingQuality, contract.RatingTimeliness, contract.RatingCommunication, contract.RatingOverall, contract.RatingComment, contract.RatedAt),
            !show);
    }

    public async Task<Result<ContractExtrasDto>> SavePaymentAsync(Guid projectId, Guid contractId, Guid? id, PaymentInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            if (error!.Kind != Strategy.Platform.Kernel.Results.ErrorKind.Forbidden)
            {
                return error;
            }

            (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectLpo, ct).ConfigureAwait(false);
            if (project is null)
            {
                return error!;
            }
        }

        if (!await db.Contracts.AnyAsync(c => c.Id == contractId && c.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Title))
        {
            errors.Add(new FieldError("title", "validation.notEmpty", "validation.notEmpty"));
        }

        ProjectGuard.Length(errors, ("title", v.Title, RequestRules.NameMaxLength));
        if (v.Amount <= 0 || decimal.Round(v.Amount, 2) != v.Amount)
        {
            errors.Add(new FieldError("amount", "portfolio.errors.twoDecimals", "portfolio.errors.twoDecimals"));
        }

        if (v.Status is { } s && !PaymentStatuses.All.Contains(s))
        {
            errors.Add(new FieldError("status", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v.Status == PaymentStatuses.Paid && v.PaidOn is null)
        {
            errors.Add(new FieldError("paidOn", "validation.notEmpty", "validation.notEmpty"));
        }

        if (v.MilestoneId is { } mid && !await db.Milestones.AnyAsync(m => m.Id == mid && m.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("milestoneId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        if (id is { } paymentId)
        {
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.ContractId == contractId, ct).ConfigureAwait(false);
            if (payment is null)
            {
                return Error.NotFound();
            }

            if (input.Version is { } version)
            {
                db.Entry(payment).Property(x => x.Version).OriginalValue = version;
            }

            payment.Edit(v);
        }
        else
        {
            db.Payments.Add(ContractPayment.Create(projectId, contractId, v));
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ContractAsync(projectId, contractId, ct).ConfigureAwait(false);
    }

    public async Task<Result<ContractExtrasDto>> RateAsync(Guid projectId, Guid contractId, RatingInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct, live: false).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && c.ProjectId == projectId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        foreach (var (field, value) in new[] { ("quality", input.Quality), ("timeliness", input.Timeliness), ("communication", input.Communication) })
        {
            if (value is < 1 or > 5)
            {
                errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 1, ["to"] = 5 }));
            }
        }

        ProjectGuard.Length(errors, ("comment", input.Comment, RequestRules.TextMaxLength));
        if (ProjectGuard.Validation(errors) is { } invalid)
        {
            return invalid;
        }

        contract.Rate(input.Quality, input.Timeliness, input.Communication, input.Comment, context.User.UserId(), clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ContractAsync(projectId, contractId, ct).ConfigureAwait(false);
    }

    /// <summary>5 × 5 probability × impact heat-map (inherent; residual places by residual score band).</summary>
    public async Task<Result<RiskMatrixDto>> RiskMatrixAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == projectId && r.Status == ItemStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(risks.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        var cells = new List<RiskMatrixCell>();
        for (var p = 1; p <= 5; p++)
        {
            for (var i = 1; i <= 5; i++)
            {
                var inCell = risks.Where(r => r.Probability == p && r.Impact == i).Select(r => r.Id).ToList();
                cells.Add(new RiskMatrixCell(p, i, inCell.Count, inCell));
            }
        }

        return new RiskMatrixDto("inherent", cells, risks.Select(r => new RaidRiskDto(r.Id, r.Title, r.Probability, r.Impact, r.ResidualBand,
            r.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, r.ReviewDate)).ToList());
    }

    public async Task<Result<ChecklistDto>> ChecklistAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await guard.ReadAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var ticks = Ticks(project);
        var items = Gates.All.SelectMany(g => settings.GateChecklists.For(g).Select(d =>
        {
            var tick = ticks.GetValueOrDefault(g)?.GetValueOrDefault(d.Code);
            return new ChecklistItemDto(g, d.Code, d.LabelAr, d.LabelEn, d.Required, tick?.Checked == true, tick?.At, tick?.Note);
        })).ToList();
        return new ChecklistDto(items,
            project.Status == ProjectStatuses.Active && await guard.HasAsync(project, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false), project.Version);
    }

    public async Task<Result<ChecklistDto>> TickAsync(Guid projectId, string gate, string code, ChecklistTickInput input, CancellationToken ct)
    {
        var (project, error) = await guard.CanAsync(projectId, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (!Gates.All.Contains(gate) || settings.GateChecklists.For(gate).All(d => d.Code != code))
        {
            return Error.NotFound();
        }

        if (input.Note is { Length: > RequestRules.TextMaxLength })
        {
            return Error.Validation([new FieldError("note", "validation.maximumLength", "validation.maximumLength")]);
        }

        var ticks = Ticks(project).ToDictionary(k => k.Key, k => new Dictionary<string, ChecklistTick>(k.Value));
        if (!ticks.TryGetValue(gate, out var gateTicks))
        {
            gateTicks = [];
            ticks[gate] = gateTicks;
        }

        gateTicks[code] = new ChecklistTick(input.Checked, context.User.UserId(), clock.UtcNow, string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim());
        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetChecklist(JsonSerializer.Serialize(ticks, Json));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ChecklistAsync(projectId, ct).ConfigureAwait(false);
    }

    public static Dictionary<string, Dictionary<string, ChecklistTick>> Ticks(Project project) =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ChecklistTick>>>(project.Checklist, Json) ?? [];

    /// <summary>Gate items of the configured checklist (empty when the switch is off).</summary>
    public static IReadOnlyList<GateItem> GateChecklist(Project project, PortfolioSettings settings, string gate) =>
        settings.Features.GateChecklists ? ProjectGates.Checklist(settings.GateChecklists.For(gate), Ticks(project).GetValueOrDefault(gate)) : [];

    public Task<IQueryable<Project>> VisibleAsync(CancellationToken ct) => loader.VisibleAsync(context.User, ct);
}
