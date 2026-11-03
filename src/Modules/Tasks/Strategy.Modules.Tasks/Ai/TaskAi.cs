using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Ai;

/// <summary>AI features of Task Management (SPEC §11.10), registered in the Phase 5 registry. Drafts only: nothing is created or saved here.</summary>
public static class TaskAiFeatures
{
    public const string FromText = "tasks-from-text";
    public const string Summary = "task-summary";
    public const string Assignee = "task-assignee";
    public const string Workload = "task-workload";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(FromText, "مسودات مهام من نص", "Draft tasks from text", TaskPermissions.AiFromText),
        new(Summary, "ملخص المهمة ومسودات الملاحظات", "Task summary and draft notes", TaskPermissions.AiSummary),
        new(Assignee, "اقتراح المكلفين", "Assignee suggestions", TaskPermissions.AiAssignee, NeedsProvider: false),
        new(Workload, "رؤى عبء العمل", "Workload insights", TaskPermissions.AiWorkload, NeedsProvider: false),
    ];
}

public sealed record TaskDraftDto(
    string? TitleAr, string? TitleEn, string? Description, string? AssigneeName, PersonDto? Assignee, IReadOnlyList<PersonDto> AssigneeCandidates, DateOnly? DueDate,
    string Priority, string? Source);

public sealed record TaskDraftsDto(IReadOnlyList<TaskDraftDto> Drafts, Guid UsageId);

public sealed record TaskSummaryDto(string Mode, string Text, Guid UsageId);

public sealed record AssigneeSuggestionDto(PersonDto Employee, double Score, int OpenTasks, int SimilarDone, IReadOnlyList<string> Reasons);

public sealed record WorkloadInsightDto(PersonDto Employee, string Kind, int Open, int Overdue, int ClosedRecently, int Late, double? LateRate);

public static class TaskSummaryModes
{
    public const string Summary = "summary";
    public const string Submission = "submission";
    public const string Return = "return";

    public static readonly IReadOnlyList<string> All = [Summary, Submission, Return];
}

/// <summary>The four features. Every call goes through the AI gateway (switches, permission, limits, masking, audit with channel <c>ai</c>).</summary>
internal sealed class TaskAiService(
    AiGateway gateway, TasksDbContext db, TaskVisibility visibility, TaskReader reader, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settingsStore,
    IRequestContext context, IClock clock)
{
    private static readonly JsonElement DraftsSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["tasks"],"properties":{"tasks":{"type":"array","maxItems":20,"items":{"type":"object","required":["title","priority"],
          "properties":{"title":{"type":"string","maxLength":300},"description":{"type":["string","null"],"maxLength":2000},
          "assigneeName":{"type":["string","null"],"maxLength":200},"dueDate":{"type":["string","null"],"maxLength":10},
          "priority":{"type":"string","enum":["high","medium","low"]},"source":{"type":["string","null"],"maxLength":400}}}}}}
        """);

    private static readonly JsonElement SummarySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["text"],"properties":{"text":{"type":"string","maxLength":4000}}}
        """);

    public async Task<Result<TaskDraftsDto>> DraftFromTextAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 30_000)
        {
            return Error.Validation([new FieldError("text", "validation.notEmpty", "validation.notEmpty")]);
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            TaskAiFeatures.FromText,
            new Dictionary<string, string> { ["today"] = clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            [new AiDocument("text", text)], DraftsSchema, TaskPermissions.SystemCode, AuditDetail: new { characters = text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var arabic = gateway.Language == "ar";
        var drafts = new List<TaskDraftDto>();
        foreach (var item in call.Value.Output.GetProperty("tasks").EnumerateArray())
        {
            var title = Str(item, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var name = Str(item, "assigneeName");
            var candidates = new List<PersonDto>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var ids = (await employees.SearchAsync(name, ct).ConfigureAwait(false)).Take(5).ToList();
                var people = await employees.GetManyAsync(ids, ct).ConfigureAwait(false);
                candidates.AddRange(ids.Where(people.ContainsKey).Where(id => people[id].Status != "left").Select(id => TaskReader.Person(id, people)));
            }

            var due = DateOnly.TryParseExact(Str(item, "dueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
            var priority = Str(item, "priority") is { } p && TaskPriorities.All.Contains(p) ? p : TaskPriorities.Medium;
            drafts.Add(new TaskDraftDto(arabic ? Clip(title, 300) : null, arabic ? null : Clip(title, 300), Str(item, "description"), name,
                candidates.Count == 1 ? candidates[0] : null, candidates, due, priority, Str(item, "source")));
        }

        return new TaskDraftsDto(drafts, call.Value.UsageId);
    }

    public async Task<Result<TaskSummaryDto>> SummariseAsync(Guid taskId, string? mode, CancellationToken ct)
    {
        mode ??= TaskSummaryModes.Summary;
        if (!TaskSummaryModes.All.Contains(mode))
        {
            return Error.Validation([new FieldError("mode", "validation.unknownCode", "validation.unknownCode")]);
        }

        var user = context.User;
        var task = await visibility.FindAsync(taskId, user, tracked: false, ct).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        var (isCreator, isAssignee) = TaskReader.Roles(task, user);
        if ((mode == TaskSummaryModes.Submission && !isAssignee) || (mode == TaskSummaryModes.Return && !isCreator))
        {
            return Error.Forbidden();
        }

        var comments = await db.Comments.AsNoTracking().Where(c => c.TaskId == task.Id).OrderBy(c => c.CreatedAt).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(comments.Where(c => c.AuthorEmployeeId != null).Select(c => c.AuthorEmployeeId!.Value)
            .Concat([task.AssigneeEmployeeId, task.CreatorEmployeeId]), ct).ConfigureAwait(false);
        string NameOf(Guid? e) => e is { } id ? people.GetValueOrDefault(id)?.NameEn ?? people.GetValueOrDefault(id)?.NameAr ?? "?" : "?";
        var body = new StringBuilder()
            .Append("Task ").Append(task.Number).Append(": ").AppendLine(task.TitleEn ?? task.TitleAr)
            .Append("Status: ").AppendLine(task.Status)
            .Append("Assignee: ").AppendLine(NameOf(task.AssigneeEmployeeId))
            .Append("Creator: ").AppendLine(NameOf(task.CreatorEmployeeId))
            .Append("Due: ").AppendLine(task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-")
            .Append("Description: ").AppendLine(task.Description ?? "-");
        var thread = new StringBuilder();
        foreach (var comment in comments)
        {
            thread.Append(comment.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(' ').Append(NameOf(comment.AuthorEmployeeId)).Append(": ")
                .AppendLine(comment.Text);
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            TaskAiFeatures.Summary, new Dictionary<string, string> { ["mode"] = mode },
            [new AiDocument("task", body.ToString()), new AiDocument("comments", thread.Length == 0 ? "No comments." : thread.ToString())], SummarySchema,
            TaskPermissions.SystemCode, people.Values.Select(p => p.NameEn ?? p.NameAr).OfType<string>().ToList(), TaskPermissions.ResourceType, task.Id.ToString(),
            AuditDetail: new { mode }), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new TaskSummaryDto(mode, Str(call.Value.Output, "text") ?? string.Empty, call.Value.UsageId);
    }

    /// <summary>
    /// Assignee suggestion without a model: employees of the unit (default: the creator's unit and below), ranked by fewer open tasks and more
    /// finished tasks with similar titles (trigram similarity). Counts exclude confidential tasks.
    /// </summary>
    public async Task<Result<IReadOnlyList<AssigneeSuggestionDto>>> SuggestAssigneesAsync(string? title, Guid? unitId, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(TaskAiFeatures.Assignee, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var unit = unitId ?? (context.User.EmployeeIdOf() is { } me ? (await employees.GetAsync(me, ct).ConfigureAwait(false))?.UnitId : null);
        if (unit is null)
        {
            return Result<IReadOnlyList<AssigneeSuggestionDto>>.Success([]);
        }

        var units = await org.GetSelfAndDescendantIdsAsync(unit.Value, ct).ConfigureAwait(false);
        var candidates = (await employees.GetEmployeeIdsInUnitsAsync(units, ct).ConfigureAwait(false)).Take(300).ToList();
        var open = await db.Tasks.AsNoTracking().Where(t => candidates.Contains(t.AssigneeEmployeeId) && !t.Confidential && !TaskStatuses.Closed.Contains(t.Status))
            .GroupBy(t => t.AssigneeEmployeeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var similar = new Dictionary<Guid, int>();
        if (!string.IsNullOrWhiteSpace(title) && title.Trim().Length >= 3)
        {
            var needle = title.Trim();
            similar = await db.Tasks.AsNoTracking()
                .Where(t => candidates.Contains(t.AssigneeEmployeeId) && !t.Confidential && (t.Status == TaskStatuses.Approved || t.Status == TaskStatuses.Done)
                    && Math.Max(EF.Functions.TrigramsSimilarity(t.TitleEn ?? string.Empty, needle), EF.Functions.TrigramsSimilarity(t.TitleAr ?? string.Empty, needle)) > 0.3)
                .GroupBy(t => t.AssigneeEmployeeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        }

        var threshold = (await settingsStore.GetAsync<TaskSettings>(ct).ConfigureAwait(false)).WorkloadThreshold;
        var ranked = candidates
            .Select(id => (Id: id, Open: open.GetValueOrDefault(id), Similar: similar.GetValueOrDefault(id)))
            .Select(c => (c.Id, c.Open, c.Similar, Score: Math.Round((2.0 * Math.Min(c.Similar, 5)) + (3.0 * Math.Max(0, threshold - c.Open) / threshold), 2)))
            .OrderByDescending(c => c.Score).ThenBy(c => c.Open).Take(5).ToList();
        var people = await reader.PeopleAsync(ranked.Select(r => r.Id), ct).ConfigureAwait(false);
        return ranked.Select(r => new AssigneeSuggestionDto(TaskReader.Person(r.Id, people), r.Score, r.Open, r.Similar,
            new[]
            {
                "tasks.ai.reasons.unit",
                r.Open < threshold / 2 ? "tasks.ai.reasons.lowWorkload" : r.Open >= threshold ? "tasks.ai.reasons.highWorkload" : null,
                r.Similar > 0 ? "tasks.ai.reasons.similarTasks" : null,
            }.OfType<string>().ToList())).ToList();
    }

    /// <summary>Workload insights without a model (SPEC §11.10): overloaded employees and chronic overdue patterns within the viewer's scope.</summary>
    public async Task<Result<IReadOnlyList<WorkloadInsightDto>>> WorkloadAsync(CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(TaskAiFeatures.Workload, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var now = clock.UtcNow;
        var since = now.AddDays(-90);
        var threshold = (await settingsStore.GetAsync<TaskSettings>(ct).ConfigureAwait(false)).WorkloadThreshold;
        var tasks = (await visibility.QueryAsync(context.User, ct).ConfigureAwait(false)).Where(t => t.Status != TaskStatuses.Draft);
        var rows = await tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status) || t.ClosedAt >= since)
            .Select(t => new { t.AssigneeEmployeeId, t.Status, t.DueAt, t.CompletedOnTime, t.ClosedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var insights = rows.GroupBy(r => r.AssigneeEmployeeId).Select(g =>
        {
            var openCount = g.Count(r => !TaskStatuses.IsClosed(r.Status));
            var overdue = g.Count(r => !TaskStatuses.IsClosed(r.Status) && r.Status != TaskStatuses.OnHold && r.DueAt < now);
            var closedRecently = g.Count(r => (r.Status == TaskStatuses.Approved || r.Status == TaskStatuses.Done) && r.CompletedOnTime is not null);
            var late = g.Count(r => (r.Status == TaskStatuses.Approved || r.Status == TaskStatuses.Done) && r.CompletedOnTime == false);
            var rate = closedRecently == 0 ? (double?)null : Math.Round((double)late / closedRecently, 4);
            var kind = openCount >= threshold ? "overloaded" : late >= 3 && rate >= 0.3 ? "chronicOverdue" : null;
            return (g.Key, kind, openCount, overdue, closedRecently, late, rate);
        }).Where(x => x.kind is not null).OrderByDescending(x => x.openCount).Take(20).ToList();
        var people = await reader.PeopleAsync(insights.Select(i => i.Key), ct).ConfigureAwait(false);
        return insights.Select(i => new WorkloadInsightDto(TaskReader.Person(i.Key, people), i.kind!, i.openCount, i.overdue, i.closedRecently, i.late, i.rate)).ToList();
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;
}
