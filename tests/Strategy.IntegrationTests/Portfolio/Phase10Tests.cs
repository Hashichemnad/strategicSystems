using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Portfolio.Api;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Settings;
using UglyToad.PdfPig;
using static Strategy.IntegrationTests.Portfolio.ProjectLifecycleTests;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>Phase 10 host: Portfolio with Gotenberg (status report PDF) on its own database.</summary>
public sealed class Phase10Fixture(PostgresFixture postgres) : IAsyncLifetime
{
    private SidecarContainers? _sidecars;

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _sidecars = new SidecarContainers(postgres);
        await _sidecars.InitializeAsync();
        Factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Gotenberg:Url"] = _sidecars.GotenbergUrl.ToString() });
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_sidecars is not null)
        {
            await _sidecars.DisposeAsync();
        }
    }

    public async Task SettingsAsync(Func<PortfolioSettings, PortfolioSettings> change)
    {
        await using var scope = TestAccess.SystemScope(Factory);
        var editor = scope.ServiceProvider.GetRequiredService<ISettingsEditor>();
        var current = await editor.GetDocumentAsync<PortfolioSettings>();
        var saved = await editor.SaveAsync(change(current.Value), current.Version, PortfolioSettings.Validate);
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? string.Join(", ", saved.Error.Fields?.Select(f => f.Field) ?? []) : null);
    }
}

/// <summary>
/// Phase 10 acceptance (docs/design/portfolio.md §45): agile delivery with the monthly roll-up and sprint approval modes, re-baselining only through an
/// approved change request, EVM and RAG, every feature switch on the API, the status report PDF in both languages and cost visibility for team members.
/// </summary>
public sealed class Phase10Tests(Phase10Fixture fx) : IClassFixture<Phase10Fixture>
{
    private const string Base = PortfolioWorld.Base;

    [Fact]
    public async Task An_agile_project_runs_two_sprints_with_board_and_burndown_and_a_monthly_roll_up_approval_and_appears_on_the_dashboard()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var (projectId, epics, stories) = await AgileInExecutionAsync(w, "Agile services", AgileApprovalModes.Monthly);
        var pm = w.C(w.Requester);

        // Baseline v1 was taken at planning approval (epics in the snapshot).
        var baselines = await PortfolioWorld.OkAsync<List<BaselineDto>>(pm.GetAsync($"{Base}/projects/{projectId}/baselines"));
        baselines.Single().VersionNo.ShouldBe(1);
        baselines[0].Items.Count(i => i.Kind == "epic").ShouldBe(2);

        // Board: WIP limit 1 on "In Progress".
        var view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        var board = view.Board with { Columns = view.Board.Columns.Select(c => c.Code == "in-progress" ? c with { WipLimit = 1 } : c).ToList() };
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PutAsJsonAsync($"{Base}/projects/{projectId}/board", new { board, version = view.ProjectVersion }));

        // Sprint 1: US1 + US2 (epic 1, 5 + 5 points).
        var sprint1 = await SprintAsync(pm, projectId, "Sprint 1", [stories[0], stories[1]], epics[0]);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint1}/start", new { version = Version(view, sprint1) }));
        view.Sprints.Single(s => s.Id == sprint1).CommittedPoints.ShouldBe(10);
        await MoveAsync(pm, view, stories[0], "in-progress");
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        var refused = await pm.PostAsJsonAsync($"{Base}/stories/{stories[1]}/move", new { column = "in-progress", rank = 2, version = StoryVersion(view, stories[1]) });
        await refused.KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.wipLimit");
        view = await MoveAsync(pm, view, stories[0], "done");
        view = await MoveAsync(pm, view, stories[1], "in-progress");
        view = await MoveAsync(pm, view, stories[1], "done");
        var burndown = await PortfolioWorld.OkAsync<BurndownDto>(pm.GetAsync($"{Base}/sprints/{sprint1}/burndown"));
        burndown.Committed.ShouldBe(10);
        burndown.Points.Where(p => p.Remaining >= 0).Last().Remaining.ShouldBe(0m, "both stories are done");
        burndown.Points[0].Ideal.ShouldBe(10m);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint1}/close", new
        {
            values = new { demoOutcome = "Online form demonstrated", wentWell = "Daily stand-ups", toImprove = "Earlier test data" },
            version = Version(view, sprint1),
        }));
        view.Sprints.Single(s => s.Id == sprint1).ShouldSatisfyAllConditions(s => s.Status.ShouldBe(SprintStatuses.Closed), s => s.CompletedPoints.ShouldBe(10));
        var lessons = await PortfolioWorld.OkAsync<List<LessonDto>>(pm.GetAsync($"{Base}/projects/{projectId}/lessons"));
        lessons.Count(l => l.Lesson!.StartsWith("Sprint 1", StringComparison.Ordinal)).ShouldBe(2, "the retrospective feeds the lessons learned");

        // Sprint 2: US3 (3 points) done, US4 left over → back to the backlog. An impediment becomes a RAID issue.
        var sprint2 = await SprintAsync(pm, projectId, "Sprint 2", [stories[2], stories[3]], epics[1]);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint2}/start", new { version = Version(view, sprint2) }));
        view = await MoveAsync(pm, view, stories[2], "done");
        await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint2}/impediments", new { title = "Test environment down", priority = "high" }));
        (await PortfolioWorld.OkAsync<RaidLogDto>(pm.GetAsync($"{Base}/projects/{projectId}/raid"))).Items
            .ShouldContain(i => i.Type == RaidTypes.Issue && i.Source == RaidSources.Sprint && i.Title == "Test environment down");
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint2}/close", new
        {
            values = new { demoOutcome = "Tracking page demonstrated" },
            version = Version(view, sprint2),
        }));
        view.Stories.Single(s => s.Id == stories[3]).SprintId.ShouldBeNull("unfinished work returns to the backlog");
        view.Metrics.Velocity.Select(v => (v.Committed, v.Completed)).ShouldBe([(10, 10), (4, 3)]);
        view.PendingProgress.ShouldBe(0.9m);
        view.ApprovedProgress.ShouldBe(0m, "nothing counts before the roll-up is approved");

        // Sprint mode is not this project's mode: a sprint cannot be submitted on its own.
        await (await pm.PostAsJsonAsync($"{Base}/sprints/{sprint1}/submit", new { version = Version(view, sprint1) }))
            .KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.sprintMonthlyMode");

        // Monthly roll-up: the window carries both closed sprints into one portfolio.monthly-update approval.
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{projectId}/monthly/open", null));
        var project = await Get(w, projectId);
        var monthly = await PortfolioWorld.OkAsync<MonthlyViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/monthly/submit",
            new { version = project.Version, comment = "Two sprints" }));
        monthly.Current!.Status.ShouldBe(MonthlyStatuses.Submitted);
        JsonDocument.Parse(monthly.Current.Sprints).RootElement.GetArrayLength().ShouldBe(2);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view.Sprints.Where(s => s.Number <= 2).ShouldAllBe(s => s.Status == SprintStatuses.InApproval);
        await ActAsync(w, w.Director, projectId, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, projectId, RequestApprovalProcess.PmOfficeStep);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view.Sprints.Where(s => s.Number <= 2).ShouldAllBe(s => s.Status == SprintStatuses.Approved);
        view.ApprovedProgress.ShouldBe(0.9m);
        project = await Get(w, projectId);
        project.Performance.ShouldBe(0.9m, "Σ epic weight × approved done points: 0.6 × 10/10 + 0.4 × 3/4");

        // An approved story is locked in done.
        await (await pm.PostAsJsonAsync($"{Base}/stories/{stories[0]}/move", new { column = "to-do", rank = 1, version = StoryVersion(view, stories[0]) }))
            .KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.storyLocked");

        // The portfolio dashboard shows the agile project beside a waterfall one on the common progress %.
        var waterfall = await ApprovedProjectAsync(w, "Waterfall beside agile");
        var dashboard = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.PmOffice).GetAsync($"{Base}/dashboard"));
        dashboard.Projects.Single(p => p.Id == projectId).ShouldSatisfyAllConditions(
            p => p.Methodology.ShouldBe(Methodologies.Agile), p => p.Performance.ShouldBe(0.9m), p => p.Health.ShouldNotBeNull());
        dashboard.Projects.Single(p => p.Id == waterfall.Id).Methodology.ShouldBe(Methodologies.Waterfall);
        dashboard.ByStage.Sum(s => s.Count).ShouldBe(dashboard.Total);
        dashboard.Funnel.Approved.ShouldBeGreaterThanOrEqualTo(2);

        // Gantt shows sprints and releases; portfolio-wide Gantt lists both projects.
        var gantt = await PortfolioWorld.OkAsync<GanttDto>(pm.GetAsync($"{Base}/projects/{projectId}/gantt"));
        gantt.Rows.Count(r => r.Kind == "sprint").ShouldBe(2);
        gantt.Rows.ShouldContain(r => r.Kind == "release");
        var portfolio = await PortfolioWorld.OkAsync<GanttDto>(w.C(w.PmOffice).GetAsync($"{Base}/gantt"));
        portfolio.Rows.Where(r => r.Kind == "project").Select(r => r.Id).ShouldContain(projectId);
        portfolio.Rows.Where(r => r.Kind == "project").Select(r => r.Id).ShouldContain(waterfall.Id);
    }

    [Fact]
    public async Task In_sprint_mode_each_sprint_is_approved_on_its_own_and_no_monthly_window_opens()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var (projectId, epics, stories) = await AgileInExecutionAsync(w, "Sprint mode", AgileApprovalModes.Sprint);
        var pm = w.C(w.Requester);
        var project = await Get(w, projectId);
        project.Delivery.ApprovalMode.ShouldBe(AgileApprovalModes.Sprint);
        var sprint = await SprintAsync(pm, projectId, "Sprint A", [stories[0]], epics[0]);
        var view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint}/start", new { version = Version(view, sprint) }));
        view = await MoveAsync(pm, view, stories[0], "done");
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/sprints/{sprint}/close",
            new { values = new { demoOutcome = "Shown" }, version = Version(view, sprint) }));
        view.Permissions.CanSubmitSprint.ShouldBeTrue();
        var submit = new HttpRequestMessage(HttpMethod.Post, $"{Base}/sprints/{sprint}/submit") { Content = JsonContent.Create(new { version = Version(view, sprint) }) };
        submit.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.SendAsync(submit));
        view.Sprints.Single(s => s.Id == sprint).Status.ShouldBe(SprintStatuses.InApproval);
        (await Get(w, projectId)).Approval.Process.ShouldBe(ProjectApprovalProcesses.SprintReview);
        await ActAsync(w, w.Director, projectId, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, projectId, RequestApprovalProcess.PmOfficeStep);
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{projectId}/agile"));
        view.Sprints.Single(s => s.Id == sprint).Status.ShouldBe(SprintStatuses.Approved);
        (await Get(w, projectId)).Performance.ShouldBe(0.3m, "epic 1 (60 %) half done: 5 of 10 points");

        await using var scope = TestAccess.SystemScope(fx.Factory);
        await scope.ServiceProvider.GetRequiredService<MonthlyCycleRunner>().RunAsync(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        (await db.MonthlyUpdates.AnyAsync(m => m.ProjectId == projectId)).ShouldBeFalse("sprint mode approves sprints, not monthly windows");
    }

    [Fact]
    public async Task The_plan_is_re_baselined_only_by_an_approved_change_request_and_variance_and_the_status_report_follow()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var projectId = await WaterfallInExecutionAsync(w, "Rebaseline");
        var pm = w.C(w.Requester);
        var baselines = await PortfolioWorld.OkAsync<List<BaselineDto>>(pm.GetAsync($"{Base}/projects/{projectId}/baselines"));
        baselines.Select(b => b.VersionNo).ShouldBe([1]);
        (await pm.PostAsJsonAsync($"{Base}/projects/{projectId}/baselines", new { })).IsSuccessStatusCode.ShouldBeFalse("there is no endpoint to take a baseline");

        // The plan is frozen in execution…
        var plan = await PlanAsync(w, projectId);
        var leaf = plan.First(m => !m.IsHeader && !m.HighLevel);
        var move = new
        {
            values = new { titleEn = leaf.TitleEn, isHeader = false, highLevel = false, plannedStart = leaf.PlannedStart, plannedEnd = leaf.PlannedEnd!.Value.AddDays(20), weight = leaf.Weight },
            version = leaf.Version,
        };
        await (await pm.PutAsJsonAsync($"{Base}/milestones/{leaf.Id}", move)).KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.notAllowedInStage");

        // …until a change request with the area "plan" is approved.
        var cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/change-requests", new
        {
            values = new { type = "details", areas = new[] { "plan", "dates" }, reason = "Vendor slipped", proposedEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(8) },
        }));
        var submitted = new HttpRequestMessage(HttpMethod.Post, $"{Base}/change-requests/{cr.Id}/submit") { Content = JsonContent.Create(new { version = cr.Version }) };
        submitted.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(pm.SendAsync(submitted));
        var apply = new HttpRequestMessage(HttpMethod.Post, $"{Base}/change-requests/{cr.Id}/apply")
        {
            Content = JsonContent.Create(new { values = new { rebaseline = true }, version = cr.Version }),
        };
        apply.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await pm.SendAsync(apply)).IsSuccessStatusCode.ShouldBeFalse("not approved yet");
        await ActAsync(w, w.Director, projectId, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, projectId, RequestApprovalProcess.PmOfficeStep);
        (await Get(w, projectId)).Delivery.ReplanChangeRequestId.ShouldBe(cr.Id);
        await PortfolioWorld.OkAsync<MilestoneDto>(pm.PutAsJsonAsync($"{Base}/milestones/{leaf.Id}", move));

        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(pm.GetAsync($"{Base}/change-requests/{cr.Id}"));
        var applied = new HttpRequestMessage(HttpMethod.Post, $"{Base}/change-requests/{cr.Id}/apply")
        {
            Content = JsonContent.Create(new { values = new { plannedEnd = cr.ProposedEnd, rebaseline = true }, version = cr.Version }),
        };
        applied.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        cr = await PortfolioWorld.OkAsync<ChangeRequestDto>(pm.SendAsync(applied));
        cr.Applied.ShouldContain(e => e.Field == "baseline" && e.New == "v2");
        (await Get(w, projectId)).Delivery.ReplanChangeRequestId.ShouldBeNull("applying closes the re-plan");
        baselines = await PortfolioWorld.OkAsync<List<BaselineDto>>(pm.GetAsync($"{Base}/projects/{projectId}/baselines"));
        baselines.Select(b => b.VersionNo).ShouldBe([2, 1]);
        baselines[0].ChangeRequestNumber.ShouldBe(cr.Number);

        // Variance against v1: the moved milestone is 20 days late at its end.
        var variance = await PortfolioWorld.OkAsync<VarianceDto>(pm.GetAsync($"{Base}/projects/{projectId}/baselines/variance?version=1"));
        variance.Rows.Single(r => r.Id == leaf.Id).ShouldSatisfyAllConditions(r => r.Change.ShouldBe(BaselineVariance.Changed), r => r.EndVarianceDays.ShouldBe(20));
        (await PortfolioWorld.OkAsync<VarianceDto>(pm.GetAsync($"{Base}/projects/{projectId}/baselines/variance"))).Rows.ShouldAllBe(r => r.Change == BaselineVariance.Unchanged);

        // Status report PDF from the approved monthly update, in Arabic and English.
        await PortfolioWorld.OkAsync<MonthlyViewDto>(w.C(w.PmOffice).PostAsync($"{Base}/projects/{projectId}/monthly/open", null));
        await PortfolioWorld.OkAsync<MonthlyViewDto>(pm.PutAsJsonAsync($"{Base}/projects/{projectId}/monthly/narrative",
            new { narrativeAr = "تقدم المشروع وفق الخطة", narrativeEn = "The project progressed as planned" }));
        var project = await Get(w, projectId);
        await (await pm.GetAsync($"{Base}/projects/{projectId}/status-report")).KeyAsync(HttpStatusCode.UnprocessableEntity, "portfolio.errors.noApprovedUpdate");
        await PortfolioWorld.OkAsync<MonthlyViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/monthly/submit", new { version = project.Version }));
        await ActAsync(w, w.Director, projectId, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, projectId, RequestApprovalProcess.PmOfficeStep);
        foreach (var (lang, expected) in new[] { ("ar", "تقرير"), ("en", "Monthly") })
        {
            var response = await pm.GetAsync($"{Base}/projects/{projectId}/status-report?language={lang}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
            using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
            var text = string.Join(' ', pdf.GetPages().SelectMany(p => p.GetWords()).Select(x => x.Text));
            pdf.NumberOfPages.ShouldBeGreaterThanOrEqualTo(1);
            if (lang == "en")
            {
                text.ShouldContain(expected);
                text.ShouldContain("progressed");
                text.ShouldContain("Budget");
            }
        }
    }

    [Fact]
    public async Task Each_switch_hides_its_api_and_evm_and_rag_work_with_override_and_justification()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var projectId = await WaterfallInExecutionAsync(w, "Switches");
        var pm = w.C(w.Requester);
        var contract = await PortfolioWorld.OkAsync<ContractDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/contracts",
            new { values = new { name = "Build", supplier = "Vendor", amount = 100_000 } }));
        var leaf = (await PlanAsync(w, projectId)).First(m => !m.IsHeader && !m.HighLevel);
        var checks = new (string Code, string Method, string Url)[]
        {
            (FeatureCodes.Health, "GET", $"/projects/{projectId}/health"),
            (FeatureCodes.Baselines, "GET", $"/projects/{projectId}/baselines"),
            (FeatureCodes.EarnedValue, "GET", $"/projects/{projectId}/evm"),
            (FeatureCodes.Raid, "GET", $"/projects/{projectId}/raid"),
            (FeatureCodes.RiskMatrix, "GET", $"/projects/{projectId}/risk-matrix"),
            (FeatureCodes.Raci, "GET", $"/projects/{projectId}/raci"),
            (FeatureCodes.CommunicationPlan, "GET", $"/projects/{projectId}/communications"),
            (FeatureCodes.Benefits, "GET", $"/projects/{projectId}/benefits"),
            (FeatureCodes.Quality, "POST", $"/milestones/{leaf.Id}/sign-off"),
            (FeatureCodes.Procurement, "GET", $"/projects/{projectId}/contracts/{contract.Id}/extras"),
            (FeatureCodes.StatusReport, "GET", $"/projects/{projectId}/status-report"),
            (FeatureCodes.GateChecklists, "GET", $"/projects/{projectId}/checklist"),
            (FeatureCodes.Templates, "GET", "/templates"),
            (FeatureCodes.Portfolios, "GET", "/groups"),
        };
        try
        {
            await fx.SettingsAsync(s => s with { Features = new PortfolioFeatures
            {
                Portfolios = false, Prioritisation = false, Health = false, Baselines = false, EarnedValue = false, Raid = false, RiskMatrix = false, Raci = false,
                CommunicationPlan = false, Benefits = false, Quality = false, Procurement = false, StatusReport = false, GateChecklists = false, Templates = false,
            } });
            var features = await PortfolioWorld.OkAsync<PortfolioFeaturesDto>(pm.GetAsync($"{Base}/features"));
            features.Features.Values.ShouldAllBe(on => !on);
            foreach (var (code, method, url) in checks)
            {
                var response = method == "GET"
                    ? await w.C(w.PmOffice).GetAsync(Base + url)
                    : await w.C(w.PmOffice).PostAsJsonAsync(Base + url, new { decision = "accepted", version = leaf.Version });
                await response.KeyAsync(HttpStatusCode.NotFound, "portfolio.errors.featureOff");
            }

            await fx.SettingsAsync(s => s with { Features = new PortfolioFeatures { EarnedValue = true } });
            (await PortfolioWorld.OkAsync<PortfolioFeaturesDto>(pm.GetAsync($"{Base}/features"))).Features.Values.ShouldAllBe(on => on);
            foreach (var (code, method, url) in checks.Where(c => c.Method == "GET" && c.Code != FeatureCodes.StatusReport))
            {
                var response = await w.C(w.PmOffice).GetAsync(Base + url);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{code}: {await response.Content.ReadAsStringAsync()}");
            }

            // EVM on the project's own figures: BAC = the baseline budget, AC = the actual yearly costs.
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
                var tracked = await db.Projects.SingleAsync(p => p.Id == projectId);
                var c = (await Get(w, projectId)).Charter;
                tracked.EditCharter(c with { Year1ActualCost = 60_000m }, "[]");
                await db.SaveChangesAsync();
            }

            ProjectFullDto project;
            var evm = await PortfolioWorld.OkAsync<EvmDto>(pm.GetAsync($"{Base}/projects/{projectId}/evm"));
            evm.Values.Bac.ShouldBe(240_000m);
            evm.Values.Ac.ShouldBe(60_000m);
            evm.Values.Ev.ShouldBe(decimal.Round(240_000m * evm.Values.ProgressRatio, 2));
            evm.Values.Cpi.ShouldBe(evm.Values.Ev == 0 ? 0m : decimal.Round(evm.Values.Ev / 60_000m, 4));
            evm.BaselineVersion.ShouldBe(1);

            // RAG: an override needs a justification and wins over the calculated colour.
            project = await Get(w, projectId);
            await (await pm.PutAsJsonAsync($"{Base}/projects/{projectId}/health/risk", new { value = "red", version = project.Version }))
                .FieldAsync("justification", "portfolio.errors.justificationRequired");
            var health = await PortfolioWorld.OkAsync<HealthDto>(pm.PutAsJsonAsync($"{Base}/projects/{projectId}/health/risk",
                new { value = "red", justification = "Regulator review pending", version = project.Version }));
            health.Health.Risk.Effective.ShouldBe(Rag.Red);
            health.Health.Risk.Auto.ShouldBe(Rag.Amber, "the charter risk is 3 × 4 = 12 (High)");
            health.Health.Overall.Effective.ShouldBe(Rag.Red);
            health = await PortfolioWorld.OkAsync<HealthDto>(pm.PutAsJsonAsync($"{Base}/projects/{projectId}/health/risk", new { value = (string?)null, version = health.Version }));
            health.Health.Risk.Override.ShouldBeNull();
            (await w.C(w.DepartmentViewer).PutAsJsonAsync($"{Base}/projects/{projectId}/health/risk", new { value = "green", justification = "x", version = health.Version }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await fx.SettingsAsync(s => s with { Features = new PortfolioFeatures(), TeamMembersSeeCosts = false });
        }
    }

    [Fact]
    public async Task A_team_member_without_the_cost_setting_sees_no_costs_anywhere_until_the_setting_allows_it()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var projectId = await WaterfallInExecutionAsync(w, "Costs");
        var member = w.OtherRequester;
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, projectId, RecordPrincipalKinds.Employee,
                member.EmployeeId!.Value, PortfolioPermissions.TeamMemberRole);
        }

        var pm = w.C(w.Requester);
        await PortfolioWorld.OkAsync<ChangeRequestDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/change-requests",
            new { values = new { type = "details", areas = new[] { "budget" }, reason = "More licences", proposedBudget = 260_000m, costAffected = true } }));
        try
        {
            await fx.SettingsAsync(s => s with { Features = new PortfolioFeatures { EarnedValue = true }, TeamMembersSeeCosts = false });
            var asPm = await Get(w, projectId, w.Requester);
            asPm.Budget.ShouldBe(240_000m);
            asPm.Delivery.CostsHidden.ShouldBeFalse();

            var c = w.C(member);
            var asMember = await Get(w, projectId, member);
            asMember.ShouldSatisfyAllConditions(
                p => p.Budget.ShouldBeNull(), p => p.Year1PlannedCost.ShouldBeNull(), p => p.Charter.Year1ActualCost.ShouldBeNull(), p => p.Delivery.CostsHidden.ShouldBeTrue());
            (await PortfolioWorld.OkAsync<MilestonePlanDto>(c.GetAsync($"{Base}/projects/{projectId}/milestones"))).Milestones.ShouldAllBe(m => m.Cost == null);
            (await PortfolioWorld.OkAsync<List<ChangeRequestDto>>(c.GetAsync($"{Base}/projects/{projectId}/change-requests")))
                .ShouldAllBe(x => x.ProposedBudget == null && x.CostsHidden);
            (await PortfolioWorld.OkAsync<List<BaselineDto>>(c.GetAsync($"{Base}/projects/{projectId}/baselines"))).ShouldAllBe(b => b.Budget == null && b.Items.All(i => i.Cost == null));
            (await c.GetAsync($"{Base}/projects/{projectId}/evm")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var dashboard = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(c.GetAsync($"{Base}/dashboard"));
            dashboard.Projects.Single(p => p.Id == projectId).Budget.ShouldBeNull();
            dashboard.Budget.ShouldNotContain(b => b.Budget > 0);
            dashboard.CostsHidden.ShouldBeGreaterThan(0);
            var list = await c.GetFromJsonAsync<JsonElement>($"{Base}/projects?pageSize=50");
            list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == projectId).GetProperty("budget").ValueKind.ShouldBe(JsonValueKind.Null);

            // Ask / approver brief record source and the MCP project tool (same service as the API).
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var principal = await PrincipalAsync(scope.ServiceProvider, member);
                var source = scope.ServiceProvider.GetServices<IAiRecordSource>().Single(s => s.ResourceType == PortfolioPermissions.ProjectResource);
                var content = await source.GetAsync(projectId, principal, CancellationToken.None);
                content.ShouldNotBeNull();
                content.Fields.ShouldNotContain(f => f.Name == nameof(Project.Budget));
                var pmPrincipal = await PrincipalAsync(scope.ServiceProvider, w.Requester);
                (await source.GetAsync(projectId, pmPrincipal, CancellationToken.None))!.Fields.ShouldContain(f => f.Name == nameof(Project.Budget));
            }

            // The setting gives the team member the costs.
            await fx.SettingsAsync(s => s with { TeamMembersSeeCosts = true });
            asMember = await Get(w, projectId, member);
            asMember.Budget.ShouldBe(240_000m);
            asMember.Delivery.CostsHidden.ShouldBeFalse();
            (await c.GetAsync($"{Base}/projects/{projectId}/evm")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await fx.SettingsAsync(s => s with { Features = new PortfolioFeatures(), TeamMembersSeeCosts = false });
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Approved request → PM takes ownership → methodology agile → (set-up) charter steps → agile planning → planning approval → execution.</summary>
    private async Task<(Guid ProjectId, Guid[] Epics, Guid[] Stories)> AgileInExecutionAsync(PortfolioWorld w, string name, string mode)
    {
        var project = await ApprovedProjectAsync(w, name);
        var pm = w.C(w.Requester);
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/take-ownership", new { version = project.Version }));
        project.Delivery.CanSetMethodology.ShouldBeTrue();
        var view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PutAsJsonAsync($"{Base}/projects/{project.Id}/methodology",
            new { methodology = Methodologies.Agile, sprintLengthDays = 14, definitionOfDone = "Tested, reviewed, deployed", agileApprovalMode = mode, version = project.Version }));
        view.Methodology.ShouldBe(Methodologies.Agile);
        view.Board.Columns.Count.ShouldBe(4);
        await DriveToPlanningAsync(project.Id);
        (await PortfolioWorld.OkAsync<AgileViewDto>(pm.GetAsync($"{Base}/projects/{project.Id}/agile"))).Permissions.CanSetMethodology
            .ShouldBeFalse("after the charter the methodology changes only through a change request");

        await PortfolioWorld.OkAsync<PlanningItemDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items",
            new { values = new { category = PlanningCategories.InScope, description = "Online services" } }));
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/releases",
            new { values = new { nameEn = "Release 1", targetDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3) } }));
        var release = view.Releases.Single().Id;
        project = await Get(w, project.Id);
        (await PortfolioWorld.ProblemFieldsAsync(await pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning", new { version = project.Version })))
            .Select(f => f.Field).ShouldContain("gate.epics");
        await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/epics", new { values = new { titleEn = "Portal", weight = 0.6m, releaseId = release } }));
        (await PortfolioWorld.ProblemFieldsAsync(await pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/epics", new { values = new { titleEn = "Too big", weight = 0.5m } })))
            .Single().MessageKey.ShouldBe("portfolio.errors.weightsOver100");
        view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/epics", new { values = new { titleEn = "Tracking", weight = 0.4m, releaseId = release } }));
        var epics = view.Epics.OrderBy(e => e.Code, StringComparer.Ordinal).Select(e => e.Id).ToArray();
        foreach (var (epic, title, points) in new[] { (epics[0], "Submit online", 5), (epics[0], "Pay online", 5), (epics[1], "Track status", 3), (epics[1], "Notify", 1) })
        {
            view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/stories",
                new { values = new { epicId = epic, title, asA = "customer", iWant = title, soThat = "I save time", storyPoints = points, priority = "must" } }));
        }

        var stories = view.Stories.OrderBy(s => s.Rank).Select(s => s.Id).ToArray();
        project = await Get(w, project.Id);
        project.Gates.PlanningMandatoryDone.ShouldBeTrue(string.Join(", ", project.Gates.Planning.Where(g => !g.Met).Select(g => g.Code)));
        await PortfolioWorld.OkAsync<ProjectFullDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning", new { version = project.Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        (await Get(w, project.Id)).SubState.ShouldBe(SubStates.Execution);
        return (project.Id, epics, stories);
    }

    private async Task<Guid> WaterfallInExecutionAsync(PortfolioWorld w, string name)
    {
        var project = await ApprovedProjectAsync(w, name);
        var pm = w.C(w.Requester);
        project = await PortfolioWorld.OkAsync<ProjectFullDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/take-ownership", new { version = project.Version }));
        await FillCharterAsync(w, project.Id);
        await DriveToPlanningAsync(project.Id);
        await PortfolioWorld.OkAsync<PlanningItemDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/planning-items",
            new { values = new { category = PlanningCategories.InScope, description = "Everything" } }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await MilestoneAsync(w, project.Id, new { titleEn = "Design", isHeader = false, highLevel = false, plannedStart = today, plannedEnd = today.AddMonths(2), weight = 0.4m, cost = 50_000m });
        await MilestoneAsync(w, project.Id, new { titleEn = "Build", isHeader = false, highLevel = false, plannedStart = today.AddMonths(2), plannedEnd = today.AddMonths(6), weight = 0.6m, cost = 150_000m });
        project = await Get(w, project.Id);
        await PortfolioWorld.OkAsync<ProjectFullDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/submit-planning", new { version = project.Version }));
        await ActAsync(w, w.Director, project.Id, RequestApprovalProcess.DepartmentDirectorStep);
        await ActAsync(w, w.PmOffice, project.Id, RequestApprovalProcess.PmOfficeStep);
        (await Get(w, project.Id)).SubState.ShouldBe(SubStates.Execution);
        return project.Id;
    }

    /// <summary>Set-up only: charter approval, LPO and team formation through the domain transitions (their own tests are in Phase 9).</summary>
    private async Task DriveToPlanningAsync(Guid projectId)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var project = await db.Projects.SingleAsync(p => p.Id == projectId);
        var template = StageTemplate.ProjectDefault;
        foreach (var action in new[] { ProjectActions.SubmitCharter, ProjectActions.CharterApproved, ProjectActions.LpoUploaded, ProjectActions.TeamFormed })
        {
            project.Apply(action, template, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue(action);
        }

        project.SubState.ShouldBe(SubStates.PlanningDraft);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SprintAsync(HttpClient pm, Guid projectId, string name, Guid[] stories, Guid epic)
    {
        var view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/projects/{projectId}/sprints",
            new { values = new { name, goal = name + " goal", startDate = DateOnly.FromDateTime(DateTime.UtcNow), capacityPoints = 20 } }));
        var sprint = view.Sprints.Single(s => s.Name == name).Id;
        foreach (var id in stories)
        {
            var s = view.Stories.Single(x => x.Id == id);
            view = await PortfolioWorld.OkAsync<AgileViewDto>(pm.PutAsJsonAsync($"{Base}/projects/{projectId}/stories/{id}", new
            {
                values = new { epicId = s.EpicId, title = s.Title, asA = s.AsA, iWant = s.IWant, soThat = s.SoThat, storyPoints = s.StoryPoints, priority = s.Priority, sprintId = sprint },
                version = s.Version,
            }));
        }

        _ = epic;
        return sprint;
    }

    private static async Task<AgileViewDto> MoveAsync(HttpClient pm, AgileViewDto view, Guid story, string column) =>
        await PortfolioWorld.OkAsync<AgileViewDto>(pm.PostAsJsonAsync($"{Base}/stories/{story}/move", new { column, rank = 1, version = StoryVersion(view, story) }));

    private static uint Version(AgileViewDto view, Guid sprint) => view.Sprints.Single(s => s.Id == sprint).Version;

    private static uint StoryVersion(AgileViewDto view, Guid story) => view.Stories.Single(s => s.Id == story).Version;
}

internal static class Phase10Problems
{
    public static async Task KeyAsync(this HttpResponseMessage response, HttpStatusCode status, string messageKey)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        var problem = JsonDocument.Parse(body).RootElement;
        var key = problem.GetProperty("messageKey").GetString();
        if (key != messageKey && problem.TryGetProperty("errors", out var errors))
        {
            errors.EnumerateArray().Select(e => e.GetProperty("messageKey").GetString()).ShouldContain(messageKey, body);
            return;
        }

        key.ShouldBe(messageKey, body);
    }

    public static async Task FieldAsync(this HttpResponseMessage response, string field, string messageKey)
    {
        var fields = await PortfolioWorld.ProblemFieldsAsync(response);
        fields.ShouldContain(f => f.Field == field && f.MessageKey == messageKey);
    }
}
