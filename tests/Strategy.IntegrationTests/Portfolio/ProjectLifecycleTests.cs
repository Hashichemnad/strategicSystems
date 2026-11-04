using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Teams.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>Phase 9 host: Portfolio with a travelling clock (monthly window tests move it; the others run on real time).</summary>
public sealed class ProjectFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<IClock>(Clock));

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    public async Task<(int Opened, int Reminded, int Overdue)> RunMonthlyCycleAsync()
    {
        await using var scope = TestAccess.SystemScope(Factory);
        return await scope.ServiceProvider.GetRequiredService<MonthlyCycleRunner>().RunAsync(CancellationToken.None);
    }
}

/// <summary>
/// The project lifecycle end to end (SPEC §1.5 W2–W8, docs/design/portfolio.md §15–§28): Take Ownership → charter gate → charter approval → LPO →
/// team formation in Teams → planning (weights 100 %) → monthly cycle with evidence / delay rules and the achievement lock → closure (team
/// evaluation opens) → Impact → Completed; Impact switched off; the initiative template; change requests; the monthly window job.
/// </summary>
public sealed class ProjectLifecycleTests(ProjectFixture fx) : IClassFixture<ProjectFixture>
{
    private const string Base = PortfolioWorld.Base;

    [Fact]
    public async Task A_project_runs_from_take_ownership_to_completed_with_every_approval_and_notification()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Lifecycle platform");
        project.Stage.ShouldBe(ProjectStages.Initiation);
        project.SubState.ShouldBe(SubStates.Ownership);
        project.StagePath.ShouldBe(ProjectStages.ProjectPath);

        // Any employee of the section (here: the request's unit) sees the unclaimed project and may take it; others cannot.
        (await w.C(w.OtherRequester).GetAsync($"{Base}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var asViewer = await Get(w, project.Id, w.DepartmentViewer);
        asViewer.Permissions.CanTakeOwnership.ShouldBeTrue();
        var status = await StatusAsync(w, project.Id, w.Requester);
        status.MessageKey.ShouldBe("portfolio.currentStatus.project.takeOwnership");
        status.NextActions.Select(a => a.Code).ShouldBe(["take-ownership"]);
        (await w.C(w.OtherRequester).PostAsJsonAsync($"{Base}/projects/{project.Id}/take-ownership", new { version = project.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/take-ownership", new { version = project.Version }));
        project.Pm!.Id.ShouldBe(w.Requester.EmployeeId!.Value);
        project.SubState.ShouldBe(SubStates.CharterDraft);
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Requester), PortfolioNotifications.OwnershipTaken.Key);
        (await w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/projects/{project.Id}/take-ownership", new { version = project.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "ownership is taken once");

        // Charter gate: submission is blocked until ≥ 1 KPI, risk, stakeholder, ≥ 3 members and the high-level plan at 100 %.
        var blocked = await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-charter",
            new { version = project.Version }));
        blocked.Select(f => f.Field).ShouldBe(["gate.kpis", "gate.risks", "gate.stakeholders", "gate.members", "gate.high-level-plan"], ignoreOrder: true);
        (await StatusAsync(w, project.Id, w.Requester)).MessageKey.ShouldBe("portfolio.currentStatus.project.fillMinimums");

        await FillCharterAsync(w, project.Id);
        var members = new[] { w.Requester, w.SectionRequester };
        foreach (var member in members)
        {
            await PortfolioWorld.OkAsync<ProposedMemberDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/members",
                new { values = new { employeeId = member.EmployeeId, roleEn = "Member" } }));
        }

        project = await Get(w, project.Id);
        project.Gates.CharterMandatoryDone.ShouldBeFalse();
        (await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-charter", new { version = project.Version })))
            .Single().Field.ShouldBe("gate.members", "two members are not three");
        await PortfolioWorld.OkAsync<ProposedMemberDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/members",
            new { values = new { employeeId = w.DepartmentViewer.EmployeeId, roleEn = "Analyst" } }));
        project = await Get(w, project.Id);
        project.Gates.CharterMandatoryDone.ShouldBeTrue();

        // Charter approval: director → PM Office (no section step: the project's unit is the department).
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-charter",
            new { version = project.Version, comment = "Charter ready" }));
        project.SubState.ShouldBe(SubStates.CharterApproval);
        project.Approval.Process.ShouldBe(ProjectApprovalProcesses.Charter);
        project.Approval.StepCode.ShouldBe(RequestApprovalProcess.DepartmentDirectorStep);
        project.Permissions.CanEditCharter.ShouldBeFalse("frozen while the charter is in approval");
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.SubState.ShouldBe(SubStates.Lpo);
        project.Approval.Result.ShouldBe(ApprovalInstanceStatuses.Approved);

        // Administration is told to upload the LPO and may do only that (record role procurement).
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Administration), PortfolioNotifications.CharterApproved.Key);
        var probe = await w.C(w.Administration).GetAsync($"{Base}/projects/{project.Id}");
        probe.StatusCode.ShouldBe(HttpStatusCode.OK, await probe.Content.ReadAsStringAsync());
        var asAdministration = await Get(w, project.Id, w.Administration);
        asAdministration.Permissions.CanLpo.ShouldBeTrue();
        asAdministration.Permissions.CanEdit.ShouldBeFalse();
        (await w.C(w.Administration).PutAsJsonAsync($"{Base}/projects/{project.Id}/charter", new { values = new { scopeOfWork = "x" }, version = project.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Administration).PostAsJsonAsync($"{Base}/projects/{project.Id}/lpo-uploaded", new { version = project.Version })))
            .Single().MessageKey.ShouldBe("portfolio.errors.lpoMissing");
        await UploadAsync(w.C(w.Administration), $"{Base}/projects/{project.Id}/files/lpo", "lpo.pdf");
        await PortfolioWorld.OkAsync<ContractDto>(w.C(w.Administration).PostAsJsonAsync($"{Base}/projects/{project.Id}/contracts",
            new { values = new { name = "Platform contract", number = "LPO-77", supplier = "Acme", issueDate = DateOnly.FromDateTime(DateTime.UtcNow), amount = 200000 } }));
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Administration).PostAsJsonAsync($"{Base}/projects/{project.Id}/lpo-uploaded", new { version = project.Version }));
        project.SubState.ShouldBe(SubStates.TeamFormation);
        project.LpoUploaded.ShouldBeTrue();
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.PmOffice), PortfolioNotifications.LpoUploaded.Key);

        // Team formation through Teams: request a project team linked to the project, director → GM approve, team Active → Planning.
        (await w.C(w.SectionRequester).GetAsync($"{Base}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "not a team member yet");
        var team = await FormTeamAsync(w, project.Id, members);
        project = await EventuallyAsync(() => Get(w, project.Id), p => p.SubState == SubStates.PlanningDraft, "the project to reach planning");
        project.Stage.ShouldBe(ProjectStages.Planning);
        project.TeamId.ShouldBe(team);
        var asMember = await EventuallyAsync(() => Get(w, project.Id, w.SectionRequester), p => p.Permissions.CanContribute, "team member access");
        asMember.Permissions.CanEdit.ShouldBeFalse();
        await PortfolioWorld.OkAsync<ChallengeDto>(w.C(w.SectionRequester).PostAsJsonAsync($"{Base}/projects/{project.Id}/challenges",
            new { values = new { challenge = "Data migration effort", impact = "Two weeks", achievedPct = 0.2 } }));
        (await w.C(w.SectionRequester).PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items", new { values = new { category = "in-scope", description = "x" } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "team members contribute; the PM plans");

        // Planning: scope item + detailed milestones (a phase with two milestones) whose weights must total exactly 100 %.
        await PortfolioWorld.OkAsync<PlanningItemDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items",
            new { values = new { category = "in-scope", description = "Online licensing service" } }));
        await PortfolioWorld.OkAsync<PlanningItemDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items",
            new { values = new { category = "assumption", description = "Payment gateway available" } }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var phase = await MilestoneAsync(w, project.Id, new { titleEn = "Build", isHeader = true, highLevel = false, weight = 0 });
        phase.Code.ShouldBe($"{project.Number}-2", "the high-level plan item is -1");
        var design = await MilestoneAsync(w, project.Id, new
        {
            titleEn = "Design",
            parentId = phase.Id,
            isHeader = false,
            highLevel = false,
            plannedStart = today.AddDays(-40),
            plannedEnd = today.AddDays(-1),
            weight = 0.6,
            isDeliverable = true,
            mainDeliverable = "Design document",
            acceptanceCriteria = "Signed off by the owner",
        });
        design.Code.ShouldBe($"{project.Number}-2.1");
        var gate = await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning",
            new { version = (await Get(w, project.Id)).Version }));
        gate.Single(f => f.Field == "gate.weights").Params!["total"].GetDecimal().ShouldBe(60m);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/milestones", new
        {
            values = new { titleEn = "Too much", parentId = phase.Id, isHeader = false, highLevel = false, plannedStart = today, plannedEnd = today.AddDays(30), weight = 0.5 },
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "weights may not exceed 100 %");
        var build = await MilestoneAsync(w, project.Id, new
        {
            titleEn = "Build and launch",
            parentId = phase.Id,
            isHeader = false,
            highLevel = false,
            plannedStart = today.AddDays(-10),
            plannedEnd = today.AddDays(60),
            weight = 0.4,
        });
        var plan = await PortfolioWorld.OkAsync<MilestonePlanDto>(w.C(w.Requester).GetAsync($"{Base}/projects/{project.Id}/milestones"));
        plan.DetailedWeight.ShouldBe(1m);
        plan.HighLevelWeight.ShouldBe(1m);
        plan.Milestones.Single(m => m.Id == phase.Id).Weight.ShouldBe(1m, "a phase carries the sum of its children");

        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning",
            new { version = (await Get(w, project.Id)).Version }));
        project.SubState.ShouldBe(SubStates.PlanningApproval);
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.Stage.ShouldBe(ProjectStages.Execution);
        project.ProgressStatus.ShouldBe(ProgressCalculator.Delayed, "Design ended yesterday below 100 %");
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Requester), PortfolioNotifications.StageChanged.Key);

        // Monthly cycle: no window yet; updates are refused until the PM Office opens it.
        (await StatusAsync(w, project.Id, w.Requester)).MessageKey.ShouldBe("portfolio.currentStatus.project.noWindow");
        design = (await PlanAsync(w, project.Id)).Single(m => m.Id == design.Id);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{design.Id}/progress", Progress(0.6m, design.Version, today.AddDays(-40), today)))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await w.C(w.Requester).PostAsync($"{Base}/projects/{project.Id}/monthly/open", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var monthly = await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{project.Id}/monthly/open", null));
        monthly.Current!.Status.ShouldBe(MonthlyStatuses.Open);
        monthly.Current.DueAt.ShouldNotBeNull();
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Requester), PortfolioNotifications.MonthlyOpened.Key);

        // 100 % needs the actual end date and evidence; a delayed milestone needs causality and corrective actions.
        var missing = await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{design.Id}/progress",
            Progress(0.6m, design.Version, today.AddDays(-40), null)));
        missing.Select(f => f.MessageKey).ShouldBe(["portfolio.errors.actualEndRequired", "portfolio.errors.evidenceRequired"], ignoreOrder: true);
        await UploadAsync(w.C(w.Requester), $"{Base}/milestones/{design.Id}/evidence", "signed-design.pdf");
        await PortfolioWorld.OkAsync<MilestoneProgressResult>(w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{design.Id}/progress",
            Progress(0.6m, design.Version, today.AddDays(-40), today)));
        var delayedBuild = (await PlanAsync(w, project.Id)).Single(m => m.Id == build.Id);
        await w.C(w.PmOffice).PutAsync($"{Base}/milestones/{build.Id}", null); // no-op probe: plans are frozen in execution
        (await w.C(w.Requester).PutAsJsonAsync($"{Base}/milestones/{build.Id}", new
        {
            values = new { titleEn = "Build and launch", parentId = phase.Id, isHeader = false, highLevel = false, plannedStart = today.AddDays(-10), plannedEnd = today.AddDays(-2), weight = 0.4 },
            version = delayedBuild.Version,
        })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "milestone dates change through a change request, not directly in execution");
        var achievement = await PortfolioWorld.OkAsync<MilestoneProgressResult>(w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{build.Id}/progress",
            Progress(0.2m, delayedBuild.Version, today.AddDays(-10), null)));
        achievement.Milestone.PendingAchieve.ShouldBe(0.2m);

        monthly = await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/monthly/submit",
            new { version = (await Get(w, project.Id)).Version, comment = "First month" }));
        monthly.Current!.Status.ShouldBe(MonthlyStatuses.Submitted);
        (await PlanAsync(w, project.Id)).Single(m => m.Id == design.Id).UpdateState.ShouldBe(UpdateStates.PendingApproval);
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.Performance.ShouldBe(0.8m);
        project.MonthlyApprovedAt.ShouldNotBeNull();
        var approved = await PlanAsync(w, project.Id);
        approved.Single(m => m.Id == design.Id).ShouldSatisfyAllConditions(
            m => m.MaxAchieved.ShouldBe(0.6m), m => m.Status.ShouldBe(MilestoneStatuses.Completed), m => m.ActualEnd.ShouldBe(today));
        approved.Single(m => m.Id == phase.Id).MaxAchieved.ShouldBe(0.8m, "the phase aggregates its children");

        // A second window in the same month: the approved achievement is locked (it cannot go down).
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{project.Id}/monthly/open", null));
        var locked = (await PlanAsync(w, project.Id)).Single(m => m.Id == build.Id);
        (await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{build.Id}/progress",
                Progress(0.1m, locked.Version, today.AddDays(-10), null))))
            .Select(f => f.MessageKey).ShouldContain("portfolio.errors.achieveBelowApproved");
        await UploadAsync(w.C(w.Requester), $"{Base}/milestones/{build.Id}/evidence", "launch.pdf");
        await PortfolioWorld.OkAsync<MilestoneProgressResult>(w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{build.Id}/progress",
            Progress(0.4m, locked.Version, today.AddDays(-10), today)));
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/monthly/submit",
            new { version = (await Get(w, project.Id)).Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.Performance.ShouldBe(1m);
        project.Stage.ShouldBe(ProjectStages.Closure, "total achievement 100 % → Closure");
        project.ProgressStatus.ShouldBe(ProgressCalculator.OnTrack);
        var history = await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).GetAsync($"{Base}/projects/{project.Id}/monthly"));
        history.History.Count.ShouldBe(2);
        history.History.ShouldAllBe(h => h.Status == MonthlyStatuses.Approved);
        history.History.Select(h => h.Sequence).ShouldBe([2, 1]);

        // Closure: summary, deliverables and a lesson; approval publishes the event that opens the project team's evaluation.
        (await PortfolioWorld.ProblemFieldsAsync(await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-closure", new { version = project.Version })))
            .Select(f => f.Field).ShouldBe(["summaryOfAchievement", "mainDeliverables", "gate.lessons"], ignoreOrder: true);
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{project.Id}/closure",
            new { summaryOfAchievement = "Licensing is online", mainDeliverables = "Portal, payments, tracking", version = project.Version }));
        await PortfolioWorld.OkAsync<LessonDto>(w.C(w.SectionRequester).PostAsJsonAsync($"{Base}/projects/{project.Id}/lessons",
            new { values = new { stage = "execution", lesson = "Book the payment gateway early", typeCode = "improvement", recommendation = "Start procurement in planning" } }));
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-closure",
            new { version = (await Get(w, project.Id)).Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.Stage.ShouldBe(ProjectStages.Impact);
        await EventuallyAsync(() => w.C(w.PmOffice).GetFromJsonAsync<TeamDto>($"/api/v1/teams/{team}")!, t => t!.Status == "pending_evaluation",
            "the project team's completion evaluation (ADR 0072)");

        // Impact → PM Office → Completed.
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{project.Id}/impact", new
        {
            costPerformance = 0.95,
            communitySatisfaction = 5,
            employeeSatisfaction = 4,
            vendorSatisfaction = 4,
            previousOperationalCost = 120000,
            impactNotes = "Queues gone",
            version = project.Version,
        }));
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-impact", new { version = project.Version }));
        project.Approval.StepCode.ShouldBe(RequestApprovalProcess.PmOfficeStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        project = await Get(w, project.Id);
        project.Stage.ShouldBe(ProjectStages.Completed);
        project.Status.ShouldBe(ProjectStatuses.Completed);
        (await StatusAsync(w, project.Id, w.Requester)).MessageKey.ShouldBe("portfolio.currentStatus.project.completed");
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/challenges", new { values = new { challenge = "late" } }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "a completed project is closed for changes");

        // Every approval of the record is on its history (audit) and the approval panel.
        var panel = (await w.C(w.Requester).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/portfolio.project/{project.Id}"))!;
        panel.Instances.Count.ShouldBe(6, "charter, planning, two monthly updates, closure, impact");
    }

    [Fact]
    public async Task Impact_switched_off_closure_approval_completes_the_project()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "No impact project");
        await DriveAsync(project.Id, w.Requester, SubStates.ClosureDraft, StageTemplate.ProjectDefault with { Impact = false });
        var current = await Get(w, project.Id);
        current.StagePath.ShouldNotContain(ProjectStages.Impact);
        current = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{project.Id}/closure",
            new { summaryOfAchievement = "Done", mainDeliverables = "Everything", version = current.Version }));
        await PortfolioWorld.OkAsync<LessonDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/lessons", new { values = new { lesson = "Keep it simple" } }));
        await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-closure",
            new { version = (await Get(w, project.Id)).Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        var done = await Get(w, project.Id);
        done.Stage.ShouldBe(ProjectStages.Completed);
        done.Status.ShouldBe(ProjectStatuses.Completed);
    }

    [Fact]
    public async Task Initiative_follows_its_lighter_template_and_the_pm_office_confirms_completion()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var request = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Quick initiative")));
        await w.ActAsync(w.Director, request.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, request.Id, RequestApprovalProcess.PmOfficeStep);
        var initiative = await Get(w, (await w.GetAsync(request.Id)).Project!.Id);
        initiative.Classification.ShouldBe(Classifications.Initiative);
        initiative.StagePath.ShouldBe([ProjectStages.Planning, ProjectStages.Execution, ProjectStages.Completed]);
        initiative.Template.ShouldBe(StageTemplate.InitiativeDefault);

        initiative = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/take-ownership",
            new { version = initiative.Version }));
        initiative.SubState.ShouldBe(SubStates.PlanningDraft, "no charter for an initiative (Q-P2)");
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/submit-charter", new { version = initiative.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await PortfolioWorld.OkAsync<PlanningItemDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/planning-items",
            new { values = new { category = "in-scope", description = "Quick win" } }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var only = await MilestoneAsync(w, initiative.Id, new
        {
            titleEn = "Deliver",
            isHeader = false,
            highLevel = false,
            plannedStart = today.AddDays(-5),
            plannedEnd = today.AddDays(20),
            weight = 1,
        });
        await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/submit-planning",
            new { version = (await Get(w, initiative.Id)).Version }));
        await ActAsync(w, w.Director, initiative.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, initiative.Id, RequestApprovalProcess.PmOfficeStep);
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{initiative.Id}/monthly/open", null));
        only = (await PlanAsync(w, initiative.Id)).Single(m => m.Id == only.Id);
        await UploadAsync(w.C(w.Requester), $"{Base}/milestones/{only.Id}/evidence", "done.pdf");
        await PortfolioWorld.OkAsync<MilestoneProgressResult>(w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{only.Id}/progress",
            Progress(1m, only.Version, today.AddDays(-5), today)));
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/monthly/submit",
            new { version = (await Get(w, initiative.Id)).Version }));
        await ActAsync(w, w.Director, initiative.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, initiative.Id, RequestApprovalProcess.PmOfficeStep);
        initiative = await Get(w, initiative.Id);
        initiative.SubState.ShouldBe(SubStates.Completion);
        (await StatusAsync(w, initiative.Id, w.PmOffice)).NextActions.Select(a => a.Code).ShouldBe(["confirm-completion"]);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/confirm-completion", new { version = initiative.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the PM Office confirms");
        initiative = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{initiative.Id}/confirm-completion",
            new { version = initiative.Version }));
        initiative.Status.ShouldBe(ProjectStatuses.Completed);
        initiative.Stage.ShouldBe(ProjectStages.Completed);
    }

    [Fact]
    public async Task Change_requests_apply_only_after_the_pm_confirms_and_ownership_moves_the_record_roles()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Change control project");
        await DriveAsync(project.Id, w.Requester, SubStates.Execution);
        var before = await Get(w, project.Id);

        // Ownership change: new PM = the department viewer (approval: director → PM Office; no Finance / GM step).
        var cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/change-requests", new
        {
            values = new { type = "ownership", areas = new[] { "ownership" }, reason = "PM moves to another programme", newPmEmployeeId = w.DepartmentViewer.EmployeeId },
        }));
        cr.Number.ShouldBe($"{before.Number}-CR-1");
        (await w.C(w.SectionRequester).PostAsJsonAsync($"{Base}/projects/{project.Id}/change-requests", new { values = new { type = "details", reason = "x" } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "outside the project");
        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/change-requests/{cr.Id}/submit", new { version = cr.Version }));
        cr.Status.ShouldBe(ChangeStatuses.UnderReview);
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Director), PortfolioNotifications.ChangeRequestSubmitted.Key);
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.Requester).GetAsync($"{Base}/change-requests/{cr.Id}"));
        cr.Status.ShouldBe(ChangeStatuses.Approved);
        cr.CanApply.ShouldBeTrue();

        // Approved is not applied: the PM is unchanged until "Apply changes" is confirmed.
        (await Get(w, project.Id)).Pm!.Id.ShouldBe(w.Requester.EmployeeId!.Value);
        (await Get(w, project.Id, w.DepartmentViewer)).Permissions.CanEdit.ShouldBeFalse();
        var work = (await w.C(w.Requester).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.MessageKey == "portfolio.myWork.applyChange" && i.ResourceId == project.Id);
        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/change-requests/{cr.Id}/apply",
            new { values = new { newPmEmployeeId = w.DepartmentViewer.EmployeeId }, version = cr.Version }));
        cr.Status.ShouldBe(ChangeStatuses.Completed);
        cr.Applied.Single().Field.ShouldBe("pm");
        var after = await Get(w, project.Id, w.DepartmentViewer);
        after.Pm!.Id.ShouldBe(w.DepartmentViewer.EmployeeId!.Value);
        after.Permissions.CanEdit.ShouldBeTrue("the new PM holds the pm record role");
        (await Get(w, project.Id, w.Requester)).Permissions.CanEdit.ShouldBeFalse("the old PM's record role was revoked");
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var holders = await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GetHoldersAsync(PortfolioPermissions.ProjectResource, project.Id);
            holders.Where(h => h.RoleCode == PortfolioPermissions.PmRole && h.ValidTo is null).Select(h => h.PrincipalId).ShouldBe([w.DepartmentViewer.EmployeeId!.Value]);
        }

        // A budget change above the GM threshold goes through Finance and the GM Office (Q-P4).
        var finance = await fx.Factory.CreateUserAsync(unitId: w.OrganisationId, roles: PortfolioPermissions.FinanceSectionRoleKey);
        var gm = await fx.Factory.CreateUserAsync(unitId: w.OrganisationId, roles: PortfolioPermissions.GmOfficeRoleKey);
        var budget = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/projects/{project.Id}/change-requests", new
        {
            values = new { type = "details", areas = new[] { "budget" }, reason = "Scope grew", proposedBudget = 500000, costAffected = true },
        }));
        await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/change-requests/{budget.Id}/submit", new { version = budget.Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        var financeClient = await fx.Factory.SignInAsync(finance);
        var gmClient = await fx.Factory.SignInAsync(gm);
        await ActOnProjectAsync(financeClient, project.Id, "finance-section");
        await ActOnProjectAsync(gmClient, project.Id, "gm-office");
        budget = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).GetAsync($"{Base}/change-requests/{budget.Id}"));
        budget.Status.ShouldBe(ChangeStatuses.Approved);
        (await Get(w, project.Id)).Budget.ShouldBe(before.Budget, "nothing applied before the PM confirms");
        budget = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/change-requests/{budget.Id}/apply",
            new { values = new { budget = 480000 }, version = budget.Version }));
        budget.Applied.Single(e => e.Field == "budget").ShouldSatisfyAllConditions(e => e.Old.ShouldBe("240000"), e => e.New.ShouldBe("480000"));
        (await Get(w, project.Id)).Budget.ShouldBe(480_000m, "the PM adjusted the value before confirming");

        // Cancellation: applied → the project is cancelled with the request's reason.
        var cancel = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/projects/{project.Id}/change-requests",
            new { values = new { type = "cancellation", reason = "Superseded by the federal platform" } }));
        await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/change-requests/{cancel.Id}/submit", new { version = cancel.Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        await ActOnProjectAsync(gmClient, project.Id, "gm-office");
        cancel = await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).GetAsync($"{Base}/change-requests/{cancel.Id}"));
        await PortfolioWorld.OkAsync<ChangeRequestDto>(w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/change-requests/{cancel.Id}/apply",
            new { values = new { }, version = cancel.Version }));
        var cancelled = await Get(w, project.Id);
        cancelled.Status.ShouldBe(ProjectStatuses.Cancelled);
        cancelled.CancelReason!.ShouldContain("Superseded");
    }

    [Fact]
    public async Task Hold_and_cancel_need_a_reason_and_the_pm_office()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Hold project");
        await DriveAsync(project.Id, w.Requester, SubStates.PlanningDraft);
        var current = await Get(w, project.Id);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/hold", new { reason = "Budget freeze", version = current.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/hold", new { reason = " ", version = current.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        current = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/hold",
            new { reason = "Budget freeze", version = current.Version }));
        current.Status.ShouldBe(ProjectStatuses.OnHold);
        (await StatusAsync(w, project.Id, w.Requester)).MessageKey.ShouldBe("portfolio.currentStatus.project.onHold");
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items", new { values = new { category = "in-scope", description = "x" } }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "on hold: read-only");
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Requester), PortfolioNotifications.ProjectOnHold.Key);
        current = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/resume", new { version = current.Version }));
        current.Status.ShouldBe(ProjectStatuses.Active);
        current.SubState.ShouldBe(SubStates.PlanningDraft);
        current = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/cancel",
            new { reason = "Merged elsewhere", version = current.Version }));
        current.Status.ShouldBe(ProjectStatuses.Cancelled);
        (await w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/resume", new { version = current.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "cancelled is final");
    }

    [Fact]
    public async Task A_pending_approval_can_be_recalled_with_a_reason_so_the_project_can_be_put_on_hold()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Recall project");
        await DriveAsync(project.Id, w.Requester, SubStates.PlanningDraft);
        await PortfolioWorld.OkAsync<PlanningItemDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items",
            new { values = new { category = "in-scope", description = "Scope" } }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var only = await MilestoneAsync(w, project.Id, new { titleEn = "All", isHeader = false, highLevel = false, plannedStart = today, plannedEnd = today.AddDays(60), weight = 1 });

        // Nothing pending: nothing to recall.
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/recall", new { reason = "x", version = (await Get(w, project.Id)).Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Planning in approval: hold is refused with the Recall hint; a viewer may not recall; the PM must give a reason.
        var submitted = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning",
            new { version = (await Get(w, project.Id)).Version }));
        submitted.Permissions.CanRecall.ShouldBeTrue();
        (await StatusAsync(w, project.Id, w.Requester)).NextActions.Select(a => a.Code).ShouldContain("recall");
        var refused = await w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/hold", new { reason = "Budget freeze", version = submitted.Version });
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("messageKey").GetString().ShouldBe("portfolio.errors.approvalInProgress");
        problem.GetProperty("params").GetProperty("action").GetString().ShouldBe("recall");
        (await Get(w, project.Id, w.DepartmentViewer)).Permissions.CanRecall.ShouldBeFalse();
        (await w.C(w.DepartmentViewer).PostAsJsonAsync($"{Base}/projects/{project.Id}/recall", new { reason = "x", version = submitted.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/recall", new { reason = " ", version = submitted.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var recalled = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/recall",
            new { reason = "Plan needs a second phase", version = submitted.Version }));
        recalled.SubState.ShouldBe(SubStates.PlanningDraft);
        recalled.Approval.Result.ShouldBe(ProjectApprovalResults.Recalled);
        var panel = (await w.C(w.Director).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/portfolio.project/{project.Id}"))!;
        panel.Instances.ShouldNotContain(i => i.Status == ApprovalInstanceStatuses.InProgress, "the approvers' task is gone");

        // Monthly update recalled by the PM Office: the window and milestones go back for correction.
        await DriveAsync(project.Id, w.Requester, SubStates.Execution);
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{project.Id}/monthly/open", null));
        only = (await PlanAsync(w, project.Id)).Single(m => m.Id == only.Id);
        await PortfolioWorld.OkAsync<MilestoneProgressResult>(w.C(w.Requester).PostAsJsonAsync($"{Base}/milestones/{only.Id}/progress",
            Progress(0.3m, only.Version, today, null)));
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/monthly/submit",
            new { version = (await Get(w, project.Id)).Version }));
        await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/recall",
            new { reason = "Hold the programme", version = (await Get(w, project.Id)).Version }));
        var monthly = await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.Requester).GetAsync($"{Base}/projects/{project.Id}/monthly"));
        monthly.Current!.Status.ShouldBe(MonthlyStatuses.Returned);
        (await PlanAsync(w, project.Id)).Single(m => m.Id == only.Id).UpdateState.ShouldBe(UpdateStates.Returned);

        // Change request recalled by the person who raised it (a co-owner: raises changes, does not edit), then the project is put on hold.
        await fx.Factory.GrantRecordRoleAsync(PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, w.DepartmentViewer.EmployeeId!.Value,
            PortfolioPermissions.OwnerRole);
        var ownerClient = w.C(w.DepartmentViewer);
        var cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(ownerClient.PostAsJsonAsync($"{Base}/projects/{project.Id}/change-requests",
            new { values = new { type = "postponement", reason = "Vendor delay", proposedEnd = today.AddMonths(6) } }));
        await PortfolioWorld.OkAsync<ChangeRequestDto>(ownerClient.PostAsJsonAsync($"{Base}/change-requests/{cr.Id}/submit", new { version = cr.Version }));
        var asOwner = await Get(w, project.Id, w.DepartmentViewer);
        asOwner.Permissions.CanEdit.ShouldBeFalse();
        asOwner.Permissions.CanRecall.ShouldBeTrue("the person who raised the change request");
        await PortfolioWorld.OkAsync<ProjectFullDto>(ownerClient.PostAsJsonAsync($"{Base}/projects/{project.Id}/recall",
            new { reason = "Wrong dates", version = (await Get(w, project.Id)).Version }));
        (await PortfolioWorld.OkAsync<ChangeRequestDto>(ownerClient.GetAsync($"{Base}/change-requests/{cr.Id}"))).Status.ShouldBe(ChangeStatuses.Draft);
        var held = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/projects/{project.Id}/hold",
            new { reason = "Budget freeze", version = (await Get(w, project.Id)).Version }));
        held.Status.ShouldBe(ProjectStatuses.OnHold);
    }

    [Fact]
    public async Task Monthly_window_opens_on_the_first_working_moment_of_the_month_with_reminder_and_overdue()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Window project");
        await DriveAsync(project.Id, w.Requester, SubStates.Execution);
        var dubai = TimeSpan.FromHours(4);
        try
        {
            // 1 May 2027 is a Saturday (default business hours Monday–Friday 07:30–15:30, Asia/Dubai): nothing opens before Monday 07:30.
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 1, 9, 0, 0, dubai);
            await fx.RunMonthlyCycleAsync();
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 3, 7, 0, 0, dubai);
            (await fx.RunMonthlyCycleAsync()).Opened.ShouldBe(0);
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().MonthlyUpdates.AnyAsync(m => m.ProjectId == project.Id)).ShouldBeFalse();
            }

            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 3, 7, 45, 0, dubai);
            await fx.RunMonthlyCycleAsync();
            await fx.RunMonthlyCycleAsync(); // idempotent
            MonthlyUpdate window;
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                window = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().MonthlyUpdates.AsNoTracking().SingleAsync(m => m.ProjectId == project.Id);
            }

            window.Period.ShouldBe("2027-05");
            window.OpenedBy.ShouldBe("job");
            window.OpenedAt.ShouldBe(new DateTimeOffset(2027, 5, 3, 7, 30, 0, dubai), "the first working moment on or after the 1st");
            window.DueAt.ShouldBe(new DateTimeOffset(2027, 5, 14, 15, 30, 0, dubai), "end of the 10th working day");
            var pm = await fx.Factory.SignInAsync(w.Requester); // the session must be valid at the travelled time
            await ApprovalFlowTests.WaitForNotificationAsync(pm, PortfolioNotifications.MonthlyOpened.Key);

            // Reminder three working days before due (Tuesday 11 May), overdue after the due moment.
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 10, 12, 0, 0, dubai);
            (await fx.RunMonthlyCycleAsync()).Reminded.ShouldBe(0);
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 11, 8, 0, 0, dubai);
            (await fx.RunMonthlyCycleAsync()).Reminded.ShouldBeGreaterThanOrEqualTo(1);
            await ApprovalFlowTests.WaitForNotificationAsync(await fx.Factory.SignInAsync(w.Requester), PortfolioNotifications.MonthlyReminder.Key);
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 14, 16, 0, 0, dubai);
            (await fx.RunMonthlyCycleAsync()).Overdue.ShouldBeGreaterThanOrEqualTo(1);
            pm = await fx.Factory.SignInAsync(w.Requester);
            await ApprovalFlowTests.WaitForNotificationAsync(pm, PortfolioNotifications.MonthlyOverdue.Key);
            var work = (await pm.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
            work.ActionItems.ShouldContain(i => i.ResourceId == project.Id && i.MessageKey == "portfolio.myWork.monthlyDue");

            // An open window blocks the next month's (no pile-up).
            fx.Clock.Frozen = new DateTimeOffset(2027, 6, 1, 9, 0, 0, dubai);
            (await fx.RunMonthlyCycleAsync()).Opened.ShouldBe(0);
        }
        finally
        {
            fx.Clock.Frozen = null;
        }
    }

    [Fact]
    public async Task Strategic_objectives_come_from_the_kpi_strategy_hierarchy_and_the_old_lookup_is_migrated()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var options = (await w.C(w.Requester).GetFromJsonAsync<PortfolioOptionsDto>($"{Base}/options"))!;
        options.Objectives.Select(o => o.Code).ShouldContain(PortfolioWorld.Objective);

        // Objectives added in Settings → KPI are immediately valid for requests; unknown codes are refused.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await scope.ServiceProvider.GetRequiredService<IKpiStrategyCatalog>().EnsureObjectivesAsync([new($"kpi-only-{w.Suffix}", "هدف", "KPI objective", true, 99)]);
        }

        await w.CreateAsync(PortfolioWorld.Valid("Uses a KPI objective") with { StrategicObjectiveCode = $"kpi-only-{w.Suffix}" });
        var refused = await w.C(w.Requester).PostAsJsonAsync($"{Base}/requests", new { values = PortfolioWorld.Valid("Unknown") with { StrategicObjectiveCode = "not-there" } });
        (await PortfolioWorld.ProblemFieldsAsync(refused)).ShouldContain(f => f.Field == "strategicObjectiveCode");

        // Phase 8 lookup values are copied into the hierarchy by the seeder (codes kept, existing elements untouched).
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var settings = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Settings.Infrastructure.SettingsDbContext>();
            if (!await settings.LookupTypes.AnyAsync(t => t.Code == ObjectivesToKpiSeeder.LegacyLookupType))
            {
                // An installation upgraded from Phase 8 still has the type row (Portfolio no longer registers it).
                settings.LookupTypes.Add(Strategy.Platform.Settings.Domain.LookupType.Create(ObjectivesToKpiSeeder.LegacyLookupType, PortfolioPermissions.SystemCode,
                    "الأهداف الاستراتيجية", "Strategic objectives", null, builtin: true));
                await settings.SaveChangesAsync();
            }

            settings.LookupValues.Add(Strategy.Platform.Settings.Domain.LookupValue.Create(ObjectivesToKpiSeeder.LegacyLookupType, $"legacy-{w.Suffix}", "قديم", "Legacy objective",
                5, null, null));
            await settings.SaveChangesAsync();
        }

        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            scope.ServiceProvider.GetRequiredService<Strategy.Platform.Settings.Application.SettingsCache>().Invalidate();
            var seeder = scope.ServiceProvider.GetServices<Strategy.Platform.Persistence.IDatabaseSeeder>().OfType<ObjectivesToKpiSeeder>().Single();
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None); // idempotent
            var objectives = await scope.ServiceProvider.GetRequiredService<IKpiStrategyCatalog>().ListObjectivesAsync(includeInactive: true);
            objectives.Count(o => o.Code == $"legacy-{w.Suffix}").ShouldBe(1);
            objectives.Single(o => o.Code == $"legacy-{w.Suffix}").NameEn.ShouldBe("Legacy objective");
        }
    }

    [Fact]
    public async Task Project_kpis_link_to_kpi_cards_and_risks_are_scored_and_listed_for_the_risk_module()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Linked KPI project");
        await DriveAsync(project.Id, w.Requester, SubStates.CharterDraft);

        // A KPI card the PM may read (responsible person = the PM): the Existing KPI picker finds it; the card lists the project.
        Guid cardId;
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await AccessGrants.GrantSystemAccessAsync(scope.ServiceProvider, "kpi", AccessGrants.Everyone, null);
        }

        var pmo = await fx.Factory.CreateUserAsync(withMfa: true, unitId: w.OrganisationId, roles: "kpi.strategy-office");
        var pmoClient = await fx.Factory.SignInAsync(pmo);
        var card = await pmoClient.PostAsJsonAsync("/api/v1/kpi/cards", new
        {
            values = Strategy.IntegrationTests.Kpi.KpiWorld.Card($"Licences issued online {w.Suffix}", w.Requester, w.DepartmentId),
        });
        card.StatusCode.ShouldBe(HttpStatusCode.Created, await card.Content.ReadAsStringAsync());
        cardId = (await card.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        var found = (await w.C(w.Requester).GetFromJsonAsync<List<Strategy.Modules.Portfolio.Api.PortfolioKpiCardDto>>($"{Base}/kpi-cards?search={w.Suffix}"))!;
        found.Select(c => c.Id).ShouldContain(cardId);
        (await w.C(w.OtherRequester).GetFromJsonAsync<List<Strategy.Modules.Portfolio.Api.PortfolioKpiCardDto>>($"{Base}/kpi-cards?search={w.Suffix}"))!
            .ShouldBeEmpty("the catalog is scope-filtered");
        var existing = await PortfolioWorld.OkAsync<ProjectKpiDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/kpis",
            new { values = new { nature = "existing", kpiCardId = cardId, projectGoal = "More online licences" } }));
        existing.KpiCardCode.ShouldNotBeNull();
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/kpis", new { values = new { nature = "existing", kpiCardId = Guid.CreateVersion7() } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fresh = await PortfolioWorld.OkAsync<ProjectKpiDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/kpis", new
        {
            values = new { nature = "new", nameEn = "Processing days", direction = "decrease", target = 5, currentResult = 4, whenMeasured = "after", targetYear = 2028 },
        }));
        fresh.CappedPerformance.ShouldBe(1m);
        fresh.GoalStatus.ShouldBe(RagCodes.Green, "5 ÷ 4 = 125 % capped at 100 %");
        var linked = (await w.C(w.Requester).GetFromJsonAsync<List<LinkView>>($"/api/v1/kpi/cards/{cardId}/linked"))!;
        linked.ShouldContain(l => l.Id == project.Id && l.Visibility == LinkVisibility.Visible && l.Number == project.Number, "the PM reads both the card and the project");
        (await pmoClient.GetFromJsonAsync<List<LinkView>>($"/api/v1/kpi/cards/{cardId}/linked"))!
            .ShouldContain(l => l.Id == project.Id && l.Visibility == LinkVisibility.Restricted, "the Strategy Office does not read this project: badge withheld");

        // Risks: 5 × 5 scoring with bands, stored; the Risk module reads them through the contract (project read scope).
        var risk = await PortfolioWorld.OkAsync<ProjectRiskDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/risks", new
        {
            values = new { title = "Vendor delay", categoryCode = "vendor", probability = 4, impact = 5, controlEvaluation = 6, response = "mitigate", mitigationActions = "Penalties" },
        }));
        risk.InherentScore.ShouldBe(20);
        risk.InherentBand.ShouldBe(RiskBands.Critical);
        risk.ResidualScore.ShouldBe(10m);
        risk.ResidualBand.ShouldBe(RiskBands.High);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/risks", new { values = new { title = "Bad", probability = 6, impact = 1 } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var directory = scope.ServiceProvider.GetRequiredService<IProjectRiskDirectory>();
            var pmView = await directory.ListAsync(await PrincipalAsync(scope.ServiceProvider, w.Requester), project.Id);
            pmView.Single().ResidualBand.ShouldBe(RiskBands.High);
            (await directory.ListAsync(await PrincipalAsync(scope.ServiceProvider, w.OtherRequester), project.Id)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Access_outsiders_get_404_and_mcp_tools_follow_the_rules()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ApprovedProjectAsync(w, "Access project");
        await DriveAsync(project.Id, w.Requester, SubStates.CharterDraft);
        foreach (var path in new[] { "", "/kpis", "/risks", "/milestones", "/monthly", "/change-requests", "/files", "/audit" })
        {
            (await w.C(w.Outsider).GetAsync($"{Base}/projects/{project.Id}{path}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            (await w.C(w.OtherRequester).GetAsync($"{Base}/projects/{project.Id}{path}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        (await Get(w, project.Id, w.DepartmentViewer)).Permissions.CanEdit.ShouldBeFalse();
        (await w.C(w.DepartmentViewer).PutAsJsonAsync($"{Base}/projects/{project.Id}/charter", new { values = new { scopeOfWork = "x" }, version = project.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "visible but not editable → 403");
        var edited = await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{project.Id}/charter", new
        {
            values = new { scopeOfWork = "Licensing online", strategicObjectiveCode = PortfolioWorld.Objective, operationalThemes = new[] { "innovative" }, resourcesHuman = "3 FTE" },
            version = (await Get(w, project.Id)).Version,
        }));
        edited.OperationalThemes.ShouldBe(["innovative"]);
        (await w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{project.Id}/charter", new { values = new { scopeOfWork = "stale" }, version = project.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The project appears on the project manager's My Work; history records the charter edit.
        var work = (await w.C(w.Requester).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == project.Id && i.MessageKey == "portfolio.myWork.charter");
        (await w.C(w.Requester).GetAsync($"{Base}/projects/{project.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = TestAccess.SystemScope(fx.Factory);
        var tools = scope.ServiceProvider.GetServices<IAppTool>().Where(t => t.Definition.SystemCode == PortfolioPermissions.SystemCode).ToList();
        tools.Single(t => t.Definition.Name == "portfolio_update_milestone_draft").Definition.ShouldSatisfyAllConditions(
            d => d.Kind.ShouldBe(ToolKind.Write), d => d.DefaultEnabled.ShouldBeFalse());
        tools.Single(t => t.Definition.Name == "portfolio_my_projects").Definition.DefaultEnabled.ShouldBeTrue();
        tools.ShouldNotContain(t => t.Definition.Kind == ToolKind.Approve);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    private static async Task<ProjectFullDto> ApprovedProjectAsync(PortfolioWorld w, string name)
    {
        var request = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid(name, budget: 240_000m)));
        await w.ActAsync(w.Director, request.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, request.Id, RequestApprovalProcess.PmOfficeStep);
        await w.ActAsync(w.DgOffice, request.Id, RequestApprovalProcess.DgOfficeStep);
        await w.ActAsync(w.Budget, request.Id, RequestApprovalProcess.BudgetStep);
        var approved = await w.GetAsync(request.Id);
        approved.Status.ShouldBe(RequestStatuses.Approved);
        var project = await Get(w, approved.Project!.Id);
        project.Classification.ShouldBe(Classifications.Project);
        return project;
    }

    /// <summary>Test set-up only: moves a project to <paramref name="target"/> through the domain transitions (gates are tested elsewhere).</summary>
    private async Task DriveAsync(Guid projectId, TestUser pm, string target, StageTemplate? template = null)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var project = await db.Projects.SingleAsync(p => p.Id == projectId);
        var t = template ?? StageTemplate.ProjectDefault;
        project.UseTemplate(t);
        project.SetPm(pm.EmployeeId!.Value, DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, projectId, RecordPrincipalKinds.Employee,
            pm.EmployeeId!.Value, PortfolioPermissions.PmRole);
        foreach (var action in new[]
                 {
                     ProjectActions.TakeOwnership, ProjectActions.SubmitCharter, ProjectActions.CharterApproved, ProjectActions.LpoUploaded, ProjectActions.TeamFormed,
                     ProjectActions.SubmitPlanning, ProjectActions.PlanningApproved, ProjectActions.AchievementComplete,
                 })
        {
            if (project.SubState == target)
            {
                break;
            }

            if (ProjectLifecycle.Allows(project.SubState, action))
            {
                project.Apply(action, t, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue($"{project.SubState} × {action}");
            }
        }

        project.SubState.ShouldBe(target);
        await db.SaveChangesAsync();
    }

    private static async Task FillCharterAsync(PortfolioWorld w, Guid projectId)
    {
        var c = w.C(w.Requester);
        await PortfolioWorld.OkAsync<ProjectKpiDto>(c.PostAsJsonAsync($"{Base}/projects/{projectId}/kpis",
            new { values = new { nature = "new", nameEn = "Online share", direction = "increase", target = 80, whenMeasured = "after", indexClassCode = "outcome" } }));
        await PortfolioWorld.OkAsync<ProjectRiskDto>(c.PostAsJsonAsync($"{Base}/projects/{projectId}/risks",
            new { values = new { title = "Vendor delay", probability = 3, impact = 4, response = "mitigate" } }));
        await PortfolioWorld.OkAsync<StakeholderDto>(c.PostAsJsonAsync($"{Base}/projects/{projectId}/stakeholders",
            new { values = new { name = "Customers", classificationCode = "customer", interestLevel = "high", impactLevel = "medium", email = "c@x.example" } }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await MilestoneAsync(w, projectId, new { titleEn = "Whole delivery (high level)", isHeader = false, highLevel = true, plannedStart = today, plannedEnd = today.AddMonths(6), weight = 1 });
    }

    private static async Task<MilestoneDto> MilestoneAsync(PortfolioWorld w, Guid projectId, object values) =>
        await PortfolioWorld.OkAsync<MilestoneDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{projectId}/milestones", new { values }));

    private static async Task<IReadOnlyList<MilestoneDto>> PlanAsync(PortfolioWorld w, Guid projectId) =>
        (await PortfolioWorld.OkAsync<MilestonePlanDto>(w.C(w.Requester).GetAsync($"{Base}/projects/{projectId}/milestones"))).Milestones;

    private static object Progress(decimal achieve, uint version, DateOnly? start, DateOnly? end, string? causality = "Vendor late", string? corrective = "Extra shift") =>
        new { values = new { achieve, actualStart = start, actualEnd = end, causality, correctiveActions = corrective }, version };

    private static async Task<ProjectFullDto> Get(PortfolioWorld w, Guid id, TestUser? user = null) =>
        await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(user ?? w.PmOffice).GetAsync($"{Base}/projects/{id}"));

    private static async Task<CurrentStatus> StatusAsync(PortfolioWorld w, Guid id, TestUser user) =>
        await PortfolioWorld.OkAsync<CurrentStatus>(w.C(user).GetAsync($"/api/v1/workflow/status/{PortfolioPermissions.ProjectResource}/{id}"));

    private static async Task ActAsync(PortfolioWorld w, TestUser approver, Guid projectId, string expectedStep) =>
        await ActOnProjectAsync(w.C(approver), projectId, expectedStep);

    private static async Task ActOnProjectAsync(HttpClient client, Guid projectId, string expectedStep)
    {
        var panel = await PortfolioWorld.OkAsync<SubjectApprovalsDto>(client.GetAsync($"/api/v1/workflow/subjects/{PortfolioPermissions.ProjectResource}/{projectId}"));
        var instance = panel.Instances.First(i => i.Status == ApprovalInstanceStatuses.InProgress);
        instance.Steps.Single(s => s.State == StepStates.Current).Code.ShouldBe(expectedStep);
        instance.MyTask.ShouldNotBeNull($"the caller should act on {expectedStep}");
        await ApprovalWorld.ActOkAsync(client, instance.MyTask.TaskId, "approve", "ok");
    }

    /// <summary>Request → submit → director → GM: an Active project team linked to the project.</summary>
    private static async Task<Guid> FormTeamAsync(PortfolioWorld w, Guid projectId, IReadOnlyList<TestUser> members)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = await w.C(w.PmOffice).PostAsJsonAsync("/api/v1/teams", new
        {
            typeCode = "project-team",
            nameAr = "فريق المشروع",
            nameEn = "Project team",
            permanent = false,
            subject = "Delivery",
            objectives = "Deliver the project",
            mandate = "Execute the plan",
            owningUnitId = w.DepartmentId,
            sponsorEmployeeId = w.Director.EmployeeId,
            memoMode = "generate",
            startDate = today,
            endDate = today.AddMonths(8),
            workload = "high",
            linkedType = PortfolioPermissions.ProjectResource,
            linkedId = projectId,
            members = members.Select((m, i) => new { employeeId = m.EmployeeId, roleCode = i == 0 ? "president" : "member", responsibilities = "Delivery" }).ToList(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var team = (await created.Content.ReadFromJsonAsync<TeamDto>())!;
        team = await PortfolioWorld.OkAsync<TeamDto>(w.C(w.PmOffice).PostAsJsonAsync($"/api/v1/teams/{team.Id}/submit", new { version = team.Version }));
        foreach (var approver in new[] { w.Director, w.DgOffice })
        {
            var panel = await PortfolioWorld.OkAsync<SubjectApprovalsDto>(w.C(approver).GetAsync($"/api/v1/workflow/subjects/teams.team/{team.Id}"));
            var task = panel.Instances.Select(i => i.MyTask).First(t => t is not null)!;
            await ApprovalWorld.ActOkAsync(w.C(approver), task.TaskId, "approve");
        }

        return team.Id;
    }

    private static async Task UploadAsync(HttpClient client, string url, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        var response = await client.PostAsync(url, form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done, string what)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(250);
        }

        throw new ShouldAssertException($"Timed out waiting for {what}.");
    }

    private static Task<System.Security.Claims.ClaimsPrincipal> PrincipalAsync(IServiceProvider services, TestUser user) =>
        Task.FromResult(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new System.Security.Claims.Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
            new System.Security.Claims.Claim(StrategyClaims.EmployeeId, user.EmployeeId!.Value.ToString()),
        ], "test")));
}
