using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Audit;

/// <summary>CLAUDE.md §8 on a real database: field-level entries, history, as-of, append-only, search and export.</summary>
public class AuditTrailTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task An_update_writes_one_entry_per_changed_field_with_old_and_new_value_user_channel_and_correlation_id()
    {
        var editor = await api.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
        var client = await api.Factory.SignInAsync(editor);
        var item = await CreateAsync(client, "Old name", "وصف");

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Items}/{item.Id}")
        {
            Content = JsonContent.Create(new { nameAr = item.NameAr, nameEn = "New name", descriptionAr = "وصف جديد", descriptionEn = (string?)null, version = item.Version }),
        };
        update.Headers.Add("X-Correlation-Id", "test-correlation-" + Guid.NewGuid().ToString("N")[..8]);
        var response = await client.SendAsync(update);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var history = await HistoryAsync(client, item.Id);
        var changes = history.Items.Where(e => e.Action == AuditActions.Update).ToList();
        changes.Select(e => e.Field).Order().ShouldBe(["DescriptionAr", "NameEn"]);
        var name = changes.Single(e => e.Field == "NameEn");
        name.OldValue.ShouldBe("Old name");
        name.NewValue.ShouldBe("New name");
        changes.Single(e => e.Field == "DescriptionAr").OldValue.ShouldBe("وصف");
        changes.Single(e => e.Field == "DescriptionAr").NewValue.ShouldBe("وصف جديد");
        foreach (var entry in changes)
        {
            entry.UserId.ShouldBe(editor.Id);
            entry.UserName.ShouldBe(editor.UserName);
            entry.Channel.ShouldBe("ui");
            entry.CorrelationId.ShouldBe(update.Headers.GetValues("X-Correlation-Id").Single());
            entry.EntityType.ShouldBe("sample.item");
            entry.SystemCode.ShouldBe("sample");
        }

        changes.Select(e => e.ChangeSetId).Distinct().Count().ShouldBe(1);
        var create = history.Items.Single(e => e.Action == AuditActions.Create);
        JsonSerializer.Deserialize<Dictionary<string, string?>>(create.NewValue!)!["NameEn"].ShouldBe("Old name");
    }

    [Fact]
    public async Task A_record_can_be_viewed_as_of_a_date()
    {
        var client = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var item = await CreateAsync(client, "First", null);
        var between = DateTimeOffset.UtcNow;
        await Task.Delay(20);
        (await client.PutAsJsonAsync($"{Items}/{item.Id}", new { nameEn = "Second", version = item.Version })).EnsureSuccessStatusCode();

        var then = await client.GetFromJsonAsync<RecordSnapshotDto>($"{Items}/{item.Id}/audit/as-of?at={Uri.EscapeDataString(between.ToString("O"))}");
        then!.Existed.ShouldBeTrue();
        then.Fields["NameEn"].ShouldBe("First");
        var now = await client.GetFromJsonAsync<RecordSnapshotDto>($"{Items}/{item.Id}/audit/as-of?at={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        now!.Fields["NameEn"].ShouldBe("Second");
        var before = await client.GetFromJsonAsync<RecordSnapshotDto>($"{Items}/{item.Id}/audit/as-of?at={Uri.EscapeDataString(item.CreatedAt.AddMinutes(-1).ToString("O"))}");
        before!.Existed.ShouldBeFalse();
    }

    [Fact]
    public async Task History_of_a_record_outside_the_callers_scope_is_404_and_a_refused_update_writes_nothing()
    {
        var owner = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var item = await CreateAsync(owner, "Private", null);
        var ownOnly = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, Strategy.Platform.Access.DataScope.Own));
        var stranger = await api.Factory.CreateSignedInClientAsync(roles: ownOnly);
        var noRole = await api.Factory.CreateSignedInClientAsync();

        (await stranger.GetAsync($"{Items}/{item.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await noRole.GetAsync($"{Items}/{item.Id}/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A stale version is refused (409): the failed save leaves no audit rows behind.
        var before = (await HistoryAsync(owner, item.Id)).TotalCount;
        (await owner.PutAsJsonAsync($"{Items}/{item.Id}", new { nameEn = "Conflict", version = item.Version + 1000 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await HistoryAsync(owner, item.Id)).TotalCount.ShouldBe(before);
    }

    [Fact]
    public async Task Role_permission_changes_show_old_and_new_values_in_the_roles_history()
    {
        var admin = await api.AdminClientAsync();
        var role = await (await admin.PostAsJsonAsync("/api/v1/access/roles",
            new { systemCode = "sample", code = "auditrole" + Guid.NewGuid().ToString("N")[..6], nameAr = "دور", nameEn = "Audited role" })).Content.ReadFromJsonAsync<JsonElement>();
        var roleId = role.GetProperty("id").GetGuid();
        var version = role.GetProperty("version").GetUInt32();
        var set = await admin.PutAsJsonAsync($"/api/v1/access/roles/{roleId}/permissions",
            new { permissions = new[] { new { permission = SamplePermissions.Read, scope = "unit" } }, version });
        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        version = (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32();
        (await admin.PutAsJsonAsync($"/api/v1/access/roles/{roleId}/permissions",
            new { permissions = new[] { new { permission = SamplePermissions.Read, scope = "all" } }, version })).EnsureSuccessStatusCode();

        var history = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/access/roles/{roleId}/audit?pageSize=100");
        var permissionRows = history!.Items.Where(e => e.EntityType == "access.role_permission").ToList();
        permissionRows.ShouldAllBe(e => e.ParentType == "access.role" && e.ParentId == roleId.ToString("D"));
        permissionRows.ShouldContain(e => e.Action == AuditActions.Update && e.Field == "Scope" && e.OldValue == "unit" && e.NewValue == "all");
        history.Items.ShouldContain(e => e.EntityType == "access.role" && e.Action == AuditActions.Create);
    }

    [Fact]
    public async Task Settings_changes_are_recorded_with_the_old_and_new_document()
    {
        var admin = await api.AdminClientAsync();
        var flipped = false;
        for (var i = 0; i < 2; i++) // the first save may create the area's row; the second one is certainly an update
        {
            var current = await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings/regional");
            var value = current.GetProperty("value");
            flipped = !value.GetProperty("arabicIndicDigits").GetBoolean();
            var body = new
            {
                value = new
                {
                    timeZone = value.GetProperty("timeZone").GetString(),
                    hijriDisplay = value.GetProperty("hijriDisplay").GetBoolean(),
                    arabicIndicDigits = flipped,
                    currencyCode = value.GetProperty("currencyCode").GetString(),
                    dateFormat = value.GetProperty("dateFormat").GetString(),
                },
                version = current.GetProperty("version").GetUInt32(),
            };
            (await admin.PutAsJsonAsync("/api/v1/settings/regional", body)).EnsureSuccessStatusCode();
        }

        var found = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>("/api/v1/audit/entries?system=settings&entityType=settings.setting&field=Value");
        found!.Items.ShouldContain(e => e.NewValue!.Contains($"\"arabicIndicDigits\":{(flipped ? "true" : "false")}", StringComparison.Ordinal)
            && e.OldValue!.Contains($"\"arabicIndicDigits\":{(flipped ? "false" : "true")}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Audit_entries_cannot_be_updated_deleted_or_truncated_even_by_the_owner()
    {
        await (await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor)).PostAsJsonAsync(Items, new { nameEn = "Something to audit" });
        await using var connection = new NpgsqlConnection(api.Factory.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { "update audit.entry set new_value = 'x'", "delete from audit.entry", "truncate audit.entry" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            error.MessageText.ShouldContain("append-only");
        }
    }

    [Fact]
    public async Task Audit_log_searches_across_systems_and_exports_to_excel_in_arabic_and_english()
    {
        var admin = await api.AdminClientAsync();
        var client = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var marker = "Exported-" + Guid.NewGuid().ToString("N")[..8];
        await CreateAsync(client, marker, null);

        var found = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?text={marker}");
        found!.Items.ShouldHaveSingleItem().EntityType.ShouldBe("sample.item");
        var logins = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>("/api/v1/audit/entries?action=auth&kind=event");
        logins!.Items.ShouldNotBeEmpty();
        logins.Items.ShouldAllBe(e => e.Action.StartsWith("auth."));

        foreach (var (lang, header, rtl) in new[] { ("ar", "المستخدم", true), ("en", "User", false) })
        {
            var export = await admin.GetAsync($"/api/v1/audit/entries/export?format=xlsx&lang={lang}&kind=change&text={marker}");
            export.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var workbook = new XLWorkbook(await export.Content.ReadAsStreamAsync());
            var sheet = workbook.Worksheets.First();
            sheet.RightToLeft.ShouldBe(rtl);
            sheet.Row(1).CellsUsed().Select(c => c.GetString()).ShouldContain(header);
            sheet.RowsUsed().Count().ShouldBe(2);
        }

        var exports = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>("/api/v1/audit/entries?action=export");
        exports!.Items.Count(e => e.EntityType == "audit.entry").ShouldBeGreaterThanOrEqualTo(2);

        var outsider = await api.Factory.CreateSignedInClientAsync();
        (await outsider.GetAsync("/api/v1/audit/entries")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync("/api/v1/audit/entries/export")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditors_may_search_but_not_verify()
    {
        var auditor = await api.Factory.CreateSignedInClientAsync(roles: "audit.auditor");
        (await auditor.GetAsync("/api/v1/audit/entries")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await auditor.PostAsJsonAsync("/api/v1/audit/verification", new { mode = "full" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<SampleItemDto> CreateAsync(HttpClient client, string nameEn, string? descriptionAr)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameAr = "بند", nameEn, descriptionAr });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }

    private static async Task<PagedResult<AuditEntryDto>> HistoryAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"{Items}/{id}/audit?pageSize=100"))!;
}
