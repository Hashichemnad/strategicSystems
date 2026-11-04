using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Contracts;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Teams.Application;

/// <summary>Team documents: read with the team, write for the people who may change or run it.</summary>
internal sealed class TeamFiles(TeamReader reader) : IFileOwnerAccess
{
    public string OwnerType => TeamPermissions.ResourceType;

    public string AttachmentPurpose => TeamFilePurposes.Documents.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id) || await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } team || TeamStatuses.IsFinished(team.Status))
        {
            return false;
        }

        foreach (var permission in new[] { TeamPermissions.Manage, TeamPermissions.Operate, TeamPermissions.Minutes, TeamPermissions.Submit })
        {
            if (await reader.CanAsync(user, permission, team, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Evidence and certificates of an evaluation: only results viewers read them; the evaluator uploads evidence while editable (ADR 0042).</summary>
internal sealed class EvaluationFiles(TeamsDbContext db, TeamReader reader) : IFileOwnerAccess
{
    public string OwnerType => TeamEvaluation.EntityType;

    public string AttachmentPurpose => TeamFilePurposes.Evidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await LoadAsync(user, ownerId, cancellationToken).ConfigureAwait(false);
        return team is not null && evaluation is not null && (await reader.CanAsync(user, TeamPermissions.Results, team, cancellationToken).ConfigureAwait(false)
            || IsEvaluator(user, evaluation));
    }

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await LoadAsync(user, ownerId, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null || !EvaluationStatuses.IsEditable(evaluation.Status))
        {
            return false;
        }

        return evaluation.EvaluatorRule == EvaluatorRules.Administrator
            ? await reader.CanAsync(user, TeamPermissions.EvaluationOpen, team, cancellationToken).ConfigureAwait(false)
            : IsEvaluator(user, evaluation) && await reader.CanAsync(user, TeamPermissions.Evaluate, team, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsEvaluator(ClaimsPrincipal user, TeamEvaluation evaluation) =>
        user.EmployeeIdOf() is { } me && (evaluation.EvaluatorEmployeeId == me || evaluation.LeaderEvaluatorEmployeeId == me);

    private async Task<(Team? Team, TeamEvaluation? Evaluation)> LoadAsync(ClaimsPrincipal user, string ownerId, CancellationToken ct)
    {
        if (!Guid.TryParse(ownerId, out var id) || await db.Evaluations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false) is not { } evaluation)
        {
            return (null, null);
        }

        return (await reader.FindAsync(evaluation.TeamId, user, ct).ConfigureAwait(false), evaluation);
    }
}

/// <summary>"Next action required + who" for a team (SPEC §0.2).</summary>
internal sealed class TeamCurrentStatus(TeamsDbContext db, TeamReader reader, TeamQueries queries, IRequestContext context, IApprovalEngine engine) : ICurrentStatusProvider
{
    public string ResourceType => TeamPermissions.ResourceType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var user = context.User;
        var team = await reader.FindAsync(resourceId, user, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return null;
        }

        var empty = new Dictionary<string, BilingualText>();
        var permissions = await queries.PermissionsAsync(team, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([team.LeaderEmployeeId, team.RequestedByEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] Who(Guid? id) => id is { } i && people.TryGetValue(i, out var p) ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        switch (team.Status)
        {
            case TeamStatuses.Draft:
                return new CurrentStatus(team.StatusReason is null ? "teams.currentStatus.draft" : "teams.currentStatus.returned", empty, team.StatusReason is null ? "info" : "warning",
                    "teams.currentStatus.requester", Who(team.RequestedByEmployeeId),
                    permissions.CanSubmit ? [new CurrentStatusAction(TeamActions.Submit, "teams.actions.submit")] : [], team.Status);
            case TeamStatuses.PendingApproval:
                return await ApprovalStatusAsync(FormationApprovalProcess.Key, team, cancellationToken).ConfigureAwait(false);
            case TeamStatuses.PendingEvaluation:
                {
                    var evaluation = await OpenEvaluationAsync(team.Id, cancellationToken).ConfigureAwait(false);
                    var evaluator = await reader.PeopleAsync([evaluation?.EvaluatorEmployeeId], cancellationToken).ConfigureAwait(false);
                    var mine = evaluation is not null && (evaluation.EvaluatorEmployeeId == user.EmployeeIdOf() || evaluation.LeaderEvaluatorEmployeeId == user.EmployeeIdOf());
                    return new CurrentStatus(evaluation?.Status == EvaluationStatuses.Returned ? "teams.currentStatus.evaluationReturned" : "teams.currentStatus.pendingEvaluation",
                        new Dictionary<string, BilingualText> { ["cycle"] = new(evaluation?.Cycle, evaluation?.Cycle) }, "warning", "teams.currentStatus.evaluator",
                        evaluation?.EvaluatorEmployeeId is { } e && evaluator.TryGetValue(e, out var p) ? [new PersonName(null, p.NameAr, p.NameEn)] : [],
                        mine && evaluation is not null ? [new CurrentStatusAction("evaluate", "teams.actions.evaluate", "primary", evaluation.Id)] : [], team.Status);
                }

            case TeamStatuses.EvaluationWaitingApproval:
                return await ApprovalStatusAsync(EvaluationApprovalProcesses.EvaluationKey, team, cancellationToken).ConfigureAwait(false);
            case TeamStatuses.Active:
                {
                    var rewards = await db.Evaluations.AsNoTracking().AnyAsync(e => e.TeamId == team.Id && e.RewardStatus == RewardStatuses.Pending, cancellationToken)
                        .ConfigureAwait(false);
                    if (rewards)
                    {
                        return await ApprovalStatusAsync(EvaluationApprovalProcesses.RewardKey, team, cancellationToken).ConfigureAwait(false);
                    }

                    var dueReport = await db.Reports.AsNoTracking().Where(r => r.TeamId == team.Id && r.Status == ReportStatuses.Due).OrderBy(r => r.DueAt)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    return new CurrentStatus(dueReport is null ? "teams.currentStatus.active" : "teams.currentStatus.reportDue",
                        dueReport is null
                            ? empty
                            : new Dictionary<string, BilingualText> { ["date"] = new(D(dueReport.DueDate), D(dueReport.DueDate)) },
                        "success", "teams.currentStatus.leader", Who(team.LeaderEmployeeId), [], team.Status);
                }

            default:
                return new CurrentStatus($"teams.currentStatus.{team.Status}", empty, team.Status == TeamStatuses.Inactive ? "warning" : "neutral", null, [], [], team.Status);
        }
    }

    private async Task<CurrentStatus> ApprovalStatusAsync(string processKey, Team team, CancellationToken ct)
    {
        var view = await engine.GetStatusViewAsync(processKey, team.Id, context.User, ct).ConfigureAwait(false);
        var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? view?.Instance.CurrentStepNameEn, view?.Instance.CurrentStepNameEn ?? view?.Instance.CurrentStepNameAr);
        var actions = view?.MyTaskId is { } taskId
            ? view.MyActions.Select(a => new CurrentStatusAction(a, $"workflow.actions.{a}", a == ApprovalActions.Approve ? "primary" : a == ApprovalActions.Reject ? "danger" : "secondary",
                taskId)).ToList()
            : [];
        return new CurrentStatus("workflow.currentStatus.waitingStep", new Dictionary<string, BilingualText> { ["step"] = step }, "info", "workflow.currentStatus.approvers",
            view?.Approvers ?? [], actions, team.Status);
    }

    private Task<TeamEvaluation?> OpenEvaluationAsync(Guid teamId, CancellationToken ct) =>
        db.Evaluations.AsNoTracking().Where(e => e.TeamId == teamId && (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned))
            .OrderByDescending(e => e.OpenedAt).FirstOrDefaultAsync(ct);

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>My Work: evaluations to fill, reports due for teams I lead, my overdue recommendations, requests returned to me.</summary>
internal sealed class TeamMyWork(TeamsDbContext db, IClock clock) : IMyWorkSource
{
    public string SystemCode => TeamPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var items = new List<MyWorkItem>();
        var evaluations = await (from e in db.Evaluations.AsNoTracking()
                                 join t in db.Teams.AsNoTracking() on e.TeamId equals t.Id
                                 where (e.EvaluatorEmployeeId == me || e.LeaderEvaluatorEmployeeId == me)
                                       && (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned)
                                 select new { e.Id, e.Number, t.NameAr, t.NameEn, e.OpenedAt, e.Status }).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(evaluations.Select(e => new MyWorkItem(SystemCode, TeamEvaluation.EntityType, e.Id, e.Number, e.NameAr, e.NameEn,
            e.Status == EvaluationStatuses.Returned ? "teams.myWork.evaluationReturned" : "teams.myWork.evaluate", $"/teams/evaluations/{e.Id:D}", e.OpenedAt)));
        var reports = await (from r in db.Reports.AsNoTracking()
                             join t in db.Teams.AsNoTracking() on r.TeamId equals t.Id
                             where t.LeaderEmployeeId == me && r.Status == ReportStatuses.Due && r.RemindAt <= clock.UtcNow
                             select new { t.Id, t.Number, t.NameAr, t.NameEn, r.DueDate }).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(reports.Select(r => new MyWorkItem(SystemCode, TeamPermissions.ResourceType, r.Id, r.Number, r.NameAr, r.NameEn, "teams.myWork.reportDue",
            $"/teams/{r.Id:D}?tab=reports", DueDate: r.DueDate)));
        var today = clock.Today;
        var recommendations = await (from r in db.Recommendations.AsNoTracking()
                                     join t in db.Teams.AsNoTracking() on r.TeamId equals t.Id
                                     where r.OwnerEmployeeId == me && (r.Status == RecommendationStatuses.Open || r.Status == RecommendationStatuses.InProgress) && r.DueDate < today
                                     select new { t.Id, r.Number, r.Text, r.DueDate }).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(recommendations.Select(r => new MyWorkItem(SystemCode, TeamPermissions.ResourceType, r.Id, r.Number, r.Text, r.Text, "teams.myWork.recommendationOverdue",
            $"/teams/{r.Id:D}?tab=recommendations", DueDate: r.DueDate)));
        var returned = await db.Teams.AsNoTracking().Where(t => t.RequestedByEmployeeId == me && t.Status == TeamStatuses.Draft && t.StatusReason != null)
            .Take(20).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(returned.Select(t => new MyWorkItem(SystemCode, TeamPermissions.ResourceType, t.Id, t.Number, t.NameAr, t.NameEn, "teams.myWork.formationReturned",
            $"/teams/{t.Id:D}", t.UpdatedAt)));
        return items;
    }
}

/// <summary>Teams, recommendations and meetings as linkable (task-able) records (ADR 0034).</summary>
internal sealed class TeamLinkTargets(TeamReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        TeamPermissions.ResourceType, TeamPermissions.SystemCode, "فريق / لجنة", "Team / committee", [LinkCapabilities.TaskAble], TeamPermissions.Read, "/teams/{id}",
        TeamPermissions.Operate);

    public string ResourceType => TeamPermissions.ResourceType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var teams = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return teams.ToDictionary(t => t.Id, Target);
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } team && await reader.CanAsync(user, permission, team, cancellationToken).ConfigureAwait(false);

    public static LinkTarget Target(Team t) => new(t.Id, t.Number, t.NameAr, t.NameEn, t.Status, null, null, Tone(t.Status), TeamStatuses.IsFinished(t.Status));

    public static string Tone(string status) => status switch
    {
        TeamStatuses.Active => "success",
        TeamStatuses.PendingApproval or TeamStatuses.PendingEvaluation or TeamStatuses.EvaluationWaitingApproval => "info",
        TeamStatuses.Inactive => "warning",
        TeamStatuses.Cancelled => "danger",
        _ => "neutral",
    };
}

internal sealed class RecommendationLinkTargets(TeamsDbContext db, TeamReader reader) : ILinkTargetSource
{
    public const string Type = Recommendation.EntityType;

    public static readonly LinkableTypeDefinition Definition = new(
        Type, TeamPermissions.SystemCode, "توصية / قرار فريق", "Team recommendation / decision", [LinkCapabilities.TaskAble], TeamPermissions.Read, "/teams/recommendations/{id}",
        TeamPermissions.Operate);

    public string ResourceType => Type;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var items = await db.Recommendations.AsNoTracking().Where(r => ids.Contains(r.Id) && visible.Any(t => t.Id == r.TeamId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, r.Number, r.Text.Length > 120 ? r.Text[..120] + "…" : r.Text,
            r.Text.Length > 120 ? r.Text[..120] + "…" : r.Text, r.Status, null, null,
            r.Status == RecommendationStatuses.Closed ? "success" : r.Status == RecommendationStatuses.Rejected ? "danger" : "info", !RecommendationStatuses.IsOpen(r.Status)));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await db.Recommendations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        return item is not null && await reader.FindAsync(item.TeamId, user, cancellationToken).ConfigureAwait(false) is { } team
            && await reader.CanAsync(user, permission, team, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class MeetingLinkTargets(TeamsDbContext db, TeamReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        Meeting.EntityType, TeamPermissions.SystemCode, "اجتماع فريق", "Team meeting", [LinkCapabilities.TaskAble], TeamPermissions.Read, "/teams/meetings/{id}",
        TeamPermissions.Operate);

    public string ResourceType => Meeting.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var items = await db.Meetings.AsNoTracking().Where(m => ids.Contains(m.Id) && visible.Any(t => t.Id == m.TeamId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.ToDictionary(m => m.Id, m => new LinkTarget(m.Id, m.Number, m.Title, m.Title, m.Status, null, null,
            m.Status == MeetingStatuses.Held ? "success" : m.Status == MeetingStatuses.Cancelled ? "danger" : "info", m.Status != MeetingStatuses.Scheduled));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await db.Meetings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        return item is not null && await reader.FindAsync(item.TeamId, user, cancellationToken).ConfigureAwait(false) is { } team
            && await reader.CanAsync(user, permission, team, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Search (global search, similar records, <c>search_records</c>): names and codes only, never scores (ADR 0042).</summary>
internal sealed class TeamSearchSource(TeamReader reader) : IRecordSearchSource
{
    public string ResourceType => TeamPermissions.ResourceType;

    public string SystemCode => TeamPermissions.SystemCode;

    public string ReadPermission => TeamPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var teams = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => EF.Functions.ILike(t.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(t.Number ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.Subject ?? string.Empty, pattern, "\\"))
            .OrderByDescending(t => t.CreatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return teams.Select(t => Hit(t, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => t.Id != excludeId)
            .Select(t => new { Team = t, Score = Math.Max(EF.Functions.TrigramsSimilarity(t.NameEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(t.NameAr ?? string.Empty, text)) })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => Hit(h.Team, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(Team t, double score) =>
        new(TeamPermissions.ResourceType, t.Id, TeamPermissions.SystemCode, t.Number, t.NameAr, t.NameEn, t.Status, $"/teams/{t.Id:D}", score);
}

/// <summary>A team's content for Ask / approver brief / <c>get_record</c>: evaluation scores only for results viewers (ADR 0042).</summary>
internal sealed class TeamRecordSource(TeamsDbContext db, TeamReader reader) : IAiRecordSource
{
    public string ResourceType => TeamPermissions.ResourceType;

    public async Task<AiRecordContent?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return null;
        }

        var members = await reader.MembersAsync(team.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), cancellationToken).ConfigureAwait(false);
        var names = members.Select(m => m.EmployeeId is { } e && people.TryGetValue(e, out var p) ? p.NameEn ?? p.NameAr ?? string.Empty : m.ExternalName ?? string.Empty).ToList();
        var fields = new List<AiRecordField>
        {
            new(nameof(Team.TypeCode), "النوع", "Type", team.TypeCode),
            new(nameof(Team.Status), "الحالة", "Status", team.Status),
            new(nameof(Team.Subject), "الموضوع", "Subject", team.Subject),
            new(nameof(Team.Objectives), "الأهداف", "Objectives", team.Objectives),
            new(nameof(Team.Mandate), "المهام", "Mandate", team.Mandate),
            new(nameof(Team.StartDate), "تاريخ البدء", "Start date", team.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new(nameof(Team.EndDate), "تاريخ الانتهاء", "End date", team.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("Members", "الأعضاء", "Members", string.Join(", ", names)),
        };
        if (await reader.CanAsync(user, TeamPermissions.Results, team, cancellationToken).ConfigureAwait(false))
        {
            var last = await db.Evaluations.AsNoTracking().Where(e => e.TeamId == team.Id && e.Status == EvaluationStatuses.Approved).OrderByDescending(e => e.ApprovedAt)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            fields.Add(new("LastEvaluationScore", "درجة آخر تقييم", "Last evaluation score", last?.Score?.ToString(CultureInfo.InvariantCulture)));
        }

        return new AiRecordContent(TeamPermissions.ResourceType, team.Id, TeamPermissions.SystemCode, Team.EntityType, team.Number, team.NameAr, team.NameEn, $"/teams/{team.Id:D}",
            fields, names);
    }
}

/// <summary>Other modules read teams through this (Tasks: "assign to a team's members").</summary>
internal sealed class TeamsDirectory(TeamReader reader) : ITeamsDirectory
{
    public async Task<TeamSummary?> GetTeamAsync(Guid teamId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var team = await reader.FindAsync(teamId, user, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return null;
        }

        var members = await reader.MembersAsync(team.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
        return new TeamSummary(team.Id, team.Number, team.NameAr, team.NameEn, team.Status, team.LeaderEmployeeId,
            members.Where(m => m.EmployeeId is not null).Select(m => m.EmployeeId!.Value).Distinct().ToList());
    }
}

/// <summary>
/// <c>teamRole</c> approver rules (ADR 0025): members holding the member-role code in the team being approved, or in the active teams linked to
/// the record being approved.
/// </summary>
internal sealed class TeamRoleResolver(TeamsDbContext db, ILinkStore links) : ITeamRoleResolver
{
    public async Task<IReadOnlyList<Guid>> ResolveEmployeesAsync(string teamRoleCode, ApprovalSubject subject, CancellationToken cancellationToken)
    {
        List<Guid> teamIds;
        if (subject.ResourceType == TeamPermissions.ResourceType)
        {
            teamIds = [subject.ResourceId];
        }
        else
        {
            var stored = await links.ListToAsync(new LinkRef(subject.ResourceType, subject.ResourceId), LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false);
            teamIds = stored.Where(l => l.FromType == TeamPermissions.ResourceType).Select(l => l.FromId).ToList();
        }

        return await db.Members.AsNoTracking()
            .Where(m => teamIds.Contains(m.TeamId) && m.Status == MemberStatuses.Active && m.RoleCode == teamRoleCode && m.EmployeeId != null
                && db.Teams.Any(t => t.Id == m.TeamId && t.Status != TeamStatuses.Closed && t.Status != TeamStatuses.Cancelled && t.Status != TeamStatuses.Replaced))
            .Select(m => m.EmployeeId!.Value).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>ADR 0043: a linked record closed (e.g. project closure approved) → open the completion evaluation of its project teams.</summary>
internal sealed class OpenEvaluationOnLinkedRecordClosed(TeamsDbContext db, ILinkStore links, Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
    : IIntegrationEventHandler<LinkedRecordClosed>
{
    public async Task HandleAsync(LinkedRecordClosed integrationEvent, CancellationToken cancellationToken)
    {
        var stored = await links.ListToAsync(new LinkRef(integrationEvent.ResourceType, integrationEvent.ResourceId), LinkCapabilities.TeamAble, cancellationToken)
            .ConfigureAwait(false);
        var ids = stored.Where(l => l.FromType == TeamPermissions.ResourceType).Select(l => l.FromId).Distinct().ToList();
        var types = await db.Types.AsNoTracking().ToDictionaryAsync(t => t.Code, cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            await using var scope = Jobs.JobScope.Create(scopes);
            var teamDb = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<TeamsDbContext>(scope.ServiceProvider);
            var team = await teamDb.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
            if (team is null || !types.TryGetValue(team.TypeCode, out var type)
                || EvaluationTriggers.Effective(type.EvaluationTrigger, team.Permanent) != EvaluationTriggers.LinkedRecordClosed)
            {
                continue;
            }

            if (team.Status is not (TeamStatuses.Active or TeamStatuses.Inactive))
            {
                continue; // already evaluating or finished: idempotent
            }

            await using var transaction = await teamDb.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var opener = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<EvaluationOpener>(scope.ServiceProvider);
            var opened = await opener.OpenAsync(team, EvaluationTriggers.LinkedRecordClosed, null, closeAfter: true, cancellationToken).ConfigureAwait(false);
            if (opened.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Active memberships of active teams with their allocation (ADR 0116).</summary>
internal sealed class TeamAllocationDirectory(TeamsDbContext db) : ITeamAllocationDirectory
{
    public async Task<IReadOnlyList<TeamAllocation>> ForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        return await db.Members.AsNoTracking()
            .Where(m => m.EmployeeId != null && employeeIds.Contains(m.EmployeeId.Value) && m.Status == MemberStatuses.Active)
            .Join(db.Teams.AsNoTracking().Where(t => t.Status == TeamStatuses.Active), m => m.TeamId, t => t.Id,
                (m, t) => new TeamAllocation(m.EmployeeId!.Value, t.Id, t.Number, t.NameAr, t.NameEn, m.AllocationPercent))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
