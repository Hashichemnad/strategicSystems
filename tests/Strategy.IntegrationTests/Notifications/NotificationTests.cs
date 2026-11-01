using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Notifications;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Scheduling.Application;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.IntegrationTests.Notifications;

/// <summary>Templates, preferences, in-app centre and the outbox with retry (CLAUDE.md §14).</summary>
public class NotificationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task A_notification_reaches_the_users_centre_and_mailbox_and_can_be_marked_read()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        var before = api.Factory.Emails.Sent.Count;

        await SendAsync(new NotificationRequest(NotificationTemplates.Test.Key, [NotificationRecipient.User(user.Id)], Link: "/sample"));

        var mine = await EventuallyAsync(() => client.GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"), m => m!.Items.Count > 0);
        var item = mine!.Items.Single();
        item.TitleEn.ShouldBe("Test email");
        item.TitleAr.ShouldBe("رسالة تجريبية");
        item.Link.ShouldBe("/sample");
        mine.Unread.ShouldBe(1);
        (await api.Factory.Emails.LastAsync(user.UserName, NotificationTemplates.Test.Key, before)).Subject.ShouldBe("رسالة تجريبية | Test email");

        (await client.PostAsync($"/api/v1/notifications/mine/{item.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"))!.Unread.ShouldBe(0);

        var other = await api.Factory.CreateSignedInClientAsync();
        (await other.PostAsync($"/api/v1/notifications/mine/{item.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preferences_turn_e_mail_off_or_into_a_daily_digest_but_mandatory_messages_still_go_out()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        var saved = await client.PutAsJsonAsync("/api/v1/notifications/preferences", new
        {
            digest = true,
            email = true,
            inApp = true,
            events = Array.Empty<object>(),
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync<NotificationPreferencesDto>())!.Digest.ShouldBeTrue();

        await SendAsync(new NotificationRequest(ApiFactory.TestEventTemplate.Key, [NotificationRecipient.User(user.Id)],
            new Dictionary<string, string> { ["subject"] = "Digest me" }));
        await EventuallyAsync(() => client.GetFromJsonAsync<MyNotificationsDto>("/api/v1/notifications/mine"), m => m!.Items.Count > 0);
        api.Factory.Emails.CountTo(user.UserName).ShouldBe(0); // waiting for the digest

        await RunJobAsync("notifications.digest");
        var digest = await api.Factory.Emails.LastAsync(user.UserName, NotificationTemplates.Digest.Key);
        digest.Values["count"].ShouldBe("1");
        digest.Html.ShouldContain("dir=\"rtl\"");
    }

    [Fact]
    public async Task An_edited_template_is_used_for_the_next_message_and_audited_and_can_be_reset()
    {
        var admin = await api.AdminClientAsync();
        var templates = await admin.GetFromJsonAsync<List<TemplateDto>>("/api/v1/notifications/templates");
        var test = templates!.Single(t => t.Key == NotificationTemplates.Test.Key);
        var edited = await admin.PutAsJsonAsync($"/api/v1/notifications/templates/{test.Key}", new
        {
            subjectAr = "اختبار معدّل",
            subjectEn = "Edited test",
            bodyAr = "نص عربي {{name}}",
            bodyEn = "English text {{name}}",
            buttonAr = (string?)null,
            buttonEn = (string?)null,
            version = test.Version,
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var after = (await edited.Content.ReadFromJsonAsync<TemplateDto>())!;
        after.Customised.ShouldBeTrue();

        var preview = await admin.PostAsJsonAsync($"/api/v1/notifications/templates/{test.Key}/preview", (object?)null);
        (await preview.Content.ReadFromJsonAsync<TemplatePreviewDto>())!.Subject.ShouldBe("اختبار معدّل | Edited test");

        var user = await api.Factory.CreateUserAsync();
        await SendAsync(new NotificationRequest(test.Key, [NotificationRecipient.User(user.Id)], Channels: NotificationChannels.Email));
        var mail = await api.Factory.Emails.LastAsync(user.UserName, test.Key);
        mail.Html.ShouldContain("English text " + user.UserName);
        mail.Html.ShouldContain("نص عربي " + user.UserName);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit/entries?entityType=notifications.template&field=SubjectEn");
        history.GetProperty("items").EnumerateArray().ShouldContain(e => e.GetProperty("newValue").GetString() == "Edited test");

        var reset = await admin.PostAsJsonAsync($"/api/v1/notifications/templates/{test.Key}/reset", new { version = after.Version });
        (await reset.Content.ReadFromJsonAsync<TemplateDto>())!.SubjectEn.ShouldBe("Test email");

        var regular = await api.Factory.CreateSignedInClientAsync();
        (await regular.GetAsync("/api/v1/notifications/templates")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_failed_delivery_is_retried_with_back_off_and_logged()
    {
        var user = await api.Factory.CreateUserAsync();
        api.Factory.Emails.FailNext = 1;
        await SendAsync(new NotificationRequest(NotificationTemplates.Test.Key, [NotificationRecipient.User(user.Id)], Channels: NotificationChannels.Email));

        // The first attempt fails: the message waits for its retry (30 s later) with the error recorded.
        var row = await EventuallyAsync(() => OutboxRowAsync(user.UserName), r => r is { Attempts: 1 });
        row!.Value.Error.ShouldBe("identity.errors.smtpFailed");
        row.Value.AvailableAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(15));
        api.Factory.Emails.CountTo(user.UserName).ShouldBe(0);

        // Bring the retry forward instead of waiting for the back-off.
        await using (var connection = new NpgsqlConnection(api.Factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update scheduling.outbox_message set available_at = now() - interval '1 second' where payload->>'to' = '{user.UserName}'", connection);
            await command.ExecuteNonQueryAsync();
        }

        await api.Factory.Services.GetRequiredService<OutboxDispatcher>().DispatchDueAsync(CancellationToken.None);
        (await api.Factory.Emails.LastAsync(user.UserName, NotificationTemplates.Test.Key)).To.ShouldBe(user.UserName);

        var admin = await api.AdminClientAsync();
        var deliveries = await admin.GetFromJsonAsync<JsonElement>("/api/v1/notifications/deliveries?pageSize=200");
        var mine = deliveries.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("to").GetString() == user.UserName).ToList();
        mine.Select(d => d.GetProperty("status").GetString()).Order().ShouldBe(["failed", "sent"]);
    }

    [Fact]
    public async Task Sensitive_values_are_never_stored_in_clear_in_the_outbox()
    {
        var user = await api.Factory.CreateUserAsync();
        const string code = "987654";
        await SendAsync(new NotificationRequest("identity.emailCode", [NotificationRecipient.Address(user.UserName)],
            new Dictionary<string, string> { ["code"] = code }, Channels: NotificationChannels.Email, Sensitive: true));
        (await api.Factory.Emails.LastAsync(user.UserName, "identity.emailCode")).Values["code"].ShouldBe(code);

        await using var scope = api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var payloads = await db.Outbox.AsNoTracking().Select(m => m.Payload).ToListAsync();
        payloads.ShouldNotContain(p => p.Contains(code));
    }

    private async Task SendAsync(NotificationRequest request)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<INotificationService>().SendAsync(request)).IsSuccess.ShouldBeTrue();
    }

    private async Task RunJobAsync(string key)
    {
        var admin = await api.AdminClientAsync();
        var before = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/scheduling/jobs/{key}/runs")).GetArrayLength();
        (await admin.PostAsync($"/api/v1/scheduling/jobs/{key}/run", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await EventuallyAsync(
            () => admin.GetFromJsonAsync<JsonElement>($"/api/v1/scheduling/jobs/{key}/runs"),
            runs => runs.GetArrayLength() > before && runs[0].GetProperty("status").GetString() != "running",
            timeoutSeconds: 30);
    }

    private async Task<(int Attempts, string? Error, DateTimeOffset AvailableAt)?> OutboxRowAsync(string to)
    {
        await using var connection = new NpgsqlConnection(api.Factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select attempts, last_error, available_at from scheduling.outbox_message where payload->>'to' = @to order by created_at desc limit 1", connection);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    internal static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done, int timeoutSeconds = 15)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow > until)
            {
                throw new ShouldAssertException($"Condition not met within {timeoutSeconds} s (last value: {value}).");
            }

            await Task.Delay(100);
        }
    }
}
