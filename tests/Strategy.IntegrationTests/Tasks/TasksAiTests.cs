using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.AI;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>Task AI features with the fake model (no real provider): drafts only, nothing created (CLAUDE.md rule 6).</summary>
public sealed class TasksAiTests(AiFixture ai) : IClassFixture<AiFixture>
{
    private const string Base = TasksWorld.Base;

    [Fact]
    public async Task Ai_turns_meeting_notes_into_drafts_without_creating_anything()
    {
        var w = await TasksWorld.CreateAsync(ai.Factory);
        await ai.EnableAsync();
        var fake = ai.Model;
        fake.Reset((_, _) => FakeChatClient.Json(new
        {
            tasks = new object[]
            {
                new { title = "Send the minutes", description = "To all members", assigneeName = (string?)null, dueDate = "2026-11-10", priority = "high", source = "send minutes" },
                new { title = "Book the hall", description = (string?)null, assigneeName = "Nobody Known", dueDate = (string?)null, priority = "low", source = "book hall" },
            },
        }));
        var before = (await TasksWorld.ListAsync(w.ManagerClient, "view=created")).TotalCount;

        var response = await w.ManagerClient.PostAsJsonAsync($"{Base}/ai/from-text", new { text = "Minutes: Ali to send minutes by 10 Nov. Book hall." });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var drafts = (await response.Content.ReadFromJsonAsync<Strategy.Modules.Tasks.Ai.TaskDraftsDto>())!;
        drafts.Drafts.Count.ShouldBe(2);
        drafts.Drafts[0].TitleEn.ShouldBe("Send the minutes");
        drafts.Drafts[0].DueDate.ShouldBe(new DateOnly(2026, 11, 10));
        drafts.Drafts[1].Assignee.ShouldBeNull();
        (await TasksWorld.ListAsync(w.ManagerClient, "view=created")).TotalCount.ShouldBe(before); // nothing created
        fake.Calls.Single().Messages.Any(m => m.Text.Contains("<untrusted_document", StringComparison.Ordinal)).ShouldBeTrue();

        // The deterministic assignee suggestion and workload insights need no model.
        var suggestions = await w.ManagerClient.PostAsJsonAsync($"{Base}/ai/suggest-assignees", new { title = "Send the minutes" });
        suggestions.StatusCode.ShouldBe(HttpStatusCode.OK, await suggestions.Content.ReadAsStringAsync());
        (await w.ManagerClient.GetAsync($"{Base}/ai/workload")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

}
