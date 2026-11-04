using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ProjectKpiDto(
    Guid Id, string Nature, Guid? KpiCardId, string? KpiCardCode, string? ProjectGoal, string? NameAr, string? NameEn, string? Equation, string? Direction,
    string? IndexClassCode, decimal? Target, int? TargetYear, string? WhenMeasured, decimal? CurrentResult, decimal? Performance, decimal? CappedPerformance,
    string GoalStatus, string CreatedStage, string Status, uint Version);

public sealed record ProjectRiskDto(
    Guid Id, string? Title, string? CategoryCode, int Probability, int Impact, string? RiskFactor, int? ControlEvaluation, int InherentScore, string InherentBand,
    decimal ResidualScore, string ResidualBand, string? Response, string? MitigationActions, decimal? ImplementationPct, DateOnly? ImplementationStart,
    DateOnly? ImplementationEnd, PortfolioPersonDto? Owner, string? RmsStatus, string CreatedStage, string Status, uint Version);

public sealed record StakeholderDto(
    Guid Id, string? Name, string? ClassificationCode, string? InterestLevel, string? ImpactLevel, string? CommunicationMethod, string? CommunicationFrequency,
    string? Email, string? Phone, string Status, uint Version);

public sealed record ProposedMemberDto(Guid Id, PortfolioPersonDto? Employee, string? RoleAr, string? RoleEn, string? Responsibilities, string Status, uint Version);

public sealed record PlanningItemDto(Guid Id, string Category, string? Description, string CreatedStage, string Status, uint Version);

public sealed record ChallengeDto(
    Guid Id, string? Challenge, string? Impact, decimal? AchievedPct, string? Recommendation, DateOnly? ReportDate, Guid? MilestoneId, string Status, uint Version);

public sealed record LessonDto(Guid Id, string? Stage, string? Lesson, string? TypeCode, string? Recommendation, string Status, uint Version);

public sealed record ProjectMeetingDto(Guid Id, DateTimeOffset MeetingAt, string? Title, string? Notes, string Status, uint Version);

public sealed record ContractDto(
    Guid Id, string? Name, string? Number, string? Supplier, DateOnly? IssueDate, decimal? Amount, string ContractStatus, string Status, uint Version);

public sealed record KpiInput(ProjectKpiValues Values, uint? Version = null);

public sealed record RiskInput(ProjectRiskValues Values, uint? Version = null);

public sealed record StakeholderInput(StakeholderValues Values, uint? Version = null);

public sealed record ProposedMemberInput(TeamMemberValues Values, uint? Version = null);

public sealed record PlanningItemInput(PlanningItemValues Values, uint? Version = null);

public sealed record ChallengeInput(ChallengeValues Values, uint? Version = null);

public sealed record LessonInput(LessonValues Values, uint? Version = null);

public sealed record ProjectMeetingInput(MeetingValues Values, uint? Version = null);

public sealed record ContractInput(ContractValues Values, uint? Version = null);

public sealed record DeactivateInput(string? Reason, uint Version);

/// <summary>Scores project risks (ADR 0074): the shared risk framework when the Risk module provides it (ADR 0101), else the Phase 9 rule.</summary>
public interface IProjectRiskScoring
{
    Task<RiskScore> ScoreAsync(int probability, int impact, int? controlEvaluation, CancellationToken cancellationToken);
}

internal sealed class DefaultProjectRiskScoring(IServiceProvider services) : IProjectRiskScoring
{
    public async Task<RiskScore> ScoreAsync(int probability, int impact, int? controlEvaluation, CancellationToken cancellationToken)
    {
        if (services.GetService<Risk.Contracts.IRiskScoring>() is not { } shared)
        {
            return DefaultRiskScoring.Score(probability, impact, controlEvaluation);
        }

        // The inherent column stays an integer (no Portfolio migration, ADR 0101); the band comes from the framework's exact score.
        var score = await shared.ScoreProjectRiskAsync(probability, impact, controlEvaluation, cancellationToken).ConfigureAwait(false);
        return new RiskScore((int)Math.Round(score.InherentScore, MidpointRounding.AwayFromZero), score.InherentBand, score.ResidualScore, score.ResidualBand);
    }
}

/// <summary>Who may write which kind of row, and when (docs/design/portfolio.md §22).</summary>
internal enum ItemRule
{
    /// <summary>PM / PM Office while the content may change (not during a stage approval, not after the end).</summary>
    Content,

    /// <summary>Owner, PM, team members, PM Office on an active project.</summary>
    Contribution,

    /// <summary>PM / PM Office or Administration (LPO) on an active project.</summary>
    Contract,
}

/// <summary>Project rows: KPIs, risks, stakeholders, proposed members, planning items, challenges, lessons, meetings, contracts.</summary>
internal sealed class ProjectItems(
    PortfolioDbContext db, ProjectLoader loader, RequestReader reader, IProjectRiskScoring scoring, ILookupService lookups, IServiceProvider services,
    IRequestContext context)
{
    private const int Text = RequestRules.TextMaxLength;

    // ---- lists -------------------------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ProjectKpiDto>>> KpisAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<ProjectKpi, ProjectKpiDto>(projectId, async rows => await KpiDtosAsync(rows, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<ProjectRiskDto>>> RisksAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<ProjectRisk, ProjectRiskDto>(projectId, async rows =>
        {
            var people = await reader.PeopleAsync(rows.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
            return rows.Select(r => RiskDto(r, people)).ToList();
        }, ct).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<StakeholderDto>>> StakeholdersAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<Stakeholder, StakeholderDto>(projectId, rows => Task.FromResult<IReadOnlyList<StakeholderDto>>(rows.Select(StakeholderDto).ToList()), ct)
            .ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<ProposedMemberDto>>> MembersAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<ProposedMember, ProposedMemberDto>(projectId, async rows =>
        {
            var people = await reader.PeopleAsync(rows.Select(r => (Guid?)r.EmployeeId), ct).ConfigureAwait(false);
            return rows.Select(r => MemberDto(r, people)).ToList();
        }, ct).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<PlanningItemDto>>> PlanningItemsAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<PlanningItem, PlanningItemDto>(projectId, rows => Task.FromResult<IReadOnlyList<PlanningItemDto>>(rows.Select(PlanningDto).ToList()), ct)
            .ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<ChallengeDto>>> ChallengesAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<Challenge, ChallengeDto>(projectId, rows => Task.FromResult<IReadOnlyList<ChallengeDto>>(rows.Select(ChallengeDto).ToList()), ct)
            .ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<LessonDto>>> LessonsAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<Lesson, LessonDto>(projectId, rows => Task.FromResult<IReadOnlyList<LessonDto>>(rows.Select(LessonDto).ToList()), ct).ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<ProjectMeetingDto>>> MeetingsAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<Meeting, ProjectMeetingDto>(projectId, rows => Task.FromResult<IReadOnlyList<ProjectMeetingDto>>(rows.OrderByDescending(m => m.MeetingAt).Select(ProjectMeetingDto).ToList()), ct)
            .ConfigureAwait(false);

    public async Task<Result<IReadOnlyList<ContractDto>>> ContractsAsync(Guid projectId, CancellationToken ct) =>
        await ListAsync<Contract, ContractDto>(projectId, rows => Task.FromResult<IReadOnlyList<ContractDto>>(rows.Select(ContractDto).ToList()), ct).ConfigureAwait(false);

    // ---- saves (create when itemId is null) ----------------------------------------------------------------------------------------------

    public async Task<Result<ProjectKpiDto>> SaveKpiAsync(Guid projectId, Guid? itemId, KpiInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        Code(errors, "nature", v.Nature, KpiNatures.All);
        Code(errors, "whenMeasured", v.WhenMeasured, WhenMeasured.All, optional: true);
        Lengths(errors, ("nameAr", v.NameAr, RequestRules.NameMaxLength), ("nameEn", v.NameEn, RequestRules.NameMaxLength), ("projectGoal", v.ProjectGoal, Text),
            ("equation", v.Equation, Text));
        await LookupAsync(errors, "indexClassCode", PortfolioPermissions.KpiIndexClassLookup, v.IndexClassCode, ct).ConfigureAwait(false);
        if (v.TargetYear is { } year && (year < 1000 || year > 9999))
        {
            Add(errors, "targetYear", "portfolio.errors.fourDigitYear");
        }

        if (v.Nature == KpiNatures.Existing)
        {
            if (v.KpiCardId is not { } card || services.GetService<IKpiCatalog>() is not { } catalog
                || await catalog.GetAsync(card, context.User, ct).ConfigureAwait(false) is null)
            {
                Add(errors, "kpiCardId", "portfolio.errors.kpiCardUnknown");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
            {
                Add(errors, "nameEn", "validation.atLeastOneLanguage");
            }

            Code(errors, "direction", v.Direction, ["increase", "decrease", "matching"], optional: true);
        }

        return await SaveAsync<ProjectKpi, ProjectKpiDto>(projectId, itemId, input.Version, ItemRule.Content, errors,
            p => ProjectKpi.Create(p.Id, p.Stage, v), k => k.Edit(v), async k => (await KpiDtosAsync([k], ct).ConfigureAwait(false))[0], ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectRiskDto>> SaveRiskAsync(Guid projectId, Guid? itemId, RiskInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        Range(errors, "probability", v.Probability, 1, 5);
        Range(errors, "impact", v.Impact, 1, 5);
        if (v.ControlEvaluation is { } control)
        {
            Range(errors, "controlEvaluation", control, 1, 10);
        }

        if (string.IsNullOrWhiteSpace(v.Title))
        {
            Add(errors, "title", "validation.notEmpty");
        }

        Code(errors, "response", v.Response, RiskResponses.All, optional: true);
        Lengths(errors, ("title", v.Title, RequestRules.NameMaxLength), ("riskFactor", v.RiskFactor, Text), ("mitigationActions", v.MitigationActions, Text));
        await LookupAsync(errors, "categoryCode", PortfolioPermissions.RiskCategoryLookup, v.CategoryCode, ct).ConfigureAwait(false);
        if (v.ImplementationPct is < 0 or > 1)
        {
            Add(errors, "implementationPct", "validation.inclusiveBetween");
        }

        var score = await scoring.ScoreAsync(v.Probability, v.Impact, v.ControlEvaluation, ct).ConfigureAwait(false);
        return await SaveAsync<ProjectRisk, ProjectRiskDto>(projectId, itemId, input.Version, ItemRule.Content, errors,
            p => ProjectRisk.Create(p.Id, p.Stage, v, score), r => r.Edit(v, score), async r =>
            {
                var people = await reader.PeopleAsync([r.OwnerEmployeeId], ct).ConfigureAwait(false);
                return RiskDto(r, people);
            }, ct).ConfigureAwait(false);
    }

    public async Task<Result<StakeholderDto>> SaveStakeholderAsync(Guid projectId, Guid? itemId, StakeholderInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Name))
        {
            Add(errors, "name", "validation.notEmpty");
        }

        Code(errors, "interestLevel", v.InterestLevel, Levels.All, optional: true);
        Code(errors, "impactLevel", v.ImpactLevel, Levels.All, optional: true);
        Lengths(errors, ("name", v.Name, RequestRules.NameMaxLength), ("communicationMethod", v.CommunicationMethod, RequestRules.NameMaxLength),
            ("communicationFrequency", v.CommunicationFrequency, RequestRules.NameMaxLength), ("email", v.Email, RequestRules.NameMaxLength), ("phone", v.Phone, 100));
        if (v.Email is { Length: > 0 } email && !email.Contains('@', StringComparison.Ordinal))
        {
            Add(errors, "email", "validation.email");
        }

        await LookupAsync(errors, "classificationCode", PortfolioPermissions.StakeholderClassLookup, v.ClassificationCode, ct).ConfigureAwait(false);
        return await SaveAsync<Stakeholder, StakeholderDto>(projectId, itemId, input.Version, ItemRule.Content, errors, p => Stakeholder.Create(p.Id, v), s => s.Edit(v),
            s => Task.FromResult(StakeholderDto(s)), ct).ConfigureAwait(false);
    }

    public async Task<Result<ProposedMemberDto>> SaveMemberAsync(Guid projectId, Guid? itemId, ProposedMemberInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        var people = await reader.PeopleAsync([v.EmployeeId], ct).ConfigureAwait(false);
        if (people.Count == 0)
        {
            Add(errors, "employeeId", "validation.unknownCode");
        }
        else if (await db.Members.AnyAsync(m => m.ProjectId == projectId && m.EmployeeId == v.EmployeeId && m.Status == ItemStatuses.Active && m.Id != itemId, ct)
                     .ConfigureAwait(false))
        {
            Add(errors, "employeeId", "validation.duplicate");
        }

        Lengths(errors, ("roleAr", v.RoleAr, RequestRules.NameMaxLength), ("roleEn", v.RoleEn, RequestRules.NameMaxLength), ("responsibilities", v.Responsibilities, Text));
        return await SaveAsync<ProposedMember, ProposedMemberDto>(projectId, itemId, input.Version, ItemRule.Content, errors, p => ProposedMember.Create(p.Id, v),
            m => m.Edit(v), m => Task.FromResult(MemberDto(m, people)), ct).ConfigureAwait(false);
    }

    public async Task<Result<PlanningItemDto>> SavePlanningItemAsync(Guid projectId, Guid? itemId, PlanningItemInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        Code(errors, "category", v.Category, PlanningCategories.All);
        if (string.IsNullOrWhiteSpace(v.Description))
        {
            Add(errors, "description", "validation.notEmpty");
        }

        Lengths(errors, ("description", v.Description, Text));
        return await SaveAsync<PlanningItem, PlanningItemDto>(projectId, itemId, input.Version, ItemRule.Content, errors, p => PlanningItem.Create(p.Id, p.Stage, v),
            i => i.Edit(v), i => Task.FromResult(PlanningDto(i)), ct).ConfigureAwait(false);
    }

    public async Task<Result<ChallengeDto>> SaveChallengeAsync(Guid projectId, Guid? itemId, ChallengeInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Challenge))
        {
            Add(errors, "challenge", "validation.notEmpty");
        }

        Lengths(errors, ("challenge", v.Challenge, Text), ("impact", v.Impact, Text), ("recommendation", v.Recommendation, Text));
        if (v.AchievedPct is < 0 or > 1)
        {
            Add(errors, "achievedPct", "validation.inclusiveBetween");
        }

        if (v.MilestoneId is { } m && !await db.Milestones.AnyAsync(x => x.Id == m && x.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            Add(errors, "milestoneId", "validation.unknownCode");
        }

        return await SaveAsync<Challenge, ChallengeDto>(projectId, itemId, input.Version, ItemRule.Contribution, errors, p => Challenge.Create(p.Id, v), c => c.Edit(v),
            c => Task.FromResult(ChallengeDto(c)), ct).ConfigureAwait(false);
    }

    public async Task<Result<LessonDto>> SaveLessonAsync(Guid projectId, Guid? itemId, LessonInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Lesson))
        {
            Add(errors, "lesson", "validation.notEmpty");
        }

        Code(errors, "stage", v.Stage, ProjectStages.ProjectPath, optional: true);
        Lengths(errors, ("lesson", v.Lesson, Text), ("recommendation", v.Recommendation, Text));
        await LookupAsync(errors, "typeCode", PortfolioPermissions.LessonTypeLookup, v.TypeCode, ct).ConfigureAwait(false);
        return await SaveAsync<Lesson, LessonDto>(projectId, itemId, input.Version, ItemRule.Contribution, errors, p => Lesson.Create(p.Id, v), l => l.Edit(v),
            l => Task.FromResult(LessonDto(l)), ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectMeetingDto>> SaveMeetingAsync(Guid projectId, Guid? itemId, ProjectMeetingInput input, CancellationToken ct)
    {
        var v = input.Values with { Notes = RichText.Sanitise(input.Values.Notes) };
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Title))
        {
            Add(errors, "title", "validation.notEmpty");
        }

        Lengths(errors, ("title", v.Title, RequestRules.NameMaxLength), ("notes", v.Notes, 20000));
        return await SaveAsync<Meeting, ProjectMeetingDto>(projectId, itemId, input.Version, ItemRule.Contribution, errors, p => Meeting.Create(p.Id, v), m => m.Edit(v),
            m => Task.FromResult(ProjectMeetingDto(m)), ct).ConfigureAwait(false);
    }

    public async Task<Result<ContractDto>> SaveContractAsync(Guid projectId, Guid? itemId, ContractInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Name))
        {
            Add(errors, "name", "validation.notEmpty");
        }

        Code(errors, "contractStatus", v.ContractStatus, ["active", "closed", "cancelled"], optional: true);
        Lengths(errors, ("name", v.Name, RequestRules.NameMaxLength), ("number", v.Number, 100), ("supplier", v.Supplier, RequestRules.NameMaxLength));
        if (v.Amount is { } a && (a < 0 || decimal.Round(a, 2) != a))
        {
            Add(errors, "amount", "portfolio.errors.twoDecimals");
        }

        return await SaveAsync<Contract, ContractDto>(projectId, itemId, input.Version, ItemRule.Contract, errors, p => Contract.Create(p.Id, v), c => c.Edit(v),
            c => Task.FromResult(ContractDto(c)), ct).ConfigureAwait(false);
    }

    /// <summary>Deactivates a row of <paramref name="kind"/> (no hard delete).</summary>
    public async Task<Result> DeactivateAsync(Guid projectId, string kind, Guid itemId, DeactivateInput input, CancellationToken ct) => kind switch
    {
        "kpis" => await DeactivateAsync<ProjectKpi>(projectId, itemId, input, ItemRule.Content, ct).ConfigureAwait(false),
        "risks" => await DeactivateAsync<ProjectRisk>(projectId, itemId, input, ItemRule.Content, ct).ConfigureAwait(false),
        "stakeholders" => await DeactivateAsync<Stakeholder>(projectId, itemId, input, ItemRule.Content, ct).ConfigureAwait(false),
        "members" => await DeactivateAsync<ProposedMember>(projectId, itemId, input, ItemRule.Content, ct).ConfigureAwait(false),
        "planning-items" => await DeactivateAsync<PlanningItem>(projectId, itemId, input, ItemRule.Content, ct).ConfigureAwait(false),
        "challenges" => await DeactivateAsync<Challenge>(projectId, itemId, input, ItemRule.Contribution, ct).ConfigureAwait(false),
        "lessons" => await DeactivateAsync<Lesson>(projectId, itemId, input, ItemRule.Contribution, ct).ConfigureAwait(false),
        "meetings" => await DeactivateAsync<Meeting>(projectId, itemId, input, ItemRule.Contribution, ct).ConfigureAwait(false),
        "contracts" => await DeactivateAsync<Contract>(projectId, itemId, input, ItemRule.Contract, ct).ConfigureAwait(false),
        _ => Error.NotFound(),
    };

    // ---- generic plumbing -------------------------------------------------------------------------------------------------------------------

    private async Task<Result<IReadOnlyList<TDto>>> ListAsync<TEntity, TDto>(Guid projectId, Func<List<TEntity>, Task<IReadOnlyList<TDto>>> map, CancellationToken ct)
        where TEntity : ProjectChild
    {
        if (await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var rows = await db.Set<TEntity>().AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return Result.Success(await map(rows).ConfigureAwait(false));
    }

    private async Task<Result<TDto>> SaveAsync<TEntity, TDto>(
        Guid projectId, Guid? itemId, uint? version, ItemRule rule, List<FieldError> errors, Func<Project, TEntity> create, Action<TEntity> edit,
        Func<TEntity, Task<TDto>> map, CancellationToken ct)
        where TEntity : ProjectChild
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (await RuleErrorAsync(project, rule, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        TEntity entity;
        if (itemId is { } id)
        {
            var found = await db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            if (found.Status != ItemStatuses.Active)
            {
                return Error.Rule("portfolio.item.inactive", "portfolio.errors.itemInactive");
            }

            if (version is { } v)
            {
                db.Entry(found).Property(x => x.Version).OriginalValue = v;
            }

            edit(found);
            entity = found;
        }
        else
        {
            entity = create(project);
            db.Set<TEntity>().Add(entity);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await map(entity).ConfigureAwait(false);
    }

    private async Task<Result> DeactivateAsync<TEntity>(Guid projectId, Guid itemId, DeactivateInput input, ItemRule rule, CancellationToken ct)
        where TEntity : ProjectChild
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (await RuleErrorAsync(project, rule, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var found = await db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == itemId && x.ProjectId == projectId, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Error.NotFound();
        }

        if ((input.Reason?.Length ?? 0) > Text)
        {
            return Error.Validation([new FieldError("reason", "validation.maximumLength", "validation.maximumLength")]);
        }

        db.Entry(found).Property(x => x.Version).OriginalValue = input.Version;
        found.Deactivate(input.Reason);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return Result.Success();
    }

    private async Task<Error?> RuleErrorAsync(Project project, ItemRule rule, CancellationToken ct)
    {
        var user = context.User;
        switch (rule)
        {
            case ItemRule.Content:
                if (!await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false))
                {
                    return Error.Forbidden();
                }

                return ProjectLifecycle.CanEditContent(project.Status, project.SubState) ? null : ProjectService.NotInStage(project);
            case ItemRule.Contribution:
                if (!await loader.CanAsync(user, PortfolioPermissions.ProjectContribute, project, ct).ConfigureAwait(false))
                {
                    return Error.Forbidden();
                }

                break;
            default:
                if (!await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false)
                    && !await loader.CanAsync(user, PortfolioPermissions.ProjectLpo, project, ct).ConfigureAwait(false))
                {
                    return Error.Forbidden();
                }

                break;
        }

        return project.Status == ProjectStatuses.Active && project.SubState != SubStates.Done ? null : ProjectService.NotInStage(project);
    }

    private async Task<IReadOnlyList<ProjectKpiDto>> KpiDtosAsync(IReadOnlyList<ProjectKpi> rows, CancellationToken ct)
    {
        var catalog = services.GetService<IKpiCatalog>();
        var performance = services.GetService<IKpiPerformance>();
        var year = (await reader.TodayAsync(ct).ConfigureAwait(false)).Year;
        var result = new List<ProjectKpiDto>();
        foreach (var k in rows)
        {
            string? cardCode = null;
            decimal? actual = null, capped = null;
            var status = RagCodes.None;
            if (k.Nature == KpiNatures.Existing && k.KpiCardId is { } card && catalog is not null)
            {
                var summary = await catalog.GetAsync(card, context.User, ct).ConfigureAwait(false);
                cardCode = summary?.Code;
                if (summary is not null && await catalog.GetPerformanceAsync(card, year, context.User, ct).ConfigureAwait(false) is { } perf)
                {
                    (actual, capped, status) = (perf.Actual, perf.Capped, perf.Rag);
                }
            }
            else if (performance is not null && k.Target is not null && k.CurrentResult is not null)
            {
                var calculated = performance.Calculate("single", k.Direction ?? "increase", k.Target, null, null, k.CurrentResult);
                (actual, capped) = (calculated.Actual, calculated.Capped);
                status = await performance.BandAsync(capped, cancellationToken: ct).ConfigureAwait(false);
            }

            result.Add(new ProjectKpiDto(k.Id, k.Nature, k.KpiCardId, cardCode, k.ProjectGoal, k.NameAr, k.NameEn, k.Equation, k.Direction, k.IndexClassCode, k.Target,
                k.TargetYear, k.WhenMeasured, k.CurrentResult, actual, capped, status, k.CreatedStage, k.Status, k.Version));
        }

        return result;
    }

    private static ProjectRiskDto RiskDto(ProjectRisk r, Dictionary<Guid, PortfolioPersonDto> people) =>
        new(r.Id, r.Title, r.CategoryCode, r.Probability, r.Impact, r.RiskFactor, r.ControlEvaluation, r.InherentScore, r.InherentBand, r.ResidualScore, r.ResidualBand,
            r.Response, r.MitigationActions, r.ImplementationPct, r.ImplementationStart, r.ImplementationEnd,
            r.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, r.RmsStatus, r.CreatedStage, r.Status, r.Version);

    private static StakeholderDto StakeholderDto(Stakeholder s) =>
        new(s.Id, s.Name, s.ClassificationCode, s.InterestLevel, s.ImpactLevel, s.CommunicationMethod, s.CommunicationFrequency, s.Email, s.Phone, s.Status, s.Version);

    private static ProposedMemberDto MemberDto(ProposedMember m, Dictionary<Guid, PortfolioPersonDto> people) =>
        new(m.Id, people.GetValueOrDefault(m.EmployeeId), m.RoleAr, m.RoleEn, m.Responsibilities, m.Status, m.Version);

    private static PlanningItemDto PlanningDto(PlanningItem i) => new(i.Id, i.Category, i.Description, i.CreatedStage, i.Status, i.Version);

    private static ChallengeDto ChallengeDto(Challenge c) =>
        new(c.Id, c.Text, c.Impact, c.AchievedPct, c.Recommendation, c.ReportDate, c.MilestoneId, c.Status, c.Version);

    private static LessonDto LessonDto(Lesson l) => new(l.Id, l.Stage, l.Text, l.TypeCode, l.Recommendation, l.Status, l.Version);

    private static ProjectMeetingDto ProjectMeetingDto(Meeting m) => new(m.Id, m.MeetingAt, m.Title, m.Notes, m.Status, m.Version);

    private static ContractDto ContractDto(Contract c) => new(c.Id, c.Name, c.Number, c.Supplier, c.IssueDate, c.Amount, c.ContractStatus, c.Status, c.Version);

    private static void Add(List<FieldError> errors, string field, string key) => errors.Add(new FieldError(field, key, key));

    private static void Range(List<FieldError> errors, string field, int value, int from, int to)
    {
        if (value < from || value > to)
        {
            errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = from, ["to"] = to }));
        }
    }

    private static void Code(List<FieldError> errors, string field, string? value, IReadOnlyList<string> allowed, bool optional = false)
    {
        if (string.IsNullOrWhiteSpace(value) ? !optional : !allowed.Contains(value, StringComparer.Ordinal))
        {
            Add(errors, field, string.IsNullOrWhiteSpace(value) ? "validation.notEmpty" : "validation.unknownCode");
        }
    }

    private static void Lengths(List<FieldError> errors, params (string Field, string? Value, int Max)[] values)
    {
        foreach (var (field, value, max) in values)
        {
            if (value is { } v && v.Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max }));
            }
        }
    }

    private async Task LookupAsync(List<FieldError> errors, string field, string type, string? code, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code) && !await lookups.IsActiveCodeAsync(type, code.Trim(), ct).ConfigureAwait(false))
        {
            Add(errors, field, "validation.unknownCode");
        }
    }
}

/// <summary>Minutes are rich text: keep a safe subset (the SPA renders it through DOMPurify as well).</summary>
internal static class RichText
{
    private static readonly System.Text.RegularExpressions.Regex Dangerous = new(
        @"<\s*(script|style|iframe|object|embed|link|meta)[^>]*>.*?<\s*/\s*\1\s*>|<\s*(script|style|iframe|object|embed|link|meta)[^>]*/?>|\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)|javascript:",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static string? Sanitise(string? html) => html is null ? null : Dangerous.Replace(html, string.Empty);
}
