using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Access;

/// <summary>Record roles (SPEC §0.2 layer 3): a person's role on one record gives access to exactly that record.</summary>
public class RecordRoleTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task Granting_a_record_role_gives_access_to_exactly_that_record()
    {
        var owner = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var first = await CreateAsync(owner, "First");
        var second = await CreateAsync(owner, "Second");

        // The guest may open the system but holds no system role in it.
        var guest = await api.Factory.CreateUserAsync();
        await api.Factory.GrantSystemAccessAsync("sample", AccessGrants.User, guest.Id);
        var client = await api.Factory.SignInAsync(guest);
        await (await client.GetAsync(Items)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        await api.Factory.GrantRecordRoleAsync(SamplePermissions.ResourceType, first.Id, RecordPrincipalKinds.Employee, guest.EmployeeId!.Value, SamplePermissions.EditorRole);

        var page = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?pageSize=200");
        page!.Items.Select(i => i.Id).ShouldBe([first.Id]);
        (await client.GetAsync($"{Items}/{first.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.GetAsync($"{Items}/{second.Id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        (await client.PutAsJsonAsync($"{Items}/{first.Id}", new { nameEn = "Edited by the guest", version = first.Version })).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        await (await client.PutAsJsonAsync($"{Items}/{second.Id}", new { nameEn = "No", version = second.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");

        // Creating needs a system-role permission; a record role on another record does not give it.
        await (await client.PostAsJsonAsync(Items, new { nameEn = "New" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Module_grants_and_revokes_editors_through_its_own_api()
    {
        var owner = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var item = await CreateAsync(owner, "Shared");
        var colleague = await api.Factory.CreateUserAsync();
        await api.Factory.GrantSystemAccessAsync("sample", AccessGrants.User, colleague.Id);
        var colleagueClient = await api.Factory.SignInAsync(colleague);
        await (await colleagueClient.GetAsync($"{Items}/{item.Id}")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        var editors = await owner.PostAsJsonAsync($"{Items}/{item.Id}/editors", new { employeeId = colleague.EmployeeId });
        editors.StatusCode.ShouldBe(HttpStatusCode.OK, await editors.Content.ReadAsStringAsync());
        (await editors.Content.ReadFromJsonAsync<List<SampleEditorDto>>())!.Single().EmployeeId.ShouldBe(colleague.EmployeeId!.Value);
        (await colleagueClient.GetAsync($"{Items}/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The colleague is not the owner: as an editor they may edit, and may share further (update on this record).
        (await owner.PostAsync($"{Items}/{item.Id}/editors/{colleague.EmployeeId}/remove", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await colleagueClient.GetAsync($"{Items}/{item.Id}")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Only_someone_who_may_edit_the_record_can_grant_editors()
    {
        var owner = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var item = await CreateAsync(owner, "Guarded");
        var other = await api.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor); // reads all, edits only own
        var otherClient = await api.Factory.SignInAsync(other);

        await (await otherClient.PostAsJsonAsync($"{Items}/{item.Id}/editors", new { employeeId = other.EmployeeId }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await owner.PostAsJsonAsync($"{Items}/{item.Id}/editors", new { employeeId = Guid.CreateVersion7() }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
    }

    private static async Task<SampleItemDto> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameEn = name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }
}
