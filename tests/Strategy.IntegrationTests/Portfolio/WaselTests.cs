using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Host.Infrastructure;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Wasel;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Time;


namespace Strategy.IntegrationTests.Portfolio;

/// <summary>Phase 11 host: Portfolio + Wasel with a travelling clock (expiry and dormant-account tests move it).</summary>
public sealed class WaselFixture(PostgresFixture postgres) : IAsyncLifetime
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
}

/// <summary>
/// Wasel vendor portal (SPEC §1.18, docs/design/portfolio.md §46–§57, ADRs 0110–0115): invitation and mandatory MFA, plan proposal accepted with edits,
/// vendor-then-PM monthly submission with the PM's edit visible to the vendor and the vendor origin visible to approvers, and the security rules
/// (separate scheme, no internal endpoint, other vendors' projects and non-assigned milestones 404, no costs, expiry with the fake clock, Contributor
/// cannot submit, every vendor action audited with channel <c>wasel</c>).
/// </summary>
public sealed class WaselTests(WaselFixture fx) : IClassFixture<WaselFixture>
{
    private const string Base = PortfolioWorld.Base;
    private const string Wasel = "/api/wasel/v1";
    private const string VendorPassword = "Vendor#Portal2026";

    [Fact]
    public async Task Vendor_proposes_the_plan_and_submits_the_monthly_update_the_pm_edits_one_value_and_approvers_see_the_vendor_origin()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var pm = w.C(w.Requester);
        var project = await ProjectInPlanningAsync(w, "Vendor portal rollout");
        var vendor = await CreateVendorAsync(w, "Acme Systems");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await AssignAsync(w, project.Id, vendor.Id, today.AddDays(120));
        var leadEmail = $"lead.{w.Suffix}@acme.example";
        var contributorEmail = $"contrib.{w.Suffix}@acme.example";
        await InviteAsync(w, project.Id, vendor.Id, leadEmail, VendorRoles.Lead);
        await InviteAsync(w, project.Id, vendor.Id, contributorEmail, VendorRoles.Contributor);

        // Invitation link → password → MFA enrolment is forced at the first sign-in.
        var (lead, leadKey) = await AcceptInvitationAndSignInAsync(leadEmail);
        var (contributor, _) = await AcceptInvitationAndSignInAsync(contributorEmail);
        var portal = await Ok<VendorPortalMeDto>(lead.GetAsync($"{Wasel}/portal"));
        portal.Projects.Single().Id.ShouldBe(project.Id);
        portal.Projects[0].Role.ShouldBe(VendorRoles.Lead);

        // A second sign-in asks for the second factor (MFA is mandatory for vendors).
        var again = await NewVendorClientAsync();
        (await LoginStatusAsync(again, leadEmail)).ShouldBe(SignInStatuses.MfaRequired);
        await VerifyAsync(again, leadKey);

        // Vendor-visible overview: no budget by default (ADR 0113).
        var overview = await Ok<VendorProjectDto>(lead.GetAsync($"{Wasel}/projects/{project.Id}"));
        overview.Budget.ShouldBeNull();
        overview.CanProposePlan.ShouldBeTrue();
        overview.ProjectManager.ShouldNotBeNull();

        // Plan proposal: one phase, two milestones (60 / 40) with tasks, inside the project's dates. Contributor prepares, cannot submit.
        var start = overview.PlannedStart is { } ps && ps > today ? ps : today;
        var end = overview.PlannedEnd ?? start.AddDays(60);
        var middle = start.AddDays(Math.Max(1, (end.DayNumber - start.DayNumber) / 3));
        var items = new object[]
        {
            new { key = "p1", type = "phase", titleEn = "Delivery", plannedStart = start, plannedEnd = end },
            new { key = "m1", type = "milestone", parentKey = "p1", titleEn = "Design", plannedStart = start, plannedEnd = middle, weight = 0.6m, tasks = new[] { "Wireframes" } },
            new { key = "m2", type = "milestone", parentKey = "p1", titleEn = "Build", plannedStart = middle.AddDays(1), plannedEnd = end, weight = 0.4m },
        };
        var proposals = await Ok<VendorProposalsDto>(contributor.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/proposals", new { items, note = "Our plan" }));
        var proposal = proposals.Proposals.Single();
        (await contributor.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/proposals/{proposal.Id}/submit", new { version = proposal.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "a Vendor Contributor prepares but cannot submit");
        proposals = await Ok<VendorProposalsDto>(lead.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/proposals/{proposal.Id}/submit", new { version = proposal.Version }));
        proposals.Proposals.Single().Status.ShouldBe(ProposalStatuses.Submitted);

        // PM accepts with one edit (Design renamed); the diff is stored and shown to the vendor; milestones are created for the vendor.
        var panel = await Ok<WaselPanelDto>(pm.GetAsync($"{Base}/projects/{project.Id}/wasel"));
        var pmProposal = panel.Proposals.Single();
        var edited = pmProposal.Items.Select(i => i.Key == "m1" ? i with { TitleEn = "Design and sign-off" } : i).ToList();
        panel = await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/proposals/{pmProposal.Id}/accept", new { items = edited, version = pmProposal.Version }));
        panel.Proposals.Single().Status.ShouldBe(ProposalStatuses.Accepted);
        var vendorView = (await Ok<VendorProposalsDto>(lead.GetAsync($"{Wasel}/projects/{project.Id}/proposals"))).Proposals.Single();
        vendorView.Diff.ShouldContain(d => d.Key == "m1" && d.Field == "titleEn" && d.Vendor == "Design" && d.Pm == "Design and sign-off");
        var milestones = await Ok<List<VendorMilestoneDto>>(lead.GetAsync($"{Wasel}/projects/{project.Id}/milestones"));
        milestones.Select(m => m.TitleEn).ShouldBe(["Design and sign-off", "Build"], ignoreOrder: true);
        milestones.ShouldAllBe(m => m.Cost == null);

        // Execution in "Vendor then PM" mode; the monthly window opens and vendors are e-mailed.
        await DriveToExecutionAsync(project.Id);
        project = await ProjectLifecycleTests.Get(w, project.Id);
        await Ok<WaselPanelDto>(pm.PutAsJsonAsync($"{Base}/projects/{project.Id}/wasel/mode", new { mode = UpdateModes.VendorThenPm, version = project.Version }));
        await Ok<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{project.Id}/monthly/open", null));
        await fx.Factory.Emails.LastAsync(leadEmail, PortfolioNotifications.WaselMonthlyOpened.Key);

        // The vendor fills both milestones; 100 % needs evidence; the Contributor cannot submit, the Lead can.
        var design = milestones.Single(m => m.TitleEn == "Design and sign-off");
        var build = milestones.Single(m => m.TitleEn == "Build");
        await Ok<VendorSubmissionDto>(contributor.PutAsJsonAsync($"{Wasel}/projects/{project.Id}/submission/items/{design.Id}",
            new { values = new { achieve = 0.3m, actualStart = today, progressNote = "Half of the design done" } }));
        await Ok<VendorSubmissionDto>(lead.PutAsJsonAsync($"{Wasel}/projects/{project.Id}/submission/items/{build.Id}",
            new { values = new { achieve = 0.4m, actualStart = today, actualEnd = today, progressNote = "Built" } }));
        var submission = await Ok<VendorSubmissionDto>(lead.GetAsync($"{Wasel}/projects/{project.Id}/submission"));
        var missingEvidence = await lead.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/submission/submit", new { version = submission.Version });
        (await PortfolioWorld.ProblemFieldsAsync(missingEvidence)).Select(f => f.MessageKey).ShouldContain("portfolio.errors.evidenceRequired");
        await ProjectLifecycleTests.UploadAsync(lead, $"{Wasel}/projects/{project.Id}/submission/items/{build.Id}/evidence", "build-report.pdf");
        submission = await Ok<VendorSubmissionDto>(lead.GetAsync($"{Wasel}/projects/{project.Id}/submission"));
        (await contributor.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/submission/submit", new { version = submission.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "only the Vendor Project Lead submits");
        submission = await Ok<VendorSubmissionDto>(lead.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/submission/submit", new { version = submission.Version }));
        submission.Status.ShouldBe(SubmissionStatuses.Submitted);

        // The PM cannot submit the month while the vendor's update waits for review.
        await (await pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/monthly/submit", new { version = (await ProjectLifecycleTests.Get(w, project.Id)).Version }))
            .KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.vendorSubmissionPending");

        // PM review side by side: edit-then-accept Design (0.3 → 0.25), accept Build as sent; AI review flags without a model are listed.
        panel = await Ok<WaselPanelDto>(pm.GetAsync($"{Base}/projects/{project.Id}/wasel"));
        var pmSubmission = panel.Submissions.Single();
        var designItem = pmSubmission.Items.Single(i => i.MilestoneId == design.Id);
        designItem.Previous!.Approved.ShouldBe(0m);
        designItem.Vendor.Achieve.ShouldBe(0.3m);
        var review = await Ok<VendorReviewAiDto>(pm.PostAsync($"{Base}/vendor-submissions/{pmSubmission.Id}/ai-review", null));
        review.Flags.ShouldContain(f => f.MilestoneId == design.Id && f.Flag == "progressJump");
        await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/vendor-submissions/{pmSubmission.Id}/items/{designItem.Id}/accept",
            new { values = new { achieve = 0.25m, actualStart = today, progressNote = "Half of the design done" }, version = designItem.Version }));
        var buildItem = pmSubmission.Items.Single(i => i.MilestoneId == build.Id);
        panel = await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/vendor-submissions/{pmSubmission.Id}/items/{buildItem.Id}/accept", new { version = buildItem.Version }));
        panel.Submissions.Single().Status.ShouldBe(SubmissionStatuses.Accepted);

        // The vendor sees the PM's change.
        submission = await Ok<VendorSubmissionDto>(lead.GetAsync($"{Wasel}/projects/{project.Id}/submission"));
        submission.Status.ShouldBe(SubmissionStatuses.Accepted);
        var seen = submission.Items.Single(i => i.MilestoneId == design.Id);
        seen.ReviewStatus.ShouldBe(ItemReviewStatuses.Edited);
        seen.Diff.ShouldContain(d => d.Field == "achieve" && d.Vendor == "0.3" && d.Pm == "0.25");
        await fx.Factory.Emails.LastAsync(leadEmail, PortfolioNotifications.WaselSubmissionReviewed.Key);

        // Accepted values enter the normal monthly approval; the snapshot carries their origin for the approvers.
        var plan = await ProjectLifecycleTests.PlanAsync(w, project.Id);
        plan.Single(m => m.Id == design.Id).PendingSource.ShouldBe(PendingSources.VendorEdited);
        plan.Single(m => m.Id == build.Id).PendingSource.ShouldBe(PendingSources.Vendor);
        var monthly = await Ok<MonthlyViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/monthly/submit",
            new { version = (await ProjectLifecycleTests.Get(w, project.Id)).Version, comment = "Vendor month" }));
        var snapshot = JsonDocument.Parse(monthly.Current!.Items).RootElement.EnumerateArray().ToList();
        var designRow = snapshot.Single(e => e.GetProperty("id").GetGuid() == design.Id);
        designRow.GetProperty("source").GetString().ShouldBe(PendingSources.VendorEdited);
        designRow.GetProperty("vendorValues").GetProperty("achieve").GetDecimal().ShouldBe(0.3m);
        designRow.GetProperty("vendorNameEn").GetString().ShouldBe("Acme Systems");
        snapshot.Single(e => e.GetProperty("id").GetGuid() == build.Id).GetProperty("source").GetString().ShouldBe(PendingSources.Vendor);

        var directorView = await Ok<MonthlyViewDto>(w.C(w.Director).GetAsync($"{Base}/projects/{project.Id}/monthly"));
        directorView.Current!.Items.ShouldContain(PendingSources.VendorEdited, customMessage: "the approver reads the vendor origin");
        await ProjectLifecycleTests.ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ProjectLifecycleTests.ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        plan = await ProjectLifecycleTests.PlanAsync(w, project.Id);
        plan.Single(m => m.Id == design.Id).MaxAchieved.ShouldBe(0.25m);
        plan.Single(m => m.Id == build.Id).MaxAchieved.ShouldBe(0.4m);
        var history = await Ok<List<VendorSubmissionHistoryDto>>(lead.GetAsync($"{Wasel}/projects/{project.Id}/submissions"));
        history.Single().Status.ShouldBe(SubmissionStatuses.Included);
        history[0].SharedOutcome.ShouldBe("approved");

        // Deliverable, proposed issue and a comment thread.
        var deliverables = await UploadDeliverableAsync(lead, project.Id, build.Id, "Build handover");
        var deliverable = deliverables.Single();
        panel = await Ok<WaselPanelDto>(pm.GetAsync($"{Base}/projects/{project.Id}/wasel"));
        await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/vendor-deliverables/{deliverable.Id}/return",
            new { comment = "Add the test report", version = panel.Deliverables.Single().Version }));
        (await Ok<List<VendorDeliverableDto>>(lead.GetAsync($"{Wasel}/projects/{project.Id}/deliverables"))).Single().ShouldSatisfyAllConditions(
            d => d.Status.ShouldBe(DeliverableStatuses.Returned), d => d.PmComment.ShouldBe("Add the test report"));
        await Ok<List<VendorItemDto>>(contributor.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/items",
            new { values = new { kind = "risk", title = "Licence delay", description = "Supplier licence may arrive late", probability = 3, impactLevel = 4 } }));
        panel = await Ok<WaselPanelDto>(pm.GetAsync($"{Base}/projects/{project.Id}/wasel"));
        var proposedRisk = panel.Items.Single();
        panel = await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/vendor-items/{proposedRisk.Id}/accept", new { version = proposedRisk.Version }));
        panel.Items.Single().CreatedRecordId.ShouldNotBeNull();
        await Ok<List<WaselCommentDto>>(lead.PostAsJsonAsync($"{Wasel}/projects/{project.Id}/comments",
            new { subjectType = CommentSubjects.Milestone, subjectId = design.Id, body = "Design sign-off planned next week" }));
        var thread = await Ok<List<WaselCommentDto>>(pm.GetAsync($"{Base}/projects/{project.Id}/wasel/comments?subjectType=milestone&subjectId={design.Id}"));
        thread.Single().AuthorKind.ShouldBe("vendor");

        // Every vendor action is in the audit trail with channel "wasel".
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
        var entries = await audit.SearchAsync(new AuditFilter(Channel: "wasel", EntityType: VendorSubmission.EntityType), new PageRequest(1, 50), CancellationToken.None);
        entries.Items.ShouldNotBeEmpty();
        (await audit.SearchAsync(new AuditFilter(Channel: "wasel", EntityType: PlanProposal.EntityType), new PageRequest(1, 50), CancellationToken.None)).Items.ShouldNotBeEmpty();
        (await audit.SearchAsync(new AuditFilter(Channel: "wasel", EntityType: VendorDeliverable.EntityType), new PageRequest(1, 50), CancellationToken.None)).Items.ShouldNotBeEmpty();
        (await audit.SearchAsync(new AuditFilter(Channel: "wasel", EntityType: VendorComment.EntityType), new PageRequest(1, 50), CancellationToken.None)).Items.ShouldNotBeEmpty();
        (await audit.SearchAsync(new AuditFilter(Channel: "wasel", Action: "auth.login"), new PageRequest(1, 50), CancellationToken.None)).Items.ShouldNotBeEmpty();

        // Vendor rating at closure feeds the vendor's history and the Impact "vendor satisfaction".
        await (await pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/wasel/ratings",
                new { vendorId = vendor.Id, values = new { timeliness = 4, quality = 5, responsiveness = 4, reporting = 3, comment = "Good" } }))
            .KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.vendorRatingStage");
        await DriveToSubStateAsync(project.Id, SubStates.ClosureDraft);
        panel = await Ok<WaselPanelDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/wasel/ratings",
            new { vendorId = vendor.Id, values = new { timeliness = 4, quality = 5, responsiveness = 4, reporting = 3, comment = "Good" } }));
        panel.Ratings.Single().Overall.ShouldBe(4m);
        (await ProjectLifecycleTests.Get(w, project.Id)).VendorSatisfaction.ShouldBe(4);
        var register = await Ok<VendorDetailDto>(w.C(w.PmOffice).GetAsync($"{Base}/vendors/{vendor.Id}"));
        register.Performance.Count.ShouldBe(1);
        register.Users.Count.ShouldBe(2);
        register.Users.ShouldAllBe(u => u.MfaEnabled);
    }

    [Fact]
    public async Task A_vendor_session_reaches_no_internal_endpoint_and_an_internal_session_no_wasel_endpoint()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var (lead, _, _, _) = await VendorOnProjectAsync(w, "Isolation");
        var routes = fx.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true && e.Metadata.GetMetadata<PublicEndpoint>() is null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (Method: m, Route: e.RoutePattern.RawText!)))
            .Distinct().ToList();
        routes.Count.ShouldBeGreaterThan(300);
        var internalRoutes = routes.Where(r => !r.Route.StartsWith("/api/wasel", StringComparison.Ordinal)).ToList();
        var waselRoutes = routes.Where(r => r.Route.StartsWith("/api/wasel", StringComparison.Ordinal)).ToList();
        waselRoutes.Count.ShouldBeGreaterThan(15);

        var leaks = new List<string>();
        foreach (var (method, route) in internalRoutes)
        {
            var response = await lead.SendAsync(Request(method, route));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                leaks.Add($"{method} {route} → {(int)response.StatusCode}");
            }
        }

        leaks.ShouldBeEmpty("a vendor session is never an internal principal");

        // MCP, the SignalR hub and the OAuth server refuse the vendor too (the cookie is not even sent there).
        (await lead.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound); // 404 while MCP is off, 401 when on: never served to the vendor cookie

        var pm = w.C(w.Requester);
        foreach (var (method, route) in waselRoutes)
        {
            var response = await pm.SendAsync(Request(method, route));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                leaks.Add($"internal {method} {route} → {(int)response.StatusCode}");
            }
        }

        leaks.ShouldBeEmpty("an internal session is never a vendor principal");

        // Sign-in endpoints refuse the other principal type, without telling which accounts exist.
        var internalLogin = await (await fx.Factory.CreateSpaClientAsync()).PostAsJsonAsync("/api/v1/identity/auth/login",
            new { login = $"lead.{w.Suffix}@isolation.example", password = VendorPassword });
        await internalLogin.KeyAsync(HttpStatusCode.BadRequest, "auth.errors.invalidCredentials");
        var waselLogin = await (await NewVendorClientAsync()).PostAsJsonAsync($"{Wasel}/auth/login", new { login = w.Requester.UserName, password = w.Requester.Password });
        await waselLogin.KeyAsync(HttpStatusCode.BadRequest, "auth.errors.invalidCredentials");
    }

    [Fact]
    public async Task A_vendor_sees_only_its_projects_and_assigned_milestones_never_costs_and_access_expires_with_the_clock()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var (lead, projectId, vendorId, assigned) = await VendorOnProjectAsync(w, "Scoped");
        var (other, otherProjectId, _, otherMilestone) = await VendorOnProjectAsync(w, "Other vendor");

        // Another vendor's project and milestones: 404.
        (await lead.GetAsync($"{Wasel}/projects/{otherProjectId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lead.GetAsync($"{Wasel}/projects/{otherProjectId}/milestones")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"{Wasel}/projects/{projectId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A milestone of the vendor's own project that is not assigned to it: invisible and not updatable.
        var unassigned = await ProjectLifecycleTests.MilestoneAsync(w, projectId, new
        {
            titleEn = "Internal work",
            isHeader = false,
            highLevel = false,
            plannedStart = DateOnly.FromDateTime(DateTime.UtcNow),
            plannedEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            weight = 0.2m,
            cost = 7654321.25m,
        });
        var milestones = await Ok<List<VendorMilestoneDto>>(lead.GetAsync($"{Wasel}/projects/{projectId}/milestones"));
        milestones.Select(m => m.Id).ShouldBe([assigned]);
        await DriveToExecutionAsync(projectId);
        var project = await ProjectLifecycleTests.Get(w, projectId);
        await Ok<WaselPanelDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/projects/{projectId}/wasel/mode", new { mode = UpdateModes.VendorThenPm, version = project.Version }));
        await Ok<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{projectId}/monthly/open", null));
        (await lead.PutAsJsonAsync($"{Wasel}/projects/{projectId}/submission/items/{unassigned.Id}", new { values = new { achieve = 0.1m } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lead.PutAsJsonAsync($"{Wasel}/projects/{projectId}/submission/items/{otherMilestone}", new { values = new { achieve = 0.1m } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No budget or cost in any vendor response (defaults); internal DTO fields never appear.
        foreach (var url in new[] { $"{Wasel}/projects/{projectId}", $"{Wasel}/projects/{projectId}/milestones", $"{Wasel}/projects/{projectId}/submission" })
        {
            var json = await lead.GetStringAsync(url);
            json.ShouldNotContain("7654321");
            json.ShouldNotContain("240000");
            json.ShouldNotContain("costsHidden");
            json.ShouldNotContain("approvalProcess");
        }

        // Revocation is immediate; then the access date passes (fake clock): the remaining member loses access without any job.
        var panel = await Ok<WaselPanelDto>(w.C(w.Requester).GetAsync($"{Base}/projects/{otherProjectId}/wasel"));
        var otherMember = panel.Assignments.Single().Members.Single();
        await Ok<WaselPanelDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/wasel-members/{otherMember.Id}/revoke", new { reason = "Left the vendor" }));
        (await other.GetAsync($"{Wasel}/projects/{otherProjectId}")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized); // no other project left: the session is revoked too

        // The record-role grant itself expires (independent of the session): checked through the one access service with the vendor principal.
        Guid leadUserId;
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            leadUserId = (await db.VendorMembers.SingleAsync(m => m.ProjectId == projectId && m.Status == VendorMemberStatuses.Active)).UserId;
        }

        var vendorPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, leadUserId.ToString()),
            new System.Security.Claims.Claim(StrategyClaims.PrincipalType, StrategyClaims.VendorPrincipal),
        ], "wasel"));
        var projectRef = new ResourceRef(PortfolioPermissions.ProjectResource, projectId);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            (await access.CanAsync(vendorPrincipal, WaselPermissions.ProjectRead, projectRef)).ShouldBeTrue();
            (await access.CanAsync(vendorPrincipal, WaselPermissions.UpdateSubmit, projectRef)).ShouldBeTrue();
            (await access.CanAsync(vendorPrincipal, PortfolioPermissions.ProjectRead, projectRef)).ShouldBeFalse("a vendor never holds an internal permission");
        }

        try
        {
            fx.Clock.Offset = TimeSpan.FromDays(100);
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                (await scope.ServiceProvider.GetRequiredService<IAccessService>().CanAsync(vendorPrincipal, WaselPermissions.ProjectRead, projectRef))
                    .ShouldBeFalse("the grant ended at access_until, before any job ran");
            }

            (await lead.GetAsync($"{Wasel}/projects/{projectId}")).StatusCode
                .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized); // the idle session has ended too after 100 days
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var result = await scope.ServiceProvider.GetRequiredService<WaselAccessRunner>().RunAsync(CancellationToken.None);
                result.ShouldContain("ended=");
                var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
                (await db.VendorAssignments.SingleAsync(a => a.ProjectId == projectId && a.VendorId == vendorId)).Status.ShouldBe(AssignmentStatuses.Ended);

                // Dormant accounts (90 days without a sign-in) are disabled by the same job.
                var dormant = await db.VendorUsers.Where(u => u.VendorId == vendorId).ToListAsync();
                dormant.ShouldAllBe(u => u.Status == VendorUserStatuses.Dormant);
            }

            await (await (await NewVendorClientAsync()).PostAsJsonAsync($"{Wasel}/auth/login", new { login = $"lead.{w.Suffix}@scoped.example", password = VendorPassword }))
                .KeyAsync(HttpStatusCode.BadRequest, "auth.errors.invalidCredentials");
        }
        finally
        {
            fx.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Suspending_a_vendor_ends_its_portal_access_at_once()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var (lead, projectId, vendorId, _) = await VendorOnProjectAsync(w, "Suspended");
        (await lead.GetAsync($"{Wasel}/projects/{projectId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Ok<VendorDetailDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/vendors/{vendorId}/status", new { status = VendorStatuses.Suspended, reason = "Audit" }));
        (await lead.GetAsync($"{Wasel}/projects/{projectId}")).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);

        // The register is PM Office business: a PM without the permission is refused.
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/vendors/{vendorId}/status", new { status = VendorStatuses.Active })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Capacity_view_warns_about_over_allocation_and_a_review_decision_of_hold_starts_a_change_request()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var first = await ProjectInPlanningAsync(w, "Capacity A");
        var second = await ProjectInPlanningAsync(w, "Capacity B");
        foreach (var (project, allocation) in new[] { (first, 0.6m), (second, 0.5m) })
        {
            await Ok<JsonElement>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{project.Id}/members",
                new { values = new { employeeId = w.SectionRequester.EmployeeId, roleEn = "Analyst", allocationPercent = allocation } }));
        }

        var capacity = await Ok<CapacityDto>(w.C(w.PmOffice).GetAsync($"{Base}/capacity?unitId={w.DepartmentId}&below=true"));
        var row = capacity.Rows.Single(r => r.EmployeeId == w.SectionRequester.EmployeeId);
        row.Total.ShouldBe(1.1m);
        row.OverAllocated.ShouldBeTrue();
        row.Allocations.Count.ShouldBe(2);
        capacity.OverAllocated.ShouldBeGreaterThanOrEqualTo(1);
        (await w.C(w.Requester).GetAsync($"{Base}/capacity?unitId={w.DepartmentId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Quarterly review: hold → a change request submitted into the normal approval; the project itself is unchanged.
        await DriveToExecutionAsync(first.Id);
        var review = await Ok<PortfolioReviewDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/reviews", new { period = $"{DateTime.UtcNow.Year}-Q4", title = "Q4 review" }));
        review = await Ok<PortfolioReviewDto>(w.C(w.PmOffice).PutAsJsonAsync($"{Base}/reviews/{review.Id}/decisions/{first.Id}",
            new { decision = ReviewDecisions.Hold, comment = "Budget freeze" }));
        review = await Ok<PortfolioReviewDto>(w.C(w.PmOffice).PutAsJsonAsync($"{Base}/reviews/{review.Id}/decisions/{second.Id}",
            new { decision = ReviewDecisions.Continue, comment = (string?)null }));
        review = await Ok<PortfolioReviewDto>(w.C(w.PmOffice).PostAsync($"{Base}/reviews/{review.Id}/complete", null));
        review.Status.ShouldBe(ReviewStatuses.Completed);
        var decided = review.Projects.Single(p => p.ProjectId == first.Id);
        decided.ChangeRequestId.ShouldNotBeNull();
        decided.ChangeRequestStatus.ShouldBe(ChangeStatuses.UnderReview);
        (await ProjectLifecycleTests.Get(w, first.Id)).Status.ShouldBe(ProjectStatuses.Active, "a review decision never changes the project directly");
        review.Projects.Single(p => p.ProjectId == second.Id).ChangeRequestId.ShouldBeNull();
        await (await w.C(w.PmOffice).PutAsJsonAsync($"{Base}/reviews/{review.Id}/decisions/{second.Id}", new { decision = ReviewDecisions.Cancel, comment = "late" }))
            .KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.reviewCompleted");
    }

    [Fact]
    public async Task Uae_pass_signs_in_only_an_active_vendor_account_by_its_email_and_still_requires_the_second_factor()
    {
        // ADR 0154: the installation's UAE Pass provider (a stand-in issuer on a loopback port) and the Wasel option.
        await using var issuer = await MockUaePassIssuer.StartStandaloneAsync();
        var origin = issuer.Urls.First();
        var admin = await fx.Factory.CreateSignedInClientAsync(admin: true);
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        (await (await NewVendorClientAsync()).GetFromJsonAsync<WaselSignInOptionsDto>($"{Wasel}/auth/options"))!.UaePass.ShouldBeFalse();
        (await (await NewVendorClientAsync()).GetAsync($"{Wasel}/auth/uae-pass/challenge")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (authorize, token, userInfo) = MockUaePassIssuer.Endpoints(origin);
        var created = await admin.PostAsJsonAsync("/api/v1/identity/admin/providers", new
        {
            code = "uaepass-wasel",
            type = "uaepass",
            displayNameAr = "الهوية الرقمية",
            displayNameEn = "UAE Pass",
            sort = 1,
            autoLink = false,
            jitCreateUser = false,
            secret = MockUaePassIssuer.ClientSecret,
            settings = new
            {
                environment = "staging",
                clientId = MockUaePassIssuer.ClientId,
                authorizationEndpoint = authorize,
                tokenEndpoint = token,
                userInfoEndpoint = userInfo,
                allowInsecureHttp = true,
                allowedUserTypes = "SOP2,SOP3",
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var providerId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/identity/admin/providers/{providerId}/enable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Provider on, vendor option still off: nothing offered.
        (await (await NewVendorClientAsync()).GetFromJsonAsync<WaselSignInOptionsDto>($"{Wasel}/auth/options"))!.UaePass.ShouldBeFalse();
        await WaselSettingsAsync(s => s with { Wasel = s.Wasel with { UaePassForVendors = true } });
        try
        {
            var options = await (await NewVendorClientAsync()).GetFromJsonAsync<WaselSignInOptionsDto>($"{Wasel}/auth/options");
            options!.UaePass.ShouldBeTrue();
            options.UaePassNameEn.ShouldBe("UAE Pass");

            var project = await ProjectInPlanningAsync(w, "UAE Pass vendor");
            var vendor = await CreateVendorAsync(w, "UAE Pass Co");
            await AssignAsync(w, project.Id, vendor.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
            var email = $"lead.{w.Suffix}@uaepass-vendor.example";
            await InviteAsync(w, project.Id, vendor.Id, email, VendorRoles.Lead);
            var (_, key) = await AcceptInvitationAndSignInAsync(email);

            // A known vendor (e-mail matched case-insensitively): half-signed-in, the second factor is still required.
            var browser = UaePassBrowser();
            (await UaePassSignInAsync(browser, origin, email.ToUpperInvariant())).ShouldStartWith("/wasel/login/mfa?methods=");
            await RefreshAntiforgeryAsync(browser);
            (await browser.GetAsync($"{Wasel}/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "no session before the second factor");
            (await browser.GetAsync($"{Wasel}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            await VerifyAsync(browser, key);
            (await browser.GetFromJsonAsync<WaselMeDto>($"{Wasel}/me"))!.Email.ShouldBe(email);
            (await browser.GetAsync($"{Wasel}/projects/{project.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // The internal application never accepts the vendor's sign-in.
            (await browser.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // Unknown e-mail, an internal account's e-mail and a disallowed UAE Pass user type: refused, nothing created, no enumeration.
            var unknown = $"nobody.{w.Suffix}@unknown.example";
            (await UaePassSignInAsync(UaePassBrowser(), origin, unknown)).ShouldBe("/wasel/login?error=wasel.auth.errors.uaePassNoAccount");
            (await UaePassSignInAsync(UaePassBrowser(), origin, w.PmOffice.UserName)).ShouldBe("/wasel/login?error=wasel.auth.errors.uaePassNoAccount");
            (await UaePassSignInAsync(UaePassBrowser(), origin, email, "SOP1")).ShouldBe("/wasel/login?error=auth.errors.externalUserType");
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
                (await users.Users.AnyAsync(u => u.Email == unknown)).ShouldBeFalse();
            }

            // A suspended vendor (its users may no longer enter) and a deactivated vendor account are refused.
            await Ok<VendorDetailDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/vendors/{vendor.Id}/status", new { status = VendorStatuses.Suspended, reason = "Audit" }));
            (await UaePassSignInAsync(UaePassBrowser(), origin, email)).ShouldBe("/wasel/login?error=wasel.auth.errors.uaePassNoAccount");
            await Ok<VendorDetailDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/vendors/{vendor.Id}/status", new { status = VendorStatuses.Active, reason = "Cleared" }));
            (await UaePassSignInAsync(UaePassBrowser(), origin, email)).ShouldStartWith("/wasel/login/mfa?methods=");
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
                var accounts = scope.ServiceProvider.GetRequiredService<IVendorAccounts>();
                await accounts.SetActiveAsync((await users.Users.SingleAsync(u => u.Email == email)).Id, false);
            }

            (await UaePassSignInAsync(UaePassBrowser(), origin, email)).ShouldBe("/wasel/login?error=wasel.auth.errors.uaePassNoAccount");

            // Every attempt is in the audit trail (provider "wasel:uaepass").
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
                var entries = await audit.SearchAsync(new AuditFilter(Action: "auth"), new PageRequest(1, 200));
                entries.Items.Count(e => e.Data?.Contains("wasel:uaepass", StringComparison.Ordinal) == true).ShouldBeGreaterThanOrEqualTo(6);
            }
        }
        finally
        {
            await WaselSettingsAsync(s => s with { Wasel = s.Wasel with { UaePassForVendors = false } });
            (await admin.PostAsync($"/api/v1/identity/admin/providers/{providerId}/disable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await (await NewVendorClientAsync()).GetAsync($"{Wasel}/auth/uae-pass/challenge")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient UaePassBrowser()
    {
        var client = fx.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Client", "spa");
        return client;
    }

    /// <summary>Plays the browser: Wasel challenge → issuer form → provider callback → Wasel callback. Returns the SPA address it ends on.</summary>
    private static async Task<string> UaePassSignInAsync(HttpClient browser, string issuerOrigin, string email, string userType = "SOP2")
    {
        var challenge = await browser.GetAsync($"{Wasel}/auth/uae-pass/challenge");
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync());
        var authorize = challenge.Headers.Location!;
        authorize.ToString().ShouldStartWith(issuerOrigin);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorize.Query);
        query["client_id"].ToString().ShouldBe(MockUaePassIssuer.ClientId);
        query.ContainsKey("code_challenge").ShouldBeTrue("PKCE");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var person = new HttpClient(handler);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["redirect_uri"] = query["redirect_uri"].ToString(),
            ["state"] = query["state"].ToString(),
            ["email"] = email,
            ["userType"] = userType,
        });
        var approved = await person.PostAsync(new Uri(authorize, MockUaePassIssuer.BasePath + "/authorize"), form);
        approved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var back = await browser.GetAsync(approved.Headers.Location!.PathAndQuery);
        var location = back.Headers.Location?.OriginalString ?? throw new InvalidOperationException(await back.Content.ReadAsStringAsync());
        while (location.StartsWith("/api/", StringComparison.Ordinal))
        {
            location = (await browser.GetAsync(location)).Headers.Location!.OriginalString;
        }

        return Uri.UnescapeDataString(location);
    }

    private async Task WaselSettingsAsync(Func<PortfolioSettings, PortfolioSettings> change)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var editor = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Settings.ISettingsEditor>();
        var current = await editor.GetDocumentAsync<PortfolioSettings>();
        (await editor.SaveAsync(change(current.Value), current.Version, PortfolioSettings.Validate)).IsSuccess.ShouldBeTrue();
    }

    private async Task<(HttpClient Lead, Guid ProjectId, Guid VendorId, Guid Milestone)> VendorOnProjectAsync(PortfolioWorld w, string name)
    {
        var project = await ProjectInPlanningAsync(w, name);
        var vendor = await CreateVendorAsync(w, name + " Co");
        await AssignAsync(w, project.Id, vendor.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var domain = name.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        var email = $"lead.{w.Suffix}@{domain}.example";
        await InviteAsync(w, project.Id, vendor.Id, email, VendorRoles.Lead);
        var milestone = await ProjectLifecycleTests.MilestoneAsync(w, project.Id, new
        {
            titleEn = "Vendor work",
            isHeader = false,
            highLevel = false,
            plannedStart = DateOnly.FromDateTime(DateTime.UtcNow),
            plannedEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            weight = 0.5m,
            cost = 7654321.25m,
        });
        await Ok<WaselPanelDto>(w.C(w.Requester).PutAsJsonAsync($"{Base}/milestones/{milestone.Id}/vendor", new { vendorId = vendor.Id }));
        var (client, _) = await AcceptInvitationAndSignInAsync(email);
        return (client, project.Id, vendor.Id, milestone.Id);
    }

    private async Task<ProjectFullDto> ProjectInPlanningAsync(PortfolioWorld w, string name)
    {
        var project = await ProjectLifecycleTests.ApprovedProjectAsync(w, $"{name} {w.Suffix}");
        await DriveToSubStateAsync(project.Id, SubStates.PlanningDraft, w.Requester);
        return await ProjectLifecycleTests.Get(w, project.Id);
    }

    private Task DriveToExecutionAsync(Guid projectId) => DriveToSubStateAsync(projectId, SubStates.Execution);

    /// <summary>Test set-up only: moves the project through the domain transitions (gates are tested in Phase 9).</summary>
    private async Task DriveToSubStateAsync(Guid projectId, string target, TestUser? pm = null)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var project = await db.Projects.SingleAsync(p => p.Id == projectId);
        var template = StageTemplate.ProjectDefault;
        project.UseTemplate(template);
        if (pm is not null)
        {
            project.SetPm(pm.EmployeeId!.Value, DateTimeOffset.UtcNow);
            await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, projectId, RecordPrincipalKinds.Employee,
                pm.EmployeeId!.Value, PortfolioPermissions.PmRole);
        }

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
                project.Apply(action, template, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue($"{project.SubState} × {action}");
            }
        }

        project.SubState.ShouldBe(target);
        await db.SaveChangesAsync();
    }

    private static async Task<VendorDetailDto> CreateVendorAsync(PortfolioWorld w, string name) =>
        await Ok<VendorDetailDto>(w.C(w.PmOffice).PostAsJsonAsync($"{Base}/vendors", new
        {
            values = new { nameEn = name, nameAr = "مورد " + name, tradeLicenceNo = $"TL-{w.Suffix}-{name.GetHashCode(StringComparison.Ordinal):x}", email = "info@vendor.example" },
        }));

    private static async Task AssignAsync(PortfolioWorld w, Guid projectId, Guid vendorId, DateOnly until) =>
        await Ok<WaselPanelDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{projectId}/wasel/assignments", new { vendorId, accessUntil = until, expiryRule = "date" }));

    private static async Task InviteAsync(PortfolioWorld w, Guid projectId, Guid vendorId, string email, string role) =>
        await Ok<WaselPanelDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/projects/{projectId}/wasel/invitations",
            new { vendorId, email, name = "Vendor person", role, language = "en" }));

    private async Task<(HttpClient Client, string TotpKey)> AcceptInvitationAndSignInAsync(string email)
    {
        var mail = await fx.Factory.Emails.LastAsync(email, "identity.waselInvitation");
        mail.Link.ShouldNotBeNull();
        mail.Link!.ShouldContain("/wasel/set-password");
        var (userId, token) = CapturingEmailTransport.ParseLink(mail.Link);
        var client = await NewVendorClientAsync();
        (await client.PostAsJsonAsync($"{Wasel}/auth/password/reset", new { userId, token, newPassword = VendorPassword, invitation = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginStatusAsync(client, email)).ShouldBe(SignInStatuses.MfaEnrolmentRequired);
        var setup = await Ok<TotpSetupDto>(client.PostAsync($"{Wasel}/auth/mfa/enrol/totp", null));
        var key = setup.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        await Ok<EnrolmentResultDto>(client.PostAsJsonAsync($"{Wasel}/auth/mfa/enrol/confirm", new { method = "totp", code = await Totp.NextCodeAsync(key) }));
        await RefreshAntiforgeryAsync(client);
        return (client, key);
    }

    private static async Task VerifyAsync(HttpClient client, string key)
    {
        (await Ok<SignInOutcome>(client.PostAsJsonAsync($"{Wasel}/auth/mfa/verify", new { method = "totp", code = await Totp.NextCodeAsync(key) })))
            .Status.ShouldBe(SignInStatuses.SignedIn);
        await RefreshAntiforgeryAsync(client);
    }

    private static async Task<string> LoginStatusAsync(HttpClient client, string email) =>
        (await Ok<SignInOutcome>(client.PostAsJsonAsync($"{Wasel}/auth/login", new { login = email, password = VendorPassword }))).Status;

    private async Task<HttpClient> NewVendorClientAsync()
    {
        var client = fx.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Client", "spa");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await RefreshAntiforgeryAsync(client);
        return client;
    }

    private static async Task RefreshAntiforgeryAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryToken>($"{Wasel}/antiforgery/token");
        client.DefaultRequestHeaders.Remove(token!.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }

    private static async Task<List<VendorDeliverableDto>> UploadDeliverableAsync(HttpClient client, Guid projectId, Guid milestoneId, string title)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "handover.pdf");
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(milestoneId.ToString()), "milestoneId");
        return await Ok<List<VendorDeliverableDto>>(client.PostAsync($"{Wasel}/projects/{projectId}/deliverables", form));
    }

    private static HttpRequestMessage Request(string method, string route)
    {
        var path = System.Text.RegularExpressions.Regex.Replace(route, @"\{(?<name>[a-zA-Z]+)(:[^}]*)?\}", m => m.Groups["name"].Value switch
        {
            "kind" => "units",
            "code" or "key" or "type" => "unknown",
            "versionNo" => "1",
            "year" => "2030",
            "period" => "q1",
            _ => Guid.CreateVersion7().ToString(),
        });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static Task<T> Ok<T>(Task<HttpResponseMessage> call) => PortfolioWorld.OkAsync<T>(call);
}
