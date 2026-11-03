using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Jobs;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Teams;

/// <summary>Teams Management end to end (docs/design/teams.md §17) on a real database, with a travelling clock and a capturing PDF renderer.</summary>
public sealed class TeamsApiTests(TeamsFixture fx) : IClassFixture<TeamsFixture>
{
    private const string Base = TeamsWorld.Base;

    [Fact]
    public async Task Formation_approval_by_director_then_general_manager_generates_a_numbered_bilingual_memo()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var draft = await w.CreateAsync(w.Request("Budget Committee"));
        draft.Status.ShouldBe(TeamStatuses.Draft);
        draft.Number!.ShouldStartWith("S-");
        draft.Members.Count.ShouldBe(4);
        draft.Leader!.Id.ShouldBe(w.President.EmployeeId!.Value);
        draft.Actions.ShouldBe([TeamActions.Submit, TeamActions.Cancel], ignoreOrder: true);

        // Members see nothing before the formation is approved; outsiders never.
        (await w.C(w.MemberA).GetAsync($"{Base}/{draft.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var submitted = await w.SubmitAsync(draft);
        submitted.Status.ShouldBe(TeamStatuses.PendingApproval);
        var directorTask = await TeamsWorld.MyTaskAsync(w.C(w.Director), draft.Id);
        await ApprovalWorld.ActOkAsync(w.C(w.Director), directorTask.TaskId, ApprovalActions.Approve);
        (await w.GetAsync(draft.Id)).Status.ShouldBe(TeamStatuses.PendingApproval); // GM step still open
        await ApprovalWorld.ActOkAsync(w.C(w.Gm), (await TeamsWorld.MyTaskAsync(w.C(w.Gm), draft.Id)).TaskId, ApprovalActions.Approve);

        var team = await w.GetAsync(draft.Id);
        team.Status.ShouldBe(TeamStatuses.Active);
        team.MemoNumber!.ShouldStartWith($"MEMO-{DateTime.UtcNow.Year}-");
        team.MemoDate.ShouldNotBeNull();
        await TeamsWorld.WaitAsync(async () => (await w.GetAsync(draft.Id)).MemoFileId is not null, "the memo PDF");
        team = await w.GetAsync(draft.Id);
        var html = fx.Pdf.Html.Last(h => h.Contains(team.MemoNumber!, StringComparison.Ordinal));
        html.ShouldContain("قرار تشكيل");
        html.ShouldContain("Formation decision");
        html.ShouldContain("Budget Committee");
        html.ShouldContain("لجنة Budget Committee");
        html.ShouldContain("Dr. Lina Haddad");
        html.ShouldContain("General Manager");
        var pdf = await w.C(w.MemberA).GetAsync($"/api/v1/files/{team.MemoFileId}/content");
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        // Members are notified and can read the team now; record roles follow the member roles.
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.MemberA), "teams.activated");
        var asMember = await w.GetAsync(team.Id, w.MemberA);
        asMember.Permissions.CanViewResults.ShouldBeFalse();
        asMember.Permissions.CanOperate.ShouldBeFalse();
        var asPresident = await w.GetAsync(team.Id, w.President);
        asPresident.Permissions.CanOperate.ShouldBeTrue();
        asPresident.Permissions.CanViewResults.ShouldBeTrue();
        (await w.GetAsync(team.Id, w.Secretary)).Permissions.CanMinute.ShouldBeTrue();
        (await w.C(w.Outsider).GetAsync($"{Base}/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Hr).GetAsync($"{Base}/{team.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_uploaded_external_memo_replaces_the_generated_one()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var draft = await w.CreateAsync(w.Request("Ethics Committee", memoMode: MemoModes.External));
        var file = await TeamsWorld.UploadAsync(w.C(w.Requester), TeamPermissions.ResourceType, draft.Id, "signed-memo.pdf");
        (await w.C(w.Requester).PostAsJsonAsync($"{Base}/{draft.Id}/memo", new { number = "DG/77/2026", date = "2026-10-01", fileId = Guid.CreateVersion7(), version = draft.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var withMemo = await TeamsWorld.OkAsync<TeamDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/{draft.Id}/memo",
            new { number = "DG/77/2026", date = "2026-10-01", fileId = file.Id, version = draft.Version }));
        withMemo.MemoMode.ShouldBe(MemoModes.External);
        var before = fx.Pdf.Html.Count;
        await w.SubmitAsync(withMemo);
        await ApprovalWorld.ActOkAsync(w.C(w.Director), (await TeamsWorld.MyTaskAsync(w.C(w.Director), draft.Id)).TaskId, ApprovalActions.Approve);
        await ApprovalWorld.ActOkAsync(w.C(w.Gm), (await TeamsWorld.MyTaskAsync(w.C(w.Gm), draft.Id)).TaskId, ApprovalActions.Approve);
        var team = await w.GetAsync(draft.Id);
        team.Status.ShouldBe(TeamStatuses.Active);
        team.MemoNumber.ShouldBe("DG/77/2026");
        team.MemoFileId.ShouldBe(file.Id);
        await Task.Delay(1500);
        fx.Pdf.Html.Skip(before).ShouldNotContain(h => h.Contains("Ethics Committee", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returned_formation_goes_back_to_draft_and_a_rejected_one_is_cancelled()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var team = await w.SubmitAsync(await w.CreateAsync(w.Request("Returned Team")));
        await ApprovalWorld.ActOkAsync(w.C(w.Director), (await TeamsWorld.MyTaskAsync(w.C(w.Director), team.Id)).TaskId, ApprovalActions.Return, "Add the mandate");
        var returned = await w.GetAsync(team.Id, w.Requester);
        returned.Status.ShouldBe(TeamStatuses.Draft);
        returned.StatusReason.ShouldBe("Add the mandate");
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Requester), "teams.formationReturned");
        var again = await w.SubmitAsync(returned);
        await ApprovalWorld.ActOkAsync(w.C(w.Director), (await TeamsWorld.MyTaskAsync(w.C(w.Director), team.Id)).TaskId, ApprovalActions.Reject, "Not needed");
        (await w.GetAsync(again.Id, w.Requester)).Status.ShouldBe(TeamStatuses.Cancelled);

        // Validation: a leader is required to submit; the type fixes permanence.
        var noLeader = await w.CreateAsync(new { typeCode = "work-team", nameEn = "No leader", permanent = false, owningUnitId = w.DepartmentId,
            startDate = "2026-11-01", endDate = "2027-01-31", members = new[] { new { employeeId = w.MemberA.EmployeeId, roleCode = "member" } } });
        var refused = await w.C(w.Requester).PostAsJsonAsync($"{Base}/{noLeader.Id}/submit", new { version = noLeader.Version });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("teams.errors.leaderRequired");
        var permanentProject = await w.C(w.Requester).PostAsJsonAsync(Base, new { typeCode = "project-team", nameEn = "X", permanent = true, owningUnitId = w.DepartmentId });
        (await permanentProject.Content.ReadAsStringAsync()).ShouldContain("teams.errors.permanenceOfType");
    }

    [Fact]
    public async Task Member_history_is_kept_on_replace_and_leave_and_record_roles_follow()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var team = await w.FormAsync(w.Request("History Committee"));
        var a = team.Members.Single(m => m.Employee?.Id == w.MemberA.EmployeeId);
        var external = team.Members.Single(m => m.ExternalName is not null);

        // Only the Teams administrator changes membership.
        (await w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/members/{a.Id}/end", new { reason = "x", version = a.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.MemberA).PostAsJsonAsync($"{Base}/{team.Id}/members", new { employeeId = w.MemberB.EmployeeId, roleCode = "member" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var members = await TeamsWorld.OkAsync<List<MemberDto>>(w.C(w.Hr).PostAsJsonAsync($"{Base}/{team.Id}/members/{a.Id}/end",
            new { reason = "Moved to another department", successor = new { employeeId = w.MemberB.EmployeeId, roleCode = "" }, version = a.Version }));
        members.ShouldContain(m => m.Employee!.Id == w.MemberB.EmployeeId && m.RoleCode == "member" && m.Status == MemberStatuses.Active);
        members = await TeamsWorld.OkAsync<List<MemberDto>>(w.C(w.Hr).PostAsJsonAsync($"{Base}/{team.Id}/members/{external.Id}/end",
            new { reason = "End of secondment", version = external.Version }));
        members.ShouldNotContain(m => m.Id == external.Id);

        var history = (await w.C(w.Hr).GetFromJsonAsync<List<MemberDto>>($"{Base}/{team.Id}/members?history=true"))!;
        history.Single(m => m.Id == a.Id).Status.ShouldBe(MemberStatuses.Replaced);
        history.Single(m => m.Id == a.Id).LeaveReason.ShouldBe("Moved to another department");
        history.Single(m => m.Id == a.Id).ReplacedByMemberId.ShouldNotBeNull();
        history.Single(m => m.Id == a.Id).ToDate.ShouldNotBeNull();
        history.Single(m => m.Id == external.Id).Status.ShouldBe(MemberStatuses.Left);
        history.Count.ShouldBe(5); // nothing deleted

        // Record roles move with the membership: A no longer reads the team, B does; both were notified.
        (await w.C(w.MemberA).GetAsync($"{Base}/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.MemberB).GetAsync($"{Base}/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.MemberA), "teams.memberLeft");
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.MemberB), "teams.memberAdded");

        // Changing a role re-evaluates the results permission (ADR 0042).
        var b = members.Single(m => m.Employee?.Id == w.MemberB.EmployeeId);
        await TeamsWorld.OkAsync<List<MemberDto>>(w.C(w.Hr).PutAsJsonAsync($"{Base}/{team.Id}/members/{b.Id}", new { roleCode = "project-manager", version = b.Version }));
        (await w.GetAsync(team.Id, w.MemberB)).Permissions.CanViewResults.ShouldBeTrue();

        // The employee Teams tab keeps the ended membership.
        var tab = (await w.C(w.MemberA).GetFromJsonAsync<List<MembershipDto>>($"{Base}/employees/{w.MemberA.EmployeeId}/memberships"))!;
        tab.ShouldContain(m => m.TeamId == team.Id && m.Status == MemberStatuses.Replaced);
        (await w.C(w.Outsider).GetAsync($"{Base}/employees/{w.MemberA.EmployeeId}/memberships")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Meetings_and_recommendations_feed_suggested_levels_then_hr_approval_rewards_and_certificates_hidden_from_members()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var year = DateTime.UtcNow.Year;
        var team = await w.FormAsync(w.Request("Strategy Committee", start: new DateOnly(year, 1, 1)));

        // A reward matrix for this year: Very active + high workload + president / member.
        var config = (await w.C(w.Hr).GetFromJsonAsync<TeamConfigurationDto>($"{Base}/settings"))!;
        var draftMatrix = await TeamsWorld.OkAsync<RewardMatrixDto>(w.C(w.Hr).PostAsJsonAsync($"{Base}/settings/reward-matrices", new
        {
            year,
            content = new RewardMatrixContent(
                [new("very-active", Workloads.High, "president", 5000), new("very-active", Workloads.High, "member", 3000), new("active", Workloads.High, "member", 1500)],
                ["very-active"], 100_000, "AED"),
        }));
        await TeamsWorld.OkAsync<RewardMatrixDto>(w.C(w.Hr).PostAsync($"{Base}/settings/reward-matrices/{draftMatrix.Id}/publish", null));
        config.Templates.ShouldContain(t => t.Status == VersionStatuses.Published);

        // Operations: leader schedules, the secretary writes and submits minutes 30 h after the meeting ended.
        var ended = DateTimeOffset.UtcNow.AddHours(-30);
        (await w.C(w.Secretary).PostAsJsonAsync($"{Base}/{team.Id}/meetings", new { title = "x", scheduledAt = ended.AddHours(-2), endsAt = ended }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/meetings",
            new { title = "Kick-off", scheduledAt = ended.AddHours(-2), endsAt = ended, location = "Board room", agenda = "Plan" }));
        meeting.Attendance.Count.ShouldBe(4);
        meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.Secretary).PutAsJsonAsync($"{Base}/meetings/{meeting.Id}/minutes", new
        {
            minutes = "Discussed the plan.", decisions = "Approve the plan.", aiDrafted = false, version = meeting.Version,
            attendance = meeting.Attendance.Select(a => new { memberId = a.MemberId, status = a.ExternalName is null ? "present" : "absent" }).ToList(),
        }));
        meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.Secretary).PostAsJsonAsync($"{Base}/meetings/{meeting.Id}/submit-minutes", new { version = meeting.Version }));
        meeting.Status.ShouldBe(MeetingStatuses.Held);
        meeting.MinutesDelayHours!.Value.ShouldBeInRange(29.9m, 30.2m);
        (await w.C(w.Secretary).PutAsJsonAsync($"{Base}/meetings/{meeting.Id}/minutes", new { minutes = "late edit", version = meeting.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // 13 recommendations, 12 closed (SPEC example: 92 %), one rejected not counted; the owner updates their own.
        var ids = new List<RecommendationDto>();
        for (var i = 0; i < 14; i++)
        {
            ids.Add(await TeamsWorld.OkAsync<RecommendationDto>(w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/recommendations",
                new { meetingId = meeting.Id, text = $"Recommendation {i}", ownerEmployeeId = w.MemberA.EmployeeId, dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) })));
        }

        for (var i = 0; i < 12; i++)
        {
            await TeamsWorld.OkAsync<RecommendationDto>(w.C(w.MemberA).PostAsJsonAsync($"{Base}/recommendations/{ids[i].Id}/status",
                new { status = RecommendationStatuses.Closed, note = "done", version = ids[i].Version }));
        }

        await TeamsWorld.OkAsync<RecommendationDto>(w.C(w.President).PostAsJsonAsync($"{Base}/recommendations/{ids[13].Id}/status",
            new { status = RecommendationStatuses.Rejected, note = "Out of scope", version = ids[13].Version }));
        (await w.C(w.MemberB).PostAsJsonAsync($"{Base}/recommendations/{ids[12].Id}/status", new { status = "closed", version = ids[12].Version }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // HR opens the cycle; the leader (default evaluator) sees the suggested levels and the data behind them.
        (await w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/evaluations", new { year })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var summary = await TeamsWorld.OkAsync<EvaluationSummaryDto>(w.C(w.Hr).PostAsJsonAsync($"{Base}/{team.Id}/evaluations", new { year }));
        summary.Cycle.ShouldBe(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        (await w.C(w.Hr).PostAsJsonAsync($"{Base}/{team.Id}/evaluations", new { year })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // one per cycle
        await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.President), "teams.evaluationOpened");
        var evaluation = (await w.C(w.President).GetFromJsonAsync<EvaluationDto>($"{Base}/evaluations/{summary.Id}"))!;
        var closure = evaluation.Criteria.Single(c => c.Code == "recommendations");
        closure.SuggestedLevel.ShouldBe("medium");
        closure.Basis!.Count.ShouldBe(12);
        closure.Basis.Total.ShouldBe(13);
        closure.Basis.Value.ShouldBe(92.31m);
        var governance = evaluation.Criteria.Single(c => c.Code == "governance");
        governance.SuggestedLevel.ShouldBe("late");
        governance.Basis!.Value.ShouldBeInRange(29.9m, 30.2m);
        evaluation.Members.Count.ShouldBe(4);
        evaluation.Members.Single(m => m.Employee?.Id == w.MemberA.EmployeeId).AttendanceRate.ShouldBe(1m);
        evaluation.Members.Single(m => m.ExternalName is not null).AttendanceRate.ShouldBe(0m);

        // Ordinary members cannot see results: 403 on the evaluation, status only, no score on the team.
        (await w.C(w.MemberA).GetAsync($"{Base}/evaluations/{summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.MemberA).GetAsync($"{Base}/evaluations/{summary.Id}/status")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.Outsider).GetAsync($"{Base}/evaluations/{summary.Id}/status")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The wizard: a level other than the suggestion needs a justification; score ≥ 90 needs evidence.
        var answers = new Dictionary<string, int> { ["contribution"] = 5, ["commitment"] = 4, ["communication"] = 4 };
        var body = new
        {
            criteria = new object[]
            {
                new { code = "recommendations", level = "high", comment = "Strong closure" },
                new { code = "effectiveness", level = "radical" },
                new { code = "quality", level = "no-feedback" },
                new { code = "governance", level = "late" },
                new { code = "cooperation", level = "shared" },
            },
            workload = Workloads.High,
            members = evaluation.Members.Select(m => new { id = m.Id, answers, comment = "Good" }).ToList(),
            version = evaluation.Version,
        };
        (await w.C(w.MemberA).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        evaluation = await TeamsWorld.OkAsync<EvaluationDto>(w.C(w.President).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}", body));
        evaluation.Summary.Score.ShouldBe(95m);
        evaluation.Permissions.EvidenceRequired.ShouldBeTrue();
        evaluation.Members.ShouldAllBe(m => m.Points == 13 && m.Grade == "very-active");
        var refused = await w.C(w.President).PostAsJsonAsync($"{Base}/evaluations/{summary.Id}/submit", new { version = evaluation.Version });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await refused.Content.ReadAsStringAsync();
        problem.ShouldContain("teams.errors.overrideJustification");
        problem.ShouldContain("teams.errors.evidenceRequired");

        var evidence = await TeamsWorld.UploadAsync(w.C(w.President), TeamEvaluation.EntityType, summary.Id, "closure-report.pdf");
        (await w.C(w.MemberA).GetAsync($"/api/v1/files/{evidence.Id}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        evaluation = await TeamsWorld.OkAsync<EvaluationDto>(w.C(w.President).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}", new
        {
            criteria = new object[] { new { code = "recommendations", level = "high", overrideJustification = "Two more closed after the cut-off" } },
            evidenceFileIds = new[] { evidence.Id },
            version = evaluation.Version,
        }));
        evaluation = await TeamsWorld.OkAsync<EvaluationDto>(w.C(w.President).PostAsJsonAsync($"{Base}/evaluations/{summary.Id}/submit", new { version = evaluation.Version }));
        evaluation.Summary.Status.ShouldBe(EvaluationStatuses.Submitted);
        (await w.GetAsync(team.Id)).Status.ShouldBe(TeamStatuses.EvaluationWaitingApproval);

        // HR approves (single step); scores locked; rewards calculated from the matrix.
        await ApprovalWorld.ActOkAsync(w.C(w.Hr), (await TeamsWorld.MyTaskAsync(w.C(w.Hr), team.Id)).TaskId, ApprovalActions.Approve);
        evaluation = (await w.C(w.Hr).GetFromJsonAsync<EvaluationDto>($"{Base}/evaluations/{summary.Id}"))!;
        evaluation.Summary.Status.ShouldBe(EvaluationStatuses.Approved);
        evaluation.Summary.RewardStatus.ShouldBe(RewardStatuses.Draft);
        (await w.GetAsync(team.Id)).Status.ShouldBe(TeamStatuses.Active); // permanent: back to active
        var president = evaluation.Members.Single(m => m.RoleCode == "president");
        president.CalculatedReward.ShouldBe(5000m);
        president.Certificate.ShouldBeTrue();
        evaluation.Members.Single(m => m.Employee?.Id == w.MemberA.EmployeeId).CalculatedReward.ShouldBe(3000m);
        evaluation.Members.Single(m => m.RoleCode == "secretary").CalculatedReward.ShouldBe(0m); // no cell for the role
        evaluation.Budget!.AnnualBudget.ShouldBe(100_000m);
        (await w.C(w.President).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}", new { version = evaluation.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden); // evaluator role revoked after approval: locked for them

        // HR adjusts with justification, then the reward approval (here: the General Manager) issues the certificates.
        var memberA = evaluation.Members.Single(m => m.Employee?.Id == w.MemberA.EmployeeId);
        (await w.C(w.Hr).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}/rewards", new
        {
            rewards = new[] { new { id = memberA.Id, approvedReward = 2500, certificate = true, financialReward = true } }, version = evaluation.Version,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        evaluation = await TeamsWorld.OkAsync<EvaluationDto>(w.C(w.Hr).PutAsJsonAsync($"{Base}/evaluations/{summary.Id}/rewards", new
        {
            rewards = new[] { new { id = memberA.Id, approvedReward = 2500, adjustmentReason = "Joined late in the year", certificate = true, financialReward = true } },
            version = evaluation.Version,
        }));
        evaluation.Summary.RewardTotal.ShouldBe(10500m); // president 5000 + member A 2500 (adjusted) + external member 3000
        var admin = await fx.Factory.CreateSignedInClientAsync(admin: true);
        var process = (await admin.GetFromJsonAsync<ProcessDetailDto>("/api/v1/workflow/processes/teams.reward"))!;
        (await admin.PutAsJsonAsync("/api/v1/workflow/processes/teams.reward", new SaveProcessInput(
            [new ApprovalStepDefinition("gm", "المدير العام", "General Manager", new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: w.Gm.EmployeeId))], "test", process.VersionNo)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        evaluation = await TeamsWorld.OkAsync<EvaluationDto>(w.C(w.Hr).PostAsJsonAsync($"{Base}/evaluations/{summary.Id}/rewards/submit", new { version = evaluation.Version }));
        evaluation.Summary.RewardStatus.ShouldBe(RewardStatuses.Pending);
        await ApprovalWorld.ActOkAsync(w.C(w.Gm), (await TeamsWorld.MyTaskAsync(w.C(w.Gm), team.Id)).TaskId, ApprovalActions.Approve);
        await TeamsWorld.WaitAsync(async () => (await w.C(w.Hr).GetFromJsonAsync<EvaluationDto>($"{Base}/evaluations/{summary.Id}"))!.Members
            .Where(m => m.Certificate).All(m => m.CertificateFileId is not null), "the certificates");
        evaluation = (await w.C(w.Hr).GetFromJsonAsync<EvaluationDto>($"{Base}/evaluations/{summary.Id}"))!;
        evaluation.Summary.RewardStatus.ShouldBe(RewardStatuses.Approved);
        evaluation.Budget!.Used.ShouldBe(10500m);
        var certificate = evaluation.Members.Single(m => m.Employee?.Id == w.MemberA.EmployeeId);
        certificate.CertificateNumber!.ShouldStartWith($"CERT-{DateTime.UtcNow.Year}-");
        fx.Pdf.Html.ShouldContain(h => h.Contains(certificate.CertificateNumber!, StringComparison.Ordinal) && h.Contains("شهادة شكر وتقدير", StringComparison.Ordinal)
            && h.Contains("Certificate of appreciation", StringComparison.Ordinal));
        (await w.C(w.Hr).GetAsync($"/api/v1/files/{certificate.CertificateFileId}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.MemberA).GetAsync($"/api/v1/files/{certificate.CertificateFileId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Results stay hidden from the ordinary member everywhere: API, team page, employee tab, search, Ask and MCP; the President sees them.
        (await w.GetAsync(team.Id, w.MemberA)).LastScore.ShouldBeNull();
        (await w.GetAsync(team.Id, w.President)).LastScore.ShouldBe(95m);
        var listed = (await w.C(w.MemberA).GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{team.Id}/evaluations"))!;
        listed.Single().Score.ShouldBeNull();
        listed.Single().RewardTotal.ShouldBeNull();
        (await w.C(w.President).GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{team.Id}/evaluations"))!.Single().Score.ShouldBe(95m);
        var tab = (await w.C(w.MemberA).GetFromJsonAsync<List<MembershipDto>>($"{Base}/employees/{w.MemberA.EmployeeId}/memberships"))!;
        tab.Single(m => m.TeamId == team.Id).Results.ShouldBeNull();
        var presidentView = (await w.C(w.President).GetFromJsonAsync<List<MembershipDto>>($"{Base}/employees/{w.MemberA.EmployeeId}/memberships"))!;
        presidentView.Single(m => m.TeamId == team.Id).Results!.Single().Grade.ShouldBe("very-active");
        (await w.C(w.Hr).GetFromJsonAsync<List<MembershipDto>>($"{Base}/employees/{w.MemberA.EmployeeId}/memberships"))!
            .Single(m => m.TeamId == team.Id).Results!.Single().ApprovedReward.ShouldBe(2500m);
        foreach (var (user, sees) in new[] { (w.MemberA, false), (w.President, true) })
        {
            await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
            var principal = scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
            var record = scope.ServiceProvider.GetServices<IAiRecordSource>().Single(s => s.ResourceType == TeamPermissions.ResourceType);
            var content = (await record.GetAsync(team.Id, principal, CancellationToken.None))!;
            content.Fields.Any(f => f.Name == "LastEvaluationScore").ShouldBe(sees);
            var search = scope.ServiceProvider.GetServices<IRecordSearchSource>().Single(s => s.ResourceType == TeamPermissions.ResourceType);
            (await search.SearchAsync("Strategy Committee", principal, 20, CancellationToken.None)).ShouldContain(h => h.Id == team.Id);
            foreach (var name in new[] { "evaluation_status", "team_details" })
            {
                var tool = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == name);
                var result = await tool.InvokeAsync(JsonDocument.Parse($$"""{"teamId":"{{team.Id}}"}""").RootElement, new(principal, "mcp", "en"), CancellationToken.None);
                result.Success.ShouldBeTrue();
                var json = JsonSerializer.Serialize(result.Data);
                (json.Contains("Score\":95", StringComparison.Ordinal)).ShouldBe(sees, json);
            }
        }

        // The dashboard shows grade and reward figures only where the caller holds results.
        var dashboard = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard"))!;
        dashboard.GradeDistribution!.ShouldContain(g => g.Code == "very-active");
        (await w.C(w.MemberA).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Hr).GetAsync($"{Base}/export?kind=rewards&format=xlsx&language=ar")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Annual_and_end_date_triggers_open_evaluations_once_with_the_fake_clock_and_history_is_kept_per_cycle()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var committee = await w.FormAsync(w.Request("Standing Committee " + Guid.CreateVersion7().ToString("N")[^5..], start: new DateOnly(today.Year, 1, 1),
            withExternal: false));
        var temporary = await w.FormAsync(w.Request("Temporary Committee", type: "temporary-committee", permanent: false, start: today, end: today.AddDays(5),
            withExternal: false));
        try
        {
            (await RunTriggersAsync()).ShouldBe((0, 0, 0));

            // Ten days later the temporary committee has ended: its completion evaluation opens (and the team closes after approval).
            fx.Clock.Offset = TimeSpan.FromDays(10);
            var first = await RunTriggersAsync();
            first.EndDate.ShouldBeGreaterThanOrEqualTo(1);
            await w.ReSignInAsync();
            var hr = await w.HrViewAsync();
            var ended = (await hr.GetFromJsonAsync<TeamDto>($"{Base}/{temporary.Id}"))!;
            ended.Status.ShouldBe(TeamStatuses.PendingEvaluation);
            var cycles = (await hr.GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{temporary.Id}/evaluations"))!;
            cycles.Single().Cycle.ShouldBe(Cycles.Completion);
            cycles.Single().Trigger.ShouldBe(EvaluationTriggers.EndDate);

            // On 1 January the standing committee's annual cycle opens for the previous year; running again changes nothing.
            var newYear = new DateTimeOffset(today.Year + 1, 1, 1, 8, 0, 0, TimeSpan.Zero);
            fx.Clock.Offset = newYear - DateTimeOffset.UtcNow;
            await RunTriggersAsync();
            await RunTriggersAsync();
            await w.ReSignInAsync();
            hr = await w.HrViewAsync();
            var annual = (await hr.GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{committee.Id}/evaluations"))!;
            annual.Single().Cycle.ShouldBe(today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            annual.Single().Trigger.ShouldBe(EvaluationTriggers.Annual);
            annual.Single().PeriodStart.ShouldBe(new DateOnly(today.Year, 1, 1));
            annual.Single().PeriodEnd.ShouldBe(new DateOnly(today.Year, 12, 31));

            // HR cancels this cycle; a year later the next annual cycle opens and both stay in the history.
            var opened = (await hr.GetFromJsonAsync<EvaluationDto>($"{Base}/evaluations/{annual.Single().Id}"))!;
            await TeamsWorld.OkAsync<EvaluationDto>(hr.PostAsJsonAsync($"{Base}/evaluations/{annual.Single().Id}/cancel",
                new { reason = "Re-opened later", version = opened.Version }));
            fx.Clock.Offset = newYear.AddYears(1) - DateTimeOffset.UtcNow;
            await RunTriggersAsync();
            await w.ReSignInAsync();
            hr = await w.HrViewAsync();
            var history = (await hr.GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{committee.Id}/evaluations"))!;
            history.Select(h => (h.Cycle, h.Status)).ShouldBe(
                [((today.Year + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), EvaluationStatuses.Open),
                    (today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), EvaluationStatuses.Cancelled)], ignoreOrder: true);
        }
        finally
        {
            fx.Clock.Offset = TimeSpan.Zero;
            await w.ReSignInAsync();
        }
    }

    [Fact]
    public async Task A_project_closure_integration_event_from_a_fake_publisher_opens_the_project_team_evaluation()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var project = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var team = await w.FormAsync(w.Request("Project Team", type: "project-team", permanent: false, start: today, end: today.AddMonths(6),
            linkedType: FakeProjectLinkTargets.Type, linkedId: project, withExternal: false));
        team.LinkedRecords.Single().Number.ShouldBe("PROJ-T");

        // Portfolio (Phase 9) publishes this when the project's closure is approved (ADR 0043).
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new LinkedRecordClosed(FakeProjectLinkTargets.Type, project, "closure-approved", DateTimeOffset.UtcNow));
        }

        await TeamsWorld.WaitAsync(async () => (await w.GetAsync(team.Id)).Status == TeamStatuses.PendingEvaluation, "the project-team evaluation");
        var cycles = (await w.C(w.Hr).GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{team.Id}/evaluations"))!;
        cycles.Single().Trigger.ShouldBe(EvaluationTriggers.LinkedRecordClosed);
        cycles.Single().Cycle.ShouldBe(Cycles.Completion);
        cycles.Single().Evaluator!.Id.ShouldBe(w.President.EmployeeId!.Value);

        // At-least-once delivery: the same event again changes nothing.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new LinkedRecordClosed(FakeProjectLinkTargets.Type, project, "closure-approved", DateTimeOffset.UtcNow));
        }

        await Task.Delay(1500);
        (await w.C(w.Hr).GetFromJsonAsync<List<EvaluationSummaryDto>>($"{Base}/{team.Id}/evaluations"))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sample_items_are_team_able_and_tasks_can_be_assigned_to_a_teams_members()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var editor = await fx.Factory.CreateUserAsync(unitId: w.DepartmentId, roles: TestIdentity.SampleEditor);
        var editorClient = await fx.Factory.SignInAsync(editor);
        var created = await editorClient.PostAsJsonAsync("/api/v1/sample/items", new { nameAr = "بند", nameEn = "Item with a team", amount = 10 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var types = (await editorClient.GetFromJsonAsync<List<JsonElement>>("/api/v1/links/types?capability=team-able"))!;
        types.ShouldContain(t => t.GetProperty("resourceType").GetString() == "sample.item");

        // The editor requests a team for the item; it shows on the item with its members.
        var team = await w.CreateAsync(w.Request("Item Team", type: "work-team", permanent: true, linkedType: "sample.item", linkedId: item), editorClient);
        var linked = (await editorClient.GetFromJsonAsync<LinkedTeamsDto>($"{Base}/linked?type=sample.item&id={item}"))!;
        linked.Teams.Single().Team.Id.ShouldBe(team.Id);
        linked.Teams.Single().Members.Count.ShouldBe(4);
        linked.CanLink.ShouldBeTrue();
        (await w.C(w.Outsider).GetAsync($"{Base}/linked?type=sample.item&id={item}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var viewer = await fx.Factory.CreateUserAsync(roles: "sample.viewer");
        var restricted = (await (await fx.Factory.SignInAsync(viewer)).GetFromJsonAsync<LinkedTeamsDto>($"{Base}/linked?type=sample.item&id={item}"))!;
        restricted.Teams.ShouldBeEmpty();
        restricted.Restricted.ShouldBe(1);
        (await w.C(w.Outsider).PostAsJsonAsync(Base, w.Request("Sneaky", type: "work-team", linkedType: "sample.item", linkedId: item)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Task Management: one task per active internal member of a team the creator can read.
        var formed = await w.FormAsync(w.Request("Task Team", withExternal: true));
        var createdTasks = await w.C(w.President).PostAsJsonAsync("/api/v1/tasks", new
        {
            titleEn = "Prepare the committee pack", typeCode = "meeting-decision", sourceCode = "committee-decision", assigneeTeamId = formed.Id,
            linkedType = TeamPermissions.ResourceType, linkedId = formed.Id,
        });
        createdTasks.StatusCode.ShouldBe(HttpStatusCode.Created, await createdTasks.Content.ReadAsStringAsync());
        var tasks = await createdTasks.Content.ReadFromJsonAsync<JsonElement>();
        tasks.GetProperty("tasks").GetArrayLength().ShouldBe(3); // president, member A, secretary — not the external member
        var outsiderTask = await w.C(w.Outsider).PostAsJsonAsync("/api/v1/tasks", new
        {
            titleEn = "x", typeCode = "meeting-decision", sourceCode = "committee-decision", assigneeTeamId = formed.Id,
        });
        outsiderTask.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await outsiderTask.Content.ReadAsStringAsync()).ShouldContain("assigneeTeamId");
        var linkedTasks = (await w.C(w.President).GetFromJsonAsync<PagedResult<JsonElement>>(
            $"/api/v1/tasks?view=all&linkedType={TeamPermissions.ResourceType}&linkedId={formed.Id}"))!;
        linkedTasks.TotalCount.ShouldBe(3); // the leader follows every task linked to the team
    }

    [Fact]
    public async Task Authorization_matrix_for_team_operations_and_settings()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var team = await w.FormAsync(w.Request("Access Committee"));
        (await w.C(w.Outsider).GetAsync($"{Base}/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/{team.Id}/meetings")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/{team.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.MemberA).PostAsJsonAsync($"{Base}/{team.Id}/meetings", new { title = "x", scheduledAt = DateTimeOffset.UtcNow, endsAt = DateTimeOffset.UtcNow.AddHours(1) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.MemberA).PostAsJsonAsync($"{Base}/{team.Id}/recommendations", new { text = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.MemberA).PostAsJsonAsync($"{Base}/{team.Id}/close", new { version = team.Version, reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/close", new { version = team.Version, reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.MemberA).GetAsync($"{Base}/settings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.President).PostAsJsonAsync($"{Base}/settings/roles", new { code = "x", nameAr = "x", nameEn = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var counts = (await w.C(w.President).GetFromJsonAsync<TeamCountsDto>($"{Base}/counts"))!;
        counts.Leading.ShouldBeGreaterThanOrEqualTo(1);

        // Grade bands with a gap are refused by the template editor (ADR 0041).
        var broken = DefaultTemplate.Content with
        {
            GradeBands = [new("very-active", "فعّال جداً", "Very active", 12, 15), new("active", "فعّال", "Active", 10, 11), new("not-active", "غير فعّال", "Not active", 3, 8)],
        };
        var save = await w.C(w.Hr).PostAsJsonAsync($"{Base}/settings/templates", new { year = 2030, content = broken });
        save.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await save.Content.ReadAsStringAsync()).ShouldContain("teams.errors.bandsGap");

        // MCP tools: read tools on, the write tool off by default; no approve tool.
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var tools = scope.ServiceProvider.GetServices<IAppTool>().Select(t => t.Definition).Where(d => d.SystemCode == TeamPermissions.SystemCode).ToList();
        tools.Select(t => t.Name).ShouldBe(["my_teams", "team_details", "my_pending_evaluations", "evaluation_status", "add_meeting_minutes"], ignoreOrder: true);
        tools.Single(t => t.Name == "add_meeting_minutes").DefaultEnabled.ShouldBeFalse();
        tools.Where(t => t.Kind == ToolKind.Read).ShouldAllBe(t => t.DefaultEnabled);
        tools.ShouldNotContain(t => t.Kind == ToolKind.Approve);
    }

    private async Task<(int Annual, int EndDate, int Reminders)> RunTriggersAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Strategy.Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<EvaluationTriggerRunner>().RunAsync(CancellationToken.None);
    }
}
