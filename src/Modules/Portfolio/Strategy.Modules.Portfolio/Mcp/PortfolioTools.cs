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

internal sealed class GetProjectTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "portfolio_get_project", PortfolioPermissions.SystemCode, ToolKind.Read, "تفاصيل مشروع", "Project details",
        "Returns a project or initiative created from an approved request: classification, stage, status, budget, dates, owner and the request.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["projectId"],"properties":{"projectId":{"type":"string","format":"uuid"}}}"""),
        PortfolioPermissions.ProjectRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("projectId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "projectId is required.");
        }

        var result = await dispatcher.SendAsync(new GetProject(id), cancellationToken).ConfigureAwait(false);
        return PortfolioToolResults.From(result, p => new
        {
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Stage, p.Status, p.Budget, p.PlannedStart, p.PlannedEnd,
            owner = p.Owner?.NameEn ?? p.Owner?.NameAr, unit = p.Unit?.NameEn ?? p.Unit?.NameAr, p.RequestNumber, link = $"/portfolio/projects/{p.Id:D}",
        });
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
