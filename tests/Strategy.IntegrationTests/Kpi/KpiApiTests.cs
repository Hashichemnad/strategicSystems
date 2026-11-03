using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Teams;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Kpi;

/// <summary>Performance Management end to end (docs/design/kpi.md §16) on a real database.</summary>
public sealed class KpiApiTests(KpiFixture fx) : IClassFixture<KpiFixture>
{
    private const string Base = KpiWorld.Base;

    [Fact]
    public async Task Owner_submits_with_evidence_and_the_pmo_approves_with_the_checklist_then_the_plan_and_events_follow()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var card = await w.CreateCardAsync(KpiWorld.Card("Licences issued", w.OwnerA, w.DepartmentA));
        card.Code.ShouldStartWith("KPI-");
        await w.StartYearAsync(2041, [KpiWorld.Item(card, target: 100)]);
        var plan = await w.PlanAsync(card.Id, 2041);
        plan.Plan.Status.ShouldBe(PlanStatuses.Confirmed);
        plan.Periods.Select(p => (p.PeriodCode, p.Target)).ShouldBe([("q1", 25m), ("q2", 25m), ("q3", 25m), ("q4", 25m)]);
        plan.Periods.ShouldAllBe(p => p.Status == ResultStatuses.Open);
        var q1 = plan.Periods[0].ResultId;
        var q2 = plan.Periods[1].ResultId;

        // Owner's pending updates and My Work.
        var pending = (await w.C(w.OwnerA).GetFromJsonAsync<List<KpiResultListItemDto>>($"{Base}/results/pending"))!;
        pending.Select(p => p.Id).ShouldContain(q1);
        var work = (await w.C(w.OwnerA).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == q1 && i.MessageKey == "kpi.myWork.open");

        // Validation: evidence required, causality and corrective actions when the target is missed.
        var current = await w.ResultAsync(q1, w.OwnerA);
        var noEvidence = await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/results/{q1}/submit", new { entry = KpiWorld.Entry(30), version = current.Version });
        await noEvidence.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        (await noEvidence.Content.ReadAsStringAsync()).ShouldContain("kpi.errors.evidenceRequired");
        await TeamsWorld.UploadAsync(w.C(w.OwnerA), "kpi.result", q2);
        current = await w.ResultAsync(q2, w.OwnerA);
        var missed = await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/results/{q2}/submit", new { entry = KpiWorld.Entry(10), version = current.Version });
        (await missed.Content.ReadAsStringAsync()).ShouldContain("kpi.errors.causalityRequired");

        // Q1: 30 of 25 → actual 120 %, capped 100 %; submitted on time.
        var submitted = await w.SubmitAsync(w.OwnerA, q1, KpiWorld.Entry(30));
        submitted.Status.ShouldBe(ResultStatuses.PendingApproval);
        (submitted.Actual, submitted.Capped, submitted.Late).ShouldBe((1.2m, 1m, false));
        submitted.Can.Recall.ShouldBeTrue();
        (await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/results/{q1}/decide", new { action = "approve", checklist = KpiWorld.FullChecklist }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The checklist's required items must be ticked.
        var partial = await w.C(w.Pmo).PostAsJsonAsync($"{Base}/results/{q1}/decide",
            new { action = "approve", checklist = new Dictionary<string, bool> { ["evidence-readable"] = true } });
        (await partial.Content.ReadAsStringAsync()).ShouldContain("kpi.errors.checklistRequired");
        var queue = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiResultListItemDto>>($"{Base}/approvals?year=2041&unitId={w.DepartmentA}"))!;
        queue.Single().Id.ShouldBe(q1);
        queue.Single().MyDecision.ShouldBeTrue();
        queue.Single().EvidenceCount.ShouldBe(1);

        // The generic approval screen cannot bypass the checklist (hook refuses), the KPI endpoint stores the answers.
        var task = await KpiWorld.MyTaskAsync(w.C(w.Pmo), "kpi.result", q1);
        var bypass = await ApprovalWorld.ActAsync(w.C(w.Pmo), task.TaskId, "approve");
        (await bypass.Content.ReadAsStringAsync()).ShouldContain("kpi.errors.checklistRequired");
        var approved = await w.DecideAsync(q1);
        approved.Status.ShouldBe(ResultStatuses.Approved);
        approved.Checklist["equation-followed"].ShouldBeTrue();
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.resultApproved");

        // Plan recalculated from approved results only (cumulative: 30 of the Q1 target 25).
        plan = await w.PlanAsync(card.Id, 2041);
        (plan.Plan.Achieved, plan.Plan.Actual, plan.Plan.Capped, plan.Plan.Rag, plan.Plan.ApprovedCount).ShouldBe((30m, 1.2m, 1m, "green", 1));
        await TeamsWorld.WaitAsync(() => Task.FromResult(RecordingKpiEvents.Approved.Any(e => e.ResultId == q1)), "KpiResultApproved");
        RecordingKpiEvents.Approved.First(e => e.ResultId == q1).ShouldSatisfyAllConditions(
            e => e.Capped.ShouldBe(1m), e => e.Rag.ShouldBe("green"), e => e.CardCode.ShouldBe(card.Code), e => e.PeriodCode.ShouldBe("q1"));

        // Q2 missed with causality: return needs a comment, owner corrects and resubmits, approval publishes KpiTargetMissed.
        var q2Submitted = await w.SubmitAsync(w.OwnerA, q2, KpiWorld.Entry(10, "Vendor delay", "Escalate to vendor"), evidence: false);
        q2Submitted.Rag.ShouldBe("red");
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/results/{q2}/decide", new { action = "return", checklist = KpiWorld.FullChecklist }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var returned = await w.DecideAsync(q2, "return", "Attach the vendor letter");
        returned.Status.ShouldBe(ResultStatuses.Returned);
        returned.ReturnCount.ShouldBe(1);
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.resultReturned");
        await w.SubmitAsync(w.OwnerA, q2, KpiWorld.Entry(12, "Vendor delay (letter attached)", "Escalate to vendor"));
        (await w.DecideAsync(q2)).Status.ShouldBe(ResultStatuses.Approved);
        await TeamsWorld.WaitAsync(() => Task.FromResult(RecordingKpiEvents.Missed.Any(e => e.ResultId == q2)), "KpiTargetMissed");
        var miss = RecordingKpiEvents.Missed.First(e => e.ResultId == q2);
        (miss.Rag, miss.ConsecutiveMissed, miss.Capped, miss.Target).ShouldBe(("red", 1, 0.48m, 25m));
        plan = await w.PlanAsync(card.Id, 2041);
        (plan.Plan.Achieved, plan.Plan.Actual).ShouldBe((42m, 0.84m)); // 42 of 50

        // Recall while pending, then Not Applicable is excluded; reopen by the Strategy Office with a reason.
        var q3 = plan.Periods[2].ResultId;
        var pendingQ3 = await w.SubmitAsync(w.OwnerA, q3, KpiWorld.Entry(null, na: true, naReason: "No licences in summer"));
        var recalled = await KpiWorld.OkAsync<KpiResultDto>(w.C(w.OwnerA).PostAsJsonAsync($"{Base}/results/{q3}/recall", new { version = pendingQ3.Version }));
        recalled.Status.ShouldBe(ResultStatuses.Open);
        await w.SubmitAsync(w.OwnerA, q3, KpiWorld.Entry(null, na: true, naReason: "No licences in summer"), evidence: false);
        await w.DecideAsync(q3);
        plan = await w.PlanAsync(card.Id, 2041);
        (plan.Plan.Achieved, plan.Plan.ApprovedCount).ShouldBe((42m, 3));
        (await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/results/{q1}/reopen", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reopened = await KpiWorld.OkAsync<KpiResultDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/results/{q1}/reopen", new { reason = "Wrong evidence file" }));
        (reopened.Status, reopened.ReopenedCount).ShouldBe((ResultStatuses.Open, 1));
        plan = await w.PlanAsync(card.Id, 2041);
        plan.Plan.Achieved.ShouldBe(12m); // Q1 no longer counts until approved again
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.resultReopened");

        // History tab: the audit trail of the result.
        (await w.C(w.Pmo).GetAsync($"{Base}/results/{q1}/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bulk_approval_by_year_and_department_approves_only_the_ticked_results()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var a1 = await w.CreateCardAsync(KpiWorld.Card("Bulk A1", w.OwnerA, w.DepartmentA, period: "annual"));
        var a2 = await w.CreateCardAsync(KpiWorld.Card("Bulk A2", w.OwnerA, w.DepartmentA, period: "annual"));
        var a3 = await w.CreateCardAsync(KpiWorld.Card("Bulk A3", w.OwnerA, w.DepartmentA, period: "annual"));
        var b1 = await w.CreateCardAsync(KpiWorld.Card("Bulk B1", w.OwnerB, w.DepartmentB, period: "annual"));
        await w.StartYearAsync(2042, [KpiWorld.Item(a1, 10), KpiWorld.Item(a2, 10), KpiWorld.Item(a3, 10), KpiWorld.Item(b1, 10)]);
        var ids = new Dictionary<Guid, Guid>();
        foreach (var (card, owner) in new[] { (a1, w.OwnerA), (a2, w.OwnerA), (a3, w.OwnerA), (b1, w.OwnerB) })
        {
            ids[card.Id] = (await w.PlanAsync(card.Id, 2042)).Periods.Single().ResultId;
            await w.SubmitAsync(owner, ids[card.Id], KpiWorld.Entry(10));
        }

        var queue = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiResultListItemDto>>($"{Base}/approvals?year=2042&unitId={w.DepartmentA}&mine=true"))!;
        queue.Select(q => q.Id).ShouldBe([ids[a1.Id], ids[a2.Id], ids[a3.Id]], ignoreOrder: true);
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/approvals/bulk", new { resultIds = new[] { ids[a1.Id] }, checklistConfirmed = false }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await w.C(w.OwnerA).PostAsJsonAsync($"{Base}/approvals/bulk", new { resultIds = new[] { ids[a1.Id] }, checklistConfirmed = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Untick A3: only A1 and A2 are approved; B1 (other department) was not selected.
        var bulk = await KpiWorld.OkAsync<KpiBulkResultDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/approvals/bulk",
            new { resultIds = new[] { ids[a1.Id], ids[a2.Id] }, comment = "Verified in bulk", checklistConfirmed = true }));
        (bulk.Approved, bulk.Failed).ShouldBe((2, 0));
        (await w.ResultAsync(ids[a1.Id])).Status.ShouldBe(ResultStatuses.Approved);
        (await w.ResultAsync(ids[a2.Id])).Status.ShouldBe(ResultStatuses.Approved);
        (await w.ResultAsync(ids[a3.Id])).Status.ShouldBe(ResultStatuses.PendingApproval);
        (await w.ResultAsync(ids[b1.Id])).Status.ShouldBe(ResultStatuses.PendingApproval);
        (await w.ResultAsync(ids[a1.Id])).Checklist.Values.ShouldAllBe(v => v);

        // A result that is no longer pending is reported as failed, the others still go through.
        var again = await KpiWorld.OkAsync<KpiBulkResultDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/approvals/bulk",
            new { resultIds = new[] { ids[a1.Id], ids[a3.Id] }, checklistConfirmed = true }));
        (again.Approved, again.Failed).ShouldBe((1, 1));
        again.Outcomes.Single(o => !o.Success).ResultId.ShouldBe(ids[a1.Id]);
    }

    [Fact]
    public async Task Aggregation_follows_the_measuring_method_and_excludes_not_applicable_periods()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var average = await w.CreateCardAsync(KpiWorld.Card("Satisfaction", w.OwnerA, w.DepartmentA, method: "average", unitCode: "percentage"));
        var range = await w.CreateCardAsync(KpiWorld.Card("Occupancy", w.OwnerA, w.DepartmentA, period: "half_yearly", method: "average", targetType: "range"));
        await w.StartYearAsync(2043, [KpiWorld.Item(average, 90), KpiWorld.Item(range, min: 85, max: 95)]);
        var periods = (await w.PlanAsync(average.Id, 2043)).Periods;
        periods.ShouldAllBe(p => p.Target == 90m); // average: the annual target repeats
        await w.SubmitAsync(w.OwnerA, periods[0].ResultId, KpiWorld.Entry(80, "Survey period short", "Extend the survey"));
        await w.SubmitAsync(w.OwnerA, periods[1].ResultId, KpiWorld.Entry(null, na: true, naReason: "Survey not run"));
        await w.SubmitAsync(w.OwnerA, periods[2].ResultId, KpiWorld.Entry(100));
        foreach (var p in periods.Take(3))
        {
            await w.DecideAsync(p.ResultId);
        }

        var plan = await w.PlanAsync(average.Id, 2043);
        (plan.Plan.Achieved, plan.Plan.Actual, plan.Plan.Capped, plan.AggregationRule).ShouldBe((90m, 1m, 1m, "average"));

        var halves = (await w.PlanAsync(range.Id, 2043)).Periods;
        halves.Select(h => (h.PeriodCode, h.RangeMin, h.RangeMax)).ShouldBe([("h1", 85m, 95m), ("h2", 85m, 95m)]);
        await w.SubmitAsync(w.OwnerA, halves[0].ResultId, KpiWorld.Entry(68, "Renovation", "Reopen the wing"));
        var decided = await w.DecideAsync(halves[0].ResultId);
        (decided.Actual, decided.Capped, decided.Rag).ShouldBe((0.8m, 0.8m, "amber"));
    }

    [Fact]
    public async Task Scorecard_and_strategy_map_use_capped_performance_while_trends_show_actual()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var suffix = Guid.CreateVersion7().ToString("N")[^6..];
        var vision = await Element(w, "vision", $"V-{suffix}", null);
        var perspective = await Element(w, "perspective", $"P-{suffix}", vision);
        var objective = await Element(w, "objective", $"O-{suffix}", perspective);
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/strategy/elements", new { kind = "objective", code = $"X-{suffix}", nameEn = "Orphan", sort = 1, active = true }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // an objective needs a perspective
        var over = await w.CreateCardAsync(KpiWorld.Card("Over achiever", w.OwnerA, w.DepartmentA, period: "annual", objectiveId: objective));
        var under = await w.CreateCardAsync(KpiWorld.Card("Under performer", w.OwnerB, w.DepartmentB, period: "annual", objectiveId: objective));
        await w.StartYearAsync(2044, [KpiWorld.Item(over, 80), KpiWorld.Item(under, 100)]);
        await w.SubmitAsync(w.OwnerA, (await w.PlanAsync(over.Id, 2044)).Periods.Single().ResultId, KpiWorld.Entry(100));
        await w.SubmitAsync(w.OwnerB, (await w.PlanAsync(under.Id, 2044)).Periods.Single().ResultId, KpiWorld.Entry(60, "Budget cut", "Request budget"));
        await w.DecideAsync((await w.PlanAsync(over.Id, 2044)).Periods.Single().ResultId);
        await w.DecideAsync((await w.PlanAsync(under.Id, 2044)).Periods.Single().ResultId);

        var scorecard = (await w.C(w.Pmo).GetFromJsonAsync<KpiScorecardDto>($"{Base}/scorecard?year=2044"))!;
        var visionNode = scorecard.Root.Children.Single(c => c.Id == vision);
        var objectiveNode = visionNode.Children.Single().Children.Single();
        objectiveNode.Score.ShouldBe(0.8m); // (100 % capped + 60 %) / 2, not (125 % + 60 %) / 2
        objectiveNode.Rag.ShouldBe("amber");
        objectiveNode.Children.Single(k => k.Id == over.Id).ShouldSatisfyAllConditions(k => k.Score.ShouldBe(1m), k => k.Actual.ShouldBe(1.25m));
        visionNode.Score.ShouldBe(0.8m);

        var map = (await w.C(w.Pmo).GetFromJsonAsync<KpiStrategyMapDto>($"{Base}/strategy/map?year=2044"))!;
        map.Perspectives.Single(p => p.Id == perspective).Objectives.Single().ShouldSatisfyAllConditions(o => o.Score.ShouldBe(0.8m), o => o.KpiCount.ShouldBe(2));

        // The department view only holds the department's KPIs; trends show the uncapped actual.
        var departmentA = (await w.C(w.Pmo).GetFromJsonAsync<KpiScorecardDto>($"{Base}/scorecard?year=2044&unitId={w.DepartmentA}"))!;
        departmentA.Root.Score.ShouldBe(1m);
        var trend = (await w.C(w.Pmo).GetFromJsonAsync<KpiPerformanceDto>($"{Base}/cards/{over.Id}/performance"))!;
        trend.Years.Single(y => y.Year == 2044).ShouldSatisfyAllConditions(y => y.Actual.ShouldBe(1.25m), y => y.Capped.ShouldBe(1m));

        // The department manager's scorecard holds only their unit (data scope).
        var managerView = (await w.C(w.ManagerA).GetFromJsonAsync<KpiScorecardDto>($"{Base}/scorecard?year=2044"))!;
        managerView.Root.KpiCount.ShouldBe(1);

        var dashboard = (await w.C(w.Pmo).GetFromJsonAsync<KpiDashboardDto>($"{Base}/dashboard?year=2044"))!;
        dashboard.Achievement.Single(a => a.Key == "onTarget").Count.ShouldBeGreaterThanOrEqualTo(1);
        dashboard.Top.ShouldContain(t => t.CardId == over.Id);
        dashboard.Bottom.ShouldContain(t => t.CardId == under.Id);
        var quality = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiDataQualityDto>>($"{Base}/data-quality?year=2044"))!;
        quality.Single(q => q.Unit.Id == w.DepartmentA).ShouldSatisfyAllConditions(q => q.Submitted.ShouldBe(1), q => q.OnTimeRate.ShouldBe(1m),
            q => q.EvidenceCompleteness.ShouldBe(1m), q => q.Score.ShouldBe(1m));

        foreach (var kind in new[] { "cards", "scorecard", "results", "excellence", "data-quality" })
        {
            var export = await w.C(w.Pmo).GetAsync($"{Base}/export?kind={kind}&format=xlsx&year=2044&language=ar");
            export.StatusCode.ShouldBe(HttpStatusCode.OK, kind);
        }

        (await w.C(w.Pmo).GetAsync($"{Base}/export?kind=scorecard&format=pdf&year=2044&language=en")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.OwnerA).GetAsync($"{Base}/export?kind=cards&format=xlsx")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owners_see_their_kpis_managers_their_unit_and_the_strategy_office_all()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var mine = await w.CreateCardAsync(KpiWorld.Card("Scope A", w.OwnerA, w.DepartmentA, period: "annual"));
        var theirs = await w.CreateCardAsync(KpiWorld.Card("Scope B", w.OwnerB, w.DepartmentB, period: "annual"));
        await w.StartYearAsync(2045, [KpiWorld.Item(mine, 5), KpiWorld.Item(theirs, 5)]);
        var mineResult = (await w.PlanAsync(mine.Id, 2045)).Periods.Single().ResultId;
        var theirsResult = (await w.PlanAsync(theirs.Id, 2045)).Periods.Single().ResultId;

        async Task<List<Guid>> ListAsync(TestUser user) =>
            (await w.C(user).GetFromJsonAsync<PagedResult<KpiCardListItemDto>>($"{Base}/cards?pageSize=200"))!.Items.Select(i => i.Id).ToList();

        var ownerA = await ListAsync(w.OwnerA);
        ownerA.ShouldContain(mine.Id);
        ownerA.ShouldNotContain(theirs.Id);
        (await w.C(w.OwnerA).GetAsync($"{Base}/cards/{theirs.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.OwnerA).GetAsync($"{Base}/results/{theirsResult}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.OwnerB).PostAsJsonAsync($"{Base}/results/{mineResult}/submit", new { entry = KpiWorld.Entry(5), version = 0 }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var manager = await ListAsync(w.ManagerA);
        manager.ShouldContain(mine.Id);
        manager.ShouldNotContain(theirs.Id);
        (await w.C(w.ManagerA).GetAsync($"{Base}/results/{mineResult}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.ManagerA).PostAsJsonAsync($"{Base}/results/{mineResult}/submit", new { entry = KpiWorld.Entry(5), version = 0 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ListAsync(w.Outsider)).ShouldNotContain(mine.Id);
        (await w.C(w.Outsider).GetAsync($"{Base}/cards/{mine.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/results/{mineResult}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/cards/{mine.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).PostAsJsonAsync($"{Base}/cards", new { values = KpiWorld.Card("x", w.Outsider, w.DepartmentA) })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Outsider).GetAsync($"{Base}/years/2045/preview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Outsider).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.OwnerA).PutAsJsonAsync($"{Base}/settings", new { settings = new KpiSettings(), version = 0 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var pmo = await ListAsync(w.Pmo);
        pmo.ShouldContain(mine.Id);
        pmo.ShouldContain(theirs.Id);

        // Evidence files follow the result: the outsider cannot list them; the manager can read but not upload.
        await TeamsWorld.UploadAsync(w.C(w.OwnerA), "kpi.result", mineResult);
        (await w.C(w.Outsider).GetAsync($"/api/v1/files/owners/kpi.result/{mineResult}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.ManagerA).GetAsync($"/api/v1/files/owners/kpi.result/{mineResult}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The catalog contract for other modules is scope-filtered too.
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var catalog = scope.ServiceProvider.GetRequiredService<IKpiCatalog>();
        var system = Strategy.Platform.Access.StrategyClaims.CreateSystemPrincipal();
        (await catalog.SearchAsync("Scope", system, 50)).Select(c => c.Id).ShouldContain(mine.Id);
        (await catalog.GetPerformanceAsync(mine.Id, 2045, system))!.TotalPeriods.ShouldBe(1);
    }

    [Fact]
    public async Task Definition_changes_after_the_year_starts_need_strategy_office_approval_and_keep_versions()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var year = DateTime.UtcNow.Year;
        var card = await w.CreateCardAsync(KpiWorld.Card("Controlled", w.OwnerA, w.DepartmentA, period: "half_yearly"));
        await w.StartYearAsync(year, [KpiWorld.Item(card, 50)], openNow: false, confirm: true);
        card = (await w.C(w.Pmo).GetFromJsonAsync<KpiCardDto>($"{Base}/cards/{card.Id}"))!;
        card.ChangeControlled.ShouldBeTrue();

        var changed = card.Values with { Direction = "decrease" };
        var refused = await w.C(w.Pmo).PutAsJsonAsync($"{Base}/cards/{card.Id}", new { card = new { values = changed }, version = card.Version });
        await refused.ShouldBeProblemAsync((HttpStatusCode)422, "kpi.changeControlled");
        var renamed = card.Values with { NameEn = "Controlled (renamed)" };
        card = await KpiWorld.OkAsync<KpiCardDto>(w.C(w.Pmo).PutAsJsonAsync($"{Base}/cards/{card.Id}", new { card = new { values = renamed, reason = "Wording" }, version = card.Version }));
        card.DefinitionVersion.ShouldBe(2);

        // Another Strategy Office member approves (the requester is excluded from their own step; approvers are resolved at submission).
        var approver = await fx.Factory.CreateUserAsync(withMfa: true, unitId: w.OrganisationId, roles: KpiPermissions.StrategyOfficeRoleKey);
        var approverClient = await fx.Factory.SignInAsync(approver);
        var change = await KpiWorld.OkAsync<KpiChangeDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/cards/{card.Id}/changes",
            new { proposed = new { target = 60m }, reason = "Board raised the target" }));
        (change.Status, change.Current.Target, change.Proposed.Target).ShouldBe((ChangeStatuses.Pending, 50m, 60m));
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/cards/{card.Id}/changes", new { proposed = new { target = 70m }, reason = "again" }))
            .StatusCode.ShouldBe((HttpStatusCode)422); // one pending change at a time

        var task = await KpiWorld.MyTaskAsync(approverClient, "kpi.card", card.Id);
        await ApprovalWorld.ActOkAsync(approverClient, task.TaskId, ApprovalActions.Approve);

        var changes = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiChangeDto>>($"{Base}/changes?cardId={card.Id}"))!;
        changes.Single().Status.ShouldBe(ChangeStatuses.Approved);
        var plan = await w.PlanAsync(card.Id, year);
        plan.Plan.Target.ShouldBe(60m);
        plan.Periods.Select(p => p.Target).ShouldBe([30m, 30m]);
        var versions = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiCardVersionDto>>($"{Base}/cards/{card.Id}/versions"))!;
        versions.Select(v => v.VersionNo).ShouldBe([3, 2, 1]);
        versions[0].ChangeRequestId.ShouldBe(change.Id);
    }

    [Fact]
    public async Task Composite_and_qualitative_kpis_compute_their_value_and_links_mcp_and_options_are_wired()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var composite = await w.CreateCardAsync(KpiWorld.Card("Served on time", w.OwnerA, w.DepartmentA, period: "annual", unitCode: "percentage",
            formula: "served / total * 100", elements: [new { code = "served", nameEn = "Served on time" }, new { code = "total", nameEn = "All requests" }],
            gem: [], globalStar: true));
        var qualitative = await w.CreateCardAsync(KpiWorld.Card("Maturity", w.OwnerA, w.DepartmentA, period: "annual", nature: "qualitative", unitCode: "score",
            scale: [new { value = 1, labelEn = "Initial" }, new { value = 3, labelEn = "Defined" }, new { value = 5, labelEn = "Optimised" }]));
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/cards", new { values = KpiWorld.Card("Bad formula", w.OwnerA, w.DepartmentA, formula: "served /", elements: [new { code = "served" }]) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await w.StartYearAsync(2046, [KpiWorld.Item(composite, 90), KpiWorld.Item(qualitative, 3)]);

        var compositeResult = (await w.PlanAsync(composite.Id, 2046)).Periods.Single().ResultId;
        var current = await w.ResultAsync(compositeResult, w.OwnerA);
        var draft = await KpiWorld.OkAsync<KpiResultDto>(w.C(w.OwnerA).PutAsJsonAsync($"{Base}/results/{compositeResult}/draft",
            new { entry = KpiWorld.Entry(null, elements: new Dictionary<string, decimal?> { ["served"] = 45, ["total"] = 50 }), version = current.Version }));
        (draft.Achieve, draft.Capped).ShouldBe((90m, 1m));
        draft.Status.ShouldBe(ResultStatuses.Open); // a draft is not a submission
        var zero = await w.C(w.OwnerA).PutAsJsonAsync($"{Base}/results/{compositeResult}/draft",
            new { entry = KpiWorld.Entry(null, elements: new Dictionary<string, decimal?> { ["served"] = 1, ["total"] = 0 }), version = draft.Version });
        (await zero.Content.ReadAsStringAsync()).ShouldContain("kpi.errors.formulaEvaluation");

        var qualitativeResult = (await w.PlanAsync(qualitative.Id, 2046)).Periods.Single().ResultId;
        current = await w.ResultAsync(qualitativeResult, w.OwnerA);
        (await w.C(w.OwnerA).PutAsJsonAsync($"{Base}/results/{qualitativeResult}/draft", new { entry = KpiWorld.Entry(null, scale: 2), version = current.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var level = await KpiWorld.OkAsync<KpiResultDto>(w.C(w.OwnerA).PutAsJsonAsync($"{Base}/results/{qualitativeResult}/draft",
            new { entry = KpiWorld.Entry(null, scale: 5), version = current.Version }));
        (level.Achieve, level.Actual, level.Capped).ShouldBe((5m, 1.6667m, 1m));

        // Linked projects (Portfolio implements the source in Phase 9): resolved through the Links registry.
        var project = Guid.CreateVersion7();
        FakeProjectKpiLinks.Links[composite.Id] = [project];
        var linked = (await w.C(w.Pmo).GetFromJsonAsync<List<LinkView>>($"{Base}/cards/{composite.Id}/linked"))!;
        linked.Single().ShouldSatisfyAllConditions(l => l.Id.ShouldBe(project), l => l.Number.ShouldBe("PROJ-T"));
        (await w.C(w.Outsider).GetAsync($"{Base}/cards/{composite.Id}/linked")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Cards and results are task-able (a task to prepare evidence).
        var types = (await w.C(w.OwnerA).GetFromJsonAsync<List<LinkableTypeDefinition>>("/api/v1/links/types?capability=task-able"))!;
        types.Select(t => t.ResourceType).ShouldContain("kpi.result");
        types.Select(t => t.ResourceType).ShouldContain("kpi.card");

        // Excellence filter (Global Star) and options.
        var star = (await w.C(w.Pmo).GetFromJsonAsync<PagedResult<KpiCardListItemDto>>($"{Base}/cards?globalStar=true&pageSize=200"))!;
        star.Items.Select(i => i.Id).ShouldContain(composite.Id);
        star.Items.Select(i => i.Id).ShouldNotContain(qualitative.Id);
        var options = (await w.C(w.OwnerA).GetFromJsonAsync<KpiOptionsDto>($"{Base}/options"))!;
        options.Lookups["kpi.unit"].Select(u => u.Code).ShouldContain("percentage");
        options.Checklist.Count(c => c.Required).ShouldBe(4);
        options.CanManagePlans.ShouldBeFalse();

        // MCP: read tools on, the draft tool off, no approve tool.
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var tools = scope.ServiceProvider.GetServices<IAppTool>().Select(t => t.Definition).Where(d => d.SystemCode == KpiPermissions.SystemCode).ToList();
        tools.Select(t => t.Name).ShouldBe(["my_pending_kpi_results", "kpi_card_lookup", "department_scorecard", "kpi_approval_queue_summary", "submit_kpi_result_draft"],
            ignoreOrder: true);
        tools.Single(t => t.Name == "submit_kpi_result_draft").DefaultEnabled.ShouldBeFalse();
        tools.Where(t => t.Kind == ToolKind.Read).ShouldAllBe(t => t.DefaultEnabled);
        tools.ShouldNotContain(t => t.Kind == ToolKind.Approve);
    }

    private static async Task<Guid> Element(KpiWorld w, string kind, string code, Guid? parent) =>
        (await KpiWorld.OkAsync<KpiElementDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/strategy/elements",
            new { kind, code, nameAr = "عنصر " + code, nameEn = "Element " + code, parentId = parent, sort = 1, active = true }))).Id;
}
