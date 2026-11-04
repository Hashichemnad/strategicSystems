using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Kpi;
using Strategy.IntegrationTests.Portfolio;
using Strategy.IntegrationTests.Teams;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Risk;

/// <summary>One host per test class with a capturing PDF renderer (no Gotenberg needed for the report tests).</summary>
public sealed class RiskFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public CapturingPdfRenderer Pdf { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(Pdf);
        });
        await Factory.GrantSystemAccessAsync(RiskPermissions.SystemCode, AccessGrants.Everyone, null);
        await Factory.GrantSystemAccessAsync(KpiPermissions.SystemCode, AccessGrants.Everyone, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// Department A (code <c>RA…</c>) and A2 whose code differs only by case (<c>ra…</c>: both render <c>RA…</c> in numbers), department B. The Risk
/// Office (MFA), the Risk Committee, coordinators of A and B, a plain risk owner in A, an action owner, a viewer of A and an outsider.
/// </summary>
internal sealed class RiskWorld
{
    public const string Base = "/api/v1/risk";

    public required ApiFactory Factory { get; init; }

    public required string Code { get; init; }

    public required Guid DepartmentA { get; init; }

    public required Guid DepartmentA2 { get; init; }

    public required Guid DepartmentB { get; init; }

    public required TestUser Office { get; init; }

    public required TestUser Committee { get; init; }

    public required TestUser CoordinatorA { get; init; }

    public required TestUser CoordinatorB { get; init; }

    public required TestUser OwnerA { get; init; }

    public required TestUser ActionOwner { get; init; }

    public required TestUser ViewerA { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<RiskWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        var code = $"RA{suffix}";
        Guid organisation, a, a2, b;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"RO{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            a = (await dispatcher.SendAsync(new CreateUnit(code, "إدارة أ", $"Department A {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            a2 = (await dispatcher.SendAsync(new CreateUnit(code.ToLowerInvariant(), "إدارة أ٢", $"Department A2 {suffix}", "department", organisation, null, null, null, null)))
                .Value.Id;
            b = (await dispatcher.SendAsync(new CreateUnit($"RB{suffix}", "إدارة ب", $"Department B {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var office = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: RiskPermissions.RiskOfficeRoleKey);
        var committee = await factory.CreateUserAsync(unitId: organisation, roles: RiskPermissions.RiskCommitteeRoleKey);
        var coordinatorA = await factory.CreateUserAsync(unitId: organisation, roles: "risk.coordinator");
        var coordinatorB = await factory.CreateUserAsync(unitId: b, roles: "risk.coordinator");
        var ownerA = await factory.CreateUserAsync(unitId: a);
        var actionOwner = await factory.CreateUserAsync(unitId: a);
        var viewerA = await factory.CreateUserAsync(unitId: a, roles: "risk.viewer");
        var outsider = await factory.CreateUserAsync();
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { office, committee, coordinatorA, coordinatorB, ownerA, actionOwner, viewerA, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new RiskWorld
        {
            Factory = factory,
            Code = code,
            DepartmentA = a,
            DepartmentA2 = a2,
            DepartmentB = b,
            Office = office,
            Committee = committee,
            CoordinatorA = coordinatorA,
            CoordinatorB = coordinatorB,
            OwnerA = ownerA,
            ActionOwner = actionOwner,
            ViewerA = viewerA,
            Outsider = outsider,
            Clients = clients,
        };
    }

    public static object Values(string name, Guid unit, TestUser owner, int l, int i, int? rl = null, int? ri = null, int? tl = null, int? ti = null,
        string category = "operational", DateOnly? identified = null) => new
        {
            titleAr = "خطر " + name,
            titleEn = name,
            cause = "Legacy platform without vendor support",
            eventText = $"{name} happens during peak period",
            consequence = "Services stop and customers complain",
            identifiedOn = identified ?? new DateOnly(2026, 2, 1),
            riskType = "internal",
            categoryCodes = new[] { category },
            emerging = false,
            opportunity = false,
            unitId = unit,
            ownerEmployeeId = owner.EmployeeId,
            inherentLikelihood = l,
            inherentImpact = i,
            residualLikelihood = rl,
            residualImpact = ri,
            targetLikelihood = tl,
            targetImpact = ti,
            responseCode = "reduce",
            needsFollowUp = true,
        };

    public async Task<RiskDto> CreateAsync(object values, TestUser? user = null)
    {
        var response = await C(user ?? CoordinatorA).PostAsJsonAsync($"{Base}/risks", values);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RiskDto>())!;
    }

    public async Task<RiskDto> GetAsync(Guid id, TestUser? user = null) => (await C(user ?? Office).GetFromJsonAsync<RiskDto>($"{Base}/risks/{id}"))!;

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>The in-progress instance of <paramref name="process"/> on the risk, as <paramref name="approver"/> sees it.</summary>
    public async Task<InstanceDto> InstanceAsync(Guid riskId, string process, TestUser approver)
    {
        var panel = await OkAsync<SubjectApprovalsDto>(C(approver).GetAsync($"/api/v1/workflow/subjects/{RiskRecord.EntityType}/{riskId}"));
        return panel.Instances.First(i => i.ProcessKey == process && i.Status == ApprovalInstanceStatuses.InProgress);
    }

    public async Task ActAsync(Guid riskId, string process, TestUser approver, string expectedStep, string action = "approve", string? comment = null)
    {
        var instance = await InstanceAsync(riskId, process, approver);
        instance.Steps.Single(s => s.State == StepStates.Current).Code.ShouldBe(expectedStep);
        instance.MyTask.ShouldNotBeNull();
        await ApprovalWorld.ActOkAsync(C(approver), instance.MyTask!.TaskId, action, comment);
    }

    public async Task<RiskDto> SubmitAndApproveAsync(RiskDto risk)
    {
        await OkAsync<RiskDto>(C(CoordinatorA).PostAsJsonAsync($"{Base}/risks/{risk.Id}/submit", new { comment = "please review" }));
        await ActAsync(risk.Id, RiskProcesses.Review, Office, "risk-office");
        var active = await GetAsync(risk.Id);
        active.Status.ShouldBe(RiskStatuses.Active);
        return active;
    }
}

/// <summary>Risk Management end to end (docs/design/risk.md §17) on a real database.</summary>
public sealed class RiskApiTests(RiskFixture fx) : IClassFixture<RiskFixture>
{
    private const string Base = RiskWorld.Base;

    [Fact]
    public async Task Register_submit_and_approve_with_numbers_unique_per_code_and_year_and_review_dates_in_working_time()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var first = await w.CreateAsync(RiskWorld.Values("Payment outage", w.DepartmentA, w.OwnerA, 5, 4, rl: 4, ri: 4, tl: 2, ti: 3));
        var second = await w.CreateAsync(RiskWorld.Values("Data leak", w.DepartmentA2, w.OwnerA, 3, 3));
        first.Number.ShouldBe($"{w.Code}26-R001");
        second.Number.ShouldBe($"{w.Code}26-R002", "units whose codes render the same share one sequence per year: numbers never collide");
        var nextYear = await w.CreateAsync(RiskWorld.Values("Next year", w.DepartmentA, w.OwnerA, 1, 1, identified: new DateOnly(2027, 1, 5)));
        nextYear.Number.ShouldBe($"{w.Code}27-R001");
        first.Status.ShouldBe(RiskStatuses.Draft);
        (first.InherentScore, first.InherentBand, first.ResidualScore, first.ResidualBand, first.TargetScore, first.TargetBand)
            .ShouldBe((20m, "critical", 16m, "high", 6m, "medium"));
        first.ResidualStatus.ShouldBe(ResidualStatuses.ActiveManagement);
        first.WithinAppetite.ShouldBeNull("Q-R3: appetite is empty by default");

        // Validation: the statement needs cause → event → consequence and valid ratings.
        var bad = await w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks", new
        {
            titleEn = "Vague", identifiedOn = new DateOnly(2026, 1, 1), riskType = "internal", categoryCodes = Array.Empty<string>(), unitId = w.DepartmentA,
            inherentLikelihood = 6, inherentImpact = 1,
        });
        await bad.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        // Scope: the owner reads (own), coordinator B and the outsider get 404, coordinator B may not create in A (403).
        (await w.GetAsync(first.Id, w.OwnerA)).Can.Edit.ShouldBeTrue();
        (await w.C(w.CoordinatorB).GetAsync($"{Base}/risks/{first.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/risks/{first.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.CoordinatorB).PostAsJsonAsync($"{Base}/risks", RiskWorld.Values("Foreign", w.DepartmentA, w.OwnerA, 2, 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // R1: submit → Risk Office approves → active, next review by the residual rating (High → 1 month) on a working day.
        var active = await w.SubmitAndApproveAsync(first);
        active.LastReviewOn.ShouldNotBeNull();
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var deadlines = scope.ServiceProvider.GetRequiredService<RiskDeadlines>();
            var (expected, _) = await deadlines.NextReviewAsync(w.DepartmentA, active.LastReviewOn!.Value, 1, CancellationToken.None);
            active.NextReviewOn.ShouldBe(expected);
        }

        var assessments = await RiskWorld.OkAsync<List<AssessmentDto>>(w.C(w.OwnerA).GetAsync($"{Base}/risks/{first.Id}/assessments"));
        assessments.Select(a => a.Kind).ShouldBe([AssessmentKinds.Registration, AssessmentKinds.Approval]);
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "risk.approved");

        // Return for correction and rejection.
        await RiskWorld.OkAsync<RiskDto>(w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{second.Id}/submit", new { comment = (string?)null }));
        await w.ActAsync(second.Id, RiskProcesses.Review, w.Office, "risk-office", "return", "Clarify the consequence");
        var returned = await w.GetAsync(second.Id);
        (returned.Status, returned.Returned, returned.ReturnComment).ShouldBe((RiskStatuses.Draft, true, "Clarify the consequence"));
        await RiskWorld.OkAsync<RiskDto>(w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{second.Id}/submit", new { comment = "fixed" }));
        await w.ActAsync(second.Id, RiskProcesses.Review, w.Office, "risk-office", "reject", "Duplicate of an existing risk");
        (await w.GetAsync(second.Id)).Status.ShouldBe(RiskStatuses.Rejected);
    }

    [Fact]
    public async Task Review_due_dates_follow_the_rating_and_roll_to_the_next_working_day()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var deadlines = scope.ServiceProvider.GetRequiredService<RiskDeadlines>();
        var scorer = new RiskScorer(DefaultFramework.Content);

        // Thursday 10 September 2026 + 1 month = Saturday 10 October → Monday 12 October, due at the end of working time (15:30 Dubai = 11:30 UTC).
        var (critical, due) = await deadlines.NextReviewAsync(w.DepartmentA, new DateOnly(2026, 9, 10), scorer.ReviewMonths("critical"), CancellationToken.None);
        critical.ShouldBe(new DateOnly(2026, 10, 12));
        due.ShouldBe(new DateTimeOffset(2026, 10, 12, 11, 30, 0, TimeSpan.Zero));
        (await deadlines.NextReviewAsync(w.DepartmentA, new DateOnly(2026, 9, 10), scorer.ReviewMonths("medium"), CancellationToken.None)).Date
            .ShouldBe(new DateOnly(2026, 12, 10));
        (await deadlines.NextReviewAsync(w.DepartmentA, new DateOnly(2026, 9, 10), scorer.ReviewMonths("low"), CancellationToken.None)).Date
            .ShouldBe(new DateOnly(2027, 3, 10));
    }

    [Fact]
    public async Task Framework_preview_counts_affected_risks_and_recalculation_is_versioned_and_audited()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var changes = await w.CreateAsync(RiskWorld.Values("Rating changes", w.DepartmentA, w.OwnerA, 2, 5));
        var stays = await w.CreateAsync(RiskWorld.Values("Rating stays", w.DepartmentA, w.OwnerA, 1, 1));
        changes.InherentBand.ShouldBe("high");
        var current = await RiskWorld.OkAsync<FrameworkVersionDto>(w.C(w.Office).GetAsync($"{Base}/framework"));
        var additive = current.Content with { ScoringMethod = ScoringMethods.Additive };

        // Preview: additive 2 + 5 = 7 → Medium; 1 + 1 = 2 → Low (unchanged). Nothing is saved.
        var preview = await RiskWorld.OkAsync<FrameworkPreviewDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/framework/preview", new { content = additive }));
        preview.Valid.ShouldBeTrue();
        preview.Matrix.Single(c => c.Likelihood == 5 && c.Impact == 5).Score.ShouldBe(10m);
        preview.Examples.ShouldContain(e => e.Id == changes.Id && e.FromBand == "high" && e.ToBand == "medium");
        preview.Examples.ShouldNotContain(e => e.Id == stays.Id);
        preview.AffectedRisks.ShouldBeGreaterThanOrEqualTo(1);
        (await w.GetAsync(changes.Id)).InherentBand.ShouldBe("high");

        // An invalid formula is reported by the preview and refused on save; only the framework manager may preview.
        var invalid = await RiskWorld.OkAsync<FrameworkPreviewDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/framework/preview",
            new { content = current.Content with { ScoringMethod = ScoringMethods.Custom, Formula = "L * Q" } }));
        invalid.Valid.ShouldBeFalse();
        invalid.Errors.ShouldContain(e => e.Field == "formula");
        (await w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/framework/preview", new { content = additive })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Save "keep until next review": a new version, the risk keeps its rating and its old version.
        var kept = await RiskWorld.OkAsync<FrameworkVersionDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/framework",
            new { content = additive, reason = "Try additive", recalculation = RiskRecalculation.NextReview }));
        kept.VersionNo.ShouldBe(current.VersionNo + 1);
        var notMoved = await w.GetAsync(changes.Id);
        notMoved.InherentBand.ShouldBe("high");
        notMoved.OnCurrentFramework.ShouldBeFalse();

        // Save a custom formula with "recalculate now": re-rated, snapshot and audit entry per changed risk.
        var custom = current.Content with { ScoringMethod = ScoringMethods.Custom, Formula = "L * I + 1" };
        var now = await RiskWorld.OkAsync<FrameworkVersionDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/framework",
            new { content = custom, reason = "Custom formula", recalculation = RiskRecalculation.Now }));
        now.VersionNo.ShouldBe(current.VersionNo + 2);
        var moved = await w.GetAsync(changes.Id);
        (moved.InherentScore, moved.InherentBand, moved.FrameworkVersionNo, moved.OnCurrentFramework).ShouldBe((11m, "high", now.VersionNo, true));
        var low = await w.GetAsync(stays.Id);
        (low.InherentScore, low.InherentBand).ShouldBe((2m, "low"));
        var snapshots = await RiskWorld.OkAsync<List<AssessmentDto>>(w.C(w.Office).GetAsync($"{Base}/risks/{changes.Id}/assessments"));
        snapshots.ShouldContain(a => a.Kind == AssessmentKinds.Recalculation && a.FrameworkVersionNo == now.VersionNo);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var history = await scope.ServiceProvider.GetRequiredService<IAuditQuery>().HistoryAsync(RiskRecord.EntityType, changes.Id.ToString("D"), new PageRequest(1, 200));
            history.Items.ShouldContain(e => e.Field != null && e.Field.Contains("inherent", StringComparison.OrdinalIgnoreCase) && e.Field.Contains("score", StringComparison.OrdinalIgnoreCase)
                && e.NewValue != null && e.NewValue.StartsWith("11", StringComparison.Ordinal));
        }

        var versions = await RiskWorld.OkAsync<List<FrameworkSummaryDto>>(w.C(w.Office).GetAsync($"{Base}/framework/versions"));
        versions.Select(v => v.VersionNo).ShouldContain(kept.VersionNo);

        // Back to the default so other tests of this class see the SPEC framework.
        await RiskWorld.OkAsync<FrameworkVersionDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/framework",
            new { content = current.Content, reason = "Back to default", recalculation = RiskRecalculation.Now }));
        (await w.GetAsync(changes.Id)).InherentScore.ShouldBe(10m);
    }

    [Fact]
    public async Task A_kri_breach_on_an_approved_kpi_result_flags_the_risk_and_notifies_the_owner()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var pmo = await fx.Factory.CreateUserAsync(withMfa: true, roles: KpiPermissions.StrategyOfficeRoleKey);
        var pmoClient = await fx.Factory.SignInAsync(pmo);
        var cardResponse = await pmoClient.PostAsJsonAsync("/api/v1/kpi/cards", new { values = KpiWorld.Card("Complaints", w.CoordinatorA, w.DepartmentA, direction: "decrease") });
        cardResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var card = (await cardResponse.Content.ReadFromJsonAsync<KpiCardDto>())!;
        var risk = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Service complaints", w.DepartmentA, w.OwnerA, 3, 3)));
        var kri = await RiskWorld.OkAsync<KriDto>(w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{risk.Id}/kris",
            new { values = new { kpiCardId = card.Id, basis = KriBases.Value, direction = KriDirections.Above, warningThreshold = 80, breachThreshold = 100 } }));
        kri.CardCode.ShouldBe(card.Code);
        (await w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{risk.Id}/kris",
                new { values = new { kpiCardId = card.Id, basis = KriBases.Value, direction = KriDirections.Above, warningThreshold = 120, breachThreshold = 100 } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the warning comes before the breach");

        // The KPI card lists the risk as a linked record.
        var linked = (await pmoClient.GetFromJsonAsync<List<Strategy.Platform.Links.LinkView>>($"/api/v1/kpi/cards/{card.Id}/linked"))!;
        linked.ShouldContain(l => l.Id == risk.Id);

        // An approved result of 85 is a warning (no flag); 120 is a breach.
        await PublishAsync(card, 85m);
        await WaitAsync(async () => (await RiskWorld.OkAsync<List<KriDto>>(w.C(w.OwnerA).GetAsync($"{Base}/risks/{risk.Id}/kris"))).Single().LastLevel == KriLevels.Warning);
        (await w.GetAsync(risk.Id)).ReassessmentRequired.ShouldBeFalse();
        await PublishAsync(card, 120m);
        await WaitAsync(async () => (await w.GetAsync(risk.Id)).ReassessmentRequired);
        var flagged = await w.GetAsync(risk.Id);
        flagged.ReassessmentReason.ShouldBe(ReassessmentReasons.KriBreach);
        var notification = await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "risk.kriBreach");
        notification.Link.ShouldBe($"/risk/risks/{risk.Id}?tab=kris");
        var dashboard = await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?includeProjectRisks=false"));
        dashboard.KriBreaches.ShouldContain(b => b.RiskId == risk.Id && b.Value == 120m);
        var work = (await w.C(w.OwnerA).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == risk.Id && i.MessageKey == "risk.myWork.reassessment");
    }

    private async Task PublishAsync(KpiCardDto card, decimal achieve)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new KpiResultApproved(Guid.CreateVersion7(), card.Id, card.Code, 2026, "q1",
            achieve, null, null, RagCodes.Red, false, null, DateTimeOffset.UtcNow));
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        (await condition()).ShouldBeTrue("the outbox should have delivered the event");
    }

    [Fact]
    public async Task Closure_of_a_critical_risk_needs_the_risk_committee_step_and_a_medium_one_does_not()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var critical = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Critical risk", w.DepartmentA, w.OwnerA, 5, 5)));
        critical.Escalated.ShouldBeTrue("Critical risks are escalated to the committee automatically");
        var medium = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Medium risk", w.DepartmentA, w.OwnerA, 2, 3)));

        await RiskWorld.OkAsync<RiskDto>(w.C(w.OwnerA).PostAsJsonAsync($"{Base}/risks/{critical.Id}/request-closure", new { reasonCode = "fully-treated", note = "Done" }));
        (await w.GetAsync(critical.Id)).PendingProcess.ShouldBe(RiskProcesses.Closure);
        (await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/risks/{critical.Id}/request-closure", new { reasonCode = "fully-treated" }))
            .IsSuccessStatusCode.ShouldBeFalse("one pending process at a time");
        await w.ActAsync(critical.Id, RiskProcesses.Closure, w.Office, "risk-office");
        (await w.GetAsync(critical.Id)).Status.ShouldBe(RiskStatuses.Active, "the Risk Committee still has to approve");
        await w.ActAsync(critical.Id, RiskProcesses.Closure, w.Committee, "risk-committee");
        var closed = await w.GetAsync(critical.Id);
        (closed.Status, closed.ClosureReasonCode, closed.Escalated).ShouldBe((RiskStatuses.Closed, "fully-treated", false));

        await RiskWorld.OkAsync<RiskDto>(w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{medium.Id}/request-closure", new { reasonCode = "no-longer-relevant" }));
        await w.ActAsync(medium.Id, RiskProcesses.Closure, w.Office, "risk-office");
        (await w.GetAsync(medium.Id)).Status.ShouldBe(RiskStatuses.Closed, "the committee step is skipped below Critical");

        // The Risk Office may reopen with a reason.
        (await w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{medium.Id}/reopen", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reopened = await RiskWorld.OkAsync<RiskDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/risks/{medium.Id}/reopen", new { reason = "Recurred" }));
        reopened.Status.ShouldBe(RiskStatuses.Active);
    }

    [Fact]
    public async Task Controls_actions_reviews_and_record_roles_follow_the_access_rules()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var risk = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Fraud", w.DepartmentA, w.OwnerA, 4, 4, rl: 4, ri: 4)));
        var c = w.C(w.CoordinatorA);

        // Controls drive the residual: an effective control with a custom-free framework does not change L × I, the overall effectiveness is shown.
        var control = await RiskWorld.OkAsync<ControlDto>(c.PostAsJsonAsync($"{Base}/risks/{risk.Id}/controls",
            new { values = new { nameEn = "Dual approval", typeCode = "preventive", natureCode = "manual", effectivenessCode = "effective" } }));
        (await w.GetAsync(risk.Id)).ControlEffectiveness.ShouldBe("effective");

        // Treatment action with an owner: the owner gets the action-owner record role (reads the risk, updates only their action).
        var action = await RiskWorld.OkAsync<ActionDto>(c.PostAsJsonAsync($"{Base}/risks/{risk.Id}/actions", new
        {
            values = new { titleEn = "Segregate duties", ownerEmployeeId = w.ActionOwner.EmployeeId, plannedStart = new DateOnly(2026, 1, 1), plannedEnd = new DateOnly(2026, 6, 30) },
        }));
        action.Number.ShouldStartWith("ORA-");
        var other = await RiskWorld.OkAsync<ActionDto>(c.PostAsJsonAsync($"{Base}/risks/{risk.Id}/actions", new
        {
            values = new { titleEn = "Audit trail", ownerEmployeeId = w.OwnerA.EmployeeId, plannedEnd = new DateOnly(2026, 12, 31) },
        }));
        (await w.C(w.ActionOwner).GetAsync($"{Base}/risks/{risk.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.ActionOwner).PutAsJsonAsync($"{Base}/risks/{risk.Id}", new { values = risk.Values, version = risk.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.ActionOwner).PostAsJsonAsync($"{Base}/actions/{other.Id}/progress", new { completionPct = 0.5, note = "not mine", version = other.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var mine = await RiskWorld.OkAsync<List<ActionDto>>(w.C(w.ActionOwner).GetAsync($"{Base}/my-actions"));
        mine.Single().Id.ShouldBe(action.Id);
        var half = await RiskWorld.OkAsync<ActionDto>(w.C(w.ActionOwner).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress",
            new { completionPct = 0.5, note = "Half the teams moved", version = action.Version }));
        half.Status.ShouldBe(ActionStatuses.InProgress);
        var done = await RiskWorld.OkAsync<ActionDto>(w.C(w.ActionOwner).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress",
            new { completionPct = 1, note = "All teams moved", version = half.Version }));
        (done.Status, done.CanConvert).ShouldBe((ActionStatuses.Completed, true));
        (await RiskWorld.OkAsync<List<ActionUpdateDto>>(w.C(w.ActionOwner).GetAsync($"{Base}/actions/{action.Id}/updates"))).Count.ShouldBe(2);

        // Completed action → control (rated Ineffective → CAP contract event), the risk is flagged for reassessment.
        var converted = await RiskWorld.OkAsync<ControlDto>(w.C(w.ActionOwner).PostAsJsonAsync($"{Base}/actions/{action.Id}/convert-to-control",
            new { effectivenessCode = "ineffective", typeCode = "preventive" }));
        converted.SourceActionId.ShouldBe(action.Id);
        var reassess = await w.GetAsync(risk.Id);
        (reassess.ReassessmentRequired, reassess.ReassessmentReason, reassess.ControlEffectiveness).ShouldBe((true, ReassessmentReasons.ControlAdded, "ineffective"));

        // Viewer of A reads, cannot write; the outsider sees nothing.
        (await w.C(w.ViewerA).GetAsync($"{Base}/risks/{risk.Id}/controls")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.ViewerA).PostAsJsonAsync($"{Base}/risks/{risk.Id}/controls", new { values = new { nameEn = "x" } })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var path in new[] { "", "/controls", "/actions", "/kris", "/reviews", "/assessments", "/audit" })
        {
            (await w.C(w.Outsider).GetAsync($"{Base}/risks/{risk.Id}{path}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        // R3: periodic review — the owner rates controls and the residual; the Risk Office approves; snapshot and next review.
        var review = await RiskWorld.OkAsync<ReviewDto>(w.C(w.OwnerA).PostAsync($"{Base}/risks/{risk.Id}/reviews", null));
        var saved = await RiskWorld.OkAsync<ReviewDto>(w.C(w.OwnerA).PutAsJsonAsync($"{Base}/reviews/{review.Id}", new
        {
            values = new
            {
                residualLikelihood = 2, residualImpact = 3, controlRatings = new Dictionary<Guid, string> { [control.Id] = "effective", [converted.Id] = "partially-effective" },
                commentary = "Controls now work",
            },
            version = review.Version,
        }));
        saved.PreviewResidualScore.ShouldBe(6m);
        await RiskWorld.OkAsync<ReviewDto>(w.C(w.OwnerA).PostAsJsonAsync($"{Base}/reviews/{review.Id}/submit", new { comment = (string?)null }));
        await w.ActAsync(risk.Id, RiskProcesses.PeriodicReview, w.Office, "risk-office");
        var reviewed = await w.GetAsync(risk.Id);
        (reviewed.ResidualScore, reviewed.ResidualBand, reviewed.ReassessmentRequired, reviewed.PendingProcess).ShouldBe((6m, "medium", false, (string?)null));
        (await RiskWorld.OkAsync<List<AssessmentDto>>(w.C(w.OwnerA).GetAsync($"{Base}/risks/{risk.Id}/assessments"))).ShouldContain(a => a.Kind == AssessmentKinds.Review);

        // A logged event flags the risk again; the event is numbered and listed.
        var logged = await RiskWorld.OkAsync<EventDto>(c.PostAsJsonAsync($"{Base}/events", new
        {
            values = new { occurredOn = new DateOnly(2026, 3, 1), description = "Payment released twice", financialImpact = 12000, nearMiss = false, riskIds = new[] { risk.Id } },
        }));
        logged.Number.ShouldStartWith("RE-");
        (await w.GetAsync(risk.Id)).ReassessmentReason.ShouldBe(ReassessmentReasons.Event);
    }

    [Fact]
    public async Task Dashboard_heatmaps_top10_reports_and_mcp_tools()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var critical = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Top risk", w.DepartmentA, w.OwnerA, 5, 5, rl: 5, ri: 4)));
        var flagged = await w.SubmitAndApproveAsync(await w.CreateAsync(RiskWorld.Values("Ranked first", w.DepartmentA, w.OwnerA, 2, 2)));
        await RiskWorld.OkAsync<RiskDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/risks/{flagged.Id}/office-update",
            new { top10 = true, top10Rank = 1, version = (await w.GetAsync(flagged.Id)).Version }));
        var dashboard = await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?unitId={w.DepartmentA}"));
        dashboard.Inherent.Single(c => c.Likelihood == 5 && c.Impact == 5).Enterprise.ShouldBe(1);
        dashboard.Residual.Single(c => c.Likelihood == 5 && c.Impact == 4).Enterprise.ShouldBe(1);
        dashboard.Top10.Select(r => r.Id).Take(2).ShouldBe([flagged.Id, critical.Id]);
        var cell = await RiskWorld.OkAsync<HeatCellRisksDto>(w.C(w.Office).GetAsync($"{Base}/heatmap/inherent/5/5?unitId={w.DepartmentA}"));
        cell.Risks.Single().Id.ShouldBe(critical.Id);
        (await w.C(w.OwnerA).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var top = await RiskWorld.OkAsync<PagedResult<RiskListItemDto>>(w.C(w.Office).GetAsync($"{Base}/risks?view=top10"));
        top.Items[0].Id.ShouldBe(flagged.Id);

        foreach (var kind in new[] { "register", "top10", "unit-profile", "action-tracker" })
        {
            foreach (var format in new[] { "pdf", "xlsx" })
            {
                var file = await w.C(w.Office).GetAsync($"{Base}/reports/{kind}?format={format}&language=ar");
                file.StatusCode.ShouldBe(HttpStatusCode.OK, $"{kind}.{format}");
            }
        }

        fx.Pdf.Html.ShouldContain(h => h.Contains("سجل المخاطر", StringComparison.Ordinal) && h.Contains(critical.Number, StringComparison.Ordinal));
        (await w.C(w.OwnerA).GetAsync($"{Base}/reports/register?format=pdf")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var mechanism = await w.C(w.OwnerA).GetAsync($"{Base}/mechanism/pdf?language=en");
        mechanism.StatusCode.ShouldBe(HttpStatusCode.OK);
        fx.Pdf.Html.ShouldContain(h => h.Contains("Risk Assessment Mechanism", StringComparison.Ordinal) && h.Contains("Catastrophic", StringComparison.Ordinal));

        await using var scope = TestAccess.SystemScope(fx.Factory);
        var tools = scope.ServiceProvider.GetServices<IAppTool>().Where(t => t.Definition.SystemCode == RiskPermissions.SystemCode).ToList();
        tools.Select(t => t.Definition.Name).ShouldBe(
            ["risk_register_search", "risk_details", "my_risk_actions", "risk_heatmap_summary", "risk_top10", "risks_outside_appetite", "update_risk_action_progress_draft"],
            ignoreOrder: true);
        tools.Where(t => t.Definition.Kind == ToolKind.Write).ShouldAllBe(t => !t.Definition.DefaultEnabled);
        tools.ShouldAllBe(t => !t.Definition.Name.Contains("approve", StringComparison.Ordinal));
    }
}

/// <summary>
/// Project Risks (ADR 0103) and Portfolio scoring on the shared framework (ADR 0101) — a separate host because the test changes the framework.
/// </summary>
public sealed class RiskProjectTests(RiskFixture fx) : IClassFixture<RiskFixture>
{
    [Fact]
    public async Task Project_risks_are_read_live_with_project_scope_never_copied_and_scored_by_the_shared_framework()
    {
        var p = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ProjectLifecycleTests.ApprovedProjectAsync(p, "Risky project");
        await TakeOwnershipAsync(project.Id, p.Requester);
        var projectRisk = await PortfolioWorld.OkAsync<ProjectRiskDto>(p.C(p.PmOffice).PostAsJsonAsync($"/api/v1/portfolio/projects/{project.Id}/risks",
            new { values = new { title = "Vendor delay", probability = 4, impact = 5, controlEvaluation = 6, response = "mitigate" } }));
        (projectRisk.InherentScore, projectRisk.InherentBand, projectRisk.ResidualScore, projectRisk.ResidualBand).ShouldBe((20, "critical", 10m, "high"));

        // Same ratings, same score: an enterprise risk 4 × 5 is Critical too.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var shared = await scope.ServiceProvider.GetRequiredService<IRiskScoring>().ScoreProjectRiskAsync(4, 5, 6);
            (shared.InherentScore, shared.InherentBand, shared.ResidualScore, shared.ResidualBand).ShouldBe((20m, "critical", 10m, "high"));
        }

        // The PM Office (all projects) and the requester (their project) see it once given the Risk role; another department sees none.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            foreach (var user in new[] { p.PmOffice, p.Requester, p.OtherRequester })
            {
                await TestAccess.GrantRoleAsync(scope.ServiceProvider, user.Id, "risk.coordinator");
            }
        }

        var countBefore = await CountRisksAsync();
        var requester = await fx.Factory.SignInAsync(p.Requester);
        var mine = await RiskWorld.OkAsync<List<ProjectRiskRowDto>>(requester.GetAsync($"{RiskWorld.Base}/project-risks?projectId={project.Id}"));
        var row = mine.Single();
        (row.Id, row.Title, row.ResidualBand, row.Link).ShouldBe((projectRisk.Id, "Vendor delay", "high", $"/portfolio/projects/{project.Id}?tab=risks"));
        var other = await fx.Factory.SignInAsync(p.OtherRequester);
        (await RiskWorld.OkAsync<List<ProjectRiskRowDto>>(other.GetAsync($"{RiskWorld.Base}/project-risks"))).ShouldNotContain(r => r.Id == projectRisk.Id);
        (await p.C(p.Outsider).GetAsync($"{RiskWorld.Base}/project-risks")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var dashboard = await RiskWorld.OkAsync<RiskDashboardDto>(requester.GetAsync($"{RiskWorld.Base}/dashboard?includeProjectRisks=true"));
        dashboard.ByCategory.ShouldContain(c => c.Code == RiskPermissions.ProjectCategory && c.Count >= 1);
        dashboard.Inherent.Single(c => c.Likelihood == 4 && c.Impact == 5).Project.ShouldBeGreaterThanOrEqualTo(1);
        (await CountRisksAsync()).ShouldBe(countBefore, "project risks are never copied into the enterprise register");

        // A framework change with "recalculate now" re-rates project risks with an audit entry per change (ADR 0101).
        // The preview counts the project risks the Risk Office member may read (here: also PM Office, all projects).
        var office = await fx.Factory.CreateUserAsync(withMfa: true, roles: [RiskPermissions.RiskOfficeRoleKey, PortfolioPermissions.PmOfficeRoleKey]);
        var officeClient = await fx.Factory.SignInAsync(office);
        var current = await RiskWorld.OkAsync<FrameworkVersionDto>(officeClient.GetAsync($"{RiskWorld.Base}/framework"));
        var preview = await RiskWorld.OkAsync<FrameworkPreviewDto>(officeClient.PostAsJsonAsync($"{RiskWorld.Base}/framework/preview",
            new { content = current.Content with { ScoringMethod = ScoringMethods.Additive } }));
        preview.AffectedProjectRisks.ShouldBeGreaterThanOrEqualTo(1);
        await RiskWorld.OkAsync<FrameworkVersionDto>(officeClient.PostAsJsonAsync($"{RiskWorld.Base}/framework",
            new { content = current.Content with { ScoringMethod = ScoringMethods.Additive }, reason = "Additive", recalculation = RiskRecalculation.Now }));
        for (var attempt = 0; attempt < 80; attempt++)
        {
            await using var scope = TestAccess.SystemScope(fx.Factory);
            var stored = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Risks.AsNoTracking().SingleAsync(r => r.Id == projectRisk.Id);
            if (stored.InherentScore == 9)
            {
                stored.InherentBand.ShouldBe("medium", "additive 4 + 5 = 9 on the default bands");
                var history = await scope.ServiceProvider.GetRequiredService<IAuditQuery>().HistoryAsync("portfolio.project-risk", projectRisk.Id.ToString("D"), new PageRequest(1, 100));
                history.Items.ShouldContain(e => e.NewValue == "9");
                return;
            }

            await Task.Delay(250);
        }

        throw new ShouldAssertException("the project risk was not re-rated");
    }

    /// <summary>Test set-up only (as the Portfolio tests): the requester becomes PM and the project moves to the charter draft.</summary>
    private async Task TakeOwnershipAsync(Guid projectId, TestUser pm)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var project = await db.Projects.SingleAsync(x => x.Id == projectId);
        project.UseTemplate(Strategy.Modules.Portfolio.Domain.StageTemplate.ProjectDefault);
        project.SetPm(pm.EmployeeId!.Value, DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<Strategy.Platform.Access.IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, projectId,
            Strategy.Platform.Access.RecordPrincipalKinds.Employee, pm.EmployeeId!.Value, PortfolioPermissions.PmRole);
        project.Apply(Strategy.Modules.Portfolio.Domain.ProjectActions.TakeOwnership, Strategy.Modules.Portfolio.Domain.StageTemplate.ProjectDefault, DateTimeOffset.UtcNow)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<int> CountRisksAsync()
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        return await scope.ServiceProvider.GetRequiredService<RiskDbContext>().Risks.CountAsync();
    }
}
