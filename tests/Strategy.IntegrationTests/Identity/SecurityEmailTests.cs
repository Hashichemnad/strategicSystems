using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Notifications;
using Strategy.Platform.Scheduling;

namespace Strategy.IntegrationTests.Identity;

/// <summary>Security e-mails (ADR 0162): one per event, in the user's language, to the account holder, never with a secret, sent once.</summary>
public class SecurityEmailTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Auth = "/api/v1/identity/auth";

    [Fact]
    public async Task Password_change_sends_one_security_e_mail_in_the_users_language_without_the_password()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        (await client.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "en" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        const string changed = "Changed#Pass2026x";

        (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = changed }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.PasswordChanged.Key);
        mail.Subject.ShouldBe("Your password was changed | تم تغيير كلمة المرور");
        mail.Language.ShouldBe("en");
        mail.Html.ShouldContain("lang=\"en\"");
        mail.Html.ShouldContain("The password of your account was changed");
        mail.Html.ShouldContain("تم تغيير كلمة مرور حسابك");
        mail.Html.ShouldContain(SecurityEmails.DefaultGuidanceEn.Replace("'", "&#39;", StringComparison.Ordinal));
        mail.Link.ShouldBeNull();
        AssertNoSecret(mail, changed, user.Password);
        await AssertCountAsync(user.UserName, SecurityEmails.PasswordChanged.Key, 1);
    }

    [Fact]
    public async Task An_arabic_profile_user_gets_the_arabic_subject_first()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        (await client.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "ar" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = "Changed#Pass2026y" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.PasswordChanged.Key);
        mail.Subject.ShouldBe("تم تغيير كلمة المرور | Your password was changed");
        mail.Language.ShouldBe("ar");
        mail.Html.ShouldContain("<html lang=\"ar\">");
    }

    [Fact]
    public async Task Password_reset_by_link_sends_the_reset_notice_but_an_invitation_does_not()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.CreateSpaClientAsync();
        (await client.PostAsJsonAsync($"{Auth}/password/forgot", new { login = user.UserName })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var (userId, token) = CapturingEmailTransport.ParseLink((await api.Factory.Emails.LastAsync(user.UserName, "passwordReset")).Link!);
        const string fresh = "Reset#Pass2026z";

        (await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword = fresh })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.PasswordReset.Key);
        AssertNoSecret(mail, fresh, token);
        mail.Html.ShouldNotContain("reset-password?");
        await AssertCountAsync(user.UserName, SecurityEmails.PasswordReset.Key, 1);
    }

    [Fact]
    public async Task Mfa_enrolment_recovery_codes_and_removal_each_send_one_e_mail_without_codes()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        var setup = await (await client.PostAsync("/api/v1/identity/me/mfa/totp", null)).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty, StringComparison.Ordinal);
        var code = await Totp.NextCodeAsync(key);
        var confirmed = await client.PostAsJsonAsync("/api/v1/identity/me/mfa/confirm", new { method = "totp", code });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstCodes = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();

        var enrolled = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.MfaEnrolled.Key);
        enrolled.Html.ShouldContain("authenticator app");
        AssertNoSecret(enrolled, [code, key, .. firstCodes]);

        var regenerated = await client.PostAsync("/api/v1/identity/me/mfa/recovery-codes", null);
        var newCodes = (await regenerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        AssertNoSecret(await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.RecoveryCodes.Key), [.. newCodes]);

        (await client.PostAsJsonAsync("/api/v1/identity/me/mfa/disable", new { method = "totp" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.MfaRemoved.Key)).Html.ShouldContain("authenticator app");

        await AssertCountAsync(user.UserName, SecurityEmails.MfaEnrolled.Key, 1);
        await AssertCountAsync(user.UserName, SecurityEmails.RecoveryCodes.Key, 1);
        await AssertCountAsync(user.UserName, SecurityEmails.MfaRemoved.Key, 1);
    }

    [Fact]
    public async Task An_administrator_resetting_mfa_notifies_the_user()
    {
        var user = await api.Factory.CreateUserAsync(withMfa: true);
        var admin = await api.AdminClientAsync();

        (await admin.PostAsync($"/api/v1/identity/admin/users/{user.Id}/reset-mfa", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.MfaRemoved.Key)).Html.ShouldContain("reset by an administrator");
    }

    [Fact]
    public async Task Lockout_sends_one_e_mail_however_many_attempts_follow()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.CreateSpaClientAsync();
        for (var i = 0; i < 8; i++)
        {
            await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = "wrong" + i });
        }

        var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.LockedOut.Key);
        mail.Html.ShouldContain("several failed sign-in attempts");
        AssertNoSecret(mail, "wrong4");
        await AssertCountAsync(user.UserName, SecurityEmails.LockedOut.Key, 1);
    }

    [Fact]
    public async Task Vendor_accounts_get_the_lockout_e_mail_too()
    {
        var email = $"vendor.{Guid.CreateVersion7():N}@example.test";
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
            var vendor = new StrategyUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PrincipalType = StrategyClaims.VendorPrincipal,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            (await users.CreateAsync(vendor, TestIdentity.Password)).Succeeded.ShouldBeTrue();
        }

        var client = await api.Factory.CreateSpaClientAsync();
        for (var i = 0; i < 6; i++)
        {
            await client.PostAsJsonAsync("/api/wasel/v1/auth/login", new { login = email, password = "wrong" + i });
        }

        await api.Factory.Emails.LastAsync(email, SecurityEmails.LockedOut.Key);
        await AssertCountAsync(email, SecurityEmails.LockedOut.Key, 1);
    }

    [Fact]
    public async Task Token_creation_mails_the_name_and_prefix_but_never_the_token()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);

        var created = await client.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Reporting robot", scopes = new[] { "api.read" }, days = 30 });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var prefix = body.GetProperty("details").GetProperty("prefix").GetString()!;

        var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.TokenCreated.Key);
        mail.Html.ShouldContain("Reporting robot");
        mail.Html.ShouldContain(prefix);
        AssertNoSecret(mail, token, token.Split('_')[^1]);
    }

    [Fact]
    public async Task Security_e_mails_cannot_be_switched_off_in_the_preferences()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        (await client.PutAsJsonAsync("/api/v1/notifications/preferences", new
        {
            digest = true,
            email = false,
            inApp = false,
            events = new[] { new { eventKey = SecurityEmails.PasswordChanged.Key, email = false, inApp = false } },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var preferences = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/preferences");
        var item = preferences.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("eventKey").GetString() == SecurityEmails.PasswordChanged.Key);
        item.GetProperty("mandatory").GetBoolean().ShouldBeTrue();
        item.GetProperty("email").GetBoolean().ShouldBeTrue();

        (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = "Changed#Pass2026w" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.PasswordChanged.Key);
    }

    [Fact]
    public async Task The_same_event_queues_once_and_a_handled_twice_outbox_message_is_delivered_once()
    {
        var user = await api.Factory.CreateUserAsync();
        var eventId = "tests.security:" + Guid.CreateVersion7().ToString("N");
        for (var i = 0; i < 2; i++)
        {
            await using var scope = api.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System,
                StrategyClaims.CreateSystemPrincipal());
            var entity = (await scope.ServiceProvider.GetRequiredService<StrategyUserManager>().FindByIdAsync(user.Id.ToString()))!;
            await scope.ServiceProvider.GetRequiredService<SecurityNotifier>().NotifyAsync(entity, SecurityEmails.RecoveryCodes, eventId);
        }

        await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.RecoveryCodes.Key);
        await AssertCountAsync(user.UserName, SecurityEmails.RecoveryCodes.Key, 1);

        // The outbox handler runs again with the same message (a crash between sending and marking it processed).
        string payload;
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Scheduling.Infrastructure.SchedulingDbContext>();
            payload = (await db.Outbox.AsNoTracking().Where(m => m.Type == "notifications.email").OrderByDescending(m => m.CreatedAt).Take(200)
                .ToListAsync()).Select(m => m.Payload).First(p => p.Contains(eventId, StringComparison.Ordinal));
            var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().Single(h => h.Type == "notifications.email");
            await handler.HandleAsync(payload, CancellationToken.None);
        }

        await AssertCountAsync(user.UserName, SecurityEmails.RecoveryCodes.Key, 1);
    }

    [Fact]
    public async Task New_device_e_mail_is_off_by_default_and_when_on_goes_only_for_an_unknown_device()
    {
        var user = await api.Factory.CreateUserAsync();
        await SignInWithAgentAsync(user, "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36");
        await SignInWithAgentAsync(user, "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Firefox/131.0"); // new device, switch off
        await Task.Delay(1500);
        api.Factory.Emails.Sent.Count(e => e.To == user.UserName && e.TemplateKey == SecurityEmails.NewDevice.Key).ShouldBe(0);

        await SetNewDeviceAsync(true);
        try
        {
            await SignInWithAgentAsync(user, "Mozilla/5.0 (Windows NT 10.0) Chrome/131.0 Safari/537.36"); // known (family + network)
            await SignInWithAgentAsync(user, "Mozilla/5.0 (Linux; Android 14) Firefox/131.0"); // new
            var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.NewDevice.Key);
            mail.Values["device"].ShouldBe("Firefox · Android");
            await AssertCountAsync(user.UserName, SecurityEmails.NewDevice.Key, 1);

            // The first device of an account is never "new".
            var fresh = await api.Factory.CreateUserAsync();
            await SignInWithAgentAsync(fresh, "Mozilla/5.0 (Windows NT 10.0) Edg/130.0");
            await Task.Delay(1500);
            api.Factory.Emails.Sent.Count(e => e.To == fresh.UserName && e.TemplateKey == SecurityEmails.NewDevice.Key).ShouldBe(0);
        }
        finally
        {
            await SetNewDeviceAsync(false);
        }
    }

    [Fact]
    public async Task Guidance_text_comes_from_the_security_settings()
    {
        var admin = await api.AdminClientAsync();
        var settings = await admin.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/identity/admin/security-settings");
        var body = settings!.ToDictionary(p => p.Key, p => (object?)p.Value);
        body["securityContactEn"] = "Call the service desk on 800-123.";
        body["securityContactAr"] = new string('x', 501);
        await (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", body)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        body["securityContactAr"] = "اتصل بمكتب الخدمة.";
        (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            var user = await api.Factory.CreateUserAsync();
            var client = await api.Factory.SignInAsync(user);
            (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = "Changed#Pass2026v" }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var mail = await api.Factory.Emails.LastAsync(user.UserName, SecurityEmails.PasswordChanged.Key);
            mail.Html.ShouldContain("Call the service desk on 800-123.");
            mail.Html.ShouldContain("اتصل بمكتب الخدمة.");
        }
        finally
        {
            var current = await admin.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/identity/admin/security-settings");
            var reset = current!.ToDictionary(p => p.Key, p => (object?)p.Value);
            reset["securityContactAr"] = null;
            reset["securityContactEn"] = null;
            (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", reset)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private async Task SignInWithAgentAsync(TestUser user, string agent)
    {
        var client = await api.Factory.CreateSpaClientAsync();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", agent);
        (await client.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.SignedIn);
    }

    private async Task SetNewDeviceAsync(bool on)
    {
        var admin = await api.AdminClientAsync();
        var settings = await admin.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/identity/admin/security-settings");
        var body = settings!.ToDictionary(p => p.Key, p => (object?)p.Value);
        body["notifyNewDevice"] = on;
        (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Waits until the outbox is quiet, then checks the exact count of one template to one address.</summary>
    private async Task AssertCountAsync(string to, string templateKey, int expected)
    {
        await Task.Delay(1500);
        api.Factory.Emails.Sent.Count(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase) && e.TemplateKey == templateKey).ShouldBe(expected);
    }

    private static void AssertNoSecret(EmailMessage mail, params string[] secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            mail.Html.ShouldNotContain(secret);
            mail.Text.ShouldNotContain(secret);
            mail.Values.Values.ShouldNotContain(secret);
        }
    }
}
