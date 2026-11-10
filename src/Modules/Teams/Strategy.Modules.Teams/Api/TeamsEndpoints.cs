using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Teams.Ai;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Integration;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Teams.Api;

public sealed record TeamVersionInput(uint Version, string? Comment = null, string? Reason = null);

public sealed record MemoInput(string Number, DateOnly Date, Guid FileId, uint Version);

public sealed record LinkInput(string Type, Guid RecordId, string? Reason = null);

public sealed record MemberUpdateInput(string RoleCode, string? Responsibilities, string? RequiredSkills, decimal? AllocationPercent, uint Version);

public sealed record EndMembershipInput(DateOnly? On, string Reason, MemberInput? Successor, uint Version);

public sealed record MeetingInput(string Title, DateTimeOffset ScheduledAt, DateTimeOffset EndsAt, string? Location, string? Agenda, uint Version = 0);

public sealed record MinutesInput(string? Minutes, string? Decisions, IReadOnlyList<AttendanceInput>? Attendance, bool AiDrafted, uint Version);

public sealed record RecommendationInput(Guid? MeetingId, string? Kind, string Text, Guid? OwnerEmployeeId, DateOnly? DueDate, uint Version = 0);

public sealed record RecommendationStatusInput(string Status, string? Note, IReadOnlyList<Guid>? EvidenceFileIds, uint Version);

public sealed record ReportInput(string Summary, uint Version);

public sealed record OpenEvaluationInput(int? Year);

public sealed record EvaluationInput(
    IReadOnlyList<CriterionInput>? Criteria, string? Workload, IReadOnlyList<Guid>? EvidenceFileIds, IReadOnlyList<MemberAnswersInput>? Members, uint Version);

public sealed record RewardsInput(IReadOnlyList<RewardInput> Rewards, uint Version);

public sealed record SettingsInput(TeamSettings Settings, uint Version);

public sealed record TeamTypeInput(Guid? Id, string Code, TeamTypeValues Values, uint? Version);

public sealed record MemberRoleInput(
    Guid? Id, string Code, string NameAr, string NameEn, int Sort, bool IsLeader, bool CanViewResults, bool CountsTowardQuorum, bool IsSecretary, bool Active, uint? Version);

public sealed record EvaluationTemplateInput(Guid? Id, int Year, TemplateContent? Content, string? Notes, uint? Version);

public sealed record RewardMatrixInput(Guid? Id, int Year, RewardMatrixContent? Content, uint? Version);

public sealed record FormationAiInput(string? TypeCode, string? Name, string? Description, string? LinkedType, Guid? LinkedId);

public sealed record MembersAiInput(Guid? UnitId, string? Skills, IReadOnlyList<Guid>? Exclude);

public sealed record MinutesAiInput(string? Notes);

/// <summary>
/// <c>/api/v1/teams</c> (docs/design/teams.md §15). Endpoints declare the permission needed to use them at all; handlers check the team itself
/// (404 outside the read scope, 403 for a visible team and a forbidden action).
/// </summary>
internal static class TeamsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (int? page, int? pageSize, string? sort, string? view, string? typeCode, string? status, Guid? unitId, string? search, IDispatcher d,
                CancellationToken ct) =>
                (await d.SendAsync(new ListTeams(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, typeCode, status, unitId, search), ct)).ToOk())
            .WithName("ListTeams").RequirePermission(TeamPermissions.Read);
        group.MapGet("/counts", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTeamCounts(), ct)).ToOk())
            .WithName("GetTeamCounts").RequirePermission(TeamPermissions.Read);
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTeamOptions(), ct)).ToOk())
            .WithName("GetTeamOptions").RequirePermission(TeamPermissions.Read);
        group.MapPost("/", async Task<Results<Created<TeamDto>, ProblemHttpResult>> (TeamInput body, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateTeam(body), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/teams/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateTeam").RequirePermission(TeamPermissions.Request);
        group.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTeam(id), ct)).ToOk())
            .WithName("GetTeam").RequirePermission(TeamPermissions.Read);
        group.MapPut("/{id:guid}", async (Guid id, UpdateTeamBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateTeam(id, b.Team, b.Version), ct)).ToOk())
            .WithName("UpdateTeam").RequirePermission(TeamPermissions.Read);
        group.MapByExternal("", TeamLinkTargets.Definition.ResourceType, TeamPermissions.Read, "Team", t => t.Id, (id, http) => http.Dispatch(new GetTeam(id)))
            .WithUpsert<TeamInput>(TeamPermissions.Request, TeamPermissions.Read, (b, http) => http.Dispatch(new CreateTeam(b)),
                (id, b, current, http) => http.Dispatch(new UpdateTeam(id, b, current.Version)));
        group.MapPost("/{id:guid}/submit", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitTeam(id, b.Version, b.Comment), ct)).ToOk())
            .WithName("SubmitTeam").RequirePermission(TeamPermissions.Read).WithIdempotency();
        foreach (var (action, name) in new[]
                 {
                     (TeamActions.Cancel, "CancelTeam"), (TeamActions.Deactivate, "DeactivateTeam"), (TeamActions.Reactivate, "ReactivateTeam"), (TeamActions.Close, "CloseTeam"),
                 })
        {
            group.MapPost($"/{{id:guid}}/{action}", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                    (await d.SendAsync(new ChangeTeamStatus(id, action, b.Version, b.Reason), ct)).ToOk())
                .WithName(name).RequirePermission(TeamPermissions.Read);
        }

        group.MapPost("/{id:guid}/memo", async (Guid id, MemoInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetExternalMemo(id, b.Number, b.Date, b.FileId, b.Version), ct)).ToOk())
            .WithName("SetExternalMemo").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/memo/regenerate", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RegenerateMemo(id), ct)).ToOk())
            .WithName("RegenerateMemo").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/links", async (Guid id, LinkInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LinkTeamRecord(id, b.Type, b.RecordId), ct)).ToOk())
            .WithName("LinkTeamRecord").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/links/remove", async (Guid id, LinkInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UnlinkTeamRecord(id, b.Type, b.RecordId, b.Reason), ct)).ToOk())
            .WithName("UnlinkTeamRecord").RequirePermission(TeamPermissions.Read);
        group.MapGet("/linked", async (string type, Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListLinkedTeams(type, id), ct)).ToOk())
            .WithName("ListLinkedTeams").RequirePermission(TeamPermissions.Read);

        group.MapGet("/{id:guid}/members", async (Guid id, bool? history, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListMembers(id, history ?? false), ct)).ToOk())
            .WithName("ListTeamMembers").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/members", async (Guid id, MemberInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new AddMember(id, b), ct)).ToOk())
            .WithName("AddTeamMember").RequirePermission(TeamPermissions.Read);
        group.MapPut("/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, MemberUpdateInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateMember(id, memberId, b.RoleCode, b.Responsibilities, b.RequiredSkills, b.AllocationPercent, b.Version), ct)).ToOk())
            .WithName("UpdateTeamMember").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/members/{memberId:guid}/end", async (Guid id, Guid memberId, EndMembershipInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new EndMembership(id, memberId, b.On, b.Reason, b.Successor, b.Version), ct)).ToOk())
            .WithName("EndTeamMembership").RequirePermission(TeamPermissions.Read);

        group.MapGet("/{id:guid}/meetings", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListMeetings(id), ct)).ToOk())
            .WithName("ListTeamMeetings").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/meetings", async (Guid id, MeetingInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CreateMeeting(id, b.Title, b.ScheduledAt, b.EndsAt, b.Location, b.Agenda), ct)).ToOk())
            .WithName("CreateTeamMeeting").RequirePermission(TeamPermissions.Read);
        var meetings = group.MapGroup("/meetings");
        meetings.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetMeeting(id), ct)).ToOk())
            .WithName("GetTeamMeeting").RequirePermission(TeamPermissions.Read);
        meetings.MapByExternal("", MeetingLinkTargets.Definition.ResourceType, TeamPermissions.Read, "TeamMeeting", m => m.Id, (id, http) => http.Dispatch(new GetMeeting(id)));
        meetings.MapPut("/{id:guid}", async (Guid id, MeetingInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateMeeting(id, b.Title, b.ScheduledAt, b.EndsAt, b.Location, b.Agenda, b.Version), ct)).ToOk())
            .WithName("UpdateTeamMeeting").RequirePermission(TeamPermissions.Read);
        meetings.MapPut("/{id:guid}/minutes", async (Guid id, MinutesInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveMinutes(id, b.Minutes, b.Decisions, b.Attendance, b.AiDrafted, b.Version), ct)).ToOk())
            .WithName("SaveMeetingMinutes").RequirePermission(TeamPermissions.Read);
        meetings.MapPost("/{id:guid}/submit-minutes", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitMinutes(id, b.Version), ct)).ToOk())
            .WithName("SubmitMeetingMinutes").RequirePermission(TeamPermissions.Read).WithIdempotency();
        meetings.MapPost("/{id:guid}/cancel", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelMeeting(id, b.Reason ?? string.Empty, b.Version), ct)).ToOk())
            .WithName("CancelTeamMeeting").RequirePermission(TeamPermissions.Read);
        meetings.MapPost("/{id:guid}/ai/minutes", async (Guid id, MinutesAiInput b, TeamAiService ai, CancellationToken ct) =>
                (await ai.DraftMinutesAsync(id, b.Notes, ct)).ToOk())
            .WithName("DraftMeetingMinutesWithAi").RequirePermission(TeamPermissions.AiMinutes);

        group.MapGet("/{id:guid}/recommendations", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRecommendations(id), ct)).ToOk())
            .WithName("ListTeamRecommendations").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/recommendations", async (Guid id, RecommendationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CreateRecommendation(id, b.MeetingId, b.Kind, b.Text, b.OwnerEmployeeId, b.DueDate), ct)).ToOk())
            .WithName("CreateTeamRecommendation").RequirePermission(TeamPermissions.Read);
        var recommendations = group.MapGroup("/recommendations");
        recommendations.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRecommendation(id), ct)).ToOk())
            .WithName("GetTeamRecommendation").RequirePermission(TeamPermissions.Read);
        recommendations.MapByExternal("", RecommendationLinkTargets.Type, TeamPermissions.Read, "TeamRecommendation", r => r.Id,
            (id, http) => http.Dispatch(new GetRecommendation(id)));
        recommendations.MapPut("/{id:guid}", async (Guid id, RecommendationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateRecommendation(id, b.Kind, b.Text, b.OwnerEmployeeId, b.DueDate, b.Version), ct)).ToOk())
            .WithName("UpdateTeamRecommendation").RequirePermission(TeamPermissions.Read);
        recommendations.MapPost("/{id:guid}/status", async (Guid id, RecommendationStatusInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetRecommendationStatus(id, b.Status, b.Note, b.EvidenceFileIds, b.Version), ct)).ToOk())
            .WithName("SetTeamRecommendationStatus").RequirePermission(TeamPermissions.Read);

        group.MapGet("/{id:guid}/reports", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListReports(id), ct)).ToOk())
            .WithName("ListTeamReports").RequirePermission(TeamPermissions.Read);
        group.MapPost("/reports/{id:guid}/submit", async (Guid id, ReportInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitReport(id, b.Summary, b.Version), ct)).ToOk())
            .WithName("SubmitTeamReport").RequirePermission(TeamPermissions.Read);

        group.MapGet("/{id:guid}/evaluations", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListEvaluations(id), ct)).ToOk())
            .WithName("ListTeamEvaluations").RequirePermission(TeamPermissions.Read);
        group.MapPost("/{id:guid}/evaluations", async (Guid id, OpenEvaluationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new OpenEvaluation(id, b.Year), ct)).ToOk())
            .WithName("OpenTeamEvaluation").RequirePermission(TeamPermissions.Read);
        var evaluations = group.MapGroup("/evaluations");
        evaluations.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetEvaluation(id), ct)).ToOk())
            .WithName("GetTeamEvaluation").RequirePermission(TeamPermissions.Read);
        evaluations.MapGet("/{id:guid}/status", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetEvaluationStatus(id), ct)).ToOk())
            .WithName("GetTeamEvaluationStatus").RequirePermission(TeamPermissions.Read);
        evaluations.MapPut("/{id:guid}", async (Guid id, EvaluationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveEvaluation(id, b.Criteria, b.Workload, b.EvidenceFileIds, b.Members, b.Version), ct)).ToOk())
            .WithName("SaveTeamEvaluation").RequirePermission(TeamPermissions.Read);
        evaluations.MapPost("/{id:guid}/submit", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitEvaluation(id, b.Version, b.Comment), ct)).ToOk())
            .WithName("SubmitTeamEvaluation").RequirePermission(TeamPermissions.Read).WithIdempotency();
        evaluations.MapPost("/{id:guid}/cancel", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelEvaluation(id, b.Reason ?? string.Empty, b.Version), ct)).ToOk())
            .WithName("CancelTeamEvaluation").RequirePermission(TeamPermissions.Read);
        evaluations.MapPut("/{id:guid}/rewards", async (Guid id, RewardsInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateRewards(id, b.Rewards, b.Version), ct)).ToOk())
            .WithName("UpdateTeamRewards").RequirePermission(TeamPermissions.Read);
        evaluations.MapPost("/{id:guid}/rewards/submit", async (Guid id, TeamVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitRewards(id, b.Version, b.Comment), ct)).ToOk())
            .WithName("SubmitTeamRewards").RequirePermission(TeamPermissions.Read).WithIdempotency();
        evaluations.MapPost("/{id:guid}/ai/summary", async (Guid id, TeamAiService ai, CancellationToken ct) => (await ai.SummariseEvaluationAsync(id, ct)).ToOk())
            .WithName("SummariseTeamEvaluationWithAi").RequirePermission(TeamPermissions.AiEvaluation);

        group.MapGet("/employees/{employeeId:guid}/memberships", async (Guid employeeId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListMemberships(employeeId), ct)).ToOk())
            .WithName("ListEmployeeTeamMemberships").RequirePermission(TeamPermissions.Read);
        group.MapGet("/dashboard", async (DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetTeamDashboard(from, to), ct)).ToOk())
            .WithName("GetTeamDashboard").RequirePermission(TeamPermissions.DashboardRead);
        group.MapGet("/export", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string kind, string format, string? language, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportTeams(kind, format, language), ct);
                return result.IsSuccess
                    ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
                    : result.Error.ToProblem();
            })
            .WithName("ExportTeams").RequirePermission(TeamPermissions.Export);

        group.MapRecordHistory(Team.EntityType, TeamPermissions.Read, "GetTeam", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetTeam(id), context.RequestAborted)).IsSuccess);

        var ai = group.MapGroup("/ai");
        ai.MapPost("/formation", async (FormationAiInput b, TeamAiService service, CancellationToken ct) =>
                (await service.DraftFormationAsync(b.TypeCode, b.Name, b.Description, b.LinkedType, b.LinkedId, ct)).ToOk())
            .WithName("DraftTeamFormationWithAi").RequirePermission(TeamPermissions.AiFormation);
        ai.MapPost("/members", async (MembersAiInput b, TeamAiService service, CancellationToken ct) =>
                (await service.SuggestMembersAsync(b.UnitId, b.Skills, b.Exclude, ct)).ToOk())
            .WithName("SuggestTeamMembers").RequirePermission(TeamPermissions.AiMembers);

        var settings = group.MapGroup("/settings");
        settings.MapGet("/", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTeamConfiguration(), ct)).ToOk())
            .WithName("GetTeamConfiguration").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPut("/", async (SettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveTeamSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveTeamSettings").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/types", async (TeamTypeInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveTeamType(b.Id, b.Code, b.Values, b.Version), ct)).ToOk())
            .WithName("SaveTeamType").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/roles", async (MemberRoleInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveMemberRole(b.Id, b.Code, b.NameAr, b.NameEn, b.Sort, b.IsLeader, b.CanViewResults, b.CountsTowardQuorum, b.IsSecretary, b.Active,
                    b.Version), ct)).ToOk())
            .WithName("SaveTeamMemberRole").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/templates", async (EvaluationTemplateInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveTemplate(b.Id, b.Year, b.Content, b.Notes, b.Version), ct)).ToOk())
            .WithName("SaveTeamEvaluationTemplate").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/templates/{id:guid}/publish", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new PublishTemplate(id), ct)).ToOk())
            .WithName("PublishTeamEvaluationTemplate").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/reward-matrices", async (RewardMatrixInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveRewardMatrix(b.Id, b.Year, b.Content, b.Version), ct)).ToOk())
            .WithName("SaveTeamRewardMatrix").RequirePermission(TeamPermissions.SettingsManage);
        settings.MapPost("/reward-matrices/{id:guid}/publish", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new PublishRewardMatrix(id), ct)).ToOk())
            .WithName("PublishTeamRewardMatrix").RequirePermission(TeamPermissions.SettingsManage);
    }
}

public sealed record UpdateTeamBody(TeamInput Team, uint Version);
