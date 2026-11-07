using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Notifications;

/// <summary>
/// End to end over real SMTP: an identity e-mail goes through the editable template, the outbox and MailKit to Mailpit, and arrives with
/// the Arabic column (right to left) and the English column side by side, plus a plain-text part in both languages.
/// </summary>
public class MailpitDeliveryTests(SidecarContainers sidecars) : IClassFixture<SidecarContainers>
{
    [Fact]
    public async Task A_notification_is_delivered_to_mailpit_in_arabic_and_english()
    {
        await using var factory = new ApiFactory(await sidecars.CreateDatabaseAsync(), realEmail: true);
        await UseMailpitAsync(factory);

        var user = await factory.CreateUserAsync();
        var anonymous = await factory.CreateSpaClientAsync();
        (await anonymous.PostAsJsonAsync("/api/v1/identity/auth/password/forgot", new { login = user.UserName })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await AssertResetMailAsync(user.UserName);
    }

    [Fact]
    public async Task A_security_e_mail_reaches_mailpit_in_the_users_language_without_the_password()
    {
        await using var factory = new ApiFactory(await sidecars.CreateDatabaseAsync(), realEmail: true);
        await UseMailpitAsync(factory);
        var user = await factory.CreateUserAsync();
        var client = await factory.SignInAsync(user);
        (await client.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "en" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        const string changed = "Mailpit#Changed2026";
        (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = changed }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var mailpit = new HttpClient { BaseAddress = sidecars.MailpitApi };
        var id = await NotificationTests.EventuallyAsync(
            async () => (await mailpit.GetFromJsonAsync<JsonElement>($"api/v1/search?query=to:{Uri.EscapeDataString(user.UserName)}"))
                .GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("ID").GetString()).FirstOrDefault(),
            found => found is not null,
            timeoutSeconds: 30);
        var message = await mailpit.GetFromJsonAsync<JsonElement>($"api/v1/message/{id}");
        message.GetProperty("Subject").GetString().ShouldBe("Your password was changed | تم تغيير كلمة المرور");
        var html = message.GetProperty("HTML").GetString()!;
        html.ShouldContain("The password of your account was changed");
        html.ShouldContain("تم تغيير كلمة مرور حسابك");
        html.ShouldNotContain(changed);
        message.GetProperty("Text").GetString()!.ShouldNotContain(changed);
    }

    private async Task UseMailpitAsync(ApiFactory factory)
    {
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var smtp = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/admin/smtp");
        var saved = await admin.PutAsJsonAsync("/api/v1/identity/admin/smtp", new
        {
            enabled = true,
            host = sidecars.MailpitHost,
            port = sidecars.MailpitSmtpPort,
            security = "none",
            userName = (string?)null,
            password = (string?)null,
            clearPassword = false,
            fromAddress = "noreply@strategy.test",
            fromNameAr = "نظام الاستراتيجية",
            fromNameEn = "Strategy System",
            publicBaseUrl = "https://strategy.example",
            version = smtp.GetProperty("version").GetUInt32(),
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private async Task AssertResetMailAsync(string to)
    {
        using var mailpit = new HttpClient { BaseAddress = sidecars.MailpitApi };
        var id = await NotificationTests.EventuallyAsync(
            async () => (await mailpit.GetFromJsonAsync<JsonElement>($"api/v1/search?query=to:{Uri.EscapeDataString(to)}"))
                .GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("ID").GetString()).FirstOrDefault(),
            found => found is not null,
            timeoutSeconds: 30);

        var message = await mailpit.GetFromJsonAsync<JsonElement>($"api/v1/message/{id}");
        message.GetProperty("Subject").GetString().ShouldBe("إعادة تعيين كلمة المرور | Reset your password");
        message.GetProperty("From").GetProperty("Address").GetString().ShouldBe("noreply@strategy.test");
        var html = message.GetProperty("HTML").GetString()!;
        html.ShouldContain("dir=\"rtl\" lang=\"ar\"");
        html.ShouldContain("dir=\"ltr\" lang=\"en\"");
        html.ShouldContain("تلقّينا طلباً لإعادة تعيين كلمة المرور");
        html.ShouldContain("We received a request to reset your password");
        html.ShouldContain("href=\"https://strategy.example/reset-password?user=");
        var text = message.GetProperty("Text").GetString()!;
        text.ShouldContain("مرحباً");
        text.ShouldContain("Hello");
    }
}
