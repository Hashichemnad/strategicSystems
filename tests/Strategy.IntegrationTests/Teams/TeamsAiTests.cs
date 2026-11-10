using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.AI;
using Strategy.Modules.Teams.Ai;
using Strategy.Modules.Teams.Application;

namespace Strategy.IntegrationTests.Teams;

/// <summary>Teams AI features with the fake model: drafts only, nothing saved (CLAUDE.md rule 6); suggestions without a model.</summary>
public sealed class TeamsAiTests(AiFixture ai) : IClassFixture<AiFixture>
{
    private const string Base = TeamsWorld.Base;

    [Fact]
    public async Task Minutes_writer_formation_assistant_and_member_suggestions_return_drafts()
    {
        var w = await TeamsWorld.CreateAsync(ai.Factory);
        await ai.EnableAsync();
        var team = await w.FormAsync(w.Request("AI Committee"));
        var ended = DateTimeOffset.UtcNow.AddHours(-2);
        var meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/meetings",
            new { title = "Review", scheduledAt = ended.AddHours(-1), endsAt = ended }));

        ai.Model.Reset((_, _) => FakeChatClient.Json(new
        {
            minutes = "The committee reviewed the budget.",
            decisions = new[] { "Approve the budget" },
            recommendations = new object[]
            {
                new { text = "Send the report", ownerName = w.MemberA.UserName, dueDate = "2026-12-01" },
                new { text = "Book the hall", ownerName = (string?)null, dueDate = (string?)null },
            },
        }));
        (await w.C(w.MemberA).PostAsJsonAsync($"{Base}/meetings/{meeting.Id}/ai/minutes", new { notes = "notes" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var draft = await TeamsWorld.OkAsync<MinutesDraftDto>(w.C(w.Secretary).PostAsJsonAsync($"{Base}/meetings/{meeting.Id}/ai/minutes",
            new { notes = "Budget reviewed. Ali to send the report by 1 Dec. Book the hall." }));
        draft.Minutes.ShouldBe("The committee reviewed the budget.");
        draft.Decisions.ShouldBe(["Approve the budget"]);
        draft.Recommendations.Count.ShouldBe(2);
        draft.Recommendations[0].DueDate.ShouldBe(new DateOnly(2026, 12, 1));
        ai.Model.Calls.Last().Messages.Any(m => m.Text.Contains("<untrusted_document", StringComparison.Ordinal)).ShouldBeTrue();
        // Notes typed in the rich-text editor reach the model as their plain-text projection (ADR 0161).
        await TeamsWorld.OkAsync<MinutesDraftDto>(w.C(w.Secretary).PostAsJsonAsync($"{Base}/meetings/{meeting.Id}/ai/minutes",
            new { notes = "<p><strong>Budget</strong> reviewed</p><ul><li>Book the hall</li></ul>" }));
        var prompt = string.Join("\n", ai.Model.Calls.Last().Messages.Select(m => m.Text));
        prompt.ShouldContain("Budget reviewed\n• Book the hall");
        prompt.ShouldNotContain("<strong>");
        var unchanged = (await w.C(w.Secretary).GetFromJsonAsync<MeetingDto>($"{Base}/meetings/{meeting.Id}"))!;
        unchanged.Minutes.ShouldBeNull(); // nothing saved until the secretary accepts the draft

        ai.Model.Reset((_, _) => FakeChatClient.Json(new
        {
            objectiveAr = "هدف",
            objectiveEn = "Objective",
            mandateAr = "مهام",
            mandateEn = "Mandate",
            memoAr = "قرار",
            memoEn = "Decision",
        }));
        var formation = await TeamsWorld.OkAsync<FormationDraftDto>(w.C(w.Requester).PostAsJsonAsync($"{Base}/ai/formation",
            new { typeCode = "temporary-committee", name = "Procurement review", description = "Review the procurement policy within three months." }));
        formation.ObjectiveEn.ShouldBe("Objective");
        formation.MemoAr.ShouldBe("قرار");

        // Member suggestions: no model; skill overlap with past memberships, workload shown.
        var suggestions = await TeamsWorld.OkAsync<List<MemberSuggestionDto>>(w.C(w.Requester).PostAsJsonAsync($"{Base}/ai/members",
            new { unitId = w.DepartmentId, skills = "budgeting" }));
        var a = suggestions.Single(s => s.Employee.Id == w.MemberA.EmployeeId);
        a.Reasons.ShouldContain("skills");
        a.ActiveMemberships.ShouldBe(1);
        a.LastGrade.ShouldBeNull(); // grades only for results holders
    }
}
