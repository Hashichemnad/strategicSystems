using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Setup;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.IntegrationTests.Identity;

/// <summary>First-run setup wizard and its one-time token (ADR 0150). Every test starts on its own empty database.</summary>
public class SetupWizardTests(PostgresFixture postgres)
{
    private const string Header = SetupService.TokenHeader;

    [Fact]
    public async Task An_empty_installation_needs_setup_and_the_bootstrap_requires_the_current_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = await factory.CreateSpaClientAsync();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/setup/status");
        status.GetProperty("needsSetup").GetBoolean().ShouldBeTrue();
        status.GetProperty("setupCompleted").GetBoolean().ShouldBeFalse();

        // No token, a wrong token, then the token issued before the current one: all refused, nothing created.
        await (await client.PostAsJsonAsync("/api/v1/setup/bootstrap", Input())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");
        await (await Send(client, "/api/v1/setup/bootstrap", "not-the-token", Input())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");
        var first = await IssueAsync(factory);
        var token = await IssueAsync(factory);
        await (await Send(client, "/api/v1/setup/verify-token", first, null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");
        (await Send(client, "/api/v1/setup/verify-token", token, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnitCountAsync(factory)).ShouldBe(0);

        var audit = await AuditAsync(factory, "setup.token.verify");
        audit.ShouldContain(e => e.Success == false);
        audit.ShouldContain(e => e.Success == true);
        (await AuditAsync(factory, "setup.bootstrap")).ShouldAllBe(e => e.Success == false);
    }

    [Fact]
    public async Task The_bootstrap_creates_the_organisation_and_the_first_administrator_and_consumes_the_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = await factory.CreateSpaClientAsync();
        var token = await IssueAsync(factory);

        var response = await Send(client, "/api/v1/setup/bootstrap", token, Input());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var userId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IUserRoleQuery>();
            (await roles.AnyPlatformAdministratorAsync()).ShouldBeTrue();
            var user = await scope.ServiceProvider.GetRequiredService<StrategyUserManager>().FindByIdAsync(userId.ToString());
            user.ShouldNotBeNull();
            user.UserName.ShouldBe("first.admin@example.test");
            user.Language.ShouldBe("en");
            (await roles.RoleKeysOfAsync(userId, user.EmployeeId)).ShouldContain("access.administrator");
            (await roles.RoleKeysOfAsync(userId, user.EmployeeId)).ShouldContain("settings.administrator");
            var org = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
            var root = await org.Units.AsNoTracking().SingleAsync();
            root.Code.ShouldBe("ACME");
            root.ParentId.ShouldBeNull();
            (await org.Employees.AsNoTracking().SingleAsync()).Id.ShouldBe(user.EmployeeId!.Value);
        }

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/public");
        settings.GetProperty("organisationNameEn").GetString().ShouldBe("Acme Authority");
        settings.GetProperty("primaryColor").GetString().ShouldBe("#1d4ed8");
        settings.GetProperty("regional").GetProperty("timeZone").GetString().ShouldBe("Asia/Riyadh");
        settings.GetProperty("regional").GetProperty("defaultLanguage").GetString().ShouldBe("en");

        // The wizard is closed: the token cannot be reused, no new token is issued.
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/setup/status")).GetProperty("needsSetup").GetBoolean().ShouldBeFalse();
        await (await Send(client, "/api/v1/setup/bootstrap", token, Input("second@example.test"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");
        (await IssueOrNullAsync(factory)).ShouldBeNull();

        // Administrators must set up multi-factor sign-in at the first sign-in.
        (await client.LoginAsync("first.admin@example.test", TestIdentity.Password)).ShouldBe(SignInStatuses.MfaEnrolmentRequired);

        var bootstrap = await AuditAsync(factory, "setup.bootstrap");
        bootstrap.ShouldContain(e => e.Success == true && e.RecordId == userId.ToString("D"));
    }

    [Fact]
    public async Task Invalid_input_creates_nothing_and_keeps_the_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = await factory.CreateSpaClientAsync();
        var token = await IssueAsync(factory);

        var weak = Input() with { AdminPassword = "short" };
        var problem = await (await Send(client, "/api/v1/setup/bootstrap", token, weak)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").EnumerateArray().ShouldContain(e => e.GetProperty("field").GetString() == "adminPassword");

        var missing = Input() with { OrganisationNameAr = " ", TimeZone = "Nowhere/City", PrimaryColor = "blue" };
        problem = await (await Send(client, "/api/v1/setup/bootstrap", token, missing)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var fields = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        fields.ShouldContain("organisationNameAr");
        fields.ShouldContain("timeZone");
        fields.ShouldContain("primaryColor");

        (await UnitCountAsync(factory)).ShouldBe(0);
        (await Send(client, "/api/v1/setup/verify-token", token, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_administrator_created_otherwise_closes_the_wizard_and_completion_needs_the_setup_permission()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var token = await IssueAsync(factory);
        var admin = await factory.SignInAsync(await factory.CreateUserAsync(admin: true));
        var user = await factory.CreateSignedInClientAsync();

        var anonymous = await factory.CreateSpaClientAsync();
        await (await Send(anonymous, "/api/v1/setup/verify-token", token, null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");
        await (await Send(anonymous, "/api/v1/setup/bootstrap", token, Input())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "setup.tokenInvalid");

        (await anonymous.PostAsync("/api/v1/setup/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await user.PostAsync("/api/v1/setup/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var completed = await admin.PostAsync("/api/v1/setup/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupCompleted").GetBoolean().ShouldBeTrue();
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/setup/status")).GetProperty("setupCompleted").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Version_and_release_notes_are_for_signed_in_users()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var anonymous = await factory.CreateSpaClientAsync();
        (await anonymous.GetAsync("/api/v1/system/version")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var user = await factory.CreateSignedInClientAsync();
        var version = await user.GetFromJsonAsync<JsonElement>("/api/v1/system/version");
        version.GetProperty("version").GetString().ShouldBe("1.0.1");

        var notes = await user.GetFromJsonAsync<List<JsonElement>>("/api/v1/system/release-notes?language=ar");
        notes!.ShouldContain(n => n.GetProperty("version").GetString() == "1.0.0" && n.GetProperty("markdown").GetString()!.Contains("الإصدار"));
        notes![0].GetProperty("version").GetString().ShouldBe("1.0.1"); // newest first
        notes[0].GetProperty("markdown").GetString()!.ShouldContain("الإصدار 1.0.1");
        notes = await user.GetFromJsonAsync<List<JsonElement>>("/api/v1/system/release-notes?language=en");
        notes!.ShouldContain(n => n.GetProperty("markdown").GetString()!.StartsWith("# Version 1.0.0", StringComparison.Ordinal));
        notes![0].GetProperty("markdown").GetString()!.ShouldStartWith("# Version 1.0.1");
    }

    private static SetupBootstrapInput Input(string email = "first.admin@example.test") => new(
        "هيئة أكمي", "Acme Authority", "acme", "Asia/Riyadh", "SAR", "yyyy-MM-dd", "#1d4ed8", null, "Acme header", null, "Acme footer", "en", true, false,
        email, TestIdentity.Password, "المسؤول الأول", "First Administrator", "A-0001", null, "Administrator");

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string? token, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Add(Header, token);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> IssueAsync(ApiFactory factory) => (await IssueOrNullAsync(factory)).ShouldNotBeNull();

    private static async Task<string?> IssueOrNullAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SetupService>().IssueTokenAsync();
    }

    private static async Task<int> UnitCountAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrgDbContext>().Units.CountAsync();
    }

    private static async Task<IReadOnlyList<AuditEntryDto>> AuditAsync(ApiFactory factory, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAuditQuery>()
            .SearchAsync(new AuditFilter(Action: action, ExactAction: true), new PageRequest(1, 100))).Items;
    }
}
