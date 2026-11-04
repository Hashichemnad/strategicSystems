using System.Globalization;
using System.Text.Json;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Mcp;

/// <summary>
/// MCP tools of Portfolio Management (SPEC §0.5 "search / get requests and projects, create draft request", ADR 0031). They run as the token
/// owner through the same handlers as the UI. The write tool is off by default and only creates a draft: it never submits.
/// </summary>
internal static class PortfolioToolResults
{
    public static ToolResult From<T>(Result<T> result, Func<T, object?> map) => result.IsSuccess
        ? ToolResult.Ok(map(result.Value))
        : result.Error.Kind switch
        {
            ErrorKind.NotFound => ToolResult.NotFound(),
            ErrorKind.Forbidden => ToolResult.Fail(ToolErrors.Forbidden, result.Error.MessageKey),
            _ => ToolResult.Fail(ToolErrors.Validation, result.Error.Fields is { Count: > 0 } fields
                ? result.Error.MessageKey + ": " + string.Join(", ", fields.Select(f => $"{f.Field} {f.Code}"))
                : result.Error.MessageKey),
        };

    public static object Summary(RequestListItemDto r) => new
    {
        r.Id,
        r.Number,
        r.NameAr,
        r.NameEn,
        r.Status,
        step = r.ApprovalStepNameEn ?? r.ApprovalStepNameAr,
        classification = r.EffectiveClassification,
        evaluationScorePercent = r.EvaluationScore is { } s ? Math.Round(s * 100, 2) : (decimal?)null,
        r.PriorityBand,
        r.EstimatedBudget,
        unit = r.Unit?.NameEn ?? r.Unit?.NameAr,
        r.ProjectNumber,
        link = $"/portfolio/requests/{r.Id:D}",
    };
}

internal sealed class SearchRequestsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_search_requests", PortfolioPermissions.SystemCode, ToolKind.Read, "البحث في طلبات البرامج", "Search program requests",
        "Searches the program requests the user may see, by text and optionally status (draft, in-approval, approved, rejected, cancelled), classification (project, initiative) or priority band.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"text":{"type":"string","maxLength":200},"status":{"type":"string"},"classification":{"type":"string"},"priorityBand":{"type":"string"}}}
            """),
        PortfolioPermissions.RequestRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRequests(new PageRequest(1, 50), RequestViews.All, arguments.Text("status"), arguments.Text("classification"),
            arguments.Text("priorityBand"), null, arguments.Text("text")), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, page => new { total = page.TotalCount, requests = page.Items.Select(PortfolioToolResults.Summary) });
    }
}

internal sealed class MyRequestsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_my_requests", PortfolioPermissions.SystemCode, ToolKind.Read, "طلباتي", "My program requests",
        "Lists the program requests the signed-in user created, newest first, with their status and current approval step.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""), PortfolioPermissions.RequestRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRequests(new PageRequest(1, 50), RequestViews.Mine), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, page => new { total = page.TotalCount, requests = page.Items.Select(PortfolioToolResults.Summary) });
    }
}

internal sealed class GetRequestTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_get_request", PortfolioPermissions.SystemCode, ToolKind.Read, "تفاصيل طلب برنامج", "Program request details",
        "Returns a program request: values, classification, evaluation score, priority, budget and Year 1–4 costs, status and current approval step.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["requestId"],"properties":{"requestId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.RequestRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("requestId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "requestId is required.");
        }

        var result = await dispatcher.SendAsync(new GetRequest(id), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, r => new
        {
            r.Id,
            r.Number,
            r.Status,
            r.ApprovalStatus,
            step = r.ApprovalStepNameEn ?? r.ApprovalStepNameAr,
            r.Values,
            classification = r.EffectiveClassification,
            evaluationScorePercent = r.EvaluationScore is { } s ? Math.Round(s * 100, 2) : (decimal?)null,
            r.PriorityBand,
            r.YearCostsTotal,
            r.ApprovedBudget,
            unit = r.Unit?.NameEn ?? r.Unit?.NameAr,
            requester = r.Requester?.NameEn ?? r.Requester?.NameAr,
            project = r.Project?.Number,
            r.AssessmentCount,
            link = $"/portfolio/requests/{r.Id:D}",
        });
    }
}

internal sealed class GetProjectTool(ProjectService projects) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_get_project", PortfolioPermissions.SystemCode, ToolKind.Read, "تفاصيل مشروع", "Project details",
        "Returns a project or initiative: classification, stage, status, project manager, owner, progress (performance, planned to date, on track / delayed), approval in progress, budget, dates and the request.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId"],"properties":{"projectId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId is required.");
        }

        var result = await projects.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, p => new
        {
            p.Id,
            p.Number,
            p.Classification,
            p.NameAr,
            p.NameEn,
            p.Stage,
            p.SubState,
            p.Status,
            p.Budget,
            p.Charter.PlannedStart,
            p.Charter.PlannedEnd,
            owner = p.Owner?.NameEn ?? p.Owner?.NameAr,
            projectManager = p.Pm?.NameEn ?? p.Pm?.NameAr,
            unit = p.Unit?.NameEn ?? p.Unit?.NameAr,
            performance = Math.Round(p.Performance * 100, 2),
            plannedProgress = Math.Round(p.PlannedProgress * 100, 2),
            p.ProgressStatus,
            approval = p.Approval.Process is null ? null : new { p.Approval.Process, step = p.Approval.StepNameEn ?? p.Approval.StepNameAr, p.Approval.Result },
            p.RequestNumber,
            link = $"/portfolio/projects/{p.Id:D}",
        });
    }
}

/// <summary>Projects I manage or own (read).</summary>
internal sealed class MyProjectsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_my_projects", PortfolioPermissions.SystemCode, ToolKind.Read, "مشاريعي", "My projects",
        "Lists the projects and initiatives the caller manages or owns, with stage, status and progress.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""), PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListProjects(new PageRequest(1, 50, null), View: "mine"), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, page => page.Items.Select(p => new
        {
            p.Id,
            p.Number,
            p.NameAr,
            p.NameEn,
            p.Classification,
            p.Stage,
            p.Status,
            performance = Math.Round(p.Performance * 100, 2),
            p.ProgressStatus,
            link = $"/portfolio/projects/{p.Id:D}",
        }).ToList());
    }
}

/// <summary>The milestones of a project with weights and achievement (read).</summary>
internal sealed class ProjectMilestonesTool(MilestoneService milestones) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_project_milestones", PortfolioPermissions.SystemCode, ToolKind.Read, "مراحل المشروع", "Project milestones",
        "Lists a project's milestones and phases: code, title, planned and actual dates, weight, approved achievement, pending update, status and delay.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId"],"properties":{"projectId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId is required.");
        }

        var result = await milestones.ListAsync(id, cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, plan => plan.Milestones.Select(m => new
        {
            m.Id,
            m.Code,
            m.TitleAr,
            m.TitleEn,
            m.IsHeader,
            m.HighLevel,
            m.PlannedStart,
            m.PlannedEnd,
            weight = Math.Round(m.Weight * 100, 2),
            approved = Math.Round(m.MaxAchieved * 100, 2),
            pending = m.PendingAchieve is { } a ? Math.Round(a * 100, 2) : (decimal?)null,
            m.Status,
            m.UpdateState,
            m.Delayed,
        }).ToList());
    }
}

/// <summary>Saves a milestone's pending monthly values while the window is open (never submits). Write tool: off by default.</summary>
internal sealed class UpdateMilestoneDraftTool(MilestoneService milestones) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_update_milestone_draft", PortfolioPermissions.SystemCode, ToolKind.Write, "حفظ مسودة تحديث مرحلة", "Save a milestone update draft",
        "Saves the pending monthly values of one milestone (achievement in percent of the whole project, actual dates, causality and corrective actions when delayed). Requires an open monthly window. It never submits the update for approval.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["milestoneId","achievePercent","version"],"properties":{"milestoneId":{"type":"string","format":"uuid"},"achievePercent":{"type":"number"},"actualStart":{"type":"string","format":"date"},"actualEnd":{"type":"string","format":"date"},"causality":{"type":"string"},"correctiveActions":{"type":"string"},"version":{"type":"integer"}}}"""),
        PortfolioPermissions.ProjectEdit);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("milestoneId") is not { } id || !arguments.TryGetProperty("achievePercent", out var percent) || percent.ValueKind != JsonValueKind.Number
            || !arguments.TryGetProperty("version", out var version) || !version.TryGetUInt32(out var v))
        {
            return ToolResult.Fail(ToolErrors.Validation, "milestoneId, achievePercent and version are required.");
        }

        static DateOnly? Date(JsonElement args, string name) =>
            args.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String && DateOnly.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var d) ? d : null;
        static string? Str(JsonElement args, string name) => args.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        var values = new MilestoneProgressValues(Math.Round(percent.GetDecimal() / 100m, 4), Date(arguments, "actualStart"), Date(arguments, "actualEnd"),
            Str(arguments, "causality"), Str(arguments, "correctiveActions"), null);
        var result = await milestones.RecordProgressAsync(id, new MilestoneUpdateInput(values, v), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, r => new { r.Milestone.Id, r.Milestone.Code, r.Milestone.UpdateState, r.Warnings, submitted = false });
    }
}

/// <summary>Creates a draft request (never submits). Write tool: off until an administrator enables it.</summary>
internal sealed class CreateDraftRequestTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_create_draft_request", PortfolioPermissions.SystemCode, ToolKind.Write, "إنشاء مسودة طلب برنامج", "Create a draft program request",
        "Creates a DRAFT program request with a name, description, justifications and an optional estimated budget and dates. The user reviews and submits it in the application.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["nameEn"],"properties":{"nameAr":{"type":"string","maxLength":300},"nameEn":{"type":"string","maxLength":300},
              "description":{"type":"string","maxLength":5000},"justifications":{"type":"string","maxLength":5000},"estimatedBudget":{"type":"string"},
              "expectedStartDate":{"type":"string","format":"date"},"expectedEndDate":{"type":"string","format":"date"}}}
            """),
        PortfolioPermissions.RequestCreate,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        static DateOnly? Date(string? text) => DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : null;
        var budget = decimal.TryParse(arguments.Text("estimatedBudget"), NumberStyles.Number, CultureInfo.InvariantCulture, out var b) ? b : (decimal?)null;
        var values = new RequestValues
        {
            NameAr = arguments.Text("nameAr"),
            NameEn = arguments.Text("nameEn"),
            Description = arguments.Text("description"),
            Justifications = arguments.Text("justifications"),
            RequiresBudget = budget is > 0 ? true : null,
            EstimatedBudget = budget,
            ExpectedStartDate = Date(arguments.Text("expectedStartDate")),
            ExpectedEndDate = Date(arguments.Text("expectedEndDate")),
        };
        var result = await dispatcher.SendAsync(new CreateRequest(values), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, r => new { r.Id, r.Number, r.Status, link = $"/portfolio/requests/{r.Id:D}" });
    }
}
