using System.Text.Json;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.AI.Tools;

namespace Strategy.Modules.Portfolio.Mcp;

/// <summary>Backlog of an agile / hybrid project: epics with progress and stories by board column (read, ADR 0099).</summary>
internal sealed class ProjectBacklogTool(AgileService agile) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_project_backlog", PortfolioPermissions.SystemCode, ToolKind.Read, "قائمة أعمال المشروع", "Project backlog",
        "Returns the releases, epics (weight, approved and pending completion) and user stories (points, column, sprint, assignee) of an agile or hybrid project.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId"],"properties":{"projectId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId is required.");
        }

        var result = await agile.ViewAsync(id, cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, v => new
        {
            v.Methodology,
            v.ApprovalMode,
            approvedProgress = Math.Round(v.ApprovedProgress * 100, 2),
            pendingProgress = Math.Round(v.PendingProgress * 100, 2),
            releases = v.Releases.Select(r => new { r.Id, name = r.NameEn ?? r.NameAr, r.TargetDate, r.Status, r.Points, r.DonePoints }),
            epics = v.Epics.Select(e => new { e.Id, e.Code, title = e.TitleEn ?? e.TitleAr, weight = Math.Round(e.Weight * 100, 2), e.Points, e.DonePoints, e.Status }),
            stories = v.Stories.Where(s => s.Status == ItemStatuses.Active).Select(s => new
            {
                s.Id, s.Code, s.Title, s.StoryPoints, s.Priority, column = s.ColumnCode, s.Category, s.SprintId, assignee = s.Assignee?.NameEn ?? s.Assignee?.NameAr,
                s.ApprovedDone,
            }),
        });
    }
}

/// <summary>Sprints with committed / completed points and the remaining points of the active sprint (read).</summary>
internal sealed class SprintSummaryTool(AgileService agile) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_sprint_summary", PortfolioPermissions.SystemCode, ToolKind.Read, "ملخص السبرنتات", "Sprint summary",
        "Returns the sprints of a project (dates, status, committed and completed points, velocity) and the burndown of the active sprint.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId"],"properties":{"projectId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId is required.");
        }

        var view = await agile.ViewAsync(id, cancellationToken).ConfigureAwait(false);
        if (view.IsFailure)
        {
            return PortfolioToolResults.From(view, _ => null);
        }

        var active = view.Value.Sprints.FirstOrDefault(s => s.Status == SprintStatuses.Active);
        var burndown = active is null ? null : await agile.BurndownAsync(active.Id, cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new
        {
            sprints = view.Value.Sprints.Select(s => new { s.Id, s.Number, s.Name, s.StartDate, s.EndDate, s.Status, s.CommittedPoints, s.CompletedPoints, s.PlannedPoints }),
            velocity = view.Value.Metrics.Velocity,
            activeBurndown = burndown?.IsSuccess == true ? burndown.Value.Points.Where(p => p.Remaining >= 0) : null,
            link = $"/portfolio/projects/{id:D}?tab=sprints",
        });
    }
}

/// <summary>Portfolio summary for leadership: counts, health, delayed projects; budgets only for cost-visible projects (read).</summary>
internal sealed class PortfolioSummaryTool(PortfolioViewService views) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_summary", PortfolioPermissions.SystemCode, ToolKind.Read, "ملخص المحفظة", "Portfolio summary",
        "Returns the portfolio dashboard figures the caller may see: projects by stage, health, delayed projects, upcoming milestones and budget vs actual by department.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"portfolioGroupId":{"type":"string","format":"uuid"},"programmeId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await views.DashboardAsync(new DashboardFilter(arguments.Id("portfolioGroupId"), arguments.Id("programmeId"), null), cancellationToken)
            .ConfigureAwait(false);
        return PortfolioToolResults.From(result, d => new
        {
            d.Total,
            d.Active,
            d.Delayed,
            averageProgress = Math.Round(d.AverageProgress * 100, 2),
            byStage = d.ByStage,
            red = d.Projects.Where(p => p.Health?.Overall.Effective == Rag.Red).Select(p => new { p.Id, p.Number, name = p.NameEn ?? p.NameAr }),
            delayed = d.DelayedProjects.Select(p => new { p.Id, p.Number, name = p.NameEn ?? p.NameAr, progress = Math.Round(p.Performance * 100, 2) }),
            upcoming = d.Upcoming.Select(m => new { m.ProjectNumber, m.Code, title = m.TitleEn ?? m.TitleAr, m.PlannedEnd }),
            budget = d.Budget.Select(b => new { unit = b.UnitEn ?? b.UnitAr, b.Budget, b.Actual }),
            d.CostsHidden,
        });
    }
}

/// <summary>Adds a user story to the backlog. Write tool: off until an administrator enables it; it never starts or approves anything.</summary>
internal sealed class CreateStoryDraftTool(AgileService agile) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_create_story_draft", PortfolioPermissions.SystemCode, ToolKind.Write, "إضافة قصة مستخدم", "Add a user story",
        "Adds a user story to the backlog of an epic (title, as a / I want / so that, story points). It does not plan it into a sprint or approve anything.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId","epicId","title"],"properties":{"projectId":{"type":"string","format":"uuid"},"epicId":{"type":"string","format":"uuid"},"title":{"type":"string"},"asA":{"type":"string"},"iWant":{"type":"string"},"soThat":{"type":"string"},"points":{"type":"integer"}}}"""),
        PortfolioPermissions.ProjectRead,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } projectId || arguments.Id("epicId") is not { } epicId)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId and epicId are required.");
        }

        static string? Str(JsonElement args, string name) => args.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        var points = arguments.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : 0;
        var result = await agile.SaveStoryAsync(projectId, null,
            new StoryInput(new StoryValues(epicId, Str(arguments, "title"), Str(arguments, "asA"), Str(arguments, "iWant"), Str(arguments, "soThat"), null, points, null, null, null)),
            cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, v => v.Stories.OrderByDescending(s => s.Rank).Select(s => new { s.Id, s.Code, s.Title, s.StoryPoints }).FirstOrDefault());
    }
}
