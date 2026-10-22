using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Sample;

public class SampleItemsApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task Create_returns_201_with_location_and_stamped_item()
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var response = await client.PostAsJsonAsync(Items, new { nameAr = "بند جديد", nameEn = "New item" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
        response.Headers.Location!.ToString().ShouldBe($"{Items}/{item.Id}");
        item.Id.Version.ShouldBe(7);
        item.NameAr.ShouldBe("بند جديد");
        item.Status.ShouldBe("active");
        item.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        item.UpdatedAt.ShouldBe(item.CreatedAt);
        item.Version.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public async Task Create_without_any_name_returns_400_with_field_error()
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var response = await client.PostAsJsonAsync(Items, new { nameAr = " ", nameEn = "" });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed", "errors.validationFailed");
        var error = problem.GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe("nameAr");
        error.GetProperty("messageKey").GetString().ShouldBe("validation.atLeastOneLanguage");
    }

    [Fact]
    public async Task Create_with_too_long_name_reports_the_limit_as_a_param()
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var response = await client.PostAsJsonAsync(Items, new { nameEn = new string('x', 201) });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var error = problem.GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe("nameEn");
        error.GetProperty("messageKey").GetString().ShouldBe("validation.maximumLength");
        error.GetProperty("params").GetProperty("maxLength").GetInt32().ShouldBe(200);
    }

    [Fact]
    public async Task Malformed_json_returns_400_problem()
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var response = await client.PostAsync(Items, new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Get_returns_the_item_and_404_for_unknown_id()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var created = await CreateAsync(client, "Get me");

        (await client.GetFromJsonAsync<SampleItemDto>($"{Items}/{created.Id}"))!.NameEn.ShouldBe("Get me");
        await (await client.GetAsync($"{Items}/{Guid.CreateVersion7()}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound", "errors.notFound");
    }

    [Fact]
    public async Task Update_with_current_version_succeeds_and_returns_a_new_version()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var created = await CreateAsync(client, "Before");

        var response = await client.PutAsJsonAsync($"{Items}/{created.Id}", new { nameEn = "After", nameAr = "بعد", version = created.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
        updated.NameEn.ShouldBe("After");
        updated.Version.ShouldNotBe(created.Version);
        updated.UpdatedAt.ShouldBeGreaterThanOrEqualTo(created.UpdatedAt);
        updated.CreatedAt.ShouldBe(created.CreatedAt);
    }

    [Fact]
    public async Task Update_with_stale_version_returns_409()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var created = await CreateAsync(client, "Contended");
        (await client.PutAsJsonAsync($"{Items}/{created.Id}", new { nameEn = "First writer", version = created.Version }))
            .EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync($"{Items}/{created.Id}", new { nameEn = "Second writer", version = created.Version });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict", "errors.concurrencyConflict");
    }

    [Fact]
    public async Task Invalid_transitions_return_422_and_leave_status_unchanged()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var item = await CreateAsync(client, "Lifecycle");

        await (await client.PostAsJsonAsync($"{Items}/{item.Id}/activate", new { version = item.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "sample.item.alreadyActive", "sample.errors.alreadyActive");

        var deactivated = await (await client.PostAsJsonAsync($"{Items}/{item.Id}/deactivate", new { version = item.Version }))
            .Content.ReadFromJsonAsync<SampleItemDto>();
        deactivated!.Status.ShouldBe("inactive");

        await (await client.PutAsJsonAsync($"{Items}/{item.Id}", new { nameEn = "Edit", version = deactivated.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "sample.item.inactive");

        var reactivated = await (await client.PostAsJsonAsync($"{Items}/{item.Id}/activate", new { version = deactivated.Version }))
            .Content.ReadFromJsonAsync<SampleItemDto>();
        reactivated!.Status.ShouldBe("active");
    }

    [Fact]
    public async Task List_filters_searches_sorts_and_pages()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var b = await CreateAsync(client, $"{tag} beta");
        await CreateAsync(client, $"{tag} alpha");
        await CreateAsync(client, $"{tag} gamma");
        (await client.PostAsJsonAsync($"{Items}/{b.Id}/deactivate", new { version = b.Version })).EnsureSuccessStatusCode();

        var page1 = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={tag}&sort=nameEn&pageSize=2");
        page1!.TotalCount.ShouldBe(3);
        page1.TotalPages.ShouldBe(2);
        page1.Items.Select(i => i.NameEn).ShouldBe([$"{tag} alpha", $"{tag} beta"]);

        var page2 = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={tag}&sort=nameEn&pageSize=2&page=2");
        page2!.Items.Single().NameEn.ShouldBe($"{tag} gamma");

        var inactive = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={tag}&status=inactive");
        inactive!.Items.Single().Id.ShouldBe(b.Id);
    }

    [Fact]
    public async Task List_search_matches_arabic_and_treats_wildcards_literally()
    {
        var client = await api.Factory.CreateSpaClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        (await client.PostAsJsonAsync(Items, new { nameAr = $"خطة {tag} الاستراتيجية" })).EnsureSuccessStatusCode();

        var arabic = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={Uri.EscapeDataString($"{tag} الاستراتيجية")}");
        arabic!.TotalCount.ShouldBe(1);

        var wildcard = await client.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={tag}%25");
        wildcard!.TotalCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("sort=password", "sort")]
    [InlineData("pageSize=500", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("status=deleted", "status")]
    public async Task List_rejects_invalid_query(string query, string field)
    {
        var client = await api.Factory.CreateSpaClientAsync();

        var problem = await (await client.GetAsync($"{Items}?{query}")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        problem.GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe(field);
    }

    private static async Task<SampleItemDto> CreateAsync(HttpClient client, string nameEn)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameEn });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }
}
