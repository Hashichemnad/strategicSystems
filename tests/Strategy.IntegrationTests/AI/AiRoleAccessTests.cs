using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.AI.Application;

namespace Strategy.IntegrationTests.AI;

/// <summary>Per-role access to AI features is the access model (ADR 0028): narrow the "AI user" role and grant a role to some users.</summary>
public sealed class AiRoleAccessTests(AiFixture ai) : IClassFixture<AiFixture>
{
    [Fact]
    public async Task A_feature_removed_from_the_everyone_role_is_refused_except_to_holders_of_another_role()
    {
        await ai.EnableAsync();
        var admin = await ai.AdminAsync();
        var role = (await admin.GetFromJsonAsync<List<RoleRow>>("/api/v1/access/roles?system=ai"))!.Single(r => r.Code == "user");
        var narrowed = await admin.PutAsJsonAsync($"/api/v1/access/roles/{role.Id}/permissions", new
        {
            permissions = role.Permissions.Where(p => p.Permission != "ai.translate.use").ToList(),
            version = role.Version,
        });
        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK, await narrowed.Content.ReadAsStringAsync());

        var ordinary = await ai.Factory.CreateSignedInClientAsync();
        (await ordinary.GetFromJsonAsync<AiCapabilitiesDto>("/api/v1/ai/capabilities"))!.Features["translate"].ShouldBeFalse();
        await (await ordinary.PostAsJsonAsync("/api/v1/ai/translate", new { text = "مرحبا", from = "ar", to = "en" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        var translators = await ai.Factory.CreateRoleAsync("ai", ("ai.translate.use", Strategy.Platform.Access.DataScope.All));
        var translator = await ai.Factory.SignInAsync(await ai.Factory.CreateUserAsync(roles: translators));
        ai.Model.Reset((_, _) => FakeChatClient.Json(new { translation = "Hello" }));
        (await translator.PostAsJsonAsync("/api/v1/ai/translate", new { text = "مرحبا", from = "ar", to = "en" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record RoleRow(Guid Id, string Code, List<PermissionRow> Permissions, uint Version);

    private sealed record PermissionRow(string Permission, string Scope);
}
