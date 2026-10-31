using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;

namespace Strategy.IntegrationTests.Settings;

/// <summary>Settings centre (SPEC §0.2): typed settings, branding, lookups, numbering, features, translation overrides.</summary>
public class SettingsCentreTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Settings = "/api/v1/settings";

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public async Task Regional_settings_have_safe_defaults_validate_and_detect_conflicts()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);

        var current = await admin.GetFromJsonAsync<JsonElement>($"{Settings}/regional");
        var value = current.GetProperty("value");
        value.GetProperty("hijriDisplay").GetBoolean().ShouldBeFalse();
        value.GetProperty("timeZone").GetString().ShouldBe("Asia/Dubai");
        value.GetProperty("currencyCode").GetString().ShouldBe("AED");

        await (await admin.PutAsJsonAsync($"{Settings}/regional", new
        {
            value = new { timeZone = "Mars/Olympus", hijriDisplay = true, arabicIndicDigits = true, currencyCode = "aed", dateFormat = "x" },
            version = 0,
        })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var saved = await admin.PutAsJsonAsync($"{Settings}/regional", new
        {
            value = new { timeZone = "Asia/Riyadh", hijriDisplay = true, arabicIndicDigits = true, currencyCode = "SAR", dateFormat = "yyyy-MM-dd" },
            version = 0,
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        await (await admin.PutAsJsonAsync($"{Settings}/regional", new
        {
            value = new { timeZone = "Asia/Dubai", hijriDisplay = false, arabicIndicDigits = false, currencyCode = "AED", dateFormat = "dd/MM/yyyy" },
            version = 0,
        })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict");

        var anonymous = await factory.CreateSpaClientAsync();
        var publicSettings = await anonymous.GetFromJsonAsync<JsonElement>($"{Settings}/public");
        publicSettings.GetProperty("regional").GetProperty("hijriDisplay").GetBoolean().ShouldBeTrue();
        publicSettings.GetProperty("regional").GetProperty("currencyCode").GetString().ShouldBe("SAR");
    }

    [Fact]
    public async Task Branding_names_colour_and_logo_are_saved_and_served_publicly_and_unsafe_files_are_refused()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        (await admin.PutAsJsonAsync($"{Settings}/branding", new
        {
            value = new { organisationNameAr = "دائرة المالية", organisationNameEn = "Department of Finance", primaryColor = "#123456", emailFooterEn = "Abu Dhabi" },
            version = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await admin.PutAsJsonAsync($"{Settings}/branding", new { value = new { primaryColor = "red" }, version = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        (await Upload(admin, "logo-light", Png, "logo.png")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var svg = await Upload(admin, "logo-dark", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(), "logo.svg");
        var problem = await svg.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors")[0].GetProperty("messageKey").GetString().ShouldBe("settings.errors.logoType");

        var anonymous = await factory.CreateSpaClientAsync();
        var publicSettings = await anonymous.GetFromJsonAsync<JsonElement>($"{Settings}/public");
        publicSettings.GetProperty("organisationNameEn").GetString().ShouldBe("Department of Finance");
        publicSettings.GetProperty("primaryColor").GetString().ShouldBe("#123456");
        publicSettings.GetProperty("hasLogoLight").GetBoolean().ShouldBeTrue();
        publicSettings.GetProperty("hasLogoDark").GetBoolean().ShouldBeFalse();
        var logo = await anonymous.GetAsync($"{Settings}/branding/logo/logo-light");
        logo.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await logo.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        (await anonymous.GetAsync($"{Settings}/branding/logo/logo-dark")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lookup_values_edited_in_settings_take_effect_for_employee_grades_at_once()
    {
        var admin = await api.AdminClientAsync();
        var unitId = (await api.Factory.CreateUserAsync()).UnitId;
        var grades = await admin.GetFromJsonAsync<List<JsonElement>>($"{Settings}/lookups/employee.grade/values");
        grades!.Select(g => g.GetProperty("code").GetString()).ShouldContain("G01");

        object Employee(string grade) => new
        {
            employeeNo = "LG" + Guid.CreateVersion7().ToString("N")[^8..],
            nameEn = "Grade test",
            email = $"g.{Guid.CreateVersion7():N}@example.test",
            unitId,
            grade,
        };

        var code = "X" + Guid.CreateVersion7().ToString("N")[^5..].ToUpperInvariant();
        var unknown = await admin.PostAsJsonAsync("/api/v1/org/employees", Employee(code));
        var problem = await unknown.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("org.employee.unknownGrade");

        var created = await admin.PostAsJsonAsync($"{Settings}/lookups/employee.grade/values", new { code, labelAr = "درجة خاصة", labelEn = "Special grade", sort = 999 });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        (await admin.PostAsJsonAsync("/api/v1/org/employees", Employee(code))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await (await admin.PostAsJsonAsync($"{Settings}/lookups/employee.grade/values", new { code, labelEn = "Twice" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "settings.lookupValue.duplicateCode");

        // Deactivated values are no longer accepted for new data.
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        (await admin.PutAsJsonAsync($"{Settings}/lookups/values/{value.GetProperty("id").GetGuid()}", new
        {
            labelAr = "درجة خاصة",
            labelEn = "Special grade",
            sort = 999,
            active = false,
            version = value.GetProperty("version").GetUInt32(),
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await admin.PostAsJsonAsync("/api/v1/org/employees", Employee(code))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        // Any signed-in user can read lists for dropdowns; only list managers change them.
        var user = await api.UserClientAsync();
        (await user.GetAsync($"{Settings}/lookups/employee.grade/values")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await user.PostAsJsonAsync($"{Settings}/lookups/employee.grade/values", new { code = "Z1", labelEn = "Z" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Custom_lookup_types_support_dependent_values()
    {
        var admin = await api.AdminClientAsync();
        var suffix = Guid.CreateVersion7().ToString("N")[^6..];
        (await admin.PostAsJsonAsync($"{Settings}/lookups/types", new { code = $"test.category-{suffix}", systemCode = "sample", nameEn = "Category" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"{Settings}/lookups/types", new
        {
            code = $"test.subcategory-{suffix}",
            systemCode = "sample",
            nameEn = "Sub-category",
            parentTypeCode = $"test.category-{suffix}",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var parent = await (await admin.PostAsJsonAsync($"{Settings}/lookups/test.category-{suffix}/values", new { code = "OPS", labelEn = "Operations" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        (await admin.PostAsJsonAsync($"{Settings}/lookups/test.subcategory-{suffix}/values", new
        {
            code = "OPS-1",
            labelEn = "Field operations",
            parentValueId = parent.GetProperty("id").GetGuid(),
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await admin.PostAsJsonAsync($"{Settings}/lookups/test.subcategory-{suffix}/values", new
        {
            code = "OPS-2",
            labelEn = "Wrong parent",
            parentValueId = Guid.CreateVersion7(),
        })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        await (await admin.PostAsJsonAsync($"{Settings}/lookups/types", new { code = "Bad Code", systemCode = "sample", nameEn = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Numbering_format_edited_in_settings_changes_the_next_number_without_a_redeploy()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var editor = await factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var year = DateTime.UtcNow.Year;

        (await CreateItemAsync(editor)).Number.ShouldBe($"SMP-{year}-0001");
        (await CreateItemAsync(editor)).Number.ShouldBe($"SMP-{year}-0002");

        var formats = await admin.GetFromJsonAsync<List<JsonElement>>($"{Settings}/numbering");
        var format = formats!.Single(f => f.GetProperty("key").GetString() == "sample.item");
        await (await admin.PutAsJsonAsync($"{Settings}/numbering/sample.item", new { pattern = "SMP-{YYYY}", version = format.GetProperty("version").GetUInt32() }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var preview = await admin.PostAsJsonAsync($"{Settings}/numbering/preview", new { pattern = "ITEM/{YY}/{SEQ:3}" });
        (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview").GetString().ShouldBe($"ITEM/{year % 100:D2}/001");
        (await admin.PutAsJsonAsync($"{Settings}/numbering/sample.item", new { pattern = "ITEM/{YY}/{SEQ:3}", version = format.GetProperty("version").GetUInt32() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CreateItemAsync(editor)).Number.ShouldBe($"ITEM/{year % 100:D2}/001");

        // Same prefix, wider sequence: the counter continues, so numbers never repeat.
        format = (await admin.GetFromJsonAsync<List<JsonElement>>($"{Settings}/numbering"))!.Single(f => f.GetProperty("key").GetString() == "sample.item");
        (await admin.PutAsJsonAsync($"{Settings}/numbering/sample.item", new { pattern = "ITEM/{YY}/{SEQ:5}", version = format.GetProperty("version").GetUInt32() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CreateItemAsync(editor)).Number.ShouldBe($"ITEM/{year % 100:D2}/00002");
    }

    [Fact]
    public async Task Concurrent_creates_get_unique_consecutive_numbers()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor)));

        var items = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => CreateItemAsync(clients[i % clients.Length])));

        items.Select(i => i.Number).Distinct().Count().ShouldBe(20);
        items.Select(i => int.Parse(i.Number![^4..], System.Globalization.CultureInfo.InvariantCulture)).Order().ShouldBe(Enumerable.Range(1, 20));
    }

    [Fact]
    public async Task Switching_off_a_feature_takes_effect_at_once()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var user = await factory.CreateSignedInClientAsync();
        (await user.GetFromJsonAsync<List<string>>($"{Settings}/features/enabled"))!.ShouldContain("identity.access-tokens");

        var flag = (await admin.GetFromJsonAsync<List<JsonElement>>($"{Settings}/features"))!.Single(f => f.GetProperty("code").GetString() == "identity.access-tokens");
        (await admin.PutAsJsonAsync($"{Settings}/features/identity.access-tokens", new { enabled = false, version = flag.GetProperty("version").GetUInt32() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await user.GetFromJsonAsync<List<string>>($"{Settings}/features/enabled"))!.ShouldNotContain("identity.access-tokens");
        await (await user.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Script", scopes = new[] { "api.read" } }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "feature.disabled", "errors.featureDisabled");
    }

    [Fact]
    public async Task Translation_override_changes_a_label_in_both_languages_and_can_be_cleared()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        (await admin.PutAsJsonAsync($"{Settings}/translations", new { language = "en", @namespace = "shell", key = "nav.home", value = "My Work" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"{Settings}/translations", new { language = "ar", @namespace = "shell", key = "nav.home", value = "أعمالي" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await admin.PutAsJsonAsync($"{Settings}/translations", new { language = "fr", @namespace = "shell", key = "nav.home", value = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var anonymous = await factory.CreateSpaClientAsync();
        var overrides = await anonymous.GetFromJsonAsync<JsonElement>($"{Settings}/translations");
        overrides.GetProperty("languages").GetProperty("en").GetProperty("shell").GetProperty("nav.home").GetString().ShouldBe("My Work");
        overrides.GetProperty("languages").GetProperty("ar").GetProperty("shell").GetProperty("nav.home").GetString().ShouldBe("أعمالي");
        overrides.GetProperty("version").GetInt64().ShouldBeGreaterThan(0);

        (await admin.PutAsJsonAsync($"{Settings}/translations", new { language = "en", @namespace = "shell", key = "nav.home", value = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        overrides = await anonymous.GetFromJsonAsync<JsonElement>($"{Settings}/translations");
        overrides.GetProperty("languages").TryGetProperty("en", out _).ShouldBeFalse();
    }

    private static async Task<SampleItemDto> CreateItemAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Numbered" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, string kind, byte[] content, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return client.PutAsync($"{Settings}/branding/logo/{kind}", form);
    }
}
