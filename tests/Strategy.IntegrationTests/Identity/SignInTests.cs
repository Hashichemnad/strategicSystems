using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.IntegrationTests.Identity;

public class SignInTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Auth = "/api/v1/identity/auth";

    [Fact]
    public async Task Local_sign_in_creates_a_session_that_me_reports_and_logout_ends()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me");
        me.GetProperty("userName").GetString().ShouldBe(user.UserName);
        me.GetProperty("isSystemAdmin").GetBoolean().ShouldBeFalse();
        me.GetProperty("employee").GetProperty("id").GetGuid().ShouldBe(user.EmployeeId!.Value);
        me.GetProperty("authMethod").GetString().ShouldBe("pwd");

        (await client.PostAsync($"{Auth}/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await AssertEventAsync(user.Id, AuthEventTypes.Login, success: true);
        await AssertEventAsync(user.Id, AuthEventTypes.Logout, success: true);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_give_the_same_generic_error()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.CreateSpaClientAsync();

        var wrong = await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = "nope" });
        var unknown = await client.PostAsJsonAsync($"{Auth}/login", new { login = "nobody@example.test", password = "nope" });

        await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCredentials", "auth.errors.invalidCredentials");
        await unknown.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCredentials", "auth.errors.invalidCredentials");
        await AssertEventAsync(user.Id, AuthEventTypes.LoginFailed, success: false);
    }

    [Fact]
    public async Task Account_locks_after_the_configured_failures_and_an_admin_can_unlock_it()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.CreateSpaClientAsync();
        for (var i = 0; i < 4; i++)
        {
            (await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = "wrong" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await (await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = "wrong" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.lockedOut", "auth.errors.lockedOut");
        // Even the right password is refused while locked.
        await (await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = user.Password }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.lockedOut");
        await AssertEventAsync(user.Id, AuthEventTypes.LockedOut, success: false);

        var admin = await api.AdminClientAsync();
        var unlocked = await admin.PostAsync($"/api/v1/identity/admin/users/{user.Id}/unlock", null);
        (await unlocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lockedOut").GetBoolean().ShouldBeFalse();
        (await client.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.SignedIn);
    }

    [Fact]
    public async Task Inactive_users_cannot_sign_in_and_their_sessions_end()
    {
        var user = await api.Factory.CreateUserAsync();
        var session = await api.Factory.SignInAsync(user);
        var admin = await api.AdminClientAsync();

        (await admin.PostAsync($"/api/v1/identity/admin/users/{user.Id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await session.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var client = await api.Factory.CreateSpaClientAsync();
        await (await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = user.Password }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCredentials");
    }

    [Fact]
    public async Task Users_with_mfa_must_give_a_valid_second_factor_and_recovery_codes_work_once()
    {
        var user = await api.Factory.CreateUserAsync(withMfa: true);
        var client = await api.Factory.CreateSpaClientAsync();

        var first = await client.PostAsJsonAsync($"{Auth}/login", new { login = user.UserName, password = user.Password });
        var outcome = await first.Content.ReadFromJsonAsync<JsonElement>();
        outcome.GetProperty("status").GetString().ShouldBe(SignInStatuses.MfaRequired);
        outcome.GetProperty("methods").EnumerateArray().Select(m => m.GetString()).ShouldBe(["totp", "recovery"]);

        // Still not signed in during the second-factor step.
        (await client.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await (await client.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "totp", code = "000000" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
        await (await client.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "email", code = "123456" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode"); // email codes are not enabled for this user

        var verified = await client.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "totp", code = await Totp.NextCodeAsync(user.TotpKey!) });
        (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe(SignInStatuses.SignedIn);
        await client.RefreshAntiforgeryAsync();
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me")).GetProperty("authMethod").GetString().ShouldBe("mfa");

        var codes = await client.PostAsync("/api/v1/identity/me/mfa/recovery-codes", null);
        var recovery = (await codes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codes")[0].GetString()!;

        var other = await api.Factory.CreateSpaClientAsync();
        (await other.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.MfaRequired);
        (await other.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "recovery", code = recovery })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var third = await api.Factory.CreateSpaClientAsync();
        (await third.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.MfaRequired);
        await (await third.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "recovery", code = recovery }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
    }

    [Fact]
    public async Task A_totp_code_is_accepted_once_and_older_codes_are_refused()
    {
        var user = await api.Factory.CreateUserAsync(withMfa: true);
        var now = DateTimeOffset.UtcNow;
        var previous = Totp.Code(user.TotpKey!, now.AddSeconds(-30));
        var current = Totp.Code(user.TotpKey!, now);
        var next = Totp.Code(user.TotpKey!, now.AddSeconds(30));

        (await VerifyTotpAsync(user, current)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // The same code again (someone who saw it), and an unused code of an earlier step, are both refused.
        await (await VerifyTotpAsync(user, current)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
        await (await VerifyTotpAsync(user, previous)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
        // The app's next code works.
        (await VerifyTotpAsync(user, next)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await VerifyTotpAsync(user, next)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
    }

    [Fact]
    public async Task The_code_that_confirms_an_authenticator_cannot_be_used_again_to_sign_in()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        var setup = await (await client.PostAsync("/api/v1/identity/me/mfa/totp", null)).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty, StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var confirmCode = Totp.Code(key, now);
        (await client.PostAsJsonAsync("/api/v1/identity/me/mfa/confirm", new { method = "totp", code = confirmCode }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var withTotp = user with { TotpKey = key };
        await (await VerifyTotpAsync(withTotp, confirmCode)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
        (await VerifyTotpAsync(withTotp, Totp.Code(key, now.AddSeconds(30)))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Password, then the given authenticator code, on a new browser-like client.</summary>
    private async Task<HttpResponseMessage> VerifyTotpAsync(TestUser user, string code)
    {
        var client = await api.Factory.CreateSpaClientAsync();
        (await client.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.MfaRequired);
        return await client.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "totp", code });
    }

    [Fact]
    public async Task Administrator_without_mfa_must_enrol_before_getting_a_session()
    {
        var admin = await CreateAdminWithoutMfaAsync();
        var client = await api.Factory.CreateSpaClientAsync();

        var login = await client.PostAsJsonAsync($"{Auth}/login", new { login = admin.UserName, password = admin.Password });
        var outcome = await login.Content.ReadFromJsonAsync<JsonElement>();
        outcome.GetProperty("status").GetString().ShouldBe(SignInStatuses.MfaEnrolmentRequired);
        (await client.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var setup = await (await client.PostAsync($"{Auth}/mfa/enrol/totp", null)).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty, StringComparison.Ordinal);
        setup.GetProperty("authenticatorUri").GetString().ShouldStartWith("otpauth://totp/");

        await (await client.PostAsJsonAsync($"{Auth}/mfa/enrol/confirm", new { method = "totp", code = "111111" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.invalidCode");
        var confirmed = await client.PostAsJsonAsync($"{Auth}/mfa/enrol/confirm", new { method = "totp", code = await Totp.NextCodeAsync(key) });
        var result = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().ShouldBe(SignInStatuses.SignedIn);
        result.GetProperty("recoveryCodes").GetArrayLength().ShouldBe(10);

        await client.RefreshAntiforgeryAsync();
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me")).GetProperty("isSystemAdmin").GetBoolean().ShouldBeTrue();
        await AssertEventAsync(admin.Id, AuthEventTypes.MfaEnabled, success: true);

        // Policy: the administrator cannot remove the only second factor.
        await (await client.PostAsJsonAsync("/api/v1/identity/me/mfa/disable", new { method = "totp" }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "auth.mfaRequired");
    }

    [Fact]
    public async Task Email_code_works_as_second_factor_after_enabling_it_in_the_profile()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);

        (await client.PostAsync("/api/v1/identity/me/mfa/email", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var enableCode = api.Factory.Emails.Last(user.UserName, "emailCode").Values["code"];
        var enabled = await client.PostAsJsonAsync("/api/v1/identity/me/mfa/confirm", new { method = "email", code = enableCode });
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());

        var next = await api.Factory.CreateSpaClientAsync();
        (await next.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.MfaRequired);
        (await next.PostAsync($"{Auth}/mfa/email-code", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var code = api.Factory.Emails.Last(user.UserName, "emailCode").Values["code"];
        var verified = await next.PostAsJsonAsync($"{Auth}/mfa/verify", new { method = "email", code });
        (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe(SignInStatuses.SignedIn);
    }

    [Fact]
    public async Task Password_reset_by_email_link_sets_a_new_password_that_must_follow_the_policy()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.CreateSpaClientAsync();

        (await client.PostAsJsonAsync($"{Auth}/password/forgot", new { login = user.UserName })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync($"{Auth}/password/forgot", new { login = "ghost@example.test" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var email = api.Factory.Emails.Last(user.UserName, "passwordReset");
        var (userId, token) = CapturingEmailSender.ParseLink(email.Link!);

        var weak = await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword = "short" });
        var problem = await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ShouldContain("identity.passwordTooShort");

        var reused = await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword = user.Password });
        (await reused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed"))
            .GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("identity.passwordReused");

        const string newPassword = "Brand#New-Pass2026";
        (await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.LoginAsync(user.UserName, user.Password)).ShouldBeNull();
        (await client.LoginAsync(user.UserName, newPassword)).ShouldBe(SignInStatuses.SignedIn);
        await client.RefreshAntiforgeryAsync();

        await (await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token = "AAAA", newPassword }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.resetTokenInvalid");
    }

    [Fact]
    public async Task Admin_creates_an_account_for_an_employee_and_the_invitation_sets_the_password_once()
    {
        var admin = await api.AdminClientAsync();
        var employeeOnly = await api.Factory.CreateUserAsync(); // gives us an employee; drop its user by creating a new employee below
        var unit = employeeOnly.UnitId!.Value;
        var number = "INV" + Guid.CreateVersion7().ToString("N")[^6..];
        var employee = await admin.PostAsJsonAsync("/api/v1/org/employees", new
        {
            employeeNo = number,
            nameEn = "Invited",
            email = $"{number.ToLowerInvariant()}@example.test",
            unitId = unit,
        });
        var employeeId = (await employee.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var created = await admin.PostAsJsonAsync("/api/v1/identity/admin/users", new { employeeId });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("invitationSent").GetBoolean().ShouldBeTrue();
        body.GetProperty("user").GetProperty("hasPassword").GetBoolean().ShouldBeFalse();

        await (await admin.PostAsJsonAsync("/api/v1/identity/admin/users", new { employeeId }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "identity.user.employeeHasUser");

        var invitation = api.Factory.Emails.Last($"{number.ToLowerInvariant()}@example.test", "invitation");
        var (userId, token) = CapturingEmailSender.ParseLink(invitation.Link!);
        invitation.Link!.ShouldContain("invite=1");
        var client = await api.Factory.CreateSpaClientAsync();
        const string password = "Invited#Pass2026";
        (await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword = password, invitation = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.PostAsJsonAsync($"{Auth}/password/reset", new { userId, token, newPassword = "Another#Pass2026", invitation = true }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.resetTokenInvalid");
        (await client.LoginAsync($"{number.ToLowerInvariant()}@example.test", password)).ShouldBe(SignInStatuses.SignedIn);
    }

    [Fact]
    public async Task Sessions_are_listed_and_revoking_others_ends_them_immediately()
    {
        var user = await api.Factory.CreateUserAsync();
        var laptop = await api.Factory.SignInAsync(user);
        var phone = await api.Factory.SignInAsync(user);

        var sessions = await laptop.GetFromJsonAsync<List<JsonElement>>("/api/v1/identity/me/sessions");
        sessions!.Count.ShouldBe(2);
        sessions.Count(s => s.GetProperty("current").GetBoolean()).ShouldBe(1);

        (await laptop.PostAsync("/api/v1/identity/me/sessions/revoke-others", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await phone.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await laptop.RefreshAntiforgeryAsync();
        (await laptop.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Another user's session id: 404.
        var stranger = await api.UserClientAsync();
        var mine = (await laptop.GetFromJsonAsync<List<JsonElement>>("/api/v1/identity/me/sessions"))![0].GetProperty("id").GetGuid();
        await (await stranger.PostAsync($"/api/v1/identity/me/sessions/{mine}/revoke", null)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
    }

    [Fact]
    public async Task Password_change_needs_the_current_password_and_signs_out_other_sessions()
    {
        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        var other = await api.Factory.SignInAsync(user);

        await (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = "wrong", newPassword = "Changed#Pass2026" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        (await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = "Changed#Pass2026" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await other.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await client.RefreshAntiforgeryAsync();
        (await client.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Preferences_are_stored_on_the_profile()
    {
        var client = await api.Factory.CreateSignedInClientAsync();

        (await client.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "ar", theme = "dark" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "fr" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me");
        me.GetProperty("language").GetString().ShouldBe("ar");
        me.GetProperty("theme").GetString().ShouldBe("dark");
    }

    [Fact]
    public async Task Employee_leaving_deactivates_the_account()
    {
        var user = await api.Factory.CreateUserAsync();
        var admin = await api.AdminClientAsync();
        var employee = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/org/employees/{user.EmployeeId}");

        (await admin.PostAsJsonAsync($"/api/v1/org/employees/{user.EmployeeId}/change-status",
            new { status = "left", version = employee.GetProperty("version").GetUInt32() })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var account = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/identity/admin/users/{user.Id}");
        account.GetProperty("active").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Admin_cannot_deactivate_or_demote_themselves()
    {
        var adminUser = await api.Factory.CreateUserAsync(admin: true);
        var admin = await api.Factory.SignInAsync(adminUser);

        await (await admin.PostAsync($"/api/v1/identity/admin/users/{adminUser.Id}/deactivate", null))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.self");
        await (await admin.PostAsJsonAsync($"/api/v1/identity/admin/users/{adminUser.Id}/set-admin", new { isSystemAdmin = false }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.self");
    }

    [Fact]
    public async Task Auth_events_are_listed_for_administrators()
    {
        var admin = await api.AdminClientAsync();

        var events = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/admin/auth-events?type=login");

        events.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
        events.GetProperty("items")[0].GetProperty("type").GetString().ShouldBe("login");
    }

    private async Task<TestUser> CreateAdminWithoutMfaAsync()
    {
        var user = await api.Factory.CreateUserAsync(admin: true);
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
        var entity = (await users.FindByIdAsync(user.Id.ToString()))!;
        entity.TotpEnabled = false;
        entity.TwoFactorEnabled = false;
        await users.UpdateAsync(entity);
        await users.RemoveAuthenticationTokenAsync(entity, "[AspNetUserStore]", "AuthenticatorKey");
        return user with { TotpKey = null };
    }

    private async Task AssertEventAsync(Guid userId, string type, bool success)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        (await db.AuthEvents.AnyAsync(e => e.UserId == userId && e.Type == type && e.Success == success)).ShouldBeTrue($"{type} event");
    }
}
