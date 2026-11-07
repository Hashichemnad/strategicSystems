using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Infrastructure;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>Rich text in Portfolio (ADR 0161): project meeting minutes sanitised on save; plain and legacy values unchanged.</summary>
public sealed class RichTextPortfolioTests(ProjectFixture fx) : IClassFixture<ProjectFixture>
{
    private const string Base = PortfolioWorld.Base;

    [Fact]
    public async Task Meeting_minutes_are_sanitised_and_plain_values_stay_as_typed()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var project = await ProjectLifecycleTests.ApprovedProjectAsync(w, $"Rich text {w.Suffix}");
        var pm = w.C(w.PmOffice);

        var saved = await PortfolioWorld.OkAsync<ProjectMeetingDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/meetings", new
        {
            values = new
            {
                meetingAt = DateTimeOffset.UtcNow,
                title = "Steering",
                notes = "<h2 dir=\"ltr\">Decisions</h2><ul><li><em>Go</em> live</li></ul><iframe src=\"https://evil.example\"></iframe>" +
                        "<table><tbody><tr><td colspan=\"2\" style=\"color:red\">a</td></tr></tbody></table><svg onload=alert(1)></svg>",
            },
        }));
        saved.Notes.ShouldBe("<h2 dir=\"ltr\">Decisions</h2><ul><li><em>Go</em> live</li></ul><table><tbody><tr><td colspan=\"2\">a</td></tr></tbody></table>");

        var plain = await PortfolioWorld.OkAsync<ProjectMeetingDto>(pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/meetings", new
        {
            values = new { meetingAt = DateTimeOffset.UtcNow, title = "Weekly", notes = "1 < 2 & 3 > 2\nnext line" },
        }));
        plain.Notes.ShouldBe("1 < 2 & 3 > 2\nnext line");

        // Legacy plain value written before rich text: returned exactly as stored.
        const string legacy = "Minutes from 2024 <unchanged>\r\nline two";
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var row = await db.Meetings.SingleAsync(m => m.Id == plain.Id);
            db.Entry(row).Property(x => x.Notes).CurrentValue = legacy;
            await db.SaveChangesAsync();
        }

        var list = await PortfolioWorld.OkAsync<List<ProjectMeetingDto>>(pm.GetAsync($"{Base}/projects/{project.Id}/meetings"));
        list.Single(m => m.Id == plain.Id).Notes.ShouldBe(legacy);

        var tooLong = await pm.PostAsJsonAsync($"{Base}/projects/{project.Id}/meetings",
            new { values = new { meetingAt = DateTimeOffset.UtcNow, title = "Long", notes = "<p>" + new string('x', 20001) + "</p>" } });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("validation.richTextTooLong");
    }
}
