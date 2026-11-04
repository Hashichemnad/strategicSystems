using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Api;

public sealed record PortfolioKpiCardDto(Guid Id, string Code, string? NameAr, string? NameEn, string UnitCode, string Direction, string Link);

/// <summary>Phase 9 project endpoints (docs/design/portfolio.md §26). Endpoints declare the minimum permission; services check the record.</summary>
internal static class ProjectEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var projects = group.MapGroup("/projects");
        projects.MapGet("/{id:guid}", async (Guid id, ProjectService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToOk())
            .WithName("GetPortfolioProject").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/take-ownership", async (Guid id, ProjectVersionInput b, ProjectService s, CancellationToken ct) =>
                (await s.TakeOwnershipAsync(id, b.Version, ct)).ToOk())
            .WithName("TakeProjectOwnership").RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();
        projects.MapPost("/{id:guid}/assign-pm", async (Guid id, AssignPmInput b, ProjectService s, CancellationToken ct) =>
                (await s.AssignPmAsync(id, b.EmployeeId, b.Version, ct)).ToOk())
            .WithName("AssignProjectManager").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/charter", async (Guid id, CharterInput b, ProjectService s, CancellationToken ct) => (await s.EditCharterAsync(id, b.Values, b.Version, ct)).ToOk())
            .WithName("UpdateProjectCharter").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/closure", async (Guid id, ClosureInput b, ProjectService s, CancellationToken ct) => (await s.EditClosureAsync(id, b, ct)).ToOk())
            .WithName("UpdateProjectClosure").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/impact", async (Guid id, ImpactInput b, ProjectService s, CancellationToken ct) => (await s.EditImpactAsync(id, b, ct)).ToOk())
            .WithName("UpdateProjectImpact").RequirePermission(PortfolioPermissions.ProjectRead);
        foreach (var (route, process, name) in new[]
                 {
                     ("submit-charter", ProjectApprovalProcesses.Charter, "SubmitProjectCharter"), ("submit-planning", ProjectApprovalProcesses.Planning, "SubmitProjectPlanning"),
                     ("submit-closure", ProjectApprovalProcesses.Closure, "SubmitProjectClosure"), ("submit-impact", ProjectApprovalProcesses.Impact, "SubmitProjectImpact"),
                 })
        {
            projects.MapPost($"/{{id:guid}}/{route}", async (Guid id, ProjectVersionInput b, ProjectService s, CancellationToken ct) =>
                    (await s.SubmitStageAsync(id, process, b.Version, b.Comment, ct)).ToOk())
                .WithName(name).RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();
        }

        projects.MapPost("/{id:guid}/lpo-uploaded", async (Guid id, ProjectVersionInput b, ProjectService s, CancellationToken ct) => (await s.LpoUploadedAsync(id, b.Version, ct)).ToOk())
            .WithName("ConfirmProjectLpo").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/confirm-completion", async (Guid id, ProjectVersionInput b, ProjectService s, CancellationToken ct) =>
                (await s.ConfirmCompletionAsync(id, b.Version, ct)).ToOk())
            .WithName("ConfirmProjectCompletion").RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();
        foreach (var (route, action, name) in new[]
                 {
                     ("hold", ProjectStatusActions.Hold, "HoldProject"), ("resume", ProjectStatusActions.Resume, "ResumeProject"), ("cancel", ProjectStatusActions.Cancel, "CancelProject"),
                 })
        {
            projects.MapPost($"/{{id:guid}}/{route}", async (Guid id, ProjectReasonInput b, ProjectService s, CancellationToken ct) =>
                    (await s.ChangeStatusAsync(id, action, b.Reason, b.Version, ct)).ToOk())
                .WithName(name).RequirePermission(PortfolioPermissions.ProjectRead);
        }

        projects.MapGet("/{id:guid}/files", async (Guid id, ProjectService s, CancellationToken ct) => (await s.ListFilesAsync(id, ct)).ToOk())
            .WithName("ListProjectFiles").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/files/{kind}", async (Guid id, string kind, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? comment, ProjectService s,
                CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.UploadFileAsync(id, kind, file.FileName, stream, comment, ct)).ToOk();
            })
            .WithName("UploadProjectFile").RequirePermission(PortfolioPermissions.ProjectRead).DisableAntiforgery();
        projects.MapRecordHistory(Project.EntityType, PortfolioPermissions.ProjectRead, "GetPortfolioProject", async (id, context) =>
            (await context.RequestServices.GetRequiredService<ProjectService>().GetAsync(id, context.RequestAborted)).IsSuccess);

        MapItems(projects);
        MapMilestones(group, projects);
        MapChanges(group, projects);

        group.MapGet("/kpi-cards", async (string? search, IServiceProvider services, IRequestContext context, CancellationToken ct) =>
            {
                var catalog = services.GetService<IKpiCatalog>();
                var cards = catalog is null ? [] : await catalog.SearchAsync(search, context.User, 20, ct);
                return Result.Success<IReadOnlyList<PortfolioKpiCardDto>>(cards.Select(c => new PortfolioKpiCardDto(c.Id, c.Code, c.NameAr, c.NameEn, c.UnitCode, c.Direction, c.Link))
                    .ToList()).ToOk();
            })
            .WithName("SearchPortfolioKpiCards").RequirePermission(PortfolioPermissions.ProjectRead);
    }

    private static void MapItems(RouteGroupBuilder projects)
    {
        projects.MapGet("/{id:guid}/kpis", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.KpisAsync(id, ct)).ToOk())
            .WithName("ListProjectKpis").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/kpis", async (Guid id, KpiInput b, ProjectItems s, CancellationToken ct) => (await s.SaveKpiAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectKpi").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/kpis/{itemId:guid}", async (Guid id, Guid itemId, KpiInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveKpiAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectKpi").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/risks", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.RisksAsync(id, ct)).ToOk())
            .WithName("ListProjectRisks").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/risks", async (Guid id, RiskInput b, ProjectItems s, CancellationToken ct) => (await s.SaveRiskAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectRisk").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/risks/{itemId:guid}", async (Guid id, Guid itemId, RiskInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveRiskAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectRisk").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/stakeholders", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.StakeholdersAsync(id, ct)).ToOk())
            .WithName("ListProjectStakeholders").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/stakeholders", async (Guid id, StakeholderInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveStakeholderAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectStakeholder").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/stakeholders/{itemId:guid}", async (Guid id, Guid itemId, StakeholderInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveStakeholderAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectStakeholder").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/members", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.MembersAsync(id, ct)).ToOk())
            .WithName("ListProjectMembers").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/members", async (Guid id, ProposedMemberInput b, ProjectItems s, CancellationToken ct) => (await s.SaveMemberAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectMember").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/members/{itemId:guid}", async (Guid id, Guid itemId, ProposedMemberInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveMemberAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectMember").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/planning-items", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.PlanningItemsAsync(id, ct)).ToOk())
            .WithName("ListProjectPlanningItems").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/planning-items", async (Guid id, PlanningItemInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SavePlanningItemAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectPlanningItem").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/planning-items/{itemId:guid}", async (Guid id, Guid itemId, PlanningItemInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SavePlanningItemAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectPlanningItem").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/challenges", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.ChallengesAsync(id, ct)).ToOk())
            .WithName("ListProjectChallenges").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/challenges", async (Guid id, ChallengeInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveChallengeAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectChallenge").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/challenges/{itemId:guid}", async (Guid id, Guid itemId, ChallengeInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveChallengeAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectChallenge").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/lessons", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.LessonsAsync(id, ct)).ToOk())
            .WithName("ListProjectLessons").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/lessons", async (Guid id, LessonInput b, ProjectItems s, CancellationToken ct) => (await s.SaveLessonAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectLesson").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/lessons/{itemId:guid}", async (Guid id, Guid itemId, LessonInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveLessonAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectLesson").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/meetings", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.MeetingsAsync(id, ct)).ToOk())
            .WithName("ListProjectMeetings").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/meetings", async (Guid id, ProjectMeetingInput b, ProjectItems s, CancellationToken ct) => (await s.SaveMeetingAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectMeeting").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/meetings/{itemId:guid}", async (Guid id, Guid itemId, ProjectMeetingInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveMeetingAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectMeeting").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapGet("/{id:guid}/contracts", async (Guid id, ProjectItems s, CancellationToken ct) => (await s.ContractsAsync(id, ct)).ToOk())
            .WithName("ListProjectContracts").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/contracts", async (Guid id, ContractInput b, ProjectItems s, CancellationToken ct) => (await s.SaveContractAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectContract").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPut("/{id:guid}/contracts/{itemId:guid}", async (Guid id, Guid itemId, ContractInput b, ProjectItems s, CancellationToken ct) =>
                (await s.SaveContractAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectContract").RequirePermission(PortfolioPermissions.ProjectRead);

        projects.MapPost("/{id:guid}/{kind}/{itemId:guid}/deactivate",
                async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Microsoft.AspNetCore.Http.HttpResults.NoContent, Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>> (
                    Guid id, string kind, Guid itemId, DeactivateInput b, ProjectItems s, CancellationToken ct) =>
                {
                    var result = await s.DeactivateAsync(id, kind, itemId, b, ct);
                    return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
                })
            .WithName("DeactivateProjectItem").RequirePermission(PortfolioPermissions.ProjectRead);
    }

    private static void MapMilestones(RouteGroupBuilder group, RouteGroupBuilder projects)
    {
        projects.MapGet("/{id:guid}/milestones", async (Guid id, MilestoneService s, CancellationToken ct) => (await s.ListAsync(id, ct)).ToOk())
            .WithName("ListProjectMilestones").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/milestones", async (Guid id, MilestoneInput b, MilestoneService s, CancellationToken ct) => (await s.CreateAsync(id, b, ct)).ToOk())
            .WithName("CreateProjectMilestone").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapGet("/{id:guid}/monthly", async (Guid id, MilestoneService s, CancellationToken ct) => (await s.MonthlyAsync(id, ct)).ToOk())
            .WithName("GetProjectMonthly").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/monthly/open", async (Guid id, MilestoneService s, CancellationToken ct) => (await s.OpenNowAsync(id, ct)).ToOk())
            .WithName("OpenProjectMonthlyWindow").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/monthly/submit", async (Guid id, ProjectVersionInput b, MilestoneService s, CancellationToken ct) =>
                (await s.SubmitMonthlyAsync(id, b.Version, b.Comment, ct)).ToOk())
            .WithName("SubmitProjectMonthlyUpdate").RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();

        var milestones = group.MapGroup("/milestones");
        milestones.MapPut("/{mid:guid}", async (Guid mid, MilestoneInput b, MilestoneService s, CancellationToken ct) => (await s.UpdateAsync(mid, b, ct)).ToOk())
            .WithName("UpdateProjectMilestone").RequirePermission(PortfolioPermissions.ProjectRead);
        milestones.MapPost("/{mid:guid}/cancel", async (Guid mid, DeactivateInput b, MilestoneService s, CancellationToken ct) => (await s.CancelAsync(mid, b, ct)).ToOk())
            .WithName("CancelProjectMilestone").RequirePermission(PortfolioPermissions.ProjectRead);
        milestones.MapPost("/{mid:guid}/progress", async (Guid mid, MilestoneUpdateInput b, MilestoneService s, CancellationToken ct) =>
                (await s.RecordProgressAsync(mid, b, ct)).ToOk())
            .WithName("RecordMilestoneProgress").RequirePermission(PortfolioPermissions.ProjectRead);
        milestones.MapGet("/{mid:guid}/evidence", async (Guid mid, MilestoneService s, CancellationToken ct) => (await s.EvidenceAsync(mid, ct)).ToOk())
            .WithName("ListMilestoneEvidence").RequirePermission(PortfolioPermissions.ProjectRead);
        milestones.MapPost("/{mid:guid}/evidence", async (Guid mid, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? comment, MilestoneService s,
                CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.UploadEvidenceAsync(mid, file.FileName, stream, comment, ct)).ToOk();
            })
            .WithName("UploadMilestoneEvidence").RequirePermission(PortfolioPermissions.ProjectRead).DisableAntiforgery();
    }

    private static void MapChanges(RouteGroupBuilder group, RouteGroupBuilder projects)
    {
        projects.MapGet("/{id:guid}/change-requests", async (Guid id, ChangeRequestService s, CancellationToken ct) => (await s.ListAsync(id, ct)).ToOk())
            .WithName("ListProjectChangeRequests").RequirePermission(PortfolioPermissions.ProjectRead);
        projects.MapPost("/{id:guid}/change-requests", async (Guid id, ChangeRequestInput b, ChangeRequestService s, CancellationToken ct) =>
                (await s.CreateAsync(id, b, ct)).ToOk())
            .WithName("CreateProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead);

        var changes = group.MapGroup("/change-requests");
        changes.MapGet("/{cid:guid}", async (Guid cid, ChangeRequestService s, CancellationToken ct) => (await s.GetAsync(cid, ct)).ToOk())
            .WithName("GetProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead);
        changes.MapPut("/{cid:guid}", async (Guid cid, ChangeRequestInput b, ChangeRequestService s, CancellationToken ct) => (await s.UpdateAsync(cid, b, ct)).ToOk())
            .WithName("UpdateProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead);
        changes.MapPost("/{cid:guid}/submit", async (Guid cid, ProjectVersionInput b, ChangeRequestService s, CancellationToken ct) =>
                (await s.SubmitAsync(cid, b.Version, b.Comment, ct)).ToOk())
            .WithName("SubmitProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();
        changes.MapPost("/{cid:guid}/cancel", async (Guid cid, ProjectReasonInput b, ChangeRequestService s, CancellationToken ct) => (await s.CancelAsync(cid, b, ct)).ToOk())
            .WithName("CancelProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead);
        changes.MapPost("/{cid:guid}/apply", async (Guid cid, ChangeApplyInput b, ChangeRequestService s, CancellationToken ct) => (await s.ApplyAsync(cid, b, ct)).ToOk())
            .WithName("ApplyProjectChangeRequest").RequirePermission(PortfolioPermissions.ProjectRead).WithIdempotency();
    }
}
