using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>
/// System → Settings → Members (SPEC-ADDENDUM §D1, ADRs 0166 / 0167): lists over the existing assignments and system access, delegated
/// administration per system. Each test owns its host: grants to units and everyone change global state.
/// </summary>
public class MembersTests(PostgresFixture postgres)
{
    private const string PortfolioMembers = "/api/v1/access/systems/portfolio/members";

    [Fact]
    public async Task A_delegated_portfolio_administrator_adds_a_person_a_unit_with_its_sub_units_and_everyone()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (_, delegateClient) = await DelegateAsync(factory, "portfolio");

        var lists = await ListsAsync(delegateClient, PortfolioMembers);
        lists.Select(l => l.GetProperty("key").GetString()).ShouldBe(["administrators", "users", "extra:pm-office", "extra:viewer", "extra:department-viewer"]);
        lists.ShouldAllBe(l => l.GetProperty("canManage").GetBoolean());

        // A person: system access + the user role (requester), notified once.
        var person = await factory.CreateUserAsync();
        var personClient = await factory.SignInAsync(person);
        (await MeAsync(personClient)).Systems.ShouldNotContain("portfolio");
        var emailsBefore = factory.Emails.Sent.Count;
        var added = await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "users", principalKind = "user", principalId = person.Id, note = "New analyst" });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var me = await MeAsync(personClient);
        me.Systems.ShouldContain("portfolio");
        me.Permissions.ShouldContain("portfolio.request.create");
        var email = await factory.Emails.LastAsync(person.UserName, MemberAdministration.AddedTemplateKey, emailsBefore);
        email.Values["listEn"].ShouldBe("Users");
        email.Values["systemEn"].ShouldBe("Portfolio management");
        factory.Emails.Sent.Skip(emailsBefore).Count(e => e.TemplateKey == MemberAdministration.AddedTemplateKey).ShouldBe(1);
        var users = (await ListsAsync(delegateClient, PortfolioMembers)).Single(l => l.GetProperty("key").GetString() == "users");
        users.GetProperty("members").EnumerateArray().ShouldContain(m => m.GetProperty("principal").GetProperty("id").GetString() == person.Id.ToString());

        // Adding the same person again changes nothing: 409.
        await (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "users", principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "access.members.alreadyMember");

        // A unit: a user placed in a sub-unit gets access and the extra role's permissions; units are not e-mailed.
        var (parent, child) = await UnitsAsync(factory);
        var subUnitUser = await factory.CreateUserAsync(unitId: child);
        var subUnitClient = await factory.SignInAsync(subUnitUser);
        (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "extra:viewer", principalKind = "unit", principalId = parent }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        me = await MeAsync(subUnitClient);
        me.Systems.ShouldContain("portfolio");
        me.Permissions.ShouldContain("portfolio.project.read");
        factory.Emails.CountTo(subUnitUser.UserName).ShouldBe(0);

        // Administrators are people only.
        await (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "administrators", principalKind = "unit", principalId = parent }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        // Everyone: any user now opens Portfolio.
        var anybody = await factory.CreateSignedInClientAsync();
        (await MeAsync(anybody)).Systems.ShouldNotContain("portfolio");
        (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "users", principalKind = "everyone" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MeAsync(anybody)).Systems.ShouldContain("portfolio");

        // The changes are ordinary audited assignment / system-access rows with the note as reason, on the delegate's request.
        var audit = await AuditAsync(factory, new AuditFilter(EntityType: "access.role_assignment"));
        audit.ShouldContain(e => e.Reason == "New analyst" && e.Action == "create");
        (await AuditAsync(factory, new AuditFilter(EntityType: "access.system_access"))).ShouldContain(e => e.Reason == "New analyst");
    }

    [Fact]
    public async Task A_delegated_administrator_manages_only_their_system_and_never_platform_roles_or_settings()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (_, delegateClient) = await DelegateAsync(factory, "portfolio");
        var person = await factory.CreateUserAsync();

        // Another system: 403 for every Members endpoint; unknown system 404.
        await (await delegateClient.GetAsync("/api/v1/access/systems/kpi/members")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.PostAsJsonAsync("/api/v1/access/systems/kpi/members", new { list = "users", principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.PostAsJsonAsync($"/api/v1/access/systems/kpi/members/{Guid.CreateVersion7()}/remove", new { reason = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.GetAsync("/api/v1/access/systems/kpi/members/user-search?search=user")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.GetAsync("/api/v1/access/systems/nope/members")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        await (await delegateClient.GetAsync("/api/v1/access/systems/org/members")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        (await delegateClient.GetFromJsonAsync<List<JsonElement>>("/api/v1/access/members/systems"))!.Select(s => s.GetProperty("code").GetString())
            .ShouldBe(["portfolio"]);

        // People search works for their own system only.
        (await delegateClient.GetAsync($"/api/v1/access/systems/portfolio/members/user-search?search={Uri.EscapeDataString(person.UserName)}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Platform roles / another system's roles cannot be named; role permissions, the member-role configuration, assignments and settings stay closed.
        await (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "extra:administrator", principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        await (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "extra:access.administrator", principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var roles = await factory.CreateSignedInClientAsync(admin: true);
        var portfolioAdmin = (await roles.GetFromJsonAsync<List<JsonElement>>("/api/v1/access/roles?system=portfolio"))!
            .Single(r => r.GetProperty("code").GetString() == "administrator");
        portfolioAdmin.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("permission").GetString()).ShouldContain("portfolio.members.manage");
        await (await delegateClient.PutAsJsonAsync($"/api/v1/access/roles/{portfolioAdmin.GetProperty("id").GetGuid()}/permissions", new { permissions = Array.Empty<object>(), version = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.GetAsync("/api/v1/access/systems/portfolio/member-roles")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.PutAsJsonAsync("/api/v1/access/systems/portfolio/member-roles", new { administratorRoles = new[] { "viewer" } }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.PostAsJsonAsync("/api/v1/access/assignments", new { roleId = portfolioAdmin.GetProperty("id").GetGuid(), principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.GetAsync("/api/v1/settings/features")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await delegateClient.PutAsJsonAsync("/api/v1/access/systems/portfolio", new { enabled = false, version = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        // Their own system's administrators: allowed (safe default, ADR 0167).
        (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "administrators", principalKind = "user", principalId = person.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var effective = await roles.GetFromJsonAsync<JsonElement>($"/api/v1/access/users/{person.Id}/effective");
        effective.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("permission").GetString())
            .ShouldContain("portfolio.members.manage");

        // Teams: its administrator role is a platform administrator role, so a delegated Teams administrator cannot grant it.
        var (_, teamsDelegate) = await DelegateAsync(factory, "teams");
        var teams = await ListsAsync(teamsDelegate, "/api/v1/access/systems/teams/members");
        teams.Single(l => l.GetProperty("key").GetString() == "administrators").GetProperty("canManage").GetBoolean().ShouldBeFalse();
        await (await teamsDelegate.PostAsJsonAsync("/api/v1/access/systems/teams/members", new { list = "administrators", principalKind = "user", principalId = person.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.members.platformRole");
        (await teamsDelegate.PostAsJsonAsync("/api/v1/access/systems/teams/members", new { list = "extra:viewer", principalKind = "user", principalId = person.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await teamsDelegate.GetAsync(PortfolioMembers)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Removal_ends_the_assignment_and_the_access_notifies_the_person_and_is_audited()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (_, delegateClient) = await DelegateAsync(factory, "portfolio");
        var person = await factory.CreateUserAsync();
        var personClient = await factory.SignInAsync(person);
        (await delegateClient.PostAsJsonAsync(PortfolioMembers, new { list = "extra:pm-office", principalKind = "user", principalId = person.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MeAsync(personClient)).Permissions.ShouldContain("portfolio.request.manage");

        var member = (await ListsAsync(delegateClient, PortfolioMembers)).Single(l => l.GetProperty("key").GetString() == "extra:pm-office")
            .GetProperty("members").EnumerateArray().Single();
        var emailsBefore = factory.Emails.Sent.Count;
        var removed = await delegateClient.PostAsJsonAsync($"{PortfolioMembers}/{member.GetProperty("id").GetGuid()}/remove", new { reason = "Moved to another department" });
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());

        // Access ends now (assignment ended yesterday, own grant revoked: no other role of the system left), nothing is deleted.
        var me = await MeAsync(personClient);
        me.Systems.ShouldNotContain("portfolio");
        me.Permissions.ShouldNotContain("portfolio.request.manage");
        (await ListsAsync(delegateClient, PortfolioMembers)).SelectMany(l => l.GetProperty("members").EnumerateArray())
            .ShouldNotContain(m => m.GetProperty("principal").GetProperty("id").GetString() == person.Id.ToString());
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var ended = (await admin.GetFromJsonAsync<List<JsonElement>>($"/api/v1/access/assignments?userId={person.Id}&includeEnded=true"))!;
        ended.ShouldContain(a => a.GetProperty("roleCode").GetString() == "pm-office" && !a.GetProperty("active").GetBoolean());

        var email = await factory.Emails.LastAsync(person.UserName, MemberAdministration.RemovedTemplateKey, emailsBefore);
        email.Values["reason"].ShouldBe("Moved to another department");
        factory.Emails.Sent.Skip(emailsBefore).Count(e => e.TemplateKey == MemberAdministration.RemovedTemplateKey).ShouldBe(1);

        (await AuditAsync(factory, new AuditFilter(EntityType: "access.role_assignment", Action: "update")))
            .ShouldContain(e => e.Reason == "Moved to another department");
        (await AuditAsync(factory, new AuditFilter(EntityType: "access.system_access", Action: "update")))
            .ShouldContain(e => e.Reason == "Moved to another department" && e.NewValue == "false");

        // Removing again: nothing left to end.
        await (await delegateClient.PostAsJsonAsync($"{PortfolioMembers}/{member.GetProperty("id").GetGuid()}/remove", new { reason = (string?)null }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "access.assignment.alreadyEnded");
    }

    [Fact]
    public async Task The_platform_administrator_configures_the_lists_and_vendors_are_refused()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);

        var config = await admin.GetFromJsonAsync<JsonElement>("/api/v1/access/systems/kpi/member-roles");
        config.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        config.GetProperty("administratorRoles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["administrator"]);

        await (await admin.PutAsJsonAsync("/api/v1/access/systems/kpi/member-roles", new { administratorRoles = new[] { "nope" }, userRoles = new[] { "owner" } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        await (await admin.PutAsJsonAsync("/api/v1/access/systems/kpi/member-roles",
                new { administratorRoles = new[] { "administrator" }, userRoles = new[] { "administrator" } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var saved = await admin.PutAsJsonAsync("/api/v1/access/systems/kpi/member-roles",
            new { administratorRoles = new[] { "administrator", "strategy-office" }, userRoles = new[] { "owner" }, extraRoles = new[] { "executive" } });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var lists = await ListsAsync(admin, "/api/v1/access/systems/kpi/members");
        lists.Select(l => l.GetProperty("key").GetString()).ShouldBe(["administrators", "users", "extra:executive"]);
        lists[0].GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ShouldBe(["administrator", "strategy-office"]);

        // The platform administrator also manages the platform administrator roles of Teams.
        var person = await factory.CreateUserAsync();
        (await admin.PostAsJsonAsync("/api/v1/access/systems/teams/members", new { list = "administrators", principalKind = "user", principalId = person.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A vendor (Wasel) principal is refused by the service itself, whatever channel reaches it.
        var vendor = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()), new Claim(StrategyClaims.PrincipalType, StrategyClaims.VendorPrincipal)], "wasel"));
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Wasel, vendor);
        var members = scope.ServiceProvider.GetRequiredService<MemberAdministration>();
        (await members.ListAsync(vendor, "portfolio", CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await members.AddAsync(vendor, "portfolio", new AddSystemMemberInput("users", "everyone", null, null, null, null), CancellationToken.None)).Error!.Kind
            .ShouldBe(ErrorKind.Forbidden);
        (await members.ManageableSystemsAsync(vendor, CancellationToken.None)).ShouldBeEmpty();
    }

    /// <summary>A user holding only <c>&lt;system&gt;.members.manage</c> (a role created for the test) and system access.</summary>
    private static async Task<(TestUser User, HttpClient Client)> DelegateAsync(ApiFactory factory, string system)
    {
        var role = await factory.CreateRoleAsync(system, (SystemMemberRoles.ManagePermission(system), DataScope.All));
        var user = await factory.CreateUserAsync(roles: role);
        return (user, await factory.SignInAsync(user));
    }

    private static async Task<(Guid Parent, Guid Child)> UnitsAsync(ApiFactory factory)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        var parent = await dispatcher.SendAsync(new CreateUnit($"MP-{suffix}", "وحدة أم", $"Parent {suffix}", "organisation", null, null, null, null, null));
        parent.IsSuccess.ShouldBeTrue(parent.IsFailure ? parent.Error.Code : null);
        var child = await dispatcher.SendAsync(new CreateUnit($"MC-{suffix}", "وحدة فرعية", $"Child {suffix}", "sector", parent.Value.Id, null, null, null, null));
        child.IsSuccess.ShouldBeTrue(child.IsFailure ? child.Error.Code : null);
        return (parent.Value.Id, child.Value.Id);
    }

    private static async Task<List<JsonElement>> ListsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lists").EnumerateArray().ToList();
    }

    private static async Task<(List<string?> Systems, List<string?> Permissions)> MeAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/access/me");
        return (me.GetProperty("systems").EnumerateArray().Select(s => s.GetString()).ToList(),
            me.GetProperty("permissions").EnumerateArray().Select(s => s.GetString()).ToList());
    }

    private static async Task<IReadOnlyList<AuditEntryDto>> AuditAsync(ApiFactory factory, AuditFilter filter)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAuditQuery>().SearchAsync(filter, new PageRequest(1, 200))).Items;
    }
}
