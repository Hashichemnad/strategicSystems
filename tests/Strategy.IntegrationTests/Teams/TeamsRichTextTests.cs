using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Infrastructure;

namespace Strategy.IntegrationTests.Teams;

/// <summary>Rich-text agenda / minutes / decisions (ADR 0161): sanitised on save, plain values kept as they are, length on visible text.</summary>
public sealed class TeamsRichTextTests(TeamsFixture fx) : IClassFixture<TeamsFixture>
{
    private const string Base = TeamsWorld.Base;

    [Fact]
    public async Task Minutes_are_sanitised_and_legacy_plain_text_is_returned_unchanged()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var team = await w.FormAsync(w.Request("Rich text committee"));
        var ended = DateTimeOffset.UtcNow.AddHours(-3);
        var meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.President).PostAsJsonAsync($"{Base}/{team.Id}/meetings", new
        {
            title = "Formatting",
            scheduledAt = ended.AddHours(-1),
            endsAt = ended,
            agenda = "<ol><li>Budget</li></ol><img src=x onerror=alert(1)>",
        }));
        meeting.Agenda.ShouldBe("<ol><li>Budget</li></ol>");

        meeting = await TeamsWorld.OkAsync<MeetingDto>(w.C(w.Secretary).PutAsJsonAsync($"{Base}/meetings/{meeting.Id}/minutes", new
        {
            minutes = "<p dir=\"rtl\" onclick=\"steal()\"><strong>تمت</strong> الموافقة</p><script>alert(1)</script>" +
                      "<p><a href=\"javascript:alert(1)\">x</a> <a href=\"https://example.org\" target=\"_blank\">y</a></p>",
            decisions = "Approve the plan < 5 days\nSecond line",
            version = meeting.Version,
        }));
        meeting.Minutes.ShouldBe("<p dir=\"rtl\"><strong>تمت</strong> الموافقة</p>" +
                                 "<p><a rel=\"noopener noreferrer\">x</a> <a href=\"https://example.org\" target=\"_blank\" rel=\"noopener noreferrer\">y</a></p>");
        meeting.Decisions.ShouldBe("Approve the plan < 5 days\nSecond line", "plain text is stored as typed");

        // A value saved before rich text existed (plain text written straight to the column) is read back unchanged.
        const string legacy = "Old minutes: a<b, c>d\n\n- item";
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<TeamsDbContext>();
            var row = await db.Meetings.SingleAsync(m => m.Id == meeting.Id);
            db.Entry(row).Property(x => x.Minutes).CurrentValue = legacy;
            await db.SaveChangesAsync();
        }

        var read = (await w.C(w.Secretary).GetFromJsonAsync<MeetingDto>($"{Base}/meetings/{meeting.Id}"))!;
        read.Minutes.ShouldBe(legacy);

        // The limit counts the visible text, not the markup.
        var tooLong = await w.C(w.Secretary).PutAsJsonAsync($"{Base}/meetings/{meeting.Id}/minutes",
            new { minutes = "<p>" + new string('x', 20001) + "</p>", version = read.Version });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("validation.richTextTooLong");
    }
}
