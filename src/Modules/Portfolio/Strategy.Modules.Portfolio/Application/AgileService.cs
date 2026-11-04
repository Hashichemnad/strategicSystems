using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ReleaseDto(Guid Id, string? NameAr, string? NameEn, string? Goal, DateOnly? TargetDate, int Sort, string Status, int Points, int DonePoints, uint Version);

public sealed record EpicDto(
    Guid Id, string Code, Guid? ReleaseId, Guid? MilestoneId, string? TitleAr, string? TitleEn, decimal Weight, string? AcceptanceCriteria, int Sort, string Status, int Points,
    int DonePoints, decimal ApprovedFraction, decimal PendingFraction, uint Version);

public sealed record StoryDto(
    Guid Id, string Code, Guid EpicId, Guid? SprintId, string? Title, string? AsA, string? IWant, string? SoThat, string? AcceptanceCriteria, int StoryPoints, string Priority,
    PortfolioPersonDto? Assignee, string ColumnCode, string Category, int Rank, DateTimeOffset? StartedAt, DateTimeOffset? DoneAt, bool Accepted, bool ApprovedDone,
    string Status, bool CanMove, uint Version);

public sealed record SprintDto(
    Guid Id, int Number, string? Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate, int? CapacityPoints, string Status, int CommittedPoints, int CompletedPoints,
    int PlannedPoints, string? DemoOutcome, string? StakeholderFeedback, string? WentWell, string? ToImprove, string? Actions, string? ApprovalPeriod, DateTimeOffset? ApprovedAt,
    uint Version);

public sealed record AgilePermissionsDto(bool CanSetMethodology, bool CanPlan, bool CanCreateStory, bool CanRunSprints, bool CanSubmitSprint);

public sealed record ReleaseBurnupDto(Guid ReleaseId, IReadOnlyList<BurnupPoint> Points);

public sealed record AgileMetricsDto(
    IReadOnlyList<VelocityPoint> Velocity, IReadOnlyList<FlowPoint> CumulativeFlow, IReadOnlyList<CycleTimePoint> CycleTimes, decimal? AverageCycleTime,
    IReadOnlyList<ReleaseBurnupDto> Burnup);

public sealed record AgileViewDto(
    string Methodology, string ApprovalMode, int? SprintLengthDays, string? DefinitionOfDone, BoardConfig Board, IReadOnlyList<ReleaseDto> Releases,
    IReadOnlyList<EpicDto> Epics, IReadOnlyList<StoryDto> Stories, IReadOnlyList<SprintDto> Sprints, decimal ApprovedProgress, decimal PendingProgress, decimal EpicWeight,
    AgilePermissionsDto Permissions, AgileMetricsDto Metrics, uint ProjectVersion);

public sealed record BurndownDto(Guid SprintId, int Number, DateOnly? Start, DateOnly? End, int Committed, IReadOnlyList<BurnPoint> Points);

public sealed record MethodologyInput(string Methodology, int? SprintLengthDays, string? DefinitionOfDone, string? AgileApprovalMode, uint Version);

public sealed record BoardInput(BoardConfig Board, uint Version);

public sealed record ReleaseInput(ReleaseValues Values, uint? Version = null);

public sealed record EpicInput(EpicValues Values, uint? Version = null);

public sealed record StoryInput(StoryValues Values, uint? Version = null);

public sealed record StoryMoveInput(string Column, int Rank, uint Version);

public sealed record SprintInput(SprintValues Values, uint? Version = null);

public sealed record SprintCloseInput(SprintReviewValues Values, uint Version);

public sealed record SprintActionInput(uint Version, string? Comment = null);

public sealed record ImpedimentInput(string? Title, string? Description, Guid? OwnerEmployeeId, string? Priority);

/// <summary>Agile and hybrid delivery (SPEC §1.13, docs/design/portfolio.md §29–§31, ADRs 0090 / 0091).</summary>
internal sealed class AgileService(
    PortfolioDbContext db,
    ProjectLoader loader,
    RequestReader reader,
    ProjectProgress progress,
    IApprovalEngine engine,
    IRequestContext context,
    IClock clock)
{
    private const int TextMax = RequestRules.TextMaxLength;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- reading ----------

    public async Task<Result<AgileViewDto>> ViewAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        return project is null ? Error.NotFound() : await ToViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<BurndownDto>> BurndownAsync(Guid sprintId, CancellationToken ct)
    {
        var sprint = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sprintId, ct).ConfigureAwait(false);
        if (sprint is null || await loader.FindAsync(sprint.ProjectId, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var committed = sprint.Committed();
        var histories = await HistoriesAsync(sprint.ProjectId, committed, ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var points = sprint.StartDate is { } s && sprint.EndDate is { } e && sprint.Status != SprintStatuses.Planned
            ? AgileMetrics.Burndown(s, e, histories, sprint.Status == SprintStatuses.Active ? today : null)
            : [];
        return new BurndownDto(sprint.Id, sprint.Number, sprint.StartDate, sprint.EndDate, sprint.CommittedPoints, points);
    }

    // ---------- methodology and board ----------

    public async Task<Result<AgileViewDto>> SetMethodologyAsync(Guid projectId, MethodologyInput input, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var template = await loader.TemplateAsync(project, ct).ConfigureAwait(false);
        if (!CanSetMethodology(project, template))
        {
            return Error.Rule("portfolio.methodology.locked", "portfolio.errors.methodologyLocked");
        }

        var errors = new List<FieldError>();
        if (!Methodologies.All.Contains(input.Methodology))
        {
            errors.Add(new FieldError("methodology", "validation.unknownCode", "validation.unknownCode"));
        }

        if (Methodologies.UsesSprints(input.Methodology))
        {
            if (input.SprintLengthDays is not { } days || days is < 1 or > 28)
            {
                errors.Add(new FieldError("sprintLengthDays", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = 1, ["to"] = 28 }));
            }

            if (input.DefinitionOfDone is { Length: > RequestRules.LongTextMaxLength })
            {
                errors.Add(new FieldError("definitionOfDone", "validation.maximumLength", "validation.maximumLength"));
            }

            if (input.AgileApprovalMode is { } mode && !AgileApprovalModes.All.Contains(mode))
            {
                errors.Add(new FieldError("agileApprovalMode", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetMethodology(input.Methodology, input.SprintLengthDays, input.DefinitionOfDone, input.AgileApprovalMode);
        if (Methodologies.UsesSprints(input.Methodology) && project.Board is null)
        {
            project.SetBoard(BoardConfig.Default);
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> SetBoardAsync(Guid projectId, BoardInput input, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!Live(project))
        {
            return ProjectService.NotInStage(project);
        }

        var board = input.Board;
        var errors = board?.Problems() ?? [new FieldError("board", "validation.notEmpty", "validation.notEmpty")];
        if (errors.Count == 0)
        {
            var codes = board!.Columns.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
            var orphan = await db.Stories.AnyAsync(s => s.ProjectId == projectId && s.Status == ItemStatuses.Active && !codes.Contains(s.ColumnCode), ct).ConfigureAwait(false);
            if (orphan)
            {
                errors = [new FieldError("columns", "portfolio.errors.columnNotEmpty", "portfolio.errors.columnNotEmpty")];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetBoard(board!);
        foreach (var story in await db.Stories.Where(s => s.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false))
        {
            if (board!.CategoryOf(story.ColumnCode) is { } category && category != story.Category)
            {
                story.Move(story.ColumnCode, category, story.Rank, clock.UtcNow);
            }
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    // ---------- releases and epics (plan structure) ----------

    public async Task<Result<AgileViewDto>> SaveReleaseAsync(Guid projectId, Guid? id, ReleaseInput input, CancellationToken ct)
    {
        var (project, error) = await PlannableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        Length(errors, ("nameAr", v.NameAr, RequestRules.NameMaxLength), ("nameEn", v.NameEn, RequestRules.NameMaxLength), ("goal", v.Goal, TextMax));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (id is { } releaseId)
        {
            var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId && r.ProjectId == projectId, ct).ConfigureAwait(false);
            if (release is null)
            {
                return Error.NotFound();
            }

            Concurrency(release, input.Version);
            release.Edit(v);
        }
        else
        {
            db.Releases.Add(Release.Create(projectId, v));
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> SaveEpicAsync(Guid projectId, Guid? id, EpicInput input, CancellationToken ct)
    {
        var (project, error) = await PlannableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var v = input.Values;
        var epics = await db.Epics.Where(e => e.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false);
        var self = id is { } epicId ? epics.FirstOrDefault(e => e.Id == epicId) : null;
        if (id is not null && self is null)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        Length(errors, ("titleAr", v.TitleAr, RequestRules.NameMaxLength), ("titleEn", v.TitleEn, RequestRules.NameMaxLength),
            ("acceptanceCriteria", v.AcceptanceCriteria, TextMax));
        if (v.Weight <= 0 || v.Weight > 1 || decimal.Round(v.Weight, 4) != v.Weight)
        {
            errors.Add(new FieldError("weight", "portfolio.errors.weightRange", "portfolio.errors.weightRange"));
        }

        if (v.ReleaseId is { } r && !await db.Releases.AnyAsync(x => x.Id == r && x.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("releaseId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (project.Methodology == Methodologies.Hybrid)
        {
            if (v.MilestoneId is not { } m
                || !await db.Milestones.AnyAsync(x => x.Id == m && x.ProjectId == projectId && x.DeliveredInSprints && x.Status != MilestoneStatuses.Cancelled, ct)
                    .ConfigureAwait(false))
            {
                errors.Add(new FieldError("milestoneId", "portfolio.errors.epicMilestone", "portfolio.errors.epicMilestone"));
            }
        }
        else if (v.MilestoneId is not null)
        {
            errors.Add(new FieldError("milestoneId", "portfolio.errors.epicMilestone", "portfolio.errors.epicMilestone"));
        }

        var total = epics.Where(e => e.IsActive && e.Id != self?.Id && e.MilestoneId == v.MilestoneId).Sum(e => e.Weight) + v.Weight;
        if (total > 1)
        {
            errors.Add(new FieldError("weight", "portfolio.errors.weightsOver100", "portfolio.errors.weightsOver100",
                new Dictionary<string, object?> { ["total"] = ProjectGates.Percent(total) }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (self is null)
        {
            db.Epics.Add(Epic.Create(projectId, $"{project.Number}-E{epics.Count + 1}", v));
        }
        else
        {
            Concurrency(self, input.Version);
            self.Edit(v);
        }

        await progress.RecalculateAsync(await db.Projects.FirstAsync(p => p.Id == projectId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> CancelEpicAsync(Guid projectId, Guid epicId, CancellationToken ct)
    {
        var (project, error) = await PlannableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var epic = await db.Epics.FirstOrDefaultAsync(e => e.Id == epicId && e.ProjectId == projectId, ct).ConfigureAwait(false);
        if (epic is null)
        {
            return Error.NotFound();
        }

        if (await db.Stories.AnyAsync(s => s.EpicId == epicId && s.ApprovedDone, ct).ConfigureAwait(false))
        {
            return Error.Rule("portfolio.epic.approved", "portfolio.errors.epicHasApprovedStories");
        }

        epic.Cancel();
        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    // ---------- stories ----------

    public async Task<Result<AgileViewDto>> SaveStoryAsync(Guid projectId, Guid? id, StoryInput input, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await CanCreateStoryAsync(project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!Live(project) || !Methodologies.UsesSprints(project.Methodology))
        {
            return ProjectService.NotInStage(project);
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Title))
        {
            errors.Add(new FieldError("title", "validation.notEmpty", "validation.notEmpty"));
        }

        Length(errors, ("title", v.Title, RequestRules.NameMaxLength), ("asA", v.AsA, RequestRules.NameMaxLength), ("iWant", v.IWant, TextMax), ("soThat", v.SoThat, TextMax),
            ("acceptanceCriteria", v.AcceptanceCriteria, TextMax));
        if (v.StoryPoints is < 0 or > UserStory.MaxPoints)
        {
            errors.Add(new FieldError("storyPoints", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 0, ["to"] = UserStory.MaxPoints }));
        }

        if (v.Priority is { } p && !MoscowPriorities.All.Contains(p))
        {
            errors.Add(new FieldError("priority", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!await db.Epics.AnyAsync(e => e.Id == v.EpicId && e.ProjectId == projectId && e.Status != EpicStatuses.Cancelled, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("epicId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v.SprintId is { } sid && !await db.Sprints.AnyAsync(
                s => s.Id == sid && s.ProjectId == projectId && (s.Status == SprintStatuses.Planned || s.Status == SprintStatuses.Active), ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("sprintId", "portfolio.errors.sprintNotOpen", "portfolio.errors.sprintNotOpen"));
        }

        if (v.AssigneeEmployeeId is { } a && (await reader.PeopleAsync([a], ct).ConfigureAwait(false)).Count == 0)
        {
            errors.Add(new FieldError("assigneeEmployeeId", "validation.unknownCode", "validation.unknownCode"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var board = project.BoardOrDefault();
        if (id is { } storyId)
        {
            var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == storyId && s.ProjectId == projectId, ct).ConfigureAwait(false);
            if (story is null)
            {
                return Error.NotFound();
            }

            if (story.ApprovedDone && story.StoryPoints != v.StoryPoints)
            {
                return Error.Rule("portfolio.story.locked", "portfolio.errors.storyLocked");
            }

            Concurrency(story, input.Version);
            story.Edit(v);
        }
        else
        {
            var count = await db.Stories.CountAsync(s => s.ProjectId == projectId, ct).ConfigureAwait(false);
            var story = UserStory.Create(projectId, $"{project.Number}-US{count + 1}", v, board.FirstColumn, count + 1);
            db.Stories.Add(story);
            db.StoryEvents.Add(StoryEvent.Of(story, null, clock.UtcNow));
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> CancelStoryAsync(Guid storyId, CancellationToken ct)
    {
        var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct).ConfigureAwait(false);
        if (story is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await EditableAsync(story.ProjectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (story.ApprovedDone)
        {
            return Error.Rule("portfolio.story.locked", "portfolio.errors.storyLocked");
        }

        story.Cancel();
        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>Drag-and-drop on the board: WIP limit, lock of approved-done stories, history for the metrics.</summary>
    public async Task<Result<AgileViewDto>> MoveStoryAsync(Guid storyId, StoryMoveInput input, CancellationToken ct)
    {
        var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct).ConfigureAwait(false);
        if (story is null)
        {
            return Error.NotFound();
        }

        var project = await loader.FindAsync(story.ProjectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await CanMoveAsync(project, story.AssigneeEmployeeId, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!Live(project) || !story.IsActive)
        {
            return ProjectService.NotInStage(project);
        }

        var board = project.BoardOrDefault();
        var inTarget = await db.Stories.CountAsync(
            s => s.ProjectId == project.Id && s.Status == ItemStatuses.Active && s.ColumnCode == input.Column && s.SprintId == story.SprintId && s.Id != story.Id, ct).ConfigureAwait(false);
        if (BoardRules.CheckMove(board, story.ColumnCode, input.Column, inTarget, story.ApprovedDone) is { } refused)
        {
            return refused;
        }

        Concurrency(story, input.Version);
        var from = story.ColumnCode;
        story.Move(input.Column, board.CategoryOf(input.Column)!, input.Rank, clock.UtcNow);
        if (from != input.Column)
        {
            db.StoryEvents.Add(StoryEvent.Of(story, from, clock.UtcNow));
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    // ---------- sprints ----------

    public async Task<Result<AgileViewDto>> SaveSprintAsync(Guid projectId, Guid? id, SprintInput input, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!Live(project) || !Methodologies.UsesSprints(project.Methodology))
        {
            return ProjectService.NotInStage(project);
        }

        var v = input.Values;
        var sprints = await db.Sprints.Where(s => s.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        Length(errors, ("name", v.Name, RequestRules.NameMaxLength), ("goal", v.Goal, TextMax));
        var start = v.StartDate;
        var end = v.EndDate;
        if (id is null && start is null)
        {
            var last = sprints.Where(s => s.Status != SprintStatuses.Cancelled).MaxBy(s => s.Number);
            start = last?.EndDate?.AddDays(1) ?? project.PlannedStart ?? await reader.TodayAsync(ct).ConfigureAwait(false);
        }

        if (end is null && start is { } s0)
        {
            end = s0.AddDays((project.SprintLengthDays ?? 14) - 1);
        }

        if (start is null || end is null)
        {
            errors.Add(new FieldError("startDate", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (end < start || end.Value.DayNumber - start.Value.DayNumber > 27)
        {
            errors.Add(new FieldError("endDate", "portfolio.errors.sprintLength", "portfolio.errors.sprintLength"));
        }

        if (v.CapacityPoints is < 0 or > 1000)
        {
            errors.Add(new FieldError("capacityPoints", "validation.inclusiveBetween", "validation.inclusiveBetween"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var values = v with { StartDate = start, EndDate = end };
        if (id is { } sprintId)
        {
            var sprint = sprints.FirstOrDefault(s => s.Id == sprintId);
            if (sprint is null)
            {
                return Error.NotFound();
            }

            if (!SprintLifecycle.Allows(sprint.Status, SprintActions.Edit))
            {
                return SprintLifecycle.Next(sprint.Status, SprintActions.Edit).Error!;
            }

            Concurrency(sprint, input.Version);
            sprint.Edit(values);
        }
        else
        {
            db.Sprints.Add(Sprint.Create(projectId, sprints.Count == 0 ? 1 : sprints.Max(s => s.Number) + 1, values));
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> StartSprintAsync(Guid sprintId, SprintActionInput input, CancellationToken ct)
    {
        var (sprint, project, error) = await SprintAsync(sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return error!;
        }

        if (project!.Status != ProjectStatuses.Active || project.SubState != SubStates.Execution)
        {
            return ProjectService.NotInStage(project);
        }

        if (await db.Sprints.AnyAsync(s => s.ProjectId == project.Id && s.Status == SprintStatuses.Active, ct).ConfigureAwait(false))
        {
            return Error.Rule("portfolio.sprint.active", "portfolio.errors.sprintAlreadyActive");
        }

        var stories = await db.Stories.Where(s => s.SprintId == sprint.Id && s.Status == ItemStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        if (stories.Count == 0)
        {
            return Error.Rule("portfolio.sprint.empty", "portfolio.errors.sprintEmpty");
        }

        Concurrency(sprint, input.Version);
        var moved = sprint.Move(SprintActions.Start, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        sprint.Commit(stories.Select(s => s.Id).ToList(), stories.Sum(s => s.StoryPoints));
        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Close with the review and retrospective: completed points, unfinished stories back to the backlog (or the next planned sprint), retrospective
    /// items become lessons learned, accepted stories as ticked (default: every done story).
    /// </summary>
    public async Task<Result<AgileViewDto>> CloseSprintAsync(Guid sprintId, SprintCloseInput input, CancellationToken ct)
    {
        var (sprint, project, error) = await SprintAsync(sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return error!;
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.DemoOutcome))
        {
            errors.Add(new FieldError("demoOutcome", "validation.notEmpty", "validation.notEmpty"));
        }

        Length(errors, ("demoOutcome", v.DemoOutcome, TextMax), ("stakeholderFeedback", v.StakeholderFeedback, TextMax), ("wentWell", v.WentWell, TextMax),
            ("toImprove", v.ToImprove, TextMax), ("actions", v.Actions, TextMax));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Concurrency(sprint, input.Version);
        var editing = sprint.Status == SprintStatuses.Closed;
        var moved = sprint.Move(editing ? SprintActions.EditReview : SprintActions.Close, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        sprint.Review(v);
        var stories = await db.Stories.Where(s => s.SprintId == sprint.Id && s.Status == ItemStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        foreach (var story in stories.Where(s => s.IsDone && !s.ApprovedDone))
        {
            story.SetAccepted(v.AcceptedStoryIds is null || v.AcceptedStoryIds.Contains(story.Id));
        }

        sprint.SetCompleted(stories.Where(s => s.IsDone && s.Accepted).Sum(s => s.StoryPoints));
        if (!editing)
        {
            var next = await db.Sprints.Where(s => s.ProjectId == project!.Id && s.Status == SprintStatuses.Planned).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            foreach (var story in stories.Where(s => !s.IsDone))
            {
                story.SetSprint(next?.Id);
            }

            foreach (var (text, type) in new[] { (v.WentWell, "positive"), (v.ToImprove, "improvement") })
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var label = $"{sprint.Name ?? $"Sprint {sprint.Number}"}: {text.Trim()}";
                    db.Lessons.Add(Lesson.Create(project!.Id, new LessonValues(ProjectStages.Execution, label.Length > TextMax ? label[..TextMax] : label, type,
                        type == "improvement" ? v.Actions : null)));
                }
            }
        }

        return await SaveAndViewAsync(project!, ct).ConfigureAwait(false);
    }

    /// <summary>Sprint mode (ADR 0091): the closed sprint goes to <c>portfolio.sprint-review</c>.</summary>
    public async Task<Result<AgileViewDto>> SubmitSprintAsync(Guid sprintId, SprintActionInput input, CancellationToken ct)
    {
        var (sprint, project, error) = await SprintAsync(sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return error!;
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (AgileApprovalModes.Effective(project!.Methodology, project.AgileApprovalMode, settings.AgileApprovalMode) != AgileApprovalModes.Sprint)
        {
            return Error.Rule("portfolio.sprint.monthlyMode", "portfolio.errors.sprintMonthlyMode");
        }

        if (project.ApprovalProcess is { } running && project.ApprovalResult is null)
        {
            return ProjectService.ApprovalRunning(running);
        }

        if (project.Status != ProjectStatuses.Active || project.SubState != SubStates.Execution)
        {
            return ProjectService.NotInStage(project);
        }

        Concurrency(sprint, input.Version);
        var moved = sprint.Move(SprintActions.Submit, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        project.EnterApproval(ProjectApprovalProcesses.SprintReview, null, null, null);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var variables = ProjectService.Variables(project);
        variables[ProjectApprovalProcesses.SprintVariable] = sprint.Number;
        variables[ProjectApprovalProcesses.CompletedPointsVariable] = sprint.CompletedPoints;
        var started = await engine.SubmitAsync(
            new ApprovalSubmission(ProjectApprovalProcesses.SprintReview, ProjectService.Subject(project), variables, input.Comment), db, ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await db.Entry(project).ReloadAsync(ct).ConfigureAwait(false);
        return await ToViewAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<AgileViewDto>> CancelSprintAsync(Guid sprintId, SprintActionInput input, CancellationToken ct)
    {
        var (sprint, project, error) = await SprintAsync(sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return error!;
        }

        Concurrency(sprint, input.Version);
        var moved = sprint.Move(SprintActions.Cancel, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        foreach (var story in await db.Stories.Where(s => s.SprintId == sprint.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            story.SetSprint(null);
        }

        return await SaveAndViewAsync(project!, ct).ConfigureAwait(false);
    }

    /// <summary>An impediment becomes a RAID issue (or a challenge when RAID is switched off).</summary>
    public async Task<Result<AgileViewDto>> ImpedimentAsync(Guid sprintId, ImpedimentInput input, CancellationToken ct)
    {
        var sprint = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return Error.NotFound();
        }

        var project = await loader.FindAsync(sprint.ProjectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectContribute, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return Error.Validation([new FieldError("title", "validation.notEmpty", "validation.notEmpty")]);
        }

        var errors = new List<FieldError>();
        Length(errors, ("title", input.Title, RequestRules.NameMaxLength), ("description", input.Description, TextMax));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (settings.Features.Raid)
        {
            var code = await RaidCodes.NextAsync(db, project, RaidTypes.Issue, ct).ConfigureAwait(false);
            var proposed = !await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false);
            db.RaidItems.Add(RaidItem.Create(project.Id, code,
                new RaidValues(RaidTypes.Issue, input.Title, input.Description, input.OwnerEmployeeId, input.Priority, sprint.EndDate, null, null, null, null, null, null, null),
                proposed, RaidSources.Sprint, sprint.Id));
        }
        else
        {
            db.Challenges.Add(Challenge.Create(project.Id, new ChallengeValues(input.Title, input.Description, null, null, await reader.TodayAsync(ct).ConfigureAwait(false), null)));
        }

        return await SaveAndViewAsync(project, ct).ConfigureAwait(false);
    }

    // ---------- monthly roll-up (ADR 0091) and approval outcomes ----------

    /// <summary>
    /// Monthly submission of an agile (monthly mode) or hybrid project: closed sprints are carried into the roll-up; hybrid sprint-delivered milestones
    /// get their pending achievement from their epics. Returns the sprint snapshot (jsonb) for the update.
    /// </summary>
    public async Task<string> CarryIntoMonthlyAsync(Project project, string period, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        var sprints = await db.Sprints.Where(s => s.ProjectId == project.Id && s.Status == SprintStatuses.Closed).OrderBy(s => s.Number).ToListAsync(ct).ConfigureAwait(false);
        foreach (var sprint in sprints)
        {
            sprint.Move(SprintActions.Submit, clock.UtcNow);
            sprint.CarryIn(period);
        }

        if (project.Methodology == Methodologies.Hybrid)
        {
            var today = await reader.TodayAsync(ct).ConfigureAwait(false);
            var epics = await db.Epics.AsNoTracking().Where(e => e.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false);
            var stories = await db.Stories.AsNoTracking().Where(s => s.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false);
            var inputs = stories.Select(s => new StoryProgressInput(s.EpicId, s.StoryPoints, s.IsDone && s.Accepted, s.ApprovedDone, !s.IsActive)).ToList();
            var milestones = await db.Milestones.Where(m => m.ProjectId == project.Id && m.DeliveredInSprints && m.Status != MilestoneStatuses.Cancelled)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var m in milestones)
            {
                var mine = epics.Where(e => e.MilestoneId == m.Id).Select(e => new EpicProgressInput(e.Id, e.Weight, !e.IsActive)).ToList();
                var achieve = Math.Max(m.MaxAchieved, AgileProgressCalculator.MilestoneAchieve(m.Weight, mine, inputs, approvedOnly: false));
                if (achieve == m.MaxAchieved && m.UpdateState is not (UpdateStates.Draft or UpdateStates.Returned))
                {
                    continue;
                }

                var started = stories.Where(s => mine.Any(e => e.Id == s.EpicId) && s.StartedAt is not null).Select(s => s.StartedAt!.Value).DefaultIfEmpty().Min();
                m.RecordProgress(new MilestoneProgressValues(achieve, m.ActualStart ?? (started == default ? today : DateOnly.FromDateTime(started.UtcDateTime)),
                    achieve >= m.Weight ? today : null, m.Causality, m.CorrectiveActions, "sprints"));
            }
        }

        return JsonSerializer.Serialize(sprints.Select(s => new { s.Id, s.Number, s.Name, s.StartDate, s.EndDate, s.CommittedPoints, s.CompletedPoints, s.DemoOutcome }),
            Json);
    }

    /// <summary>Approval of the sprint review or the monthly roll-up: the carried sprints are approved and their accepted done stories locked.</summary>
    public async Task ApproveCarriedAsync(Guid projectId, DateTimeOffset at, CancellationToken ct)
    {
        var sprints = await db.Sprints.Where(s => s.ProjectId == projectId && s.Status == SprintStatuses.InApproval).ToListAsync(ct).ConfigureAwait(false);
        var ids = sprints.Select(s => s.Id).ToList();
        foreach (var story in await db.Stories.Where(s => s.SprintId != null && ids.Contains(s.SprintId.Value)).ToListAsync(ct).ConfigureAwait(false))
        {
            story.ApproveDone();
        }

        foreach (var sprint in sprints)
        {
            sprint.Move(SprintActions.Approve, at);
        }
    }

    public async Task ReturnCarriedAsync(Guid projectId, DateTimeOffset at, CancellationToken ct)
    {
        foreach (var sprint in await db.Sprints.Where(s => s.ProjectId == projectId && s.Status == SprintStatuses.InApproval).ToListAsync(ct).ConfigureAwait(false))
        {
            sprint.Move(SprintActions.Return, at);
        }
    }

    // ---------- helpers ----------

    public static bool CanSetMethodology(Project project, StageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(template);
        return project.Status == ProjectStatuses.Active
            && (project.SubState == SubStates.CharterDraft || (!template.Charter && project.SubState == SubStates.PlanningDraft));
    }

    /// <summary>Plan structure (releases, epics) is edited while planning — or while an approved change request re-opened the plan (ADR 0094).</summary>
    public static bool CanPlan(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ProjectLifecycle.CanPlanMilestones(project.Status, project.SubState)
            || (project.Status == ProjectStatuses.Active && project.SubState == SubStates.Execution && project.ReplanChangeRequestId is not null);
    }

    private static bool Live(Project p) => p.Status == ProjectStatuses.Active && p.SubState is not (SubStates.Ownership or SubStates.Done);

    private async Task<bool> CanCreateStoryAsync(Project project, CancellationToken ct) =>
        await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false)
        || ((await reader.SettingsAsync(ct).ConfigureAwait(false)).TeamMembersCreateStories
            && await loader.CanAsync(context.User, PortfolioPermissions.ProjectContribute, project, ct).ConfigureAwait(false));

    private async Task<bool> CanMoveAsync(Project project, Guid? assignee, CancellationToken ct) =>
        await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false)
        || (assignee is not null && assignee == context.User.EmployeeIdOf()
            && await loader.CanAsync(context.User, PortfolioPermissions.ProjectContribute, project, ct).ConfigureAwait(false));

    private async Task<(Project? Project, Error? Error)> EditableAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return (null, Error.NotFound());
        }

        return await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false) ? (project, null) : (null, Error.Forbidden());
    }

    private async Task<(Project? Project, Error? Error)> PlannableAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return (null, error);
        }

        if (!Methodologies.UsesSprints(project.Methodology))
        {
            return (null, Error.Rule("portfolio.methodology.notAgile", "portfolio.errors.notAgile"));
        }

        return CanPlan(project) ? (project, null) : (null, ProjectService.NotInStage(project));
    }

    private async Task<(Sprint? Sprint, Project? Project, Error? Error)> SprintAsync(Guid sprintId, CancellationToken ct)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == sprintId, ct).ConfigureAwait(false);
        if (sprint is null)
        {
            return (null, null, Error.NotFound());
        }

        var (project, error) = await EditableAsync(sprint.ProjectId, ct).ConfigureAwait(false);
        return project is null ? (null, null, error) : (sprint, project, null);
    }

    private void Concurrency<T>(T entity, uint? version)
        where T : Strategy.Platform.Kernel.Domain.AuditableEntity
    {
        if (version is { } v)
        {
            db.Entry(entity).Property(x => x.Version).OriginalValue = v;
        }
    }

    private static void Length(List<FieldError> errors, params (string Field, string? Text, int Max)[] fields)
    {
        foreach (var (field, text, max) in fields)
        {
            if (text is { } t && t.Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max }));
            }
        }
    }

    private async Task<Result<AgileViewDto>> SaveAndViewAsync(Project project, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ToViewAsync(project, ct).ConfigureAwait(false);
    }

    private async Task<List<StoryHistory>> HistoriesAsync(Guid projectId, IReadOnlyCollection<Guid>? only, CancellationToken ct)
    {
        var zone = await reader.ZoneAsync(ct).ConfigureAwait(false);
        var storiesQuery = db.Stories.AsNoTracking().Where(s => s.ProjectId == projectId);
        if (only is not null)
        {
            storiesQuery = storiesQuery.Where(s => only.Contains(s.Id));
        }

        var stories = await storiesQuery.ToListAsync(ct).ConfigureAwait(false);
        var ids = stories.Select(s => s.Id).ToList();
        var events = await db.StoryEvents.AsNoTracking().Where(e => ids.Contains(e.StoryId)).OrderBy(e => e.At).ToListAsync(ct).ConfigureAwait(false);
        return stories.Select(s => new StoryHistory(s.Id, s.Code, s.StoryPoints, RequestReader.LocalDate(s.CreatedAt, zone),
            events.Where(e => e.StoryId == s.Id).Select(e => (RequestReader.LocalDate(e.At, zone), e.Category)).ToList(), s.StartedAt, s.DoneAt)).ToList();
    }

    public async Task<AgileViewDto> ToViewAsync(Project project, CancellationToken ct)
    {
        var id = project.Id;
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var template = await loader.TemplateAsync(project, ct).ConfigureAwait(false);
        var releases = await db.Releases.AsNoTracking().Where(r => r.ProjectId == id).OrderBy(r => r.Sort).ThenBy(r => r.TargetDate).ToListAsync(ct).ConfigureAwait(false);
        var epics = await db.Epics.AsNoTracking().Where(e => e.ProjectId == id).OrderBy(e => e.Sort).ThenBy(e => e.Code).ToListAsync(ct).ConfigureAwait(false);
        var stories = await db.Stories.AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Rank).ToListAsync(ct).ConfigureAwait(false);
        var sprints = await db.Sprints.AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Number).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(stories.Select(s => s.AssigneeEmployeeId), ct).ConfigureAwait(false);
        var inputs = stories.Select(s => new StoryProgressInput(s.EpicId, s.StoryPoints, s.IsDone, s.ApprovedDone, !s.IsActive)).ToList();
        var epicInputs = epics.Where(e => e.MilestoneId is null).Select(e => new EpicProgressInput(e.Id, e.Weight, !e.IsActive)).ToList();
        var user = context.User;
        var canEdit = await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false);
        var canContribute = await loader.CanAsync(user, PortfolioPermissions.ProjectContribute, project, ct).ConfigureAwait(false);
        var me = user.EmployeeIdOf();
        var live = Live(project);
        var mode = AgileApprovalModes.Effective(project.Methodology, project.AgileApprovalMode, settings.AgileApprovalMode);
        var permissions = new AgilePermissionsDto(
            canEdit && CanSetMethodology(project, template), canEdit && CanPlan(project) && Methodologies.UsesSprints(project.Methodology),
            live && (canEdit || (settings.TeamMembersCreateStories && canContribute)), canEdit && live,
            canEdit && mode == AgileApprovalModes.Sprint && project.SubState == SubStates.Execution && !(project.ApprovalProcess is not null && project.ApprovalResult is null));

        var histories = await HistoriesAsync(id, null, ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var from = histories.Count == 0 ? today : histories.Min(h => h.Created);
        if (today.DayNumber - from.DayNumber > 90)
        {
            from = today.AddDays(-90);
        }

        var cycle = AgileMetrics.CycleTimes(histories);
        var burnup = releases.Select(r =>
        {
            var epicIds = epics.Where(e => e.ReleaseId == r.Id).Select(e => e.Id).ToHashSet();
            var mine = histories.Where(h => stories.Any(s => s.Id == h.StoryId && epicIds.Contains(s.EpicId) && s.IsActive)).ToList();
            var checkpoints = sprints.Where(s => s.EndDate is not null && s.Status != SprintStatuses.Cancelled && s.Status != SprintStatuses.Planned)
                .Select(s => (s.Name ?? $"#{s.Number}", s.EndDate!.Value)).ToList();
            if (checkpoints.Count == 0 || checkpoints.Max(c => c.Value) < today)
            {
                checkpoints.Add(("today", today));
            }

            return new ReleaseBurnupDto(r.Id, AgileMetrics.Burnup(mine, checkpoints));
        }).ToList();
        var metrics = new AgileMetricsDto(
            AgileMetrics.Velocity(sprints.Where(s => s.Status is SprintStatuses.Closed or SprintStatuses.InApproval or SprintStatuses.Approved)
                .Select(s => (s.Number, s.Name, s.CommittedPoints, s.CompletedPoints))),
            AgileMetrics.CumulativeFlow(histories, from, today), cycle, AgileMetrics.AverageCycleTime(cycle), burnup);

        return new AgileViewDto(
            project.Methodology, mode, project.SprintLengthDays, project.DefinitionOfDone, project.BoardOrDefault(),
            releases.Select(r =>
            {
                var epicIds = epics.Where(e => e.ReleaseId == r.Id && e.IsActive).Select(e => e.Id).ToHashSet();
                var mine = stories.Where(s => s.IsActive && epicIds.Contains(s.EpicId)).ToList();
                return new ReleaseDto(r.Id, r.NameAr, r.NameEn, r.Goal, r.TargetDate, r.Sort, r.Status, mine.Sum(s => s.StoryPoints), mine.Where(s => s.IsDone).Sum(s => s.StoryPoints),
                    r.Version);
            }).ToList(),
            epics.Select(e =>
            {
                var mine = stories.Where(s => s.IsActive && s.EpicId == e.Id).ToList();
                return new EpicDto(e.Id, e.Code, e.ReleaseId, e.MilestoneId, e.TitleAr, e.TitleEn, e.Weight, e.AcceptanceCriteria, e.Sort, e.Status, mine.Sum(s => s.StoryPoints),
                    mine.Where(s => s.IsDone).Sum(s => s.StoryPoints), AgileProgressCalculator.EpicFraction(e.Id, inputs, true),
                    AgileProgressCalculator.EpicFraction(e.Id, inputs, false), e.Version);
            }).ToList(),
            stories.Select(s => new StoryDto(s.Id, s.Code, s.EpicId, s.SprintId, s.Title, s.AsA, s.IWant, s.SoThat, s.AcceptanceCriteria, s.StoryPoints, s.Priority,
                s.AssigneeEmployeeId is { } a ? people.GetValueOrDefault(a) : null, s.ColumnCode, s.Category, s.Rank, s.StartedAt, s.DoneAt, s.Accepted, s.ApprovedDone, s.Status,
                live && s.IsActive && (canEdit || (canContribute && s.AssigneeEmployeeId is not null && s.AssigneeEmployeeId == me)), s.Version)).ToList(),
            sprints.Select(s => new SprintDto(s.Id, s.Number, s.Name, s.Goal, s.StartDate, s.EndDate, s.CapacityPoints, s.Status, s.CommittedPoints, s.CompletedPoints,
                stories.Where(x => x.SprintId == s.Id && x.IsActive).Sum(x => x.StoryPoints), s.DemoOutcome, s.StakeholderFeedback, s.WentWell, s.ToImprove, s.Actions,
                s.ApprovalPeriod, s.ApprovedAt, s.Version)).ToList(),
            AgileProgressCalculator.Progress(epicInputs, inputs), AgileProgressCalculator.Progress(epicInputs, inputs, approvedOnly: false),
            epics.Where(e => e.IsActive && e.MilestoneId is null).Sum(e => e.Weight), permissions, metrics, project.Version);
    }
}

/// <summary>RAID codes <c>&lt;Number&gt;-I1</c>, <c>-A1</c>, <c>-D1</c>, <c>-C1</c>.</summary>
internal static class RaidCodes
{
    public static async Task<string> NextAsync(PortfolioDbContext db, Project project, string type, CancellationToken ct)
    {
        var count = await db.RaidItems.CountAsync(r => r.ProjectId == project.Id && r.Type == type, ct).ConfigureAwait(false)
            + db.RaidItems.Local.Count(r => r.ProjectId == project.Id && r.Type == type && db.Entry(r).State == EntityState.Added);
        return $"{project.Number}-{RaidTypes.Prefix(type)}{count + 1}";
    }
}
