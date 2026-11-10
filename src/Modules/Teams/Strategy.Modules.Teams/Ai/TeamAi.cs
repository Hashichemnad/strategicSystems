using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Ai;

/// <summary>AI features of Teams (SPEC §4.8), registered in the Phase 5 registry. Drafts only (CLAUDE.md rule 6).</summary>
public static class TeamAiFeatures
{
    public const string Formation = "teams-formation";
    public const string Members = "teams-members";
    public const string Evaluation = "teams-evaluation";
    public const string Minutes = "teams-minutes";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Formation, "مساعد تشكيل الفرق", "Team formation assistant", TeamPermissions.AiFormation),
        new(Members, "اقتراح أعضاء الفريق", "Team member suggestions", TeamPermissions.AiMembers, NeedsProvider: false),
        new(Evaluation, "مساعد تقييم الفريق", "Team evaluation assistant", TeamPermissions.AiEvaluation),
        new(Minutes, "كاتب محاضر الاجتماعات", "Meeting minutes writer", TeamPermissions.AiMinutes),
    ];
}

public sealed record FormationDraftDto(string? ObjectiveAr, string? ObjectiveEn, string? MandateAr, string? MandateEn, string? MemoAr, string? MemoEn, Guid UsageId);

public sealed record MemberSuggestionDto(TeamPersonDto Employee, double Score, int ActiveMemberships, bool OverAssigned, int PastTeams, string? LastGrade, IReadOnlyList<string> Reasons);

public sealed record EvaluationAssistantDto(string Summary, IReadOnlyList<string> Points, IReadOnlyList<EvaluationFlag> Flags, Guid UsageId);

public sealed record MinutesRecommendationDraft(string Text, string? OwnerName, TeamPersonDto? Owner, DateOnly? DueDate);

public sealed record MinutesDraftDto(string Minutes, IReadOnlyList<string> Decisions, IReadOnlyList<MinutesRecommendationDraft> Recommendations, Guid UsageId);

/// <summary>The four features; every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class TeamAiService(
    AiGateway gateway, TeamsDbContext db, TeamReader reader, EvaluationReader evaluations, TeamActivity activity, IEmployeeDirectory employees, IOrgDirectory org,
    ILinkRegistry links, ISettingsStore settingsStore, IRequestContext context, IClock clock, Platform.Access.IAccessService access)
{
    private static readonly JsonElement FormationSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["objectiveAr","objectiveEn","mandateAr","mandateEn","memoAr","memoEn"],"properties":{
          "objectiveAr":{"type":"string","maxLength":2000},"objectiveEn":{"type":"string","maxLength":2000},
          "mandateAr":{"type":"string","maxLength":4000},"mandateEn":{"type":"string","maxLength":4000},
          "memoAr":{"type":"string","maxLength":6000},"memoEn":{"type":"string","maxLength":6000}}}
        """);

    private static readonly JsonElement EvaluationSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summary","points"],"properties":{"summary":{"type":"string","maxLength":3000},
          "points":{"type":"array","maxItems":6,"items":{"type":"string","maxLength":400}}}}
        """);

    private static readonly JsonElement MinutesSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["minutes","decisions","recommendations"],"properties":{"minutes":{"type":"string","maxLength":15000},
          "decisions":{"type":"array","maxItems":30,"items":{"type":"string","maxLength":1000}},
          "recommendations":{"type":"array","maxItems":30,"items":{"type":"object","required":["text"],"properties":{
            "text":{"type":"string","maxLength":2000},"ownerName":{"type":["string","null"],"maxLength":200},"dueDate":{"type":["string","null"],"maxLength":10}}}}}}
        """);

    public async Task<Result<FormationDraftDto>> DraftFormationAsync(string? typeCode, string? name, string? description, string? linkedType, Guid? linkedId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 20_000)
        {
            return Error.Validation([new FieldError("description", "validation.notEmpty", "validation.notEmpty")]);
        }

        var type = typeCode is null ? null : await reader.TypeAsync(typeCode, ct).ConfigureAwait(false);
        var documents = new List<AiDocument> { new("description", description) };
        if (linkedType is not null && linkedId is { } id)
        {
            var resolved = await links.ResolveAsync([new LinkRef(linkedType, id)], context.User, ct).ConfigureAwait(false);
            var view = resolved.Count > 0 ? resolved[0] : null;
            if (view is { Visibility: LinkVisibility.Visible })
            {
                documents.Add(new AiDocument("linked-record", $"{view.Number} {view.TitleEn ?? view.TitleAr} ({view.StatusCode})"));
            }
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            TeamAiFeatures.Formation, new Dictionary<string, string> { ["type"] = type?.NameEn ?? typeCode ?? "team", ["name"] = name ?? string.Empty },
            documents, FormationSchema, TeamPermissions.SystemCode, AuditDetail: new { characters = description.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new FormationDraftDto(Str(o, "objectiveAr"), Str(o, "objectiveEn"), Str(o, "mandateAr"), Str(o, "mandateEn"), Str(o, "memoAr"), Str(o, "memoEn"), call.Value.UsageId);
    }

    /// <summary>Deterministic ranking: skills overlap with past memberships − current workload; over-assigned flagged; grades only for results holders.</summary>
    public async Task<Result<IReadOnlyList<MemberSuggestionDto>>> SuggestMembersAsync(Guid? unitId, string? skills, IReadOnlyList<Guid>? exclude, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(TeamAiFeatures.Members, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var configuration = await settingsStore.GetAsync<TeamSettings>(ct).ConfigureAwait(false);
        var unit = unitId ?? (context.User.EmployeeIdOf() is { } me ? (await employees.GetAsync(me, ct).ConfigureAwait(false))?.UnitId : null);
        var candidates = new HashSet<Guid>();
        if (unit is { } u)
        {
            candidates.UnionWith((await employees.GetEmployeeIdsInUnitsAsync(await org.GetSelfAndDescendantIdsAsync(u, ct).ConfigureAwait(false), ct).ConfigureAwait(false)).Take(300));
        }

        var words = Words(skills);
        var history = await db.Members.AsNoTracking().Where(m => m.EmployeeId != null).Select(m => new
        {
            Employee = m.EmployeeId!.Value,
            m.TeamId,
            m.Status,
            m.RequiredSkills,
            m.Responsibilities,
        }).ToListAsync(ct).ConfigureAwait(false);
        if (words.Count > 0)
        {
            candidates.UnionWith(history.Where(h => Overlap(words, h.RequiredSkills, h.Responsibilities) > 0).Select(h => h.Employee).Take(300));
        }

        candidates.ExceptWith(exclude ?? []);
        var operating = await db.Teams.AsNoTracking().Where(t => t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive
            || t.Status == TeamStatuses.PendingEvaluation || t.Status == TeamStatuses.EvaluationWaitingApproval).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(candidates.ToList(), ct).ConfigureAwait(false);
        var hrResults = await access.CanAsync(context.User, TeamPermissions.Results, new Platform.Access.ResourceRef(TeamPermissions.ResourceType, Guid.Empty), ct).ConfigureAwait(false);
        var grades = hrResults
            ? (await db.MemberEvaluations.AsNoTracking().Where(m => m.EmployeeId != null && candidates.Contains(m.EmployeeId.Value) && m.Grade != null)
                .Select(m => new { m.EmployeeId, m.Grade, m.CreatedAt }).ToListAsync(ct).ConfigureAwait(false))
                .GroupBy(m => m.EmployeeId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First().Grade)
            : [];
        var result = new List<MemberSuggestionDto>();
        foreach (var id in candidates.Where(people.ContainsKey).Where(id => people[id].Status != "left"))
        {
            var mine = history.Where(h => h.Employee == id).ToList();
            var active = mine.Count(h => h.Status == MemberStatuses.Active && operating.Contains(h.TeamId));
            var overlap = mine.Sum(h => Overlap(words, h.RequiredSkills, h.Responsibilities));
            var reasons = new List<string>();
            if (overlap > 0)
            {
                reasons.Add("skills");
            }

            if (unit is not null && people[id].UnitId == unit)
            {
                reasons.Add("unit");
            }

            var over = active >= configuration.OverAssignedThreshold;
            reasons.Add(over ? "over-assigned" : active == 0 ? "available" : "workload");
            var grade = grades.GetValueOrDefault(id);
            var score = Math.Round((2.0 * Math.Min(overlap, 5)) + (3.0 * Math.Max(0, configuration.OverAssignedThreshold - active) / configuration.OverAssignedThreshold)
                + (grade == "very-active" ? 1 : 0), 2);
            var e = people[id];
            result.Add(new MemberSuggestionDto(new TeamPersonDto(id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr), score, active, over,
                mine.Select(h => h.TeamId).Distinct().Count(), grade, reasons));
        }

        return result.OrderByDescending(r => r.Score).ThenBy(r => r.ActiveMemberships).Take(configuration.MemberSuggestionLimit).ToList();
    }

    public async Task<Result<EvaluationAssistantDto>> SummariseEvaluationAsync(Guid evaluationId, CancellationToken ct)
    {
        var (team, evaluation) = await evaluations.LoadAsync(evaluationId, false, ct).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await evaluations.IsEvaluatorAsync(team, evaluation, ct).ConfigureAwait(false)
            && !await reader.CanAsync(context.User, TeamPermissions.Results, team, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var data = await activity.ComputeAsync(team.Id, evaluation.PeriodStart, evaluation.PeriodEnd, ct).ConfigureAwait(false);
        var meetings = await db.Meetings.AsNoTracking().Where(m => m.TeamId == team.Id && m.Status == MeetingStatuses.Held).CountAsync(ct).ConfigureAwait(false);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Team: {team.NameEn ?? team.NameAr} ({team.Number}). Cycle {evaluation.Cycle}: {evaluation.PeriodStart:yyyy-MM-dd} to {evaluation.PeriodEnd:yyyy-MM-dd}.\n")
            .Append(CultureInfo.InvariantCulture, $"Meetings held: {meetings}. Minutes delays (hours): {string.Join(", ", data.MinutesDelays)}.\n")
            .Append(CultureInfo.InvariantCulture, $"Recommendations closed: {data.RecommendationsClosed} of {data.RecommendationsCounted}.\n");
        foreach (var c in evaluation.ReadCriteria())
        {
            text.Append(CultureInfo.InvariantCulture, $"Criterion {c.Code}: suggested {c.SuggestedLevel ?? "-"} (basis {c.Basis?.Value}), chosen {c.Level ?? "-"}.\n");
        }

        var flags = evaluation.ReadFlags();
        foreach (var f in flags)
        {
            text.Append(CultureInfo.InvariantCulture, $"Flag: {f.Code}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(TeamAiFeatures.Evaluation, new Dictionary<string, string>(), [new AiDocument("activity", text.ToString())],
            EvaluationSchema, TeamPermissions.SystemCode, ResourceType: TeamEvaluation.EntityType, ResourceId: evaluation.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var points = call.Value.Output.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
        return new EvaluationAssistantDto(Str(call.Value.Output, "summary") ?? string.Empty, points, flags, call.Value.UsageId);
    }

    public async Task<Result<MinutesDraftDto>> DraftMinutesAsync(Guid meetingId, string? notes, CancellationToken ct)
    {
        // The notes may come from the rich-text editor: the model only ever sees the plain-text projection, as data (ADR 0161).
        notes = RichText.ToPlainText(notes);
        if (string.IsNullOrWhiteSpace(notes) || notes.Length > 30_000)
        {
            return Error.Validation([new FieldError("notes", "validation.notEmpty", "validation.notEmpty")]);
        }

        var meeting = await db.Meetings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meetingId, ct).ConfigureAwait(false);
        var team = meeting is null ? null : await reader.FindAsync(meeting.TeamId, context.User, ct).ConfigureAwait(false);
        if (meeting is null || team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Minutes, team, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(TeamAiFeatures.Minutes,
            new Dictionary<string, string> { ["today"] = clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, [new AiDocument("notes", notes)], MinutesSchema,
            TeamPermissions.SystemCode, ResourceType: Meeting.EntityType, ResourceId: meeting.Id.ToString("D"), AuditDetail: new { characters = notes.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var members = await reader.MembersAsync(team.Id, activeOnly: true, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), ct).ConfigureAwait(false);
        var recommendations = new List<MinutesRecommendationDraft>();
        foreach (var item in o.GetProperty("recommendations").EnumerateArray())
        {
            var owner = Str(item, "ownerName");
            var match = owner is null
                ? null
                : people.Values.FirstOrDefault(p => (p.NameEn ?? string.Empty).Contains(owner, StringComparison.OrdinalIgnoreCase)
                    || (p.NameAr ?? string.Empty).Contains(owner, StringComparison.Ordinal));
            var due = DateOnly.TryParseExact(Str(item, "dueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
            if (Str(item, "text") is { } text)
            {
                recommendations.Add(new MinutesRecommendationDraft(text, owner, match, due));
            }
        }

        var decisions = o.GetProperty("decisions").EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
        return new MinutesDraftDto(Str(o, "minutes") ?? string.Empty, decisions, recommendations, call.Value.UsageId);
    }

    private static List<string> Words(string? text) =>
        (text ?? string.Empty).Split([' ', ',', '،', ';', '\n', '/', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 3).Select(w => w.ToLowerInvariant()).Distinct().ToList();

    private static int Overlap(List<string> words, params string?[] texts)
    {
        if (words.Count == 0)
        {
            return 0;
        }

        var haystack = string.Join(' ', texts.Where(t => t is not null)).ToLowerInvariant();
        return words.Count(w => haystack.Contains(w, StringComparison.Ordinal));
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
}
