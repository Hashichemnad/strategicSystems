using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Notifications;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>Program requests end to end (docs/design/portfolio.md §14) on a real database.</summary>
public sealed class PortfolioApiTests(PortfolioFixture fx) : IClassFixture<PortfolioFixture>
{
    private const string Base = PortfolioWorld.Base;

    [Fact]
    public async Task The_three_approval_routes_follow_budget_and_threshold_and_final_approval_creates_the_project_or_initiative()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);

        // 1. No budget: Department Director → PM Office → approved. Section manager skipped (requester sits in the department, Q-P4).
        var noBudget = await w.SubmitAsync(await w.CreateAsync(PortfolioWorld.Valid("Open data portal")));
        noBudget.Status.ShouldBe(RequestStatuses.InApproval);
        noBudget.ApprovalStepCode.ShouldBe(RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.Director, noBudget.Id, RequestApprovalProcess.DepartmentDirectorStep);
        (await w.GetAsync(noBudget.Id)).ApprovalStepNameEn.ShouldBe("PM Office");
        await w.ActAsync(w.PmOffice, noBudget.Id, RequestApprovalProcess.PmOfficeStep);
        var approved = await w.GetAsync(noBudget.Id);
        approved.Status.ShouldBe(RequestStatuses.Approved);
        approved.ApprovalStatus.ShouldBe(RequestApprovalResults.Approved);
        (await w.StepsAsync(noBudget.Id)).ShouldBe([RequestApprovalProcess.DepartmentDirectorStep, RequestApprovalProcess.PmOfficeStep]);
        approved.Project.ShouldNotBeNull();
        approved.Project.Classification.ShouldBe(Classifications.Initiative); // no budget → initiative (Q-P5)
        var initiative = (await w.C(w.Requester).GetFromJsonAsync<ProjectDto>($"{Base}/projects/{approved.Project.Id}"))!;
        initiative.Stage.ShouldBe(ProjectStages.Planning); // Q-P2: an initiative starts at Planning
        initiative.Budget.ShouldBeNull();

        // 2. Budget ≤ threshold: … → PM Office → Budget Section (no DG Office). From the section: the section manager step is included.
        var small = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Archive scanning", 30_000m), w.SectionRequester), w.SectionRequester);
        small.Classification.ShouldBe(Classifications.Initiative); // 30 000 is not above the threshold
        await w.ActAsync(w.SectionManager, small.Id, RequestApprovalProcess.SectionManagerStep);
        await w.ActAsync(w.Director, small.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, small.Id, RequestApprovalProcess.PmOfficeStep);
        (await w.CurrentAsync(w.PmOffice, small.Id)).Step.ShouldBe(RequestApprovalProcess.BudgetStep); // no DG Office step at or below the threshold
        await w.ActAsync(w.Budget, small.Id, RequestApprovalProcess.BudgetStep);
        (await w.StepsAsync(small.Id)).ShouldBe([
            RequestApprovalProcess.SectionManagerStep, RequestApprovalProcess.DepartmentDirectorStep, RequestApprovalProcess.PmOfficeStep,
            RequestApprovalProcess.BudgetStep]);
        var smallDone = await w.GetAsync(small.Id);
        smallDone.Status.ShouldBe(RequestStatuses.Approved);
        smallDone.ApprovedBudget.ShouldBe(30_000m); // not set at the budget step → the estimated budget (ADR 0048)

        // 3. Budget > threshold, all screening Yes: … → PM Office → DG Office → Budget Section; a Project.
        var large = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Smart licensing platform", 250_000m)));
        large.Classification.ShouldBe(Classifications.Project);
        large.EvaluationScore.ShouldBe(0.8m); // (5 + 4 + 3 + 4) / 4 / 5
        large.PriorityBand.ShouldBe("high");
        await w.ActAsync(w.Director, large.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, large.Id, RequestApprovalProcess.PmOfficeStep);

        // Only the approver of the budget step may set the approved budget.
        (await w.C(w.DgOffice).PostAsJsonAsync($"{Base}/requests/{large.Id}/approved-budget", new { amount = 1m, version = (await w.GetAsync(large.Id)).Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await w.ActAsync(w.DgOffice, large.Id, RequestApprovalProcess.DgOfficeStep);
        var atBudget = await w.GetAsync(large.Id, w.Budget);
        atBudget.Can.SetApprovedBudget.ShouldBeTrue();
        await PortfolioWorld.OkAsync<RequestDto>(w.C(w.Budget).PostAsJsonAsync($"{Base}/requests/{large.Id}/approved-budget",
            new { amount = 240_000m, version = atBudget.Version }));
        await w.ActAsync(w.Budget, large.Id, RequestApprovalProcess.BudgetStep);
        (await w.StepsAsync(large.Id)).ShouldBe([
            RequestApprovalProcess.DepartmentDirectorStep, RequestApprovalProcess.PmOfficeStep, RequestApprovalProcess.DgOfficeStep,
            RequestApprovalProcess.BudgetStep]);

        var done = await w.GetAsync(large.Id);
        done.Status.ShouldBe(RequestStatuses.Approved);
        var project = (await w.C(w.Requester).GetFromJsonAsync<ProjectDto>($"{Base}/projects/{done.Project!.Id}"))!;
        project.Number.ShouldBe($"PROJ-DEP-{w.Suffix.ToUpperInvariant()}-{PortfolioWorld.Year}-002",
            "the request unit's code (no owning unit set), the approval year, the second project of that unit");
        project.Classification.ShouldBe(Classifications.Project);
        project.Stage.ShouldBe(ProjectStages.Initiation);
        project.Status.ShouldBe(ProjectStatuses.Active);
        project.Budget.ShouldBe(240_000m);
        project.NameEn.ShouldBe("Smart licensing platform");
        project.NameAr.ShouldBe("برنامج Smart licensing platform");
        project.Year1PlannedCost.ShouldBe(175_000m);
        project.PlannedStart.ShouldBe(new DateOnly(PortfolioWorld.Year + 1, 1, 1));
        project.Owner!.Id.ShouldBe(w.Director.EmployeeId!.Value);
        project.RequestNumber.ShouldBe(done.Number);
        project.StagePath.ShouldBe(ProjectStages.ProjectPath);
        (await w.C(w.Director).GetAsync($"{Base}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK); // project owner
        (await w.C(w.OtherRequester).GetAsync($"{Base}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The requester is told the project was created; the banner says so.
        var mine = await NotificationTests.EventuallyAsync(() => w.C(w.Requester).GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"),
            m => m!.Items.Any(i => i.TemplateKey == PortfolioNotifications.RequestApproved.Key && i.Link == $"/portfolio/projects/{project.Id:D}"));
        mine!.Items.ShouldContain(i => i.TemplateKey == PortfolioNotifications.RequestApproved.Key);
        var status = (await w.C(w.Requester).GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/portfolio.request/{large.Id}"))!;
        status.MessageKey.ShouldBe("portfolio.currentStatus.approved");
        status.Params["project"].En.ShouldBe(project.Number);
    }

    [Fact]
    public async Task Owning_unit_code_numbers_projects()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        await SetOwningUnitAsync(w.SectionId);

        var request = await w.SubmitAsync(await w.CreateAsync(PortfolioWorld.Valid("Numbered by owning unit")));
        await w.ActAsync(w.Director, request.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, request.Id, RequestApprovalProcess.PmOfficeStep);
        var project = (await w.GetAsync(request.Id)).Project!;
        project.Number.ShouldBe($"PROJ-SEC-{w.Suffix.ToUpperInvariant()}-{PortfolioWorld.Year}-001");
        await SetOwningUnitAsync(null);
    }

    private async Task SetOwningUnitAsync(Guid? unitId)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var administration = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Access.Application.AccessAdministration>();
        var system = (await administration.ListSystemsAsync(CancellationToken.None)).Single(s => s.Code == PortfolioPermissions.SystemCode);
        var updated = await administration.UpdateSystemAsync(system.Code, system.Enabled, unitId, system.Version, CancellationToken.None);
        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
    }

    [Fact]
    public async Task Submission_applies_every_request_rule_with_clear_keys()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var valid = PortfolioWorld.Valid("Rules", 100_000m);
        var created = await w.CreateWithCostFileAsync(valid with { Year4Cost = valid.Year4Cost + 1 });

        // Year 1–4 ≠ budget blocks submission with the totals in the message parameters.
        var response = await w.SubmitRawAsync(created);
        var problem = await PortfolioWorld.ProblemFieldsAsync(response);
        var mismatch = problem.Single(f => f.Field == "yearCosts");
        mismatch.Code.ShouldBe("portfolio.errors.yearCostsMismatch");
        mismatch.Params!["total"]!.ToString().ShouldBe("100001.00");
        mismatch.Params["budget"]!.ToString().ShouldBe("100000.00");
        (await w.GetAsync(created.Id)).Status.ShouldBe(RequestStatuses.Draft);

        // Every other rule at once.
        var broken = valid with
        {
            ExpectedStartDate = new DateOnly(PortfolioWorld.Year - 1, 12, 31),
            ExpectedEndDate = new DateOnly(PortfolioWorld.Year + 4, 1, 1),
            ProgramNature = ProgramNatures.Continuous,
            PreviousProgramName = null,
            PracticeService = null,
            PracticeOperation = null,
            ScreenOneTime = null,
            ReturnExpected = true,
        };
        var update = await w.C(w.Requester).PutAsJsonAsync($"{Base}/requests/{created.Id}", new { values = broken, version = (await w.GetAsync(created.Id)).Version });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync()); // drafts may be incomplete
        problem = await PortfolioWorld.ProblemFieldsAsync((await w.SubmitRawAsync(await w.GetAsync(created.Id))));
        var codes = problem.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.First().Code);
        codes["expectedStartDate"].ShouldBe("portfolio.errors.startOutOfRange");
        codes["expectedEndDate"].ShouldBe("portfolio.errors.endOutOfRange");
        codes["previousProgramName"].ShouldBe("portfolio.errors.previousProgramRequired");
        codes["practiceOperation"].ShouldBe("portfolio.errors.operationOrService");
        codes["screening"].ShouldBe("portfolio.errors.screeningRequired");
        codes["returnValue"].ShouldBe("validation.notEmpty");
        codes["returnYear"].ShouldBe("validation.notEmpty");
        codes.ShouldNotContainKey("unitId"); // an edit without a unit keeps the requesting unit

        // Format rules apply on every save: 4-digit years, two decimals, "other" needs its text, unknown lookup codes.
        var save = await w.C(w.Requester).PutAsJsonAsync($"{Base}/requests/{created.Id}", new
        {
            values = valid with { ReturnYear = 999, EstimatedBudget = 100_000.005m, SuggestionSourceCode = "other", MainOptionCode = "nope" },
            version = (await w.GetAsync(created.Id)).Version,
        });
        problem = await PortfolioWorld.ProblemFieldsAsync(save);
        codes = problem.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.First().Code);
        codes["returnYear"].ShouldBe("portfolio.errors.fourDigitYear");
        codes["estimatedBudget"].ShouldBe("portfolio.errors.twoDecimals");
        codes["suggestionSourceOther"].ShouldBe("portfolio.errors.otherTextRequired");
        codes["mainOptionCode"].ShouldBe("validation.unknownCode");

        // Budget required without the cost estimation file.
        var noFile = await w.CreateAsync(valid);
        problem = await PortfolioWorld.ProblemFieldsAsync((await w.SubmitRawAsync(noFile)));
        problem.ShouldContain(f => f.Field == "costEstimation" && f.Code == "portfolio.errors.costEstimationMissing");
    }

    [Fact]
    public async Task Return_for_correction_loops_back_to_the_requester_and_resubmission_starts_a_new_attempt()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var request = await w.SubmitAsync(await w.CreateAsync(PortfolioWorld.Valid("Customer survey")));
        await w.ActAsync(w.Director, request.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, request.Id, RequestApprovalProcess.PmOfficeStep, ApprovalActions.Return, "Add measurable benefits");

        var returned = await w.GetAsync(request.Id, w.Requester);
        returned.Status.ShouldBe(RequestStatuses.Draft);
        returned.ApprovalStatus.ShouldBe(RequestApprovalResults.Returned);
        returned.Can.Edit.ShouldBeTrue();
        var status = (await w.C(w.Requester).GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/portfolio.request/{request.Id}"))!;
        status.MessageKey.ShouldBe("portfolio.currentStatus.returned");
        status.NextActions.Select(a => a.Code).ShouldBe(["resubmit"]);
        var work = (await w.C(w.Requester).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == request.Id && i.MessageKey == "portfolio.myWork.returned");

        var corrected = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/requests/{request.Id}",
            new { values = returned.Values with { Justifications = "Customers wait 10 days; target 2 days." }, version = returned.Version }));
        var resubmitted = await w.SubmitAsync(corrected);
        resubmitted.Status.ShouldBe(RequestStatuses.InApproval);
        await w.ActAsync(w.Director, request.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await w.ActAsync(w.PmOffice, request.Id, RequestApprovalProcess.PmOfficeStep);
        (await w.GetAsync(request.Id)).Status.ShouldBe(RequestStatuses.Approved);
        var panel = (await w.C(w.Requester).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/portfolio.request/{request.Id}"))!;
        panel.Instances.Count.ShouldBe(2);
        panel.Instances[0].Attempt.ShouldBe(2);
        panel.Instances[1].Status.ShouldBe(ApprovalInstanceStatuses.Returned);
    }

    [Fact]
    public async Task Reject_is_final_and_cancel_needs_a_reason()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var rejected = await w.SubmitAsync(await w.CreateAsync(PortfolioWorld.Valid("Rejected idea")));
        await w.ActAsync(w.Director, rejected.Id, RequestApprovalProcess.DepartmentDirectorStep, ApprovalActions.Reject, "Out of scope");
        var after = await w.GetAsync(rejected.Id, w.Requester);
        after.Status.ShouldBe(RequestStatuses.Rejected);
        after.Can.Edit.ShouldBeFalse();
        (await w.SubmitRawAsync(after)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The requester cancels their own draft (with a reason); not someone else's.
        var draft = await w.CreateAsync(PortfolioWorld.Valid("Cancelled draft"));
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/requests/{draft.Id}/cancel", new { reason = "", version = draft.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await w.C(w.OtherRequester).PostAsJsonAsync($"{Base}/requests/{draft.Id}/cancel", new { reason = "x", version = draft.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var cancelled = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/requests/{draft.Id}/cancel",
            new { reason = "Merged with another program", version = draft.Version }));
        cancelled.Status.ShouldBe(RequestStatuses.Cancelled);
        cancelled.CancelReason.ShouldBe("Merged with another program");

        // The requester cannot cancel a request in approval; the PM Office can, which cancels the approval too.
        var pending = await w.SubmitAsync(await w.CreateAsync(PortfolioWorld.Valid("Cancelled in approval")));
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/requests/{pending.Id}/cancel", new { reason = "x", version = pending.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var stopped = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/requests/{pending.Id}/cancel",
            new { reason = "Budget freeze", version = pending.Version }));
        stopped.Status.ShouldBe(RequestStatuses.Cancelled);
        var panel = (await w.C(w.PmOffice).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/portfolio.request/{pending.Id}"))!;
        panel.Instances[0].Status.ShouldBe(ApprovalInstanceStatuses.Cancelled);
        await NotificationTests.EventuallyAsync(() => w.C(w.Requester).GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"),
            m => m!.Items.Any(i => i.TemplateKey == PortfolioNotifications.RequestCancelled.Key));
    }

    [Fact]
    public async Task Requester_sees_only_own_requests_department_viewer_their_unit_and_below_pm_office_all()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var own = await w.CreateAsync(PortfolioWorld.Valid("Own " + w.Suffix));
        var section = await w.CreateAsync(PortfolioWorld.Valid("Section " + w.Suffix), w.SectionRequester);
        var other = await w.CreateAsync(PortfolioWorld.Valid("Other " + w.Suffix), w.OtherRequester);

        async Task<List<Guid>> ListAsync(TestUser user) =>
            (await w.C(user).GetFromJsonAsync<PagedResult<RequestListItemDto>>($"{Base}/requests?search={w.Suffix}&pageSize=200"))!.Items.Select(i => i.Id).ToList();

        (await ListAsync(w.Requester)).ShouldBe([own.Id]);
        (await ListAsync(w.DepartmentViewer)).ShouldBe([own.Id, section.Id], ignoreOrder: true); // department and the section below it
        (await ListAsync(w.PmOffice)).ShouldBe([own.Id, section.Id, other.Id], ignoreOrder: true);
        (await ListAsync(w.OtherRequester)).ShouldBe([other.Id]);

        (await w.C(w.Requester).GetAsync($"{Base}/requests/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.DepartmentViewer).GetAsync($"{Base}/requests/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/requests/{own.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Visible but not editable: 403.
        var viewed = await w.GetAsync(own.Id, w.DepartmentViewer);
        viewed.Can.Edit.ShouldBeFalse();
        (await w.C(w.DepartmentViewer).PutAsJsonAsync($"{Base}/requests/{own.Id}", new { values = PortfolioWorld.Valid("x"), version = viewed.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An approver sees the request while it waits for them (the engine's approver record role).
        var submitted = await w.SubmitAsync(await w.GetAsync(other.Id, w.OtherRequester), w.OtherRequester);
        submitted.Status.ShouldBe(RequestStatuses.InApproval);
        (await w.C(w.PmOffice).GetAsync($"{Base}/requests/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pm_office_overrides_the_classification_with_a_reason()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var request = await w.CreateAsync(PortfolioWorld.Valid("Override", 30_000m));
        request.Classification.ShouldBe(Classifications.Initiative);
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/requests/{request.Id}/classification", new { value = "project", reason = "x", version = request.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.PmOffice).PostAsJsonAsync($"{Base}/requests/{request.Id}/classification", new { value = "project", version = request.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var overridden = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/requests/{request.Id}/classification",
            new { value = "project", reason = "Strategic program of the year", version = request.Version }));
        overridden.Classification.ShouldBe(Classifications.Initiative);
        overridden.EffectiveClassification.ShouldBe(Classifications.Project);
        overridden.OverrideReason.ShouldBe("Strategic program of the year");
    }

    [Fact]
    public async Task Project_scope_goes_to_administration_for_quotations_before_submission()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var values = PortfolioWorld.Valid("Fleet tracking", 80_000m) with { CostPath = CostPaths.ProjectScope };
        var request = await w.CreateAsync(values);
        var status = (await w.C(w.Requester).GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/portfolio.request/{request.Id}"))!;
        status.MessageKey.ShouldBe("portfolio.currentStatus.uploadScope");

        // Administration cannot see the request before the hand-off.
        (await w.C(w.Administration).GetAsync($"{Base}/requests/{request.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await PortfolioWorld.UploadAsync(w.C(w.Requester), request.Id, CostPaths.ProjectScope, "scope.pdf");
        request = await w.GetAsync(request.Id, w.Requester);
        request.ProjectScopeUploaded.ShouldBeTrue();
        var handed = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/requests/{request.Id}/hand-off", new { version = request.Version }));
        handed.ProcurementStatus.ShouldBe(ProcurementStatuses.WaitingQuotations);
        await NotificationTests.EventuallyAsync(() => w.C(w.Administration).GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"),
            m => m!.Items.Any(i => i.TemplateKey == PortfolioNotifications.ScopeSubmitted.Key));
        var work = (await w.C(w.Administration).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == request.Id && i.MessageKey == "portfolio.myWork.uploadQuotations");

        // Submission waits for the quotations.
        var problem = await PortfolioWorld.ProblemFieldsAsync((await w.SubmitRawAsync(handed)));
        problem.ShouldContain(f => f.Code == "portfolio.errors.quotationsMissing");

        // Only Administration uploads quotations; the requester cannot.
        (await PortfolioWorld.UploadRawAsync(w.C(w.Requester), request.Id, "quotation")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var adminView = await w.GetAsync(request.Id, w.Administration);
        adminView.Can.Quotations.ShouldBeTrue();
        (await w.C(w.Administration).PostAsJsonAsync($"{Base}/requests/{request.Id}/quotations-uploaded", new { version = adminView.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // no quotation yet
        await PortfolioWorld.UploadAsync(w.C(w.Administration), request.Id, "quotation", "quote-a.pdf");
        var confirmed = await PortfolioWorld.OkAsync<RequestDto>(w.C(w.Administration).PostAsJsonAsync($"{Base}/requests/{request.Id}/quotations-uploaded",
            new { version = (await w.GetAsync(request.Id)).Version }));
        confirmed.ProcurementStatus.ShouldBe(ProcurementStatuses.QuotationsUploaded);
        await NotificationTests.EventuallyAsync(() => w.C(w.Requester).GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"),
            m => m!.Items.Any(i => i.TemplateKey == PortfolioNotifications.QuotationsUploaded.Key));
        var files = await PortfolioWorld.OkAsync<List<RequestFileDto>>(w.C(w.Requester).GetAsync($"{Base}/requests/{request.Id}/files"));
        files.Select(f => f.Kind).ShouldBe([CostPaths.ProjectScope, "quotation"], ignoreOrder: true);

        var submitted = await w.SubmitAsync(await w.GetAsync(request.Id, w.Requester));
        submitted.Status.ShouldBe(RequestStatuses.InApproval);
    }

    [Fact]
    public async Task Requests_are_task_able_and_mcp_tools_follow_the_rules()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var request = await w.CreateAsync(PortfolioWorld.Valid("Linked " + w.Suffix));
        var task = await w.C(w.Requester).PostAsJsonAsync("/api/v1/tasks", new
        {
            titleEn = "Prepare the cost estimation",
            typeCode = "personal",
            sourceCode = "manager-assignment",
            assigneeEmployeeIds = new[] { w.Requester.EmployeeId },
            dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            linkedType = PortfolioPermissions.RequestResource,
            linkedId = request.Id,
        });
        task.StatusCode.ShouldBe(HttpStatusCode.Created, await task.Content.ReadAsStringAsync());

        // Someone who cannot read the request cannot link a task to it.
        var refused = await w.C(w.OtherRequester).PostAsJsonAsync("/api/v1/tasks", new
        {
            titleEn = "Sneaky link",
            typeCode = "personal",
            sourceCode = "manager-assignment",
            assigneeEmployeeIds = new[] { w.OtherRequester.EmployeeId },
            linkedType = PortfolioPermissions.RequestResource,
            linkedId = request.Id,
        });
        refused.IsSuccessStatusCode.ShouldBeFalse();

        await using var scope = TestAccess.SystemScope(fx.Factory);
        var tools = scope.ServiceProvider.GetServices<IAppTool>().Where(t => t.Definition.SystemCode == PortfolioPermissions.SystemCode).ToList();
        tools.Select(t => t.Definition.Name).ShouldBe(
            ["portfolio_search_requests", "portfolio_my_requests", "portfolio_get_request", "portfolio_get_project", "portfolio_create_draft_request"], ignoreOrder: true);
        tools.Single(t => t.Definition.Kind == ToolKind.Write).Definition.DefaultEnabled.ShouldBeFalse();
        tools.ShouldNotContain(t => t.Definition.Kind == ToolKind.Approve);
    }
}
