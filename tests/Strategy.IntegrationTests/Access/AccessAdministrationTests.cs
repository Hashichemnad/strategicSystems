using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Identity.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>Settings → Access: roles and the matrix, assignments, bulk transfer, effective access and the access review.</summary>
public class AccessAdministrationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Access = "/api/v1/access";

    [Fact]
    public async Task Matrix_lists_every_permission_with_labels_and_roles_with_scopes()
    {
        var admin = await api.AdminClientAsync();

        var matrix = await admin.GetFromJsonAsync<JsonElement>($"{Access}/systems/sample/matrix");

        var permissions = matrix.GetProperty("permissions").EnumerateArray().ToList();
        permissions.Select(p => p.GetProperty("code").GetString()).ShouldBe(["sample.item.read", "sample.item.create", "sample.item.update"]);
        permissions.ShouldAllBe(p => p.GetProperty("labelAr").GetString()!.Length > 0 && p.GetProperty("labelEn").GetString()!.Length > 0);
        var editor = matrix.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "editor");
        editor.GetProperty("permissions").EnumerateArray()
            .Select(p => $"{p.GetProperty("permission").GetString()}:{p.GetProperty("scope").GetString()}")
            .ShouldBe(["sample.item.create:all", "sample.item.read:all", "sample.item.update:own"]);
        matrix.GetProperty("recordRoles").EnumerateArray().Single().GetProperty("permissions").GetArrayLength().ShouldBe(2);
        await (await admin.GetAsync($"{Access}/systems/unknown/matrix")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
    }

    [Fact]
    public async Task Role_is_created_given_permissions_and_assigned_and_the_user_gains_exactly_that()
    {
        var admin = await api.AdminClientAsync();
        var code = "auditor-" + Guid.CreateVersion7().ToString("N")[^6..];
        var created = await admin.PostAsJsonAsync($"{Access}/roles", new { systemCode = "org", code, nameAr = "مدقق", nameEn = "Auditor", requiresMfa = false });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var role = await created.Content.ReadFromJsonAsync<JsonElement>();
        await (await admin.PostAsJsonAsync($"{Access}/roles", new { systemCode = "org", code, nameEn = "Again" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "access.role.duplicateCode");

        // Permissions must belong to the role's system; non-scopable permissions are stored as "all".
        await (await admin.PutAsJsonAsync($"{Access}/roles/{Id(role)}/permissions", new
        {
            permissions = new[] { new { permission = "sample.item.read", scope = "all" } },
            version = role.GetProperty("version").GetUInt32(),
        })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var updated = await admin.PutAsJsonAsync($"{Access}/roles/{Id(role)}/permissions", new
        {
            permissions = new[] { new { permission = "org.import.manage", scope = "own" } },
            version = role.GetProperty("version").GetUInt32(),
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("permissions")[0].GetProperty("scope").GetString().ShouldBe("all");

        var user = await api.Factory.CreateUserAsync();
        var client = await api.Factory.SignInAsync(user);
        await (await client.GetAsync("/api/v1/org/import/templates/units")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        var assignment = await admin.PostAsJsonAsync($"{Access}/assignments", new { roleId = Id(role), principalKind = "user", principalId = user.Id });
        assignment.StatusCode.ShouldBe(HttpStatusCode.OK, await assignment.Content.ReadAsStringAsync());
        (await client.GetAsync("/api/v1/org/import/templates/units")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.PostAsJsonAsync("/api/v1/org/levels", new { code = "x" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await admin.PostAsJsonAsync($"{Access}/assignments", new { roleId = Id(role), principalKind = "user", principalId = user.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "access.assignment.overlap");

        var effective = await admin.GetFromJsonAsync<JsonElement>($"{Access}/users/{user.Id}/effective");
        effective.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["org.viewer", $"org.{code}"], ignoreOrder: true);

        // Ending the assignment (last day yesterday) removes the access at once.
        var a = await assignment.Content.ReadFromJsonAsync<JsonElement>();
        (await admin.PostAsJsonAsync($"{Access}/assignments/{Id(a)}/end", new { version = a.GetProperty("version").GetUInt32() })).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        await (await client.GetAsync("/api/v1/org/import/templates/units")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Locked_access_administrator_role_cannot_lose_permissions_and_the_last_holder_cannot_be_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var adminUser = await factory.CreateUserAsync(admin: true);
        var admin = await factory.SignInAsync(adminUser);
        var roles = await admin.GetFromJsonAsync<List<JsonElement>>($"{Access}/roles?system=access");
        var locked = roles!.Single(r => r.GetProperty("code").GetString() == "administrator");
        locked.GetProperty("locked").GetBoolean().ShouldBeTrue();

        await (await admin.PutAsJsonAsync($"{Access}/roles/{Id(locked)}/permissions", new
        {
            permissions = Array.Empty<object>(),
            version = locked.GetProperty("version").GetUInt32(),
        })).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "access.role.locked");

        var assignments = await admin.GetFromJsonAsync<List<JsonElement>>($"{Access}/assignments?roleId={Id(locked)}");
        var mine = assignments!.Single(x => x.GetProperty("principal").GetProperty("id").GetString() == adminUser.Id.ToString());
        await (await admin.PostAsJsonAsync($"{Access}/assignments/{Id(mine)}/end", new { version = mine.GetProperty("version").GetUInt32() }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "access.lastAdministrator");

        // A user administrator (no access administration) can deactivate accounts, but never the last access administrator.
        var userAdmin = await factory.CreateUserAsync(withMfa: true, roles: "identity.administrator");
        var userAdminClient = await factory.SignInAsync(userAdmin);
        await (await userAdminClient.PostAsync($"/api/v1/identity/admin/users/{adminUser.Id}/deactivate", null))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.lastAdmin");

        var second = await factory.CreateUserAsync(admin: true);
        (await userAdminClient.PostAsync($"/api/v1/identity/admin/users/{adminUser.Id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await userAdminClient.PostAsync($"/api/v1/identity/admin/users/{second.Id}/deactivate", null))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.lastAdmin");
    }

    [Fact]
    public async Task Bulk_transfer_moves_roles_from_one_user_to_another_and_logs_it()
    {
        var admin = await api.AdminClientAsync();
        var from = await api.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
        var to = await api.Factory.CreateUserAsync();
        var roles = await admin.GetFromJsonAsync<List<JsonElement>>($"{Access}/roles?system=sample");
        var editor = roles!.Single(r => r.GetProperty("code").GetString() == "editor");

        await (await admin.PostAsJsonAsync($"{Access}/transfers", new { roleIds = new[] { Id(editor) }, fromUserIds = new[] { from.Id }, toUserId = to.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed"); // a reason is required
        var transfer = await admin.PostAsJsonAsync($"{Access}/transfers", new
        {
            roleIds = new[] { Id(editor) },
            fromUserIds = new[] { from.Id },
            toUserId = to.Id,
            reason = "Moved to another department",
        });
        transfer.StatusCode.ShouldBe(HttpStatusCode.OK, await transfer.Content.ReadAsStringAsync());
        (await transfer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movedCount").GetInt32().ShouldBe(1);

        var fromRoles = (await admin.GetFromJsonAsync<JsonElement>($"{Access}/users/{from.Id}/effective")).GetProperty("roles");
        fromRoles.EnumerateArray().Select(r => r.GetString()).ShouldNotContain("sample.editor");
        var toRoles = (await admin.GetFromJsonAsync<JsonElement>($"{Access}/users/{to.Id}/effective")).GetProperty("roles");
        toRoles.EnumerateArray().Select(r => r.GetString()).ShouldContain("sample.editor");
        (await admin.GetFromJsonAsync<List<JsonElement>>($"{Access}/transfers"))!
            .ShouldContain(t => t.GetProperty("reason").GetString() == "Moved to another department");

        await (await admin.PostAsJsonAsync($"{Access}/transfers", new
        {
            roleIds = new[] { Id(editor) },
            fromUserIds = new[] { from.Id },
            toUserId = to.Id,
            reason = "Again",
        })).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "access.transfer.nothingToMove");
    }

    [Fact]
    public async Task Access_review_lists_who_holds_what_and_exports_to_excel_in_both_languages()
    {
        var admin = await api.AdminClientAsync();
        var user = await api.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);

        var rows = await admin.GetFromJsonAsync<List<JsonElement>>($"{Access}/review?userId={user.Id}");
        var sample = rows!.Single(r => r.GetProperty("roleCode").GetString() == "editor");
        sample.GetProperty("systemCode").GetString().ShouldBe("sample");
        sample.GetProperty("systemAccess").GetBoolean().ShouldBeTrue();
        sample.GetProperty("source").GetString().ShouldBe("user");
        rows!.ShouldContain(r => r.GetProperty("roleCode").GetString() == "viewer" && r.GetProperty("source").GetString() == "everyone");

        foreach (var (lang, header) in new[] { ("en", "User"), ("ar", "المستخدم") })
        {
            var export = await admin.GetAsync($"{Access}/review/export?system=sample&lang={lang}");
            export.StatusCode.ShouldBe(HttpStatusCode.OK);
            export.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using var workbook = new XLWorkbook(await export.Content.ReadAsStreamAsync());
            var sheet = workbook.Worksheets.First();
            sheet.Cell(1, 1).GetString().ShouldBe(header);
            sheet.RightToLeft.ShouldBe(lang == "ar");
            sheet.RowsUsed().Skip(1).ShouldContain(r => r.Cell(1).GetString() == user.UserName);
        }
    }

    [Fact]
    public async Task A_role_that_requires_mfa_makes_two_step_verification_mandatory_at_sign_in()
    {
        var user = await api.Factory.CreateUserAsync(roles: "settings.administrator");
        var client = await api.Factory.CreateSpaClientAsync();

        (await client.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.MfaEnrolmentRequired);
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();
}
