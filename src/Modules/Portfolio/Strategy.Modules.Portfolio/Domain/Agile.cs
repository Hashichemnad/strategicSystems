using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Domain;

/// <summary>Delivery methodology of a project (SPEC §1.13, ADR 0090).</summary>
public static class Methodologies
{
    public const string Waterfall = "waterfall";
    public const string Agile = "agile";
    public const string Hybrid = "hybrid";

    public static readonly IReadOnlyList<string> All = [Waterfall, Agile, Hybrid];

    public static bool UsesSprints(string methodology) => methodology is Agile or Hybrid;

    public static bool UsesMilestones(string methodology) => methodology is Waterfall or Hybrid;
}

/// <summary>How agile progress is approved (SPEC §1.13 decision, ADR 0091).</summary>
public static class AgileApprovalModes
{
    public const string Sprint = "sprint";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Sprint, Monthly];

    /// <summary>Hybrid is always a monthly roll-up; agile uses the project override or the installation default.</summary>
    public static string Effective(string methodology, string? projectMode, string installationMode) =>
        methodology == Methodologies.Hybrid ? Monthly
        : methodology == Methodologies.Agile ? (projectMode is AgileApprovalModes.Sprint or AgileApprovalModes.Monthly ? projectMode : installationMode == AgileApprovalModes.Sprint ? AgileApprovalModes.Sprint : Monthly)
        : Monthly;
}

public static class BoardCategories
{
    public const string ToDo = "todo";
    public const string Doing = "doing";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [ToDo, Doing, Done];
}

public static class Swimlanes
{
    public const string None = "none";
    public const string Epic = "epic";
    public const string Assignee = "assignee";

    public static readonly IReadOnlyList<string> All = [None, Epic, Assignee];
}

public sealed record BoardColumn(string Code, string? LabelAr, string? LabelEn, string Category, int? WipLimit = null);

/// <summary>A project's board (configurable columns, WIP limits, swimlanes — SPEC §1.13).</summary>
public sealed record BoardConfig(IReadOnlyList<BoardColumn> Columns, string Swimlane = Swimlanes.None)
{
    public static readonly BoardConfig Default = new(
    [
        new("to-do", "قيد الانتظار", "To Do", BoardCategories.ToDo),
        new("in-progress", "قيد التنفيذ", "In Progress", BoardCategories.Doing),
        new("review", "مراجعة", "Review", BoardCategories.Doing),
        new("done", "منجز", "Done", BoardCategories.Done),
    ]);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string FirstColumn => Columns.FirstOrDefault(c => c.Category == BoardCategories.ToDo)?.Code ?? Columns[0].Code;

    public string? CategoryOf(string column) => Columns.FirstOrDefault(c => c.Code == column)?.Category;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BoardConfig FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) || json.Length < 3 ? Default : JsonSerializer.Deserialize<BoardConfig>(json, Json) ?? Default;

    /// <summary>Codes unique, 2–10 columns, at least one to-do and one done column, WIP limits 1–100.</summary>
    public IReadOnlyList<FieldError> Problems()
    {
        var errors = new List<FieldError>();
        if (Columns is null || Columns.Count is < 2 or > 10)
        {
            errors.Add(new FieldError("columns", "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 2, ["to"] = 10 }));
            return errors;
        }

        for (var i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            if (string.IsNullOrWhiteSpace(c.Code) || c.Code.Length > 40 || !c.Code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            {
                errors.Add(new FieldError($"columns[{i}].code", "validation.code", "validation.code"));
            }

            if (string.IsNullOrWhiteSpace(c.LabelAr) && string.IsNullOrWhiteSpace(c.LabelEn))
            {
                errors.Add(new FieldError($"columns[{i}].labelEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
            }

            if (!BoardCategories.All.Contains(c.Category))
            {
                errors.Add(new FieldError($"columns[{i}].category", "validation.unknownCode", "validation.unknownCode"));
            }

            if (c.WipLimit is < 1 or > 100)
            {
                errors.Add(new FieldError($"columns[{i}].wipLimit", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = 1, ["to"] = 100 }));
            }
        }

        if (Columns.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() != Columns.Count)
        {
            errors.Add(new FieldError("columns", "validation.duplicate", "validation.duplicate"));
        }

        if (!Columns.Any(c => c.Category == BoardCategories.ToDo) || !Columns.Any(c => c.Category == BoardCategories.Done))
        {
            errors.Add(new FieldError("columns", "portfolio.errors.boardNeedsTodoAndDone", "portfolio.errors.boardNeedsTodoAndDone"));
        }

        if (!Swimlanes.All.Contains(Swimlane))
        {
            errors.Add(new FieldError("swimlane", "validation.unknownCode", "validation.unknownCode"));
        }

        return errors;
    }
}

/// <summary>Board moves (ADR 0090): WIP limits enforced; an approved-done story stays done.</summary>
public static class BoardRules
{
    public static Error? CheckMove(BoardConfig board, string fromColumn, string toColumn, int storiesInTarget, bool approvedDone)
    {
        ArgumentNullException.ThrowIfNull(board);
        var target = board.Columns.FirstOrDefault(c => c.Code == toColumn);
        if (target is null)
        {
            return Error.Validation([new FieldError("column", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (approvedDone && target.Category != BoardCategories.Done)
        {
            return Error.Rule("portfolio.story.locked", "portfolio.errors.storyLocked");
        }

        if (fromColumn != toColumn && target.WipLimit is { } limit && storiesInTarget >= limit)
        {
            return Error.Rule("portfolio.story.wip", "portfolio.errors.wipLimit", new Dictionary<string, object?> { ["limit"] = limit, ["column"] = toColumn });
        }

        return null;
    }
}

public static class ReleaseStatuses
{
    public const string Planned = "planned";
    public const string Released = "released";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Released, Cancelled];
}

public sealed record ReleaseValues(string? NameAr, string? NameEn, string? Goal, DateOnly? TargetDate, string? Status, int Sort = 0);

/// <summary>Release of an agile project (name, target date, goal).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Release : AuditableEntity
{
    public const string EntityType = "portfolio.release";

    private Release()
    {
    }

    public Guid ProjectId { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Goal { get; private set; }

    public DateOnly? TargetDate { get; private set; }

    public int Sort { get; private set; }

    public string Status { get; private set; } = ReleaseStatuses.Planned;

    public static Release Create(Guid projectId, ReleaseValues values)
    {
        var item = new Release { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(ReleaseValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = AgileText.Clean(v.NameAr);
        NameEn = AgileText.Clean(v.NameEn);
        Goal = AgileText.Clean(v.Goal);
        TargetDate = v.TargetDate;
        Sort = v.Sort;
        Status = v.Status is { } s && ReleaseStatuses.All.Contains(s) ? s : Status;
    }
}

public static class EpicStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

public sealed record EpicValues(
    string? TitleAr, string? TitleEn, decimal Weight, Guid? ReleaseId, Guid? MilestoneId, string? AcceptanceCriteria, int Sort = 0, string? Status = null);

/// <summary>Epic (title AR / EN, weight %, acceptance criteria, status). Weights total 100 % (agile) or 100 % of their milestone (hybrid).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Epic : AuditableEntity
{
    public const string EntityType = "portfolio.epic";

    private Epic()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid? ReleaseId { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public decimal Weight { get; private set; }

    public string? AcceptanceCriteria { get; private set; }

    public int Sort { get; private set; }

    public string Status { get; private set; } = EpicStatuses.Open;

    public bool IsActive => Status != EpicStatuses.Cancelled;

    public static Epic Create(Guid projectId, string code, EpicValues values)
    {
        var item = new Epic { ProjectId = projectId, Code = code };
        item.Edit(values);
        return item;
    }

    public void Edit(EpicValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = AgileText.Clean(v.TitleAr);
        TitleEn = AgileText.Clean(v.TitleEn);
        Weight = v.Weight;
        ReleaseId = v.ReleaseId;
        MilestoneId = v.MilestoneId;
        AcceptanceCriteria = AgileText.Clean(v.AcceptanceCriteria);
        Sort = v.Sort;
        if (v.Status is EpicStatuses.Open or EpicStatuses.Done)
        {
            Status = v.Status;
        }
    }

    public void Cancel() => Status = EpicStatuses.Cancelled;
}

public static class MoscowPriorities
{
    public const string Must = "must";
    public const string Should = "should";
    public const string Could = "could";
    public const string Wont = "wont";

    public static readonly IReadOnlyList<string> All = [Must, Should, Could, Wont];
}

public sealed record StoryValues(
    Guid EpicId,
    string? Title,
    string? AsA,
    string? IWant,
    string? SoThat,
    string? AcceptanceCriteria,
    int StoryPoints,
    string? Priority,
    Guid? AssigneeEmployeeId,
    Guid? SprintId);

/// <summary>User story (as-a / I-want / so-that, acceptance criteria, points, MoSCoW, assignee). Its state is its board column.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class UserStory : AuditableEntity
{
    public const string EntityType = "portfolio.story";
    public const int MaxPoints = 100;

    private UserStory()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid EpicId { get; private set; }

    public Guid? SprintId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? AsA { get; private set; }

    public string? IWant { get; private set; }

    public string? SoThat { get; private set; }

    public string? AcceptanceCriteria { get; private set; }

    public int StoryPoints { get; private set; }

    public string Priority { get; private set; } = MoscowPriorities.Should;

    public Guid? AssigneeEmployeeId { get; private set; }

    public string ColumnCode { get; private set; } = string.Empty;

    public string Category { get; private set; } = BoardCategories.ToDo;

    public int Rank { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? DoneAt { get; private set; }

    /// <summary>Accepted at the sprint review.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Counted by an approved sprint review / monthly roll-up — locked in done (ADR 0091).</summary>
    public bool ApprovedDone { get; private set; }

    public string Status { get; private set; } = ItemStatuses.Active;

    public bool IsActive => Status == ItemStatuses.Active;

    public bool IsDone => Category == BoardCategories.Done;

    public static UserStory Create(Guid projectId, string code, StoryValues values, string column, int rank)
    {
        var item = new UserStory { ProjectId = projectId, Code = code, ColumnCode = column, Category = BoardCategories.ToDo, Rank = rank };
        item.Edit(values);
        return item;
    }

    public void Edit(StoryValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        EpicId = v.EpicId;
        Title = AgileText.Clean(v.Title);
        AsA = AgileText.Clean(v.AsA);
        IWant = AgileText.Clean(v.IWant);
        SoThat = AgileText.Clean(v.SoThat);
        AcceptanceCriteria = AgileText.Clean(v.AcceptanceCriteria);
        StoryPoints = v.StoryPoints;
        Priority = v.Priority is { } p && MoscowPriorities.All.Contains(p) ? p : MoscowPriorities.Should;
        AssigneeEmployeeId = v.AssigneeEmployeeId;
        SprintId = v.SprintId;
    }

    /// <summary>Moves on the board; first entry into doing sets started, entering done sets done (leaving done clears it).</summary>
    public void Move(string column, string category, int rank, DateTimeOffset at)
    {
        ColumnCode = column;
        Rank = rank;
        if (category != BoardCategories.ToDo && StartedAt is null)
        {
            StartedAt = at;
        }

        if (category == BoardCategories.Done && Category != BoardCategories.Done)
        {
            DoneAt = at;
            Accepted = true;
        }
        else if (category != BoardCategories.Done)
        {
            DoneAt = null;
            Accepted = false;
        }

        Category = category;
    }

    public void SetSprint(Guid? sprintId) => SprintId = sprintId;

    /// <summary>Responsibility reassignment (ADR 0168).</summary>
    public void AssignTo(Guid employeeId) => AssigneeEmployeeId = employeeId;

    public void SetAccepted(bool accepted) => Accepted = accepted && IsDone;

    public void ApproveDone()
    {
        if (IsDone && Accepted)
        {
            ApprovedDone = true;
        }
    }

    public void Cancel() => Status = ItemStatuses.Inactive;
}

public static class SprintStatuses
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string Closed = "closed";
    public const string InApproval = "in-approval";
    public const string Approved = "approved";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Active, Closed, InApproval, Approved, Cancelled];
}

public static class SprintActions
{
    public const string Edit = "edit";
    public const string Start = "start";
    public const string Close = "close";
    public const string EditReview = "edit-review";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Return = "return";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [Edit, Start, Close, EditReview, Submit, Approve, Return, Cancel];
}

/// <summary>Sprint lifecycle (docs/design/portfolio.md §30). Submit covers both the sprint review approval and the monthly roll-up.</summary>
public static class SprintLifecycle
{
    private static readonly Dictionary<(string Status, string Action), string> Transitions = new()
    {
        [(SprintStatuses.Planned, SprintActions.Edit)] = SprintStatuses.Planned,
        [(SprintStatuses.Planned, SprintActions.Start)] = SprintStatuses.Active,
        [(SprintStatuses.Planned, SprintActions.Cancel)] = SprintStatuses.Cancelled,
        [(SprintStatuses.Active, SprintActions.Edit)] = SprintStatuses.Active,
        [(SprintStatuses.Active, SprintActions.Close)] = SprintStatuses.Closed,
        [(SprintStatuses.Closed, SprintActions.EditReview)] = SprintStatuses.Closed,
        [(SprintStatuses.Closed, SprintActions.Submit)] = SprintStatuses.InApproval,
        [(SprintStatuses.InApproval, SprintActions.Approve)] = SprintStatuses.Approved,
        [(SprintStatuses.InApproval, SprintActions.Return)] = SprintStatuses.Closed,
    };

    public static bool Allows(string status, string action) => Transitions.ContainsKey((status, action));

    public static Result<string> Next(string status, string action) =>
        Transitions.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.sprint.transition", "portfolio.errors.sprintNotAllowed", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });
}

public sealed record SprintValues(string? Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate, int? CapacityPoints);

public sealed record SprintReviewValues(
    string? DemoOutcome, string? StakeholderFeedback, string? WentWell, string? ToImprove, string? Actions, IReadOnlyList<Guid>? AcceptedStoryIds = null);

/// <summary>Sprint (goal, dates, capacity, committed vs completed points, review and retrospective).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Sprint : AuditableEntity
{
    public const string EntityType = "portfolio.sprint";

    private Sprint()
    {
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string? Name { get; private set; }

    public string? Goal { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public int? CapacityPoints { get; private set; }

    public string Status { get; private set; } = SprintStatuses.Planned;

    public int CommittedPoints { get; private set; }

    /// <summary>jsonb array of the story ids committed at start (burndown scope).</summary>
    public string CommittedStoryIds { get; private set; } = "[]";

    public int CompletedPoints { get; private set; }

    public string? DemoOutcome { get; private set; }

    public string? StakeholderFeedback { get; private set; }

    public string? WentWell { get; private set; }

    public string? ToImprove { get; private set; }

    public string? Actions { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The monthly roll-up period that carried the sprint (monthly mode).</summary>
    public string? ApprovalPeriod { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public static Sprint Create(Guid projectId, int number, SprintValues values)
    {
        var item = new Sprint { ProjectId = projectId, Number = number };
        item.Edit(values);
        return item;
    }

    public void Edit(SprintValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Name = AgileText.Clean(v.Name);
        Goal = AgileText.Clean(v.Goal);
        StartDate = v.StartDate;
        EndDate = v.EndDate;
        CapacityPoints = v.CapacityPoints;
    }

    public Result Move(string action, DateTimeOffset at)
    {
        var next = SprintLifecycle.Next(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        switch (action)
        {
            case SprintActions.Start:
                StartedAt = at;
                break;
            case SprintActions.Close:
                ClosedAt = at;
                break;
            case SprintActions.Approve:
                ApprovedAt = at;
                break;
            case SprintActions.Return:
                ApprovalPeriod = null;
                break;
        }

        return Result.Success();
    }

    public void Commit(IReadOnlyCollection<Guid> storyIds, int points)
    {
        ArgumentNullException.ThrowIfNull(storyIds);
        CommittedStoryIds = JsonSerializer.Serialize(storyIds);
        CommittedPoints = points;
    }

    public IReadOnlyList<Guid> Committed() => JsonSerializer.Deserialize<List<Guid>>(CommittedStoryIds) ?? [];

    public void Review(SprintReviewValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        DemoOutcome = AgileText.Clean(v.DemoOutcome);
        StakeholderFeedback = AgileText.Clean(v.StakeholderFeedback);
        WentWell = AgileText.Clean(v.WentWell);
        ToImprove = AgileText.Clean(v.ToImprove);
        Actions = AgileText.Clean(v.Actions);
    }

    public void SetCompleted(int points) => CompletedPoints = points;

    public void CarryIn(string period) => ApprovalPeriod = period;
}

/// <summary>Append-only board history of a story (burndown, cumulative flow, cycle time).</summary>
public sealed class StoryEvent : Entity
{
    private StoryEvent()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid StoryId { get; private set; }

    public Guid? SprintId { get; private set; }

    public string? FromColumn { get; private set; }

    public string ToColumn { get; private set; } = string.Empty;

    public string Category { get; private set; } = BoardCategories.ToDo;

    public int Points { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static StoryEvent Of(UserStory story, string? from, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(story);
        return new StoryEvent
        {
            ProjectId = story.ProjectId,
            StoryId = story.Id,
            SprintId = story.SprintId,
            FromColumn = from,
            ToColumn = story.ColumnCode,
            Category = story.Category,
            Points = story.StoryPoints,
            At = at,
        };
    }
}

internal static class AgileText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record EpicProgressInput(Guid Id, decimal Weight, bool Cancelled = false);

public sealed record StoryProgressInput(Guid EpicId, int Points, bool Done, bool ApprovedDone, bool Cancelled = false);

/// <summary>Agile progress (SPEC §1.13, ADR 0091): Σ epic weight × done points ÷ epic points. Ratios, 1.0 = 100 %.</summary>
public static class AgileProgressCalculator
{
    /// <summary>Done fraction of one epic (approved-done only, or every done story).</summary>
    public static decimal EpicFraction(Guid epicId, IEnumerable<StoryProgressInput> stories, bool approvedOnly)
    {
        var mine = stories.Where(s => s.EpicId == epicId && !s.Cancelled).ToList();
        var total = mine.Sum(s => s.Points);
        if (total <= 0)
        {
            return 0;
        }

        var done = mine.Where(s => approvedOnly ? s.ApprovedDone : s.Done).Sum(s => s.Points);
        return decimal.Round((decimal)done / total, 4);
    }

    /// <summary>Σ epic weight × fraction over the active epics, capped at 100 %.</summary>
    public static decimal Progress(IEnumerable<EpicProgressInput> epics, IReadOnlyCollection<StoryProgressInput> stories, bool approvedOnly = true)
    {
        ArgumentNullException.ThrowIfNull(stories);
        var total = epics.Where(e => !e.Cancelled).Sum(e => e.Weight * EpicFraction(e.Id, stories, approvedOnly));
        return Math.Min(1m, decimal.Round(total, 4));
    }

    /// <summary>Hybrid: a sprint-delivered milestone's achievement = its weight × the progress of its epics (weights = 100 % of the milestone).</summary>
    public static decimal MilestoneAchieve(decimal milestoneWeight, IEnumerable<EpicProgressInput> epics, IReadOnlyCollection<StoryProgressInput> stories, bool approvedOnly) =>
        decimal.Round(milestoneWeight * Progress(epics, stories, approvedOnly), 4);
}

public sealed record VelocityPoint(int Number, string? Name, int Committed, int Completed);

public sealed record BurnPoint(DateOnly Date, decimal Remaining, decimal Ideal);

public sealed record BurnupPoint(string Label, DateOnly Date, int Done, int Scope);

public sealed record FlowPoint(DateOnly Date, int ToDo, int Doing, int Done);

public sealed record CycleTimePoint(Guid StoryId, string Code, decimal Days);

/// <summary>A story's board history as the metrics see it: local day of each move.</summary>
public sealed record StoryHistory(Guid StoryId, string Code, int Points, DateOnly Created, IReadOnlyList<(DateOnly Day, string Category)> Moves, DateTimeOffset? StartedAt,
    DateTimeOffset? DoneAt);

/// <summary>Agile metrics (SPEC §1.13): velocity, sprint burndown, release burnup, cumulative flow, cycle time. Pure.</summary>
public static class AgileMetrics
{
    public static IReadOnlyList<VelocityPoint> Velocity(IEnumerable<(int Number, string? Name, int Committed, int Completed)> sprints) =>
        sprints.OrderBy(s => s.Number).Select(s => new VelocityPoint(s.Number, s.Name, s.Committed, s.Completed)).ToList();

    /// <summary>Category of a story at the end of <paramref name="day"/>.</summary>
    public static string CategoryAt(StoryHistory story, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(story);
        var category = BoardCategories.ToDo;
        foreach (var (d, c) in story.Moves)
        {
            if (d > day)
            {
                break;
            }

            category = c;
        }

        return category;
    }

    /// <summary>Remaining committed points at the end of each sprint day, with the ideal straight line.</summary>
    public static IReadOnlyList<BurnPoint> Burndown(DateOnly start, DateOnly end, IReadOnlyCollection<StoryHistory> committed, DateOnly? until = null)
    {
        ArgumentNullException.ThrowIfNull(committed);
        var points = new List<BurnPoint>();
        if (end < start)
        {
            return points;
        }

        var total = committed.Sum(s => s.Points);
        var days = end.DayNumber - start.DayNumber;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var ideal = days == 0 ? 0 : decimal.Round(total * (1 - ((decimal)(day.DayNumber - start.DayNumber) / days)), 2);
            if (until is { } u && day > u)
            {
                points.Add(new BurnPoint(day, -1, ideal));
                continue;
            }

            var remaining = committed.Where(s => CategoryAt(s, day) != BoardCategories.Done).Sum(s => s.Points);
            points.Add(new BurnPoint(day, remaining, ideal));
        }

        return points;
    }

    /// <summary>Done points and scope (all active story points) at each date.</summary>
    public static IReadOnlyList<BurnupPoint> Burnup(IReadOnlyCollection<StoryHistory> stories, IEnumerable<(string Label, DateOnly Date)> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(stories);
        return checkpoints.OrderBy(c => c.Date).Select(c => new BurnupPoint(c.Label, c.Date,
            stories.Where(s => CategoryAt(s, c.Date) == BoardCategories.Done).Sum(s => s.Points),
            stories.Where(s => s.Created <= c.Date).Sum(s => s.Points))).ToList();
    }

    /// <summary>Story counts per category for each day (stories that existed that day).</summary>
    public static IReadOnlyList<FlowPoint> CumulativeFlow(IReadOnlyCollection<StoryHistory> stories, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(stories);
        var result = new List<FlowPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var existing = stories.Where(s => s.Created <= day).Select(s => CategoryAt(s, day)).ToList();
            result.Add(new FlowPoint(day, existing.Count(c => c == BoardCategories.ToDo), existing.Count(c => c == BoardCategories.Doing),
                existing.Count(c => c == BoardCategories.Done)));
        }

        return result;
    }

    /// <summary>Days from the first move into doing to done, per done story.</summary>
    public static IReadOnlyList<CycleTimePoint> CycleTimes(IEnumerable<StoryHistory> stories) =>
        stories.Where(s => s.StartedAt is not null && s.DoneAt is not null && s.DoneAt >= s.StartedAt)
            .Select(s => new CycleTimePoint(s.StoryId, s.Code, decimal.Round((decimal)(s.DoneAt!.Value - s.StartedAt!.Value).TotalDays, 1)))
            .ToList();

    public static decimal? AverageCycleTime(IReadOnlyCollection<CycleTimePoint> points) =>
        points is null || points.Count == 0 ? null : decimal.Round(points.Average(p => p.Days), 1);
}
