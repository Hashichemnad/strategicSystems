using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Cap;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Kpi;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Benchmarking.Jobs;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Bench;

/// <summary>One host with a travelling clock; Benchmarking, CAP, Requests, KPI and Teams open for everyone.</summary>
public sealed class BenchFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<Strategy.Platform.Kernel.Time.IClock>(Clock));
        foreach (var system in new[] { BenchPermissions.SystemCode, CapPermissions.SystemCode, "requests", KpiPermissions.SystemCode })
        {
            await Factory.GrantSystemAccessAsync(system, AccessGrants.Everyone, null);
        }
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
/// Department A (head = manager, the default CAP remark owner) and department B. The office (Benchmarking office, PM Office for CAP and KPI
/// Strategy Office, MFA), the coordinator of A (responsible employee), a topic owner in A, an employee of B.
/// </summary>
internal sealed class BenchWorld
{
    public const string Base = "/api/v1/bench";

    public required ApiFactory Factory { get; init; }

    public required Guid DepartmentA { get; init; }

    public required Guid DepartmentB { get; init; }

    public required TestUser Office { get; init; }

    public required TestUser Manager { get; init; }

    public required TestUser Coordinator { get; init; }

    public required TestUser TopicOwner { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<BenchWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        Guid organisation, a, b;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"BO{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            a = (await dispatcher.SendAsync(new CreateUnit($"BA{suffix}", "إدارة أ", $"Department A {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            b = (await dispatcher.SendAsync(new CreateUnit($"BB{suffix}", "إدارة ب", $"Department B {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var office = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: [BenchPermissions.OfficeRoleKey, CapPermissions.OfficeRoleKey, KpiPermissions.StrategyOfficeRoleKey, "requests.office"]);
        var manager = await factory.CreateUserAsync(unitId: a);
        var coordinator = await factory.CreateUserAsync(unitId: a);
        var topicOwner = await factory.CreateUserAsync(unitId: a);
        var outsider = await factory.CreateUserAsync(unitId: b);
        await ApprovalWorld.SetHeadAsync(factory, a, manager.EmployeeId);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { office, manager, coordinator, topicOwner, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new BenchWorld
        {
            Factory = factory,
            DepartmentA = a,
            DepartmentB = b,
            Office = office,
            Manager = manager,
            Coordinator = coordinator,
            TopicOwner = topicOwner,
            Outsider = outsider,
            Clients = clients,
        };
    }

    public static Task<T> OkAsync<T>(Task<HttpResponseMessage> call) => CapWorld.OkAsync<T>(call);

    public object PlanData(int year, int quarter, string title = "Digital services benchmarking") => new
    {
        titleAr = "مقارنة " + title,
        titleEn = title,
        responsibleUnit = DepartmentA,
        responsibleEmployee = Coordinator.EmployeeId,
        targetYear = year,
        targetQuarter = quarter,
        comparisonType = "functional",
        purpose = "process-improvement",
        scope = "Customer service channels and turnaround times",
        topics = new object[]
        {
            new { topic = "Service channels", owner = TopicOwner.EmployeeId, description = "Channels and their uptake" },
            new { topic = "Turnaround time", owner = Coordinator.EmployeeId },
        },
    };

    /// <summary>The coordinator requests a study from the catalogue; the office approves it into the annual plan.</summary>
    public async Task<BenchStudyDto> ApprovedStudyAsync(int year, int quarter, string title = "Digital services benchmarking")
    {
        var draft = await OkAsync<RequestRecordDto>(C(Coordinator).PostAsJsonAsync("/api/v1/requests", new { typeCode = BenchRequestTypes.Request, data = PlanData(year, quarter, title) }));
        var submitted = await OkAsync<RequestRecordDto>(C(Coordinator).PostAsJsonAsync($"/api/v1/requests/{draft.Id}/submit", new { version = draft.Version }));
        await OkAsync<RequestRecordDto>(C(Office).PostAsJsonAsync($"/api/v1/requests/{submitted.Id}/decide", new { action = "approve", comment = (string?)null }));
        return await StudyOfRequestAsync(submitted.Id);
    }

    public async Task<BenchStudyDto> StudyOfRequestAsync(Guid requestId)
    {
        await using var scope = TestAccess.SystemScope(Factory);
        var id = await scope.ServiceProvider.GetRequiredService<BenchDbContext>().Studies.Where(s => s.RequestId == requestId).Select(s => s.Id).SingleAsync();
        return await GetAsync(id);
    }

    public async Task<BenchStudyDto> GetAsync(Guid id, TestUser? user = null) => await OkAsync<BenchStudyDto>(C(user ?? Office).GetAsync($"{Base}/studies/{id}"));

    /// <summary>The office decides the pending gate of the study through the generic task screen.</summary>
    public async Task DecideAsync(Guid studyId, string process, string action = "approve", string? comment = null)
    {
        var panel = await OkAsync<SubjectApprovalsDto>(C(Office).GetAsync($"/api/v1/workflow/subjects/{Study.EntityType}/{studyId}"));
        var instance = panel.Instances.First(i => i.ProcessKey == process && i.Status == ApprovalInstanceStatuses.InProgress);
        instance.MyTask.ShouldNotBeNull();
        await ApprovalWorld.ActOkAsync(C(Office), instance.MyTask!.TaskId, action, comment);
    }

    public async Task<BenchStudyDto> ToAnalysisAsync(BenchStudyDto study)
    {
        var candidates = await OkAsync<List<BenchCandidateDto>>(C(Coordinator).PutAsJsonAsync($"{Base}/studies/{study.Id}/search", new
        {
            candidates = new object[]
            {
                new
                {
                    newComparator = new { nameAr = "هيئة الخدمات الذكية", nameEn = "Smart Services Authority", country = "UAE", scope = "local", active = true },
                    selected = true, reason = "Leader in digital service channels", availableInfo = "Annual report 2025", contact = "excellence@example.test",
                    codeOfConductAck = true,
                },
            },
        }));
        await OkAsync<BenchStudyDto>(C(Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/search/submit", new { comment = "Search form ready" }));
        await DecideAsync(study.Id, BenchProcesses.Search);
        var comparator = candidates.Single();
        await OkAsync<List<BenchCandidateDto>>(C(Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/outreach",
            new { studyEntityId = comparator.Id, on = new DateOnly(2026, 10, 5), channel = "email", summary = "Visit requested", response = "accepted" }));
        await OkAsync<List<BenchVisitDto>>(C(Coordinator).PutAsJsonAsync($"{Base}/studies/{study.Id}/visits", new
        {
            visits = new object[]
            {
                new
                {
                    comparatorId = comparator.ComparatorId, agreedOn = new DateOnly(2026, 11, 20), method = VisitMethods.SiteVisit,
                    participantIds = new[] { Coordinator.EmployeeId, TopicOwner.EmployeeId }, questionnaire = "1. How do you run your channels?",
                    notes = "They route 80 % of requests through a single app with live tracking.",
                },
            },
        }));
        return await OkAsync<BenchStudyDto>(C(Coordinator).PostAsync($"{Base}/studies/{study.Id}/send-to-analysis", null));
    }
}

/// <summary>Benchmarking end to end (docs/design/benchmarking.md §12) on a real database.</summary>
public sealed class BenchApiTests(BenchFixture fx) : IClassFixture<BenchFixture>
{
    private const string Base = BenchWorld.Base;

    [Fact]
    public async Task A_study_runs_plan_search_acquire_analysis_hands_its_development_plan_to_cap_and_closes_when_the_actions_complete()
    {
        var w = await BenchWorld.CreateAsync(fx.Factory);
        var year = DateTime.UtcNow.Year + 1;

        // Plan: from the request catalogue; the office returns, the requester corrects and resubmits, the office approves.
        var draft = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Coordinator).PostAsJsonAsync("/api/v1/requests",
            new { typeCode = BenchRequestTypes.Request, data = w.PlanData(year, 2) }));
        var submitted = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Coordinator).PostAsJsonAsync($"/api/v1/requests/{draft.Id}/submit", new { version = draft.Version }));
        var planned = await w.StudyOfRequestAsync(submitted.Id);
        planned.Number.ShouldStartWith("BMS-");
        planned.Stage.ShouldBe(StudyStages.Plan);
        planned.PendingProcess.ShouldBe(BenchProcesses.Plan);
        planned.Topics.Count.ShouldBe(2);
        planned.Topics[0].Owner!.Id.ShouldBe(w.TopicOwner.EmployeeId!.Value);
        var returned = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Office).PostAsJsonAsync($"/api/v1/requests/{submitted.Id}/decide",
            new { action = "return", comment = "Narrow the scope" }));
        var afterReturn = await w.GetAsync(planned.Id, w.Coordinator);
        afterReturn.Returned.ShouldBeTrue();
        var status = await BenchWorld.OkAsync<CurrentStatus>(w.C(w.Coordinator).GetAsync($"/api/v1/workflow/status/{Study.EntityType}/{planned.Id}"));
        status.MessageKey.ShouldBe("bench.currentStatus.waitingSubmit");
        var resubmitted = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Coordinator).PostAsJsonAsync($"/api/v1/requests/{returned.Id}/submit", new { version = returned.Version }));
        (await BenchWorld.OkAsync<CurrentStatus>(w.C(w.Coordinator).GetAsync($"/api/v1/workflow/status/{Study.EntityType}/{planned.Id}"))).MessageKey
            .ShouldBe("bench.currentStatus.waitingPmo");
        await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Office).PostAsJsonAsync($"/api/v1/requests/{resubmitted.Id}/decide", new { action = "approve" }));
        var study = await w.GetAsync(planned.Id);
        study.Stage.ShouldBe(StudyStages.Search);
        study.PlanApprovedAt.ShouldNotBeNull();

        // Annual plan: visible to everyone (plan.read), circulated by the office.
        var plan = await BenchWorld.OkAsync<BenchPlanDto>(w.C(w.Outsider).GetAsync($"{Base}/plan?year={year}"));
        plan.Studies.ShouldContain(s => s.Id == study.Id);
        plan.CanCirculate.ShouldBeFalse();
        (await w.C(w.Outsider).PostAsync($"{Base}/plan/{year}/circulate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var circulated = await BenchWorld.OkAsync<BenchPlanDto>(w.C(w.Office).PostAsync($"{Base}/plan/{year}/circulate", null));
        circulated.Circulations.Single().Studies.ShouldBeGreaterThanOrEqualTo(1);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Manager), "bench.planCirculated")).Link.ShouldBe($"/bench/plan?year={year}");

        // Search: a selected candidate needs a reason; the office approves; outreach, visit, send to analysis.
        await BenchWorld.OkAsync<List<BenchCandidateDto>>(w.C(w.Coordinator).PutAsJsonAsync($"{Base}/studies/{study.Id}/search", new
        {
            candidates = new object[] { new { newComparator = new { nameEn = "Peer agency", scope = "regional", active = true }, selected = true, codeOfConductAck = false } },
        }));
        await (await w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/search/submit", new { })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        (await w.C(w.Outsider).GetAsync($"{Base}/studies/{study.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        study = await w.ToAnalysisAsync(study);
        study.Stage.ShouldBe(StudyStages.Analysis);
        (await BenchWorld.OkAsync<CurrentStatus>(w.C(w.Coordinator).GetAsync($"/api/v1/workflow/status/{Study.EntityType}/{study.Id}"))).MessageKey
            .ShouldBe("bench.currentStatus.addAction");

        // Analysis: practice and indicator rows (one linked to a KPI card), development plan of two items.
        var cardResponse = await w.C(w.Office).PostAsJsonAsync("/api/v1/kpi/cards", new { values = KpiWorld.Card("Digital uptake", w.Coordinator, w.DepartmentA) });
        cardResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var cardId = (await cardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        await (await w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/analysis/submit", new { }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "bench.noPlanItem");
        var topic = study.Topics[0];
        var due = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90);
        var analysis = await BenchWorld.OkAsync<BenchAnalysisDto>(w.C(w.Coordinator).PutAsJsonAsync($"{Base}/studies/{study.Id}/analysis", new
        {
            practices = new object[]
            {
                new { topicId = topic.Id, subject = "Single service app", ourPractice = "Five separate portals", theirPractice = "One app with live tracking", category = "compared", applicable = true, addToLibrary = true },
                new { subject = "Physical counters", category = "not-applicable", applicable = false, addToLibrary = false },
            },
            indicators = new object[]
            {
                new { kpiCardId = cardId, indicator = "Digital uptake", ourValue = 0.45m, theirValue = 0.8m, period = "2025", source = "Annual report", updateKpiBenchmark = true },
            },
            planItems = new object[]
            {
                new { topicId = topic.Id, titleAr = "إطلاق تطبيق خدمات موحد", titleEn = "Launch a single services app", ownerEmployeeId = w.TopicOwner.EmployeeId, dueOn = due, priority = "high", actionTypeCode = "improvement" },
                new { titleAr = "نشر التتبع المباشر للطلبات", titleEn = "Publish live request tracking", ownerEmployeeId = w.Coordinator.EmployeeId, dueOn = due, priority = "medium" },
            },
        }));
        analysis.PlanItems.Count.ShouldBe(2);
        analysis.Indicators.Single().KpiCardCode.ShouldNotBeNull();
        await BenchWorld.OkAsync<BenchStudyDto>(w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/analysis/submit", new { comment = "Analysis ready" }));
        await w.DecideAsync(study.Id, BenchProcesses.Analysis);

        // Approval → a CAP source for the study, one remark per item with the proposed action; best practice; KPI benchmark fields.
        study = await w.GetAsync(study.Id);
        study.AnalysisApprovedAt.ShouldNotBeNull();
        study.CapSourceId.ShouldNotBeNull();
        var progress = await BenchWorld.OkAsync<CapSourceProgress>(w.C(w.Coordinator).GetAsync($"{Base}/studies/{study.Id}/actions"));
        progress.Remarks.Count.ShouldBe(2);
        progress.Remarks.ShouldAllBe(r => r.Actions.Count == 1 && r.Actions[0].ValidationStatus == ValidationStatuses.Draft);
        progress.AllActionsCompleted.ShouldBeFalse();
        var items = (await BenchWorld.OkAsync<BenchAnalysisDto>(w.C(w.Coordinator).GetAsync($"{Base}/studies/{study.Id}/analysis"))).PlanItems;
        items.ShouldAllBe(i => i.RemarkId != null);
        var library = await BenchWorld.OkAsync<PagedResult<BenchBestPracticeDto>>(w.C(w.Outsider).GetAsync($"{Base}/practices?search=service%20app"));
        library.Items.ShouldContain(p => p.StudyId == study.Id);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var card = await scope.ServiceProvider.GetRequiredService<KpiDbContext>().Cards.AsNoTracking().SingleAsync(c => c.Id == cardId);
            card.BenchmarkResult.ShouldBe(0.8m);
            card.BenchmarkDescription!.ShouldContain(study.Number);
        }

        // The coordinator and topic owner follow the remarks in CAP (source-viewer); closure is refused while actions are open.
        var firstRemark = progress.Remarks.Single(r => r.ExternalKey == items[0].Id.ToString("D"));
        (await w.C(w.TopicOwner).GetAsync($"/api/v1/cap/remarks/{firstRemark.RemarkId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.Outsider).GetAsync($"/api/v1/cap/remarks/{firstRemark.RemarkId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await (await w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/request-closure", new { }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "bench.actionsOpen");

        // CAP: the owner (head of A) accepts and submits the proposed actions, the PM Office validates, assignees complete with evidence.
        var cap = new CapWorld
        {
            Factory = fx.Factory,
            DepartmentA = w.DepartmentA,
            SectionA1 = w.DepartmentA,
            DepartmentB = w.DepartmentB,
            Pmo = w.Office,
            SectionManager = w.Manager,
            Assignee = w.TopicOwner,
            DepartmentManager = w.Manager,
            Outsider = w.Outsider,
            Clients = w.Clients,
        };
        foreach (var remark in progress.Remarks)
        {
            (await cap.GetAsync(remark.RemarkId)).Owner!.Id.ShouldBe(w.Manager.EmployeeId!.Value);
            await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Manager).PostAsync($"/api/v1/cap/remarks/{remark.RemarkId}/accept", null));
            var current = await cap.GetAsync(remark.RemarkId, w.Manager);
            await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Manager).PutAsJsonAsync($"/api/v1/cap/remarks/{remark.RemarkId}/root-cause",
                new { values = new { method = RootCauseMethods.Text, text = "Channels grew without a design authority" }, version = current.Version }));
            await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Manager).PostAsJsonAsync($"/api/v1/cap/remarks/{remark.RemarkId}/submit", new { }));
            await cap.ValidateAsync(remark.Actions[0].ActionId);
        }

        for (var i = 0; i < progress.Remarks.Count; i++)
        {
            var actionId = progress.Remarks[i].Actions[0].ActionId;
            var assignee = progress.Remarks[i].Actions[0].AssignedEmployeeId == w.TopicOwner.EmployeeId ? w.TopicOwner : w.Coordinator;
            var current = await cap.ActionAsync(actionId, assignee);
            await CapWorld.OkAsync<CapActionDto>(w.C(assignee).PostAsJsonAsync($"/api/v1/cap/actions/{actionId}/progress", new { pct = 1m, version = current.Version }));
            await cap.UploadEvidenceAsync(actionId, assignee);
            await CapWorld.OkAsync<CapActionDto>(w.C(assignee).PostAsJsonAsync($"/api/v1/cap/actions/{actionId}/request-completion", new { note = "Done" }));
            if (i == 0)
            {
                await cap.DecideAsync(actionId, CapProcesses.Completion);
            }
        }

        // One action still waits for completion approval → not ready; approve it → ready (notified once), closure requested and approved.
        (await BenchWorld.OkAsync<CapSourceProgress>(w.C(w.Coordinator).GetAsync($"{Base}/studies/{study.Id}/actions"))).AllActionsCompleted.ShouldBeFalse();
        await cap.DecideAsync(progress.Remarks[1].Actions[0].ActionId, CapProcesses.Completion);
        var done = await BenchWorld.OkAsync<CapSourceProgress>(w.C(w.Coordinator).GetAsync($"{Base}/studies/{study.Id}/actions"));
        done.AllActionsCompleted.ShouldBeTrue();
        done.Remarks.ShouldAllBe(r => r.Status == CapRemarkStatuses.Completed && r.Actions.All(a => a.HasEvidence));
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Coordinator), "bench.readyForClosure")).Link.ShouldBe($"/bench/studies/{study.Id}?tab=actions");
        await BenchWorld.OkAsync<BenchStudyDto>(w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/request-closure", new { comment = "All actions completed" }));
        await w.DecideAsync(study.Id, BenchProcesses.Closure);
        var closed = await w.GetAsync(study.Id);
        closed.Status.ShouldBe(StudyStatuses.Completed);
        closed.Stage.ShouldBe(StudyStages.Completed);
        closed.ImpactDueOn.ShouldNotBeNull();
        (await BenchWorld.OkAsync<CurrentStatus>(w.C(w.Coordinator).GetAsync($"/api/v1/workflow/status/{Study.EntityType}/{study.Id}"))).MessageKey
            .ShouldBe("bench.currentStatus.completed");

        // Comparator register: history and rating; dashboard.
        var comparators = await BenchWorld.OkAsync<PagedResult<BenchComparatorDto>>(w.C(w.Office).GetAsync($"{Base}/comparators?search=Smart"));
        var registered = comparators.Items.First(c => c.Values.NameEn == "Smart Services Authority");
        (await BenchWorld.OkAsync<BenchComparatorDto>(w.C(w.Office).GetAsync($"{Base}/comparators/{registered.Id}"))).History.ShouldContain(h => h.StudyId == study.Id);
        var dashboard = await BenchWorld.OkAsync<BenchDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?year={year}"));
        dashboard.Completed.ShouldBeGreaterThanOrEqualTo(1);
        dashboard.PracticesAdopted.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task The_search_form_notice_is_sent_once_30_days_before_the_target_quarter()
    {
        var w = await BenchWorld.CreateAsync(fx.Factory);
        var study = await w.ApprovedStudyAsync(2027, 3, "Procurement benchmarking");
        study.Stage.ShouldBe(StudyStages.Search);
        study.SearchNoticeOn.ShouldBe(new DateOnly(2027, 6, 1)); // 1 July − 30 days, a Tuesday

        // The notice day rolls back to the previous working day: 1 October 2029 − 30 days = Saturday 1 September → Friday 31 August.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var calendar = scope.ServiceProvider.GetRequiredService<BenchCalendar>();
            (await calendar.SearchNoticeAsync(w.DepartmentA, 2029, 4, 30, false, CancellationToken.None)).Day.ShouldBe(new DateOnly(2029, 8, 31));
            (await calendar.SearchNoticeAsync(w.DepartmentA, 2027, 3, 30, true, CancellationToken.None)).Day.ShouldBe(new DateOnly(2027, 5, 20), "30 working days");
        }

        try
        {
            fx.Clock.Frozen = new DateTimeOffset(2027, 5, 31, 12, 0, 0, TimeSpan.Zero);
            await RunMonitorAsync();
            (await NotifiedAtAsync(study.Id)).ShouldBeNull("the day before the notice day");
            fx.Clock.Frozen = new DateTimeOffset(2027, 6, 1, 5, 0, 0, TimeSpan.Zero); // 09:00 in Dubai
            (await RunMonitorAsync()).SearchNotices.ShouldBeGreaterThanOrEqualTo(1);
            (await NotifiedAtAsync(study.Id)).ShouldNotBeNull();
            var first = await QueuedAsync(study.Id);
            first.ShouldBeGreaterThan(0);
            fx.Clock.Frozen = new DateTimeOffset(2027, 6, 2, 5, 0, 0, TimeSpan.Zero);
            await RunMonitorAsync();
            (await QueuedAsync(study.Id)).ShouldBe(first, "sent once");
        }
        finally
        {
            fx.Clock.Frozen = null;
        }

        // Queued for the coordinator and the unit head (the outbox delivers it at the frozen moment, in the future of this run).
        await using var check = TestAccess.SystemScope(fx.Factory);
        var queued = (await check.ServiceProvider.GetRequiredService<Strategy.Platform.Scheduling.Infrastructure.SchedulingDbContext>().Outbox.AsNoTracking().ToListAsync())
            .Where(m => m.Payload.Contains("bench.searchDue", StringComparison.Ordinal) && m.Payload.Contains(study.Id.ToString(), StringComparison.Ordinal)).ToList();
        queued.ShouldNotBeEmpty();
        queued.ShouldAllBe(m => m.Payload.Contains($"/bench/studies/{study.Id}?tab=search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scope_rules_follow_the_coordinator_topic_owner_unit_and_office()
    {
        var w = await BenchWorld.CreateAsync(fx.Factory);
        var study = await w.ApprovedStudyAsync(DateTime.UtcNow.Year + 1, 1, "Scope benchmarking");
        (await w.C(w.Coordinator).GetAsync($"{Base}/studies/{study.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.TopicOwner).GetAsync($"{Base}/studies/{study.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK, "topic owner (record role)");
        (await w.C(w.Outsider).GetAsync($"{Base}/studies/{study.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/studies/{study.Id}/history")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var topicView = await w.GetAsync(study.Id, w.TopicOwner);
        topicView.Can.EditSearch.ShouldBeFalse();
        (await w.C(w.TopicOwner).PutAsJsonAsync($"{Base}/studies/{study.Id}/search", new { candidates = Array.Empty<object>() })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.GetAsync(study.Id, w.Coordinator)).Can.EditSearch.ShouldBeTrue();
        (await w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{study.Id}/cancel", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Coordinator).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Outsider).GetAsync($"{Base}/practices")).StatusCode.ShouldBe(HttpStatusCode.OK, "the library is open to everyone");
        var mine = await BenchWorld.OkAsync<PagedResult<BenchStudyListItemDto>>(w.C(w.TopicOwner).GetAsync($"{Base}/studies?view=mine"));
        mine.Items.ShouldContain(s => s.Id == study.Id);
        var cancelled = await BenchWorld.OkAsync<BenchStudyDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/studies/{study.Id}/cancel", new { reason = "Merged with another study" }));
        cancelled.Status.ShouldBe(StudyStatuses.Cancelled);
        (await BenchWorld.OkAsync<BenchPlanDto>(w.C(w.Office).GetAsync($"{Base}/plan?year={study.TargetYear}"))).Studies.ShouldNotContain(s => s.Id == study.Id);
    }

    private async Task<BenchMonitorResult> RunMonitorAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<BenchMonitorRunner>().RunAsync(CancellationToken.None);
    }

    private async Task<int> QueuedAsync(Guid studyId)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        return (await scope.ServiceProvider.GetRequiredService<Strategy.Platform.Scheduling.Infrastructure.SchedulingDbContext>().Outbox.AsNoTracking().Select(m => m.Payload)
            .ToListAsync()).Count(p => p.Contains("bench.searchDue", StringComparison.Ordinal) && p.Contains(studyId.ToString(), StringComparison.Ordinal));
    }

    private async Task<DateTimeOffset?> NotifiedAtAsync(Guid studyId)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        return await scope.ServiceProvider.GetRequiredService<BenchDbContext>().Studies.Where(s => s.Id == studyId).Select(s => s.SearchNotifiedAt).SingleAsync();
    }
}
