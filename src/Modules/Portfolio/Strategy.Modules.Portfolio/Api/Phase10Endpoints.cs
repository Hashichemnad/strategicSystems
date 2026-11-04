using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;

namespace Strategy.Modules.Portfolio.Api;

public sealed record TemplateActiveInput(bool Active);

public sealed record PortfolioFeaturesDto(IReadOnlyDictionary<string, bool> Features, string AgileApprovalMode, int DefaultSprintLengthDays, bool TeamMembersSeeCosts,
    bool TeamMembersCreateStories);

/// <summary>
/// Phase 10 endpoints (docs/design/portfolio.md §43). Endpoints declare the minimum permission; services check the record; each standards
/// enhancement's endpoints carry its feature switch (404 <c>portfolio.errors.featureOff</c> when off, ADR 0092).
/// </summary>
internal static class Phase10Endpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        const string Read = PortfolioPermissions.ProjectRead;
        group.MapGet("/features", async (RequestReader reader, CancellationToken ct) =>
            {
                var s = await reader.SettingsAsync(ct).ConfigureAwait(false);
                return TypedResults.Ok(new PortfolioFeaturesDto(s.Features.ToMap(), s.AgileApprovalMode, s.DefaultSprintLengthDays, s.TeamMembersSeeCosts,
                    s.TeamMembersCreateStories));
            })
            .WithName("GetPortfolioFeatures").RequirePermission(Read);

        var projects = group.MapGroup("/projects");
        MapAgile(group, projects, Read);
        MapStandards(group, projects, Read);
        MapViews(group, projects, Read);
    }

    private static void MapAgile(RouteGroupBuilder group, RouteGroupBuilder projects, string read)
    {
        projects.MapGet("/{id:guid}/agile", async (Guid id, AgileService s, CancellationToken ct) => (await s.ViewAsync(id, ct)).ToOk())
            .WithName("GetProjectAgile").RequirePermission(read);
        projects.MapPut("/{id:guid}/methodology", async (Guid id, MethodologyInput b, AgileService s, CancellationToken ct) => (await s.SetMethodologyAsync(id, b, ct)).ToOk())
            .WithName("SetProjectMethodology").RequirePermission(read);
        projects.MapPut("/{id:guid}/board", async (Guid id, BoardInput b, AgileService s, CancellationToken ct) => (await s.SetBoardAsync(id, b, ct)).ToOk())
            .WithName("SetProjectBoard").RequirePermission(read);
        projects.MapPost("/{id:guid}/releases", async (Guid id, ReleaseInput b, AgileService s, CancellationToken ct) => (await s.SaveReleaseAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectRelease").RequirePermission(read);
        projects.MapPut("/{id:guid}/releases/{itemId:guid}", async (Guid id, Guid itemId, ReleaseInput b, AgileService s, CancellationToken ct) =>
                (await s.SaveReleaseAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectRelease").RequirePermission(read);
        projects.MapPost("/{id:guid}/epics", async (Guid id, EpicInput b, AgileService s, CancellationToken ct) => (await s.SaveEpicAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectEpic").RequirePermission(read);
        projects.MapPut("/{id:guid}/epics/{itemId:guid}", async (Guid id, Guid itemId, EpicInput b, AgileService s, CancellationToken ct) =>
                (await s.SaveEpicAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectEpic").RequirePermission(read);
        projects.MapPost("/{id:guid}/epics/{itemId:guid}/cancel", async (Guid id, Guid itemId, AgileService s, CancellationToken ct) =>
                (await s.CancelEpicAsync(id, itemId, ct)).ToOk())
            .WithName("CancelProjectEpic").RequirePermission(read);
        projects.MapPost("/{id:guid}/stories", async (Guid id, StoryInput b, AgileService s, CancellationToken ct) => (await s.SaveStoryAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectStory").RequirePermission(read);
        projects.MapPut("/{id:guid}/stories/{itemId:guid}", async (Guid id, Guid itemId, StoryInput b, AgileService s, CancellationToken ct) =>
                (await s.SaveStoryAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectStory").RequirePermission(read);
        projects.MapPost("/{id:guid}/sprints", async (Guid id, SprintInput b, AgileService s, CancellationToken ct) => (await s.SaveSprintAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectSprint").RequirePermission(read);
        projects.MapPut("/{id:guid}/sprints/{itemId:guid}", async (Guid id, Guid itemId, SprintInput b, AgileService s, CancellationToken ct) =>
                (await s.SaveSprintAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectSprint").RequirePermission(read);

        var stories = group.MapGroup("/stories");
        stories.MapPost("/{sid:guid}/move", async (Guid sid, StoryMoveInput b, AgileService s, CancellationToken ct) => (await s.MoveStoryAsync(sid, b, ct)).ToOk())
            .WithName("MoveProjectStory").RequirePermission(read);
        stories.MapPost("/{sid:guid}/cancel", async (Guid sid, AgileService s, CancellationToken ct) => (await s.CancelStoryAsync(sid, ct)).ToOk())
            .WithName("CancelProjectStory").RequirePermission(read);

        var sprints = group.MapGroup("/sprints");
        sprints.MapGet("/{sid:guid}/burndown", async (Guid sid, AgileService s, CancellationToken ct) => (await s.BurndownAsync(sid, ct)).ToOk())
            .WithName("GetSprintBurndown").RequirePermission(read);
        sprints.MapPost("/{sid:guid}/start", async (Guid sid, SprintActionInput b, AgileService s, CancellationToken ct) => (await s.StartSprintAsync(sid, b, ct)).ToOk())
            .WithName("StartProjectSprint").RequirePermission(read);
        sprints.MapPost("/{sid:guid}/close", async (Guid sid, SprintCloseInput b, AgileService s, CancellationToken ct) => (await s.CloseSprintAsync(sid, b, ct)).ToOk())
            .WithName("CloseProjectSprint").RequirePermission(read);
        sprints.MapPost("/{sid:guid}/submit", async (Guid sid, SprintActionInput b, AgileService s, CancellationToken ct) => (await s.SubmitSprintAsync(sid, b, ct)).ToOk())
            .WithName("SubmitProjectSprint").RequirePermission(read).WithIdempotency();
        sprints.MapPost("/{sid:guid}/cancel", async (Guid sid, SprintActionInput b, AgileService s, CancellationToken ct) => (await s.CancelSprintAsync(sid, b, ct)).ToOk())
            .WithName("CancelProjectSprint").RequirePermission(read);
        sprints.MapPost("/{sid:guid}/impediments", async (Guid sid, ImpedimentInput b, AgileService s, CancellationToken ct) => (await s.ImpedimentAsync(sid, b, ct)).ToOk())
            .WithName("RaiseSprintImpediment").RequirePermission(read);
    }

    private static void MapStandards(RouteGroupBuilder group, RouteGroupBuilder projects, string read)
    {
        projects.MapGet("/{id:guid}/health", async (Guid id, HealthService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToOk())
            .WithName("GetProjectHealth").RequirePermission(read).RequireFeature(FeatureCodes.Health);
        projects.MapPut("/{id:guid}/health/{dimension}", async (Guid id, string dimension, HealthOverrideInput b, HealthService s, CancellationToken ct) =>
                (await s.OverrideAsync(id, dimension, b, ct)).ToOk())
            .WithName("OverrideProjectHealth").RequirePermission(read).RequireFeature(FeatureCodes.Health);

        projects.MapGet("/{id:guid}/baselines", async (Guid id, BaselineService s, CancellationToken ct) => (await s.ListAsync(id, ct)).ToOk())
            .WithName("ListProjectBaselines").RequirePermission(read).RequireFeature(FeatureCodes.Baselines);
        projects.MapGet("/{id:guid}/baselines/variance", async (Guid id, int? version, BaselineService s, CancellationToken ct) => (await s.VarianceAsync(id, version, ct)).ToOk())
            .WithName("GetProjectBaselineVariance").RequirePermission(read).RequireFeature(FeatureCodes.Baselines);
        projects.MapGet("/{id:guid}/evm", async (Guid id, BaselineService s, CancellationToken ct) => (await s.EvmAsync(id, ct)).ToOk())
            .WithName("GetProjectEarnedValue").RequirePermission(read).RequireFeature(FeatureCodes.EarnedValue);

        projects.MapGet("/{id:guid}/raid", async (Guid id, RaidService s, CancellationToken ct) => (await s.ListAsync(id, ct)).ToOk())
            .WithName("GetProjectRaidLog").RequirePermission(read).RequireFeature(FeatureCodes.Raid);
        projects.MapPost("/{id:guid}/raid", async (Guid id, RaidInput b, RaidService s, CancellationToken ct) => (await s.SaveAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectRaidItem").RequirePermission(read).RequireFeature(FeatureCodes.Raid);
        projects.MapPut("/{id:guid}/raid/{itemId:guid}", async (Guid id, Guid itemId, RaidInput b, RaidService s, CancellationToken ct) =>
                (await s.SaveAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectRaidItem").RequirePermission(read).RequireFeature(FeatureCodes.Raid);
        projects.MapPost("/{id:guid}/raid/{itemId:guid}/accept", async (Guid id, Guid itemId, RaidService s, CancellationToken ct) => (await s.AcceptAsync(id, itemId, ct)).ToOk())
            .WithName("AcceptProjectRaidItem").RequirePermission(read).RequireFeature(FeatureCodes.Raid);

        projects.MapGet("/{id:guid}/risk-matrix", async (Guid id, ProjectExtrasService s, CancellationToken ct) => (await s.RiskMatrixAsync(id, ct)).ToOk())
            .WithName("GetProjectRiskMatrix").RequirePermission(read).RequireFeature(FeatureCodes.RiskMatrix);

        projects.MapGet("/{id:guid}/raci", async (Guid id, RaciService s, CancellationToken ct) => (await s.MatrixAsync(id, ct)).ToOk())
            .WithName("GetProjectRaci").RequirePermission(read).RequireFeature(FeatureCodes.Raci);
        projects.MapPost("/{id:guid}/raci", async (Guid id, RaciInput b, RaciService s, CancellationToken ct) => (await s.SaveAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectRaciEntry").RequirePermission(read).RequireFeature(FeatureCodes.Raci);
        projects.MapPut("/{id:guid}/raci/{itemId:guid}", async (Guid id, Guid itemId, RaciInput b, RaciService s, CancellationToken ct) => (await s.SaveAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectRaciEntry").RequirePermission(read).RequireFeature(FeatureCodes.Raci);
        projects.MapPost("/{id:guid}/raci/{itemId:guid}/remove", async (Guid id, Guid itemId, RaciService s, CancellationToken ct) => (await s.RemoveAsync(id, itemId, ct)).ToOk())
            .WithName("RemoveProjectRaciEntry").RequirePermission(read).RequireFeature(FeatureCodes.Raci);

        projects.MapGet("/{id:guid}/communications", async (Guid id, CommunicationService s, CancellationToken ct) => (await s.ListAsync(id, ct)).ToOk())
            .WithName("ListProjectCommunications").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);
        projects.MapPost("/{id:guid}/communications", async (Guid id, CommunicationInput b, CommunicationService s, CancellationToken ct) =>
                (await s.SaveAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectCommunication").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);
        projects.MapPut("/{id:guid}/communications/{itemId:guid}", async (Guid id, Guid itemId, CommunicationInput b, CommunicationService s, CancellationToken ct) =>
                (await s.SaveAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectCommunication").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);
        projects.MapPost("/{id:guid}/communications/generate", async (Guid id, CommunicationService s, CancellationToken ct) => (await s.GenerateAsync(id, ct)).ToOk())
            .WithName("GenerateProjectCommunications").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);
        projects.MapPost("/{id:guid}/communications/{itemId:guid}/done", async (Guid id, Guid itemId, CommunicationService s, CancellationToken ct) =>
                (await s.MarkDoneAsync(id, itemId, ct)).ToOk())
            .WithName("MarkProjectCommunicationDone").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);
        projects.MapPost("/{id:guid}/communications/{itemId:guid}/remove", async (Guid id, Guid itemId, CommunicationService s, CancellationToken ct) =>
                (await s.RemoveAsync(id, itemId, ct)).ToOk())
            .WithName("RemoveProjectCommunication").RequirePermission(read).RequireFeature(FeatureCodes.CommunicationPlan);

        projects.MapGet("/{id:guid}/benefits", async (Guid id, BenefitService s, CancellationToken ct) => (await s.ForProjectAsync(id, ct)).ToOk())
            .WithName("ListProjectBenefits").RequirePermission(read).RequireFeature(FeatureCodes.Benefits);
        projects.MapPost("/{id:guid}/benefits", async (Guid id, BenefitInput b, BenefitService s, CancellationToken ct) => (await s.SaveOnProjectAsync(id, null, b, ct)).ToOk())
            .WithName("CreateProjectBenefit").RequirePermission(read).RequireFeature(FeatureCodes.Benefits);
        projects.MapPut("/{id:guid}/benefits/{itemId:guid}", async (Guid id, Guid itemId, BenefitInput b, BenefitService s, CancellationToken ct) =>
                (await s.SaveOnProjectAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateProjectBenefit").RequirePermission(read).RequireFeature(FeatureCodes.Benefits);
        projects.MapPost("/{id:guid}/benefits/{itemId:guid}/measure", async (Guid id, Guid itemId, BenefitMeasureInput b, BenefitService s, CancellationToken ct) =>
                (await s.MeasureAsync(id, itemId, b, ct)).ToOk())
            .WithName("MeasureProjectBenefit").RequirePermission(read).RequireFeature(FeatureCodes.Benefits);
        projects.MapPost("/{id:guid}/benefits/{itemId:guid}/remove", async (Guid id, Guid itemId, BenefitService s, CancellationToken ct) =>
                (await s.RemoveAsync(null, id, itemId, ct)).ToOk())
            .WithName("RemoveProjectBenefit").RequirePermission(read).RequireFeature(FeatureCodes.Benefits);
        var requests = group.MapGroup("/requests");
        requests.MapGet("/{id:guid}/benefits", async (Guid id, BenefitService s, CancellationToken ct) => (await s.ForRequestAsync(id, ct)).ToOk())
            .WithName("ListRequestBenefits").RequirePermission(PortfolioPermissions.RequestRead).RequireFeature(FeatureCodes.Benefits);
        requests.MapPost("/{id:guid}/benefits", async (Guid id, BenefitInput b, BenefitService s, CancellationToken ct) => (await s.SaveOnRequestAsync(id, null, b, ct)).ToOk())
            .WithName("CreateRequestBenefit").RequirePermission(PortfolioPermissions.RequestRead).RequireFeature(FeatureCodes.Benefits);
        requests.MapPut("/{id:guid}/benefits/{itemId:guid}", async (Guid id, Guid itemId, BenefitInput b, BenefitService s, CancellationToken ct) =>
                (await s.SaveOnRequestAsync(id, itemId, b, ct)).ToOk())
            .WithName("UpdateRequestBenefit").RequirePermission(PortfolioPermissions.RequestRead).RequireFeature(FeatureCodes.Benefits);
        requests.MapPost("/{id:guid}/benefits/{itemId:guid}/remove", async (Guid id, Guid itemId, BenefitService s, CancellationToken ct) =>
                (await s.RemoveAsync(id, null, itemId, ct)).ToOk())
            .WithName("RemoveRequestBenefit").RequirePermission(PortfolioPermissions.RequestRead).RequireFeature(FeatureCodes.Benefits);

        group.MapPost("/milestones/{mid:guid}/sign-off", async (Guid mid, SignOffInput b, ProjectExtrasService s, CancellationToken ct) => (await s.SignOffAsync(mid, b, ct)).ToOk())
            .WithName("SignOffMilestoneQuality").RequirePermission(read).RequireFeature(FeatureCodes.Quality);

        projects.MapGet("/{id:guid}/contracts/{cid:guid}/extras", async (Guid id, Guid cid, ProjectExtrasService s, CancellationToken ct) => (await s.ContractAsync(id, cid, ct)).ToOk())
            .WithName("GetProjectContractExtras").RequirePermission(read).RequireFeature(FeatureCodes.Procurement);
        projects.MapPost("/{id:guid}/contracts/{cid:guid}/payments", async (Guid id, Guid cid, PaymentInput b, ProjectExtrasService s, CancellationToken ct) =>
                (await s.SavePaymentAsync(id, cid, null, b, ct)).ToOk())
            .WithName("CreateContractPayment").RequirePermission(read).RequireFeature(FeatureCodes.Procurement);
        projects.MapPut("/{id:guid}/contracts/{cid:guid}/payments/{pid:guid}", async (Guid id, Guid cid, Guid pid, PaymentInput b, ProjectExtrasService s, CancellationToken ct) =>
                (await s.SavePaymentAsync(id, cid, pid, b, ct)).ToOk())
            .WithName("UpdateContractPayment").RequirePermission(read).RequireFeature(FeatureCodes.Procurement);
        projects.MapPost("/{id:guid}/contracts/{cid:guid}/rating", async (Guid id, Guid cid, RatingInput b, ProjectExtrasService s, CancellationToken ct) =>
                (await s.RateAsync(id, cid, b, ct)).ToOk())
            .WithName("RateContractSupplier").RequirePermission(read).RequireFeature(FeatureCodes.Procurement);

        projects.MapGet("/{id:guid}/checklist", async (Guid id, ProjectExtrasService s, CancellationToken ct) => (await s.ChecklistAsync(id, ct)).ToOk())
            .WithName("GetProjectGateChecklist").RequirePermission(read).RequireFeature(FeatureCodes.GateChecklists);
        projects.MapPut("/{id:guid}/checklist/{gate}/{code}", async (Guid id, string gate, string code, ChecklistTickInput b, ProjectExtrasService s, CancellationToken ct) =>
                (await s.TickAsync(id, gate, code, b, ct)).ToOk())
            .WithName("TickProjectGateChecklist").RequirePermission(read).RequireFeature(FeatureCodes.GateChecklists);

        group.MapGet("/templates", async (bool? all, TemplateService s, CancellationToken ct) => (await s.ListAsync(all == true, ct)).ToOk())
            .WithName("ListProjectTemplates").RequirePermission(read).RequireFeature(FeatureCodes.Templates);
        group.MapPost("/templates", async (ProjectTemplateInput b, TemplateService s, CancellationToken ct) => (await s.SaveAsync(null, b, ct)).ToOk())
            .WithName("CreateProjectTemplate").RequirePermission(PortfolioPermissions.SettingsManage).RequireFeature(FeatureCodes.Templates);
        group.MapPut("/templates/{tid:guid}", async (Guid tid, ProjectTemplateInput b, TemplateService s, CancellationToken ct) => (await s.SaveAsync(tid, b, ct)).ToOk())
            .WithName("UpdateProjectTemplate").RequirePermission(PortfolioPermissions.SettingsManage).RequireFeature(FeatureCodes.Templates);
        group.MapPost("/templates/{tid:guid}/active", async (Guid tid, TemplateActiveInput b, TemplateService s, CancellationToken ct) =>
                (await s.SetActiveAsync(tid, b.Active, ct)).ToOk())
            .WithName("SetProjectTemplateActive").RequirePermission(PortfolioPermissions.SettingsManage).RequireFeature(FeatureCodes.Templates);
        projects.MapPost("/{id:guid}/apply-template", async (Guid id, ApplyTemplateInput b, TemplateService s, CancellationToken ct) => (await s.ApplyAsync(id, b, ct)).ToOk())
            .WithName("ApplyProjectTemplate").RequirePermission(read).RequireFeature(FeatureCodes.Templates);
        projects.MapPost("/{id:guid}/save-as-template", async (Guid id, SaveAsTemplateInput b, TemplateService s, CancellationToken ct) => (await s.SaveAsAsync(id, b, ct)).ToOk())
            .WithName("SaveProjectAsTemplate").RequirePermission(read).RequireFeature(FeatureCodes.Templates);

        group.MapGet("/groups", async (GroupService s, CancellationToken ct) => (await s.ListAsync(ct)).ToOk())
            .WithName("ListPortfolioGroups").RequirePermission(read).RequireFeature(FeatureCodes.Portfolios);
        group.MapPost("/groups", async (PortfolioGroupInput b, GroupService s, CancellationToken ct) => (await s.SaveAsync(null, b, ct)).ToOk())
            .WithName("CreatePortfolioGroup").RequirePermission(PortfolioPermissions.PortfolioManage).RequireFeature(FeatureCodes.Portfolios);
        group.MapPut("/groups/{gid:guid}", async (Guid gid, PortfolioGroupInput b, GroupService s, CancellationToken ct) => (await s.SaveAsync(gid, b, ct)).ToOk())
            .WithName("UpdatePortfolioGroup").RequirePermission(PortfolioPermissions.PortfolioManage).RequireFeature(FeatureCodes.Portfolios);
        projects.MapPut("/{id:guid}/grouping", async (Guid id, GroupingInput b, GroupService s, CancellationToken ct) => (await s.AssignAsync(id, b, ct)).ToOk())
            .WithName("SetProjectGrouping").RequirePermission(read).RequireFeature(FeatureCodes.Portfolios);

        projects.MapGet("/{id:guid}/status-report", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (
                Guid id, string? period, string? language, StatusReportService s, CancellationToken ct) =>
            {
                var result = await s.RenderAsync(id, period, language, ct).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, "application/pdf", result.Value.FileName) : result.Error!.ToProblem();
            })
            .WithName("GetProjectStatusReport").RequirePermission(read).RequireFeature(FeatureCodes.StatusReport);
        projects.MapPut("/{id:guid}/monthly/narrative", async (Guid id, NarrativeInput b, MilestoneService s, CancellationToken ct) => (await s.SaveNarrativeAsync(id, b, ct)).ToOk())
            .WithName("SaveProjectMonthlyNarrative").RequirePermission(read);
    }

    private static void MapViews(RouteGroupBuilder group, RouteGroupBuilder projects, string read)
    {
        projects.MapGet("/{id:guid}/gantt", async (Guid id, PortfolioViewService s, CancellationToken ct) => (await s.ProjectGanttAsync(id, ct)).ToOk())
            .WithName("GetProjectGantt").RequirePermission(read);
        group.MapGet("/gantt", async (Guid? portfolioGroupId, Guid? programmeId, Guid? unitId, PortfolioViewService s, CancellationToken ct) =>
                (await s.PortfolioGanttAsync(new DashboardFilter(portfolioGroupId, programmeId, unitId), ct)).ToOk())
            .WithName("GetPortfolioGantt").RequirePermission(read);
        group.MapGet("/dashboard", async (Guid? portfolioGroupId, Guid? programmeId, Guid? unitId, PortfolioViewService s, CancellationToken ct) =>
                (await s.DashboardAsync(new DashboardFilter(portfolioGroupId, programmeId, unitId), ct)).ToOk())
            .WithName("GetPortfolioDashboard").RequirePermission(read);

        const string Ai = PortfolioPermissions.AiAssist;
        projects.MapPost("/{id:guid}/ai/charter", async (Guid id, ProjectAssistService s, CancellationToken ct) => (await s.CharterAsync(id, ct)).ToOk())
            .WithName("SuggestProjectCharter").RequirePermission(Ai);
        projects.MapPost("/{id:guid}/ai/plan", async (Guid id, ProjectAssistService s, CancellationToken ct) => (await s.PlanAsync(id, ct)).ToOk())
            .WithName("DraftProjectPlan").RequirePermission(Ai);
        projects.MapPost("/{id:guid}/ai/update", async (Guid id, ProjectAssistService s, CancellationToken ct) => (await s.UpdateAsync(id, ct)).ToOk())
            .WithName("DraftProjectUpdate").RequirePermission(Ai);
        projects.MapPost("/{id:guid}/ai/status-report", async (Guid id, ProjectAssistService s, CancellationToken ct) => (await s.StatusNarrativeAsync(id, ct)).ToOk())
            .WithName("DraftProjectStatusNarrative").RequirePermission(Ai);
        group.MapPost("/change-requests/{cid:guid}/ai/impact", async (Guid cid, ProjectAssistService s, CancellationToken ct) => (await s.ChangeImpactAsync(cid, ct)).ToOk())
            .WithName("AnalyseChangeImpact").RequirePermission(Ai);
        group.MapPost("/ai/insights", async (Guid? portfolioGroupId, Guid? programmeId, Guid? unitId, ProjectAssistService s, CancellationToken ct) =>
                (await s.InsightsAsync(new DashboardFilter(portfolioGroupId, programmeId, unitId), ct)).ToOk())
            .WithName("DraftPortfolioInsights").RequirePermission(Ai);
        group.MapGet("/lessons/search", async (string? q, Guid? excludeProjectId, ProjectAssistService s, CancellationToken ct) =>
                (await s.SearchLessonsAsync(q, excludeProjectId, ct)).ToOk())
            .WithName("SearchPortfolioLessons").RequirePermission(read);
    }
}
