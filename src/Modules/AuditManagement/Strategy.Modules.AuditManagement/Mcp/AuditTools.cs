using System.Text.Json;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.AuditManagement.Mcp;

/// <summary>
/// MCP tools of Audit Management (SPEC §10.7, ADR 0137). They run as the token owner through the same handlers as the UI (same scopes: department
/// users only see their own unit's findings). The write tool is off by default and needs the Audit Office permission.
/// </summary>
internal static class AuditToolResults
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

    public static object Finding(AuditFindingListItemDto f) => new
    {
        findingId = f.Id,
        number = f.Number,
        title = f.TitleEn ?? f.TitleAr,
        report = f.ReportNumber,
        reportType = f.ReportTypeCode,
        f.Priority,
        f.Status,
        completionPercent = Math.Round(f.Completion * 100, 0),
        department = f.ResponsibleUnit?.NameEn ?? f.ResponsibleUnit?.NameAr,
        f.Year,
        nextDue = f.NextDueOn,
        nextFollowUp = f.NextFollowUpOn,
        auditOfficeComment = f.LastComment,
        repeat = f.Repeat,
        link = $"/audit-management/findings/{f.Id:D}",
    };

    public static int Size(JsonElement arguments) =>
        arguments.TryGetProperty("pageSize", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetInt32(), 1, 100) : 25;

    public static readonly JsonElement ListSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"unitId":{"type":"string","format":"uuid"},"status":{"type":"string","enum":["open","late","closed","risk_accepted","cancelled"]},
          "year":{"type":"integer"},"search":{"type":"string","maxLength":200},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}
        """);

    public static async Task<ToolResult> ListAsync(IDispatcher dispatcher, JsonElement arguments, string view, CancellationToken ct)
    {
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        var result = await dispatcher.SendAsync(new ListAuditFindings(new PageRequest(1, Size(arguments)), view, arguments.Text("search"), null, arguments.Text("status"), null,
            arguments.Id("unitId"), year, null), ct).ConfigureAwait(false);
        return From(result, page => new { total = page.TotalCount, findings = page.Items.Select(Finding) });
    }
}

internal sealed class AuditFindingsByDepartmentTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_findings_by_department", AuditPermissions.SystemCode, ToolKind.Read, "ملاحظات التدقيق حسب الإدارة", "Audit findings by department",
        "Lists the audit findings the user may see, optionally for one department (unit id and below), status or year, with priority, completion and the latest Audit Office comment.",
        AuditToolResults.ListSchema, AuditPermissions.Read);

    public Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) =>
        AuditToolResults.ListAsync(dispatcher, arguments, "all", cancellationToken);
}

internal sealed class AuditOverdueFindingsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_overdue_findings", AuditPermissions.SystemCode, ToolKind.Read, "ملاحظات التدقيق المتأخرة", "Overdue audit findings",
        "Lists late audit findings (a recommendation past its due date below 100 %) the user may see, with department, priority and completion.",
        AuditToolResults.ListSchema, AuditPermissions.Read);

    public Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) =>
        AuditToolResults.ListAsync(dispatcher, arguments, "late", cancellationToken);
}

internal sealed class AuditMyFindingStatusTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_my_finding_status", AuditPermissions.SystemCode, ToolKind.Read, "حالة ملاحظات التدقيق لإدارتي", "Audit finding status of my department",
        "Read-only status of the open audit findings of the user's own section / department: status, completion, next due date and the latest Audit Office comment.",
        AuditToolResults.ListSchema, AuditPermissions.Read);

    public Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) =>
        AuditToolResults.ListAsync(dispatcher, arguments, "open", cancellationToken);
}

internal sealed class AuditFindingDetailsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_finding_details", AuditPermissions.SystemCode, ToolKind.Read, "تفاصيل ملاحظة تدقيق", "Audit finding details",
        "Returns one audit finding the user may see (condition, criteria, cause, effect, status, completion) with its recommendations, due dates and follow-up history.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["findingId"],"properties":{"findingId":{"type":"string","format":"uuid"}}}"""),
        AuditPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("findingId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "findingId is required.");
        }

        var finding = await dispatcher.SendAsync(new GetAuditFinding(id), cancellationToken).ConfigureAwait(false);
        if (finding.IsFailure)
        {
            return AuditToolResults.From(finding, _ => null);
        }

        var recommendations = await dispatcher.SendAsync(new ListAuditRecommendations(id), cancellationToken).ConfigureAwait(false);
        return AuditToolResults.From(recommendations, recs => new
        {
            findingId = finding.Value.Id,
            number = finding.Value.Number,
            report = finding.Value.ReportNumber,
            title = finding.Value.Values.TitleEn ?? finding.Value.Values.TitleAr,
            finding.Value.Values.Condition,
            finding.Value.Values.Criteria,
            finding.Value.Values.Cause,
            finding.Value.Values.Effect,
            finding.Value.Values.Priority,
            finding.Value.Status,
            completionPercent = Math.Round(finding.Value.Completion * 100, 0),
            department = finding.Value.ResponsibleUnit?.NameEn ?? finding.Value.ResponsibleUnit?.NameAr,
            recommendations = recs.Select(r => new
            {
                recommendationId = r.Id,
                number = r.Number,
                action = r.Values.ActionEn ?? r.Values.ActionAr,
                r.Status,
                dueDate = r.EffectiveDue,
                achievedPercent = Math.Round(r.LatestPct * 100, 0),
                nextFollowUp = r.NextFollowUpOn,
                auditOfficeComment = r.LastComment,
                followUps = r.FollowUps.Where(u => u.Applied).Select(u => new { u.Number, u.FollowUpOn, percent = Math.Round(u.Pct * 100, 0), u.Comment }),
            }),
            link = $"/audit-management/findings/{id:D}",
        });
    }
}

internal sealed class AuditFollowUpsDueTool(IDispatcher dispatcher, Strategy.Platform.Kernel.Time.IClock clock) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_followups_due", AuditPermissions.SystemCode, ToolKind.Read, "المتابعات المستحقة", "Audit follow-ups due",
        "Audit Office only: recommendations whose next follow-up date or due date falls in the next N days (default 14) or is overdue, by auditor.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":120}}}"""),
        AuditPermissions.Office);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var days = arguments.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? Math.Clamp(d.GetInt32(), 1, 120) : 14;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var result = await dispatcher.SendAsync(new GetAuditCalendar(today, today.AddDays(days), null), cancellationToken).ConfigureAwait(false);
        return AuditToolResults.From(result, items => new
        {
            total = items.Count,
            items = items.Select(i => new
            {
                i.Date,
                i.Kind,
                recommendation = i.RecommendationNumber,
                finding = i.FindingNumber,
                action = i.ActionEn ?? i.ActionAr,
                department = i.Unit?.NameEn ?? i.Unit?.NameAr,
                auditor = i.Auditor?.NameEn ?? i.Auditor?.NameAr,
                achievedPercent = Math.Round(i.LatestPct * 100, 0),
                i.Overdue,
                link = $"/audit-management/findings/{i.FindingId:D}",
            }),
        });
    }
}

internal sealed class AuditRecordFollowUpDraftTool(IDispatcher dispatcher, Strategy.Platform.Kernel.Time.IClock clock) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "audit_record_follow_up_draft", AuditPermissions.SystemCode, ToolKind.Write, "تسجيل متابعة توصية", "Record a follow-up",
        "Audit Office only: records a follow-up on a recommendation (date, achieved percent, actions implemented, comment, next follow-up date below 100 %).",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["recommendationId","achievedPercent","comment"],"properties":{
              "recommendationId":{"type":"string","format":"uuid"},"achievedPercent":{"type":"number","minimum":0,"maximum":100},
              "actionsImplemented":{"type":"string","maxLength":4000},"comment":{"type":"string","maxLength":4000},"nextFollowUpOn":{"type":"string","format":"date"}}}
            """),
        AuditPermissions.Office,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("recommendationId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "recommendationId is required.");
        }

        var pct = arguments.TryGetProperty("achievedPercent", out var p) && p.ValueKind == JsonValueKind.Number ? Math.Clamp(p.GetDecimal(), 0, 100) / 100m : 0m;
        DateOnly? next = DateOnly.TryParse(arguments.Text("nextFollowUpOn"), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var saved = await dispatcher.SendAsync(new RecordAuditFollowUp(id, new FollowUpValues(today, pct >= 1m ? today : null, pct, arguments.Text("actionsImplemented"), null,
            next, arguments.Text("comment"))), cancellationToken).ConfigureAwait(false);
        return AuditToolResults.From(saved, r => new { recommendationId = r.Id, r.Number, r.Status, achievedPercent = Math.Round(r.LatestPct * 100, 0) });
    }
}
