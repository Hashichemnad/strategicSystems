using System.Text.Json;
using Strategy.Modules.Teams.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Teams.Mcp;

/// <summary>
/// MCP tools of Teams (SPEC §4.8, ADR 0031). They run as the token owner through the same handlers as the UI: the same scopes, and results
/// only for results viewers (ADR 0042). The write tool is off by default.
/// </summary>
internal static class TeamToolResults
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

    public static object Summary(TeamListItemDto t) => new
    {
        t.Id,
        t.Number,
        t.NameAr,
        t.NameEn,
        type = t.TypeNameEn ?? t.TypeCode,
        t.Status,
        leader = t.Leader?.NameEn ?? t.Leader?.NameAr,
        t.StartDate,
        t.EndDate,
        t.ActiveMembers,
        link = $"/teams/{t.Id:D}",
    };
}

internal sealed class MyTeamsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_teams", TeamPermissions.SystemCode, ToolKind.Read, "فرقي", "My teams",
        "Lists the teams and committees the signed-in user belongs to ('mine', default) or leads ('leading').",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"view":{"type":"string","enum":["mine","leading"]}}}"""),
        TeamPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListTeams(new PageRequest(1, 50), arguments.Text("view") ?? TeamViews.Mine), cancellationToken).ConfigureAwait(false);
        return TeamToolResults.From(result, page => new { total = page.TotalCount, teams = page.Items.Select(TeamToolResults.Summary) });
    }
}

internal sealed class TeamDetailsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "team_details", TeamPermissions.SystemCode, ToolKind.Read, "تفاصيل فريق", "Team details",
        "Returns a team's details: type, status, objectives, mandate, dates, members and roles, meetings and recommendations counts.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["teamId"],"properties":{"teamId":{"type":"string","format":"uuid"}}}"""),
        TeamPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("teamId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "teamId is required.");
        }

        var result = await dispatcher.SendAsync(new GetTeam(id), cancellationToken).ConfigureAwait(false);
        return TeamToolResults.From(result, t => new
        {
            t.Id,
            t.Number,
            t.NameAr,
            t.NameEn,
            type = t.TypeNameEn ?? t.TypeCode,
            t.Status,
            t.Subject,
            t.Objectives,
            t.Mandate,
            t.StartDate,
            t.EndDate,
            t.MemoNumber,
            leader = t.Leader?.NameEn ?? t.Leader?.NameAr,
            members = t.Members.Select(m => new { name = m.Employee?.NameEn ?? m.Employee?.NameAr ?? m.ExternalName, role = m.RoleNameEn ?? m.RoleCode, m.Responsibilities }),
            t.Stats,
            lastEvaluationScore = t.Permissions.CanViewResults ? t.LastScore : null,
            link = $"/teams/{t.Id:D}",
        });
    }
}

internal sealed class MyPendingEvaluationsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_pending_evaluations", TeamPermissions.SystemCode, ToolKind.Read, "تقييماتي المعلقة", "My pending evaluations",
        "Lists the team evaluations waiting for the signed-in user (as evaluator) and teams pending evaluation the user can see.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""),
        TeamPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListTeams(new PageRequest(1, 50), TeamViews.PendingEvaluation), cancellationToken).ConfigureAwait(false);
        var counts = await dispatcher.SendAsync(new GetTeamCounts(), cancellationToken).ConfigureAwait(false);
        return TeamToolResults.From(result, page => new
        {
            mine = counts.IsSuccess ? counts.Value.MyEvaluations : 0,
            teams = page.Items.Select(TeamToolResults.Summary),
        });
    }
}

internal sealed class EvaluationStatusTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "evaluation_status", TeamPermissions.SystemCode, ToolKind.Read, "حالة تقييم فريق", "Team evaluation status",
        "Returns the evaluation cycles of a team with their status and dates. Scores are included only for people allowed to see evaluation results.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["teamId"],"properties":{"teamId":{"type":"string","format":"uuid"}}}"""),
        TeamPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("teamId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "teamId is required.");
        }

        var result = await dispatcher.SendAsync(new ListEvaluations(id), cancellationToken).ConfigureAwait(false);
        return TeamToolResults.From(result, list => new
        {
            cycles = list.Select(e => new { e.Id, e.Number, e.Cycle, e.Status, e.OpenedAt, e.SubmittedAt, e.ApprovedAt, e.Score, e.RewardStatus }),
        });
    }
}

internal sealed class AddMeetingMinutesTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "add_meeting_minutes", TeamPermissions.SystemCode, ToolKind.Write, "إضافة محضر اجتماع (مسودة)", "Add meeting minutes (draft)",
        "Saves minutes and decisions on a meeting as a draft (not submitted). Requires the right to write minutes for that team.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["meetingId","minutes"],"properties":{"meetingId":{"type":"string","format":"uuid"},
              "minutes":{"type":"string","maxLength":20000},"decisions":{"type":"string","maxLength":20000}}}
            """),
        TeamPermissions.Read,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("meetingId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "meetingId is required.");
        }

        var meeting = await dispatcher.SendAsync(new GetMeeting(id), cancellationToken).ConfigureAwait(false);
        if (meeting.IsFailure)
        {
            return TeamToolResults.From(meeting, _ => null);
        }

        var result = await dispatcher.SendAsync(new SaveMinutes(id, arguments.Text("minutes"), arguments.Text("decisions"), null, true, meeting.Value.Version), cancellationToken)
            .ConfigureAwait(false);
        return TeamToolResults.From(result, m => new { m.Id, m.Number, m.Status, draft = true, link = $"/teams/meetings/{m.Id:D}" });
    }
}
