using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Api;
using Strategy.Platform.Integration.Application;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Mcp.Application;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Import;

namespace Strategy.IntegrationTests.Integration;

/// <summary>
/// External references (SPEC-ADDENDUM §B, ADR 0164 / 0165): source systems, both uniqueness rules (validation and the unique index under a
/// concurrent writer), the record-page endpoint, upsert by external ID in imports, REST GET / PUT and the MCP tool — always with the caller's access.
/// </summary>
public sealed class ExternalReferenceTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Sources = "/api/v1/integration/source-systems";
    private const string Items = "/api/v1/sample/items";

    private HttpClient? _editor;

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];

    /// <summary>A sample-module editor (read all, create, update own): the platform administrator holds no module role.</summary>
    private async Task<HttpClient> EditorAsync() => _editor ??= await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);

    private async Task<string> SourceAsync(string type = "salesforce")
    {
        var code = "src-" + Suffix();
        var response = await (await api.AdminClientAsync()).PostAsJsonAsync(Sources, new SourceSystemInput(code, "مصدر", $"Source {code}", type, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return code;
    }

    private static async Task<Guid> ItemAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameEn = name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!.Id;
    }

    private async Task<Result<ExternalReference>> LinkAsync(ExternalReferenceInput input)
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        return await scope.ServiceProvider.GetRequiredService<IExternalReferenceStore>().LinkAsync(input);
    }

    private static void ShouldFailWith<T>(Result<T> result, string messageKey)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error!.Fields!.Single().MessageKey.ShouldBe(messageKey);
    }

    [Fact]
    public async Task Source_systems_are_managed_with_the_manage_permission_validated_audited_and_never_deleted()
    {
        var admin = await api.AdminClientAsync();
        var code = await SourceAsync("sap");
        await (await admin.PostAsJsonAsync(Sources, new SourceSystemInput(code, null, "Again", "sap", null)))
            .ShouldBeValidationProblemAsync("code", "integration.errors.codeTaken");
        await (await admin.PostAsJsonAsync(Sources, new SourceSystemInput("Bad Code!", null, "Bad", "sap", null)))
            .ShouldBeValidationProblemAsync("code", "integration.errors.codeFormat");
        await (await admin.PostAsJsonAsync(Sources, new SourceSystemInput("ok-" + Suffix(), null, null, "nope", null)))
            .ShouldBeValidationProblemAsync("typeCode", "validation.unknownCode");

        // Everyone may list them (record pages name the source); only the manage permission changes them.
        var user = await api.UserClientAsync();
        var listed = (await user.GetFromJsonAsync<List<SourceSystemDto>>(Sources))!;
        var source = listed.Single(s => s.Code == code);
        await (await user.PostAsJsonAsync(Sources, new SourceSystemInput("x-" + Suffix(), null, "X", "sap", null))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        await (await user.PostAsJsonAsync($"{Sources}/{source.Id}/deactivate", new SourceSystemVersionInput(source.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        var deactivated = await admin.PostAsJsonAsync($"{Sources}/{source.Id}/deactivate", new SourceSystemVersionInput(source.Version));
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await deactivated.Content.ReadFromJsonAsync<SourceSystemDto>())!.Active.ShouldBeFalse();
        (await admin.GetFromJsonAsync<List<SourceSystemDto>>(Sources))!.ShouldNotContain(s => s.Code == code);
        (await admin.GetFromJsonAsync<List<SourceSystemDto>>($"{Sources}?includeInactive=true"))!.ShouldContain(s => s.Code == code && !s.Active);
        ShouldFailWith(await LinkAsync(new ExternalReferenceInput(code, "sample.item", "X1", Guid.CreateVersion7())), ExternalReferenceErrors.InactiveSource);

        await using var scope = api.Factory.Services.CreateAsyncScope();
        var audit = (await scope.ServiceProvider.GetRequiredService<IAuditQuery>()
            .HistoryAsync("integration.source_system", source.Id.ToString("D"), new PageRequest(1, 50))).Items;
        audit.ShouldContain(e => e.Action == "create");
        audit.ShouldContain(e => e.Field == "Active" && e.NewValue == "false");
    }

    [Fact]
    public async Task Both_uniqueness_rules_are_validation_errors_and_a_record_may_have_references_from_several_sources()
    {
        var salesforce = await SourceAsync();
        var sap = await SourceAsync("sap");
        var editor = await EditorAsync();
        var a = await ItemAsync(editor, "Ref A " + Suffix());
        var b = await ItemAsync(editor, "Ref B " + Suffix());

        (await LinkAsync(new ExternalReferenceInput(salesforce, "sample.item", "SF-1", a, IsOrigin: true))).IsSuccess.ShouldBeTrue();
        ShouldFailWith(await LinkAsync(new ExternalReferenceInput(salesforce, "sample.item", "SF-1", b)), ExternalReferenceErrors.ExternalIdTaken);
        ShouldFailWith(await LinkAsync(new ExternalReferenceInput(salesforce, "sample.item", "SF-2", a)), ExternalReferenceErrors.RecordAlreadyReferenced);
        ShouldFailWith(await LinkAsync(new ExternalReferenceInput("missing-" + Suffix(), "sample.item", "SF-3", a)), ExternalReferenceErrors.UnknownSource);
        ShouldFailWith(await LinkAsync(new ExternalReferenceInput(salesforce, "no.such-type", "SF-3", a)), ExternalReferenceErrors.UnknownType);

        // Same triple and record again: a refresh, not a duplicate; another source may reference the same record.
        var again = await LinkAsync(new ExternalReferenceInput(salesforce, "sample.item", "SF-1", a, SourceHash: "h2", SyncStatus: SyncStatuses.Synced));
        again.IsSuccess.ShouldBeTrue();
        again.Value.IsOrigin.ShouldBeTrue("origin is never switched off by a refresh");
        again.Value.SourceHash.ShouldBe("h2");
        (await LinkAsync(new ExternalReferenceInput(sap, "sample.item", "MAT-9", a))).IsSuccess.ShouldBeTrue();

        await using var scope = TestAccess.SystemScope(api.Factory);
        var store = scope.ServiceProvider.GetRequiredService<IExternalReferenceStore>();
        (await store.ListForRecordAsync("sample.item", a)).Select(r => r.SourceSystem).ShouldBe([salesforce, sap], ignoreOrder: true);
        (await store.FindByExternalIdAsync("sf-1")).ShouldContain(m => m.RecordId == a && m.SourceSystem == salesforce);

        // The reference rows are in the audit trail.
        var reference = await store.FindAsync(salesforce, "sample.item", "SF-1");
        var audit = (await scope.ServiceProvider.GetRequiredService<IAuditQuery>()
            .HistoryAsync("integration.external_ref", reference!.Id.ToString("D"), new PageRequest(1, 50))).Items;
        audit.ShouldContain(e => e.Action == "create");
    }

    [Fact]
    public async Task A_concurrent_writer_that_passes_the_check_hits_the_unique_index_and_gets_the_same_validation_error()
    {
        var source = await SourceAsync();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await using var holder = TestAccess.SystemScope(api.Factory);
        var db = holder.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await holder.ServiceProvider.GetRequiredService<IExternalReferenceStore>()
            .LinkAsync(new ExternalReferenceInput(source, "sample.item", "RACE-1", first), db)).IsSuccess.ShouldBeTrue();

        // The second writer does not see the uncommitted row, passes the check and waits on the index until the first commits.
        var racing = LinkAsync(new ExternalReferenceInput(source, "sample.item", "RACE-1", second));
        await Task.Delay(500);
        racing.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync();
        ShouldFailWith(await racing, ExternalReferenceErrors.ExternalIdTaken);

        // Same for the other index: the record already has an external ID from that source.
        var third = Guid.CreateVersion7();
        await using var holder2 = TestAccess.SystemScope(api.Factory);
        var db2 = holder2.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        await using var transaction2 = await db2.Database.BeginTransactionAsync();
        (await holder2.ServiceProvider.GetRequiredService<IExternalReferenceStore>()
            .LinkAsync(new ExternalReferenceInput(source, "sample.item", "RACE-2", third), db2)).IsSuccess.ShouldBeTrue();
        var racing2 = LinkAsync(new ExternalReferenceInput(source, "sample.item", "RACE-3", third));
        await Task.Delay(500);
        await transaction2.CommitAsync();
        ShouldFailWith(await racing2, ExternalReferenceErrors.RecordAlreadyReferenced);
    }

    [Fact]
    public async Task The_record_page_lists_references_only_for_a_record_the_user_may_read()
    {
        var source = await SourceAsync();
        var admin = await EditorAsync();
        var item = await ItemAsync(admin, "Page " + Suffix());
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", "PAGE-1", item, IsOrigin: true))).IsSuccess.ShouldBeTrue();

        var refs = (await admin.GetFromJsonAsync<RecordReferencesDto>($"/api/v1/integration/references/sample.item/{item}"))!;
        refs.References.Single().ExternalId.ShouldBe("PAGE-1");
        refs.Origin!.SourceSystem.ShouldBe(source);
        refs.Origin.SourceNameEn.ShouldBe($"Source {source}");

        var own = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own));
        var outsider = await api.Factory.CreateUserAsync(roles: own);
        await api.Factory.GrantSystemAccessAsync("sample", "user", outsider.Id);
        var client = await api.Factory.SignInAsync(outsider);
        await (await client.GetAsync($"/api/v1/integration/references/sample.item/{item}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        await (await admin.GetAsync($"/api/v1/integration/references/no.such-type/{item}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
    }

    [Fact]
    public async Task Put_by_external_id_creates_then_updates_the_same_record_and_get_returns_the_module_dto()
    {
        var source = await SourceAsync();
        var marker = Suffix();
        var editor = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var url = $"{Items}/by-external/{source}/ACC-{marker}";

        var created = await editor.PutAsJsonAsync(url, new { nameEn = $"Upsert {marker}", amount = 10m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var item = (await created.Content.ReadFromJsonAsync<SampleItemDto>())!;

        var updated = await editor.PutAsJsonAsync(url, new { nameEn = $"Upsert {marker} renamed", amount = 12m });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<SampleItemDto>())!.Id.ShouldBe(item.Id);
        (await editor.PutAsJsonAsync(url, new { nameEn = $"Upsert {marker} renamed", amount = 12m })).StatusCode.ShouldBe(HttpStatusCode.OK, "repeating is idempotent");

        var got = (await editor.GetFromJsonAsync<SampleItemDto>(url))!;
        (got.Id, got.NameEn).ShouldBe((item.Id, $"Upsert {marker} renamed"));
        var list = (await editor.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search={marker}"))!;
        list.TotalCount.ShouldBe(1, "no duplicate record");
        var refs = (await editor.GetFromJsonAsync<RecordReferencesDto>($"/api/v1/integration/references/sample.item/{item.Id}"))!;
        refs.Origin!.ExternalId.ShouldBe($"ACC-{marker}");

        await (await editor.GetAsync($"{Items}/by-external/{source}/missing-{marker}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        await (await editor.PutAsJsonAsync($"{Items}/by-external/nosuch-{marker}/X", new { nameEn = "x" }))
            .ShouldBeValidationProblemAsync("source", ExternalReferenceErrors.UnknownSource);
    }

    [Fact]
    public async Task By_external_endpoints_keep_the_module_rules_out_of_scope_is_404_missing_permission_is_403_and_vendors_are_refused()
    {
        var source = await SourceAsync();
        var marker = Suffix();
        var admin = await EditorAsync();
        var hidden = await ItemAsync(admin, $"Hidden {marker}");
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", $"HID-{marker}", hidden))).IsSuccess.ShouldBeTrue();

        var own = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Update, DataScope.Own));
        var outsider = await api.Factory.CreateUserAsync(roles: own);
        await api.Factory.GrantSystemAccessAsync("sample", "user", outsider.Id);
        var client = await api.Factory.SignInAsync(outsider);
        await (await client.GetAsync($"{Items}/by-external/{source}/HID-{marker}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        await (await client.PutAsJsonAsync($"{Items}/by-external/{source}/HID-{marker}", new { nameEn = "Taken over" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        (await admin.GetFromJsonAsync<SampleItemDto>($"{Items}/{hidden}"))!.NameEn.ShouldBe($"Hidden {marker}");

        // No create permission: a new external ID is refused (403) and nothing is created.
        await (await client.PutAsJsonAsync($"{Items}/by-external/{source}/NEW-{marker}", new { nameEn = $"New {marker}" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        (await admin.GetFromJsonAsync<PagedResult<SampleItemDto>>($"{Items}?search=New {marker}"))!.TotalCount.ShouldBe(0);

        // Vendor principals never reach internal endpoints.
        var vendor = await api.Factory.CreateUserAsync(withEmployee: false);
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
            var entity = (await users.FindByIdAsync(vendor.Id.ToString()))!;
            entity.PrincipalType = StrategyClaims.VendorPrincipal;
            (await users.UpdateAsync(entity)).Succeeded.ShouldBeTrue();
        }

        var token = await TokenAsync(vendor.Id, TokenScopes.ApiWrite);
        var vendorClient = api.Factory.CreateTokenClient(token);
        await (await vendorClient.GetAsync($"{Items}/by-external/{source}/HID-{marker}")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.principalRefused");
        await (await vendorClient.GetAsync($"/api/v1/integration/references/sample.item/{hidden}")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.principalRefused");
    }

    [Fact]
    public async Task Unit_and_employee_imports_upsert_by_external_id_and_a_rerun_creates_no_duplicates()
    {
        var source = await SourceAsync();
        var admin = await api.AdminClientAsync();
        var s = Suffix().ToUpperInvariant();
        var units = Csv(
            "code,parent_code,name_ar,name_en,level_code,source_system,external_id",
            $"X{s}-ROOT,,جذر,Root {s},organisation,{source},U-{s}-1",
            $"X{s}-DEP,X{s}-ROOT,إدارة,Department {s},department,{source},U-{s}-2");
        var first = await ImportAsync(admin, "units", "commit", units);
        (first.Committed, first.Created, first.Updated, first.Issues.Count).ShouldBe((true, 2, 0, 0));

        // Re-run with a renamed unit and even a changed code: the reference wins, the record is updated, nothing new is created.
        var rerun = Csv(
            "code,parent_code,name_ar,name_en,level_code,source_system,external_id",
            $"X{s}-ROOT,,جذر,Root {s} renamed,organisation,{source},U-{s}-1",
            $"X{s}-DEPT,X{s}-ROOT,إدارة,Department {s},department,{source},U-{s}-2");
        var second = await ImportAsync(admin, "units", "commit", rerun);
        (second.Committed, second.Created, second.Updated, second.Issues.Count).ShouldBe((true, 0, 2, 0), string.Join(", ", second.Issues.Select(i => i.Code)));
        var tree = (await admin.GetFromJsonAsync<List<OrgTreeNodeDto>>("/api/v1/org/units/tree"))!;
        tree.Count(n => n.Code.StartsWith($"X{s}-", StringComparison.Ordinal)).ShouldBe(2);
        tree.ShouldContain(n => n.Code == $"X{s}-DEPT");
        var root = tree.Single(n => n.Code == $"X{s}-ROOT");
        (await admin.GetFromJsonAsync<OrgUnitDto>($"/api/v1/org/units/by-external/{source}/U-{s}-1"))!.Id.ShouldBe(root.Id);
        (await admin.GetFromJsonAsync<RecordReferencesDto>($"/api/v1/integration/references/org.unit/{root.Id}"))!.Origin!.ExternalId.ShouldBe($"U-{s}-1");

        var employees = Csv(
            "employee_no,name_en,email,unit_code,source_system,external_id",
            $"X{s}-1,Staff {s},staff.{s.ToLowerInvariant()}@example.test,X{s}-DEPT,{source},E-{s}-1");
        (await ImportAsync(admin, "employees", "commit", employees)).Created.ShouldBe(1);
        var again = await ImportAsync(admin, "employees", "commit", employees.Replace($"Staff {s}", $"Staff {s} renamed", StringComparison.Ordinal));
        (again.Created, again.Updated).ShouldBe((0, 1));
        (await admin.GetFromJsonAsync<EmployeeDto>($"/api/v1/org/employees/by-external/{source}/E-{s}-1"))!.NameEn.ShouldBe($"Staff {s} renamed");
    }

    [Fact]
    public async Task Import_validation_reports_unknown_or_inactive_sources_duplicates_in_the_file_and_ids_linked_elsewhere()
    {
        var source = await SourceAsync();
        var inactive = await SourceAsync();
        var admin = await api.AdminClientAsync();
        var current = (await admin.GetFromJsonAsync<List<SourceSystemDto>>(Sources))!.Single(x => x.Code == inactive);
        (await admin.PostAsJsonAsync($"{Sources}/{current.Id}/deactivate", new SourceSystemVersionInput(current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var s = Suffix().ToUpperInvariant();
        (await ImportAsync(admin, "units", "commit", Csv("code,name_en,level_code,source_system,external_id",
            $"V{s}-A,A,organisation,{source},TAKEN-{s}", $"V{s}-Z,Z,organisation,,"))).Committed.ShouldBeTrue();

        var report = await ImportAsync(admin, "units", "validate", Csv(
            "code,name_en,level_code,source_system,external_id",
            $"V{s}-B,B,organisation,nosuch-{s},X1",
            $"V{s}-C,C,organisation,{inactive},X2",
            $"V{s}-D,D,organisation,{source},DUP-{s}",
            $"V{s}-E,E,organisation,{source},DUP-{s}",
            $"V{s}-F,F,organisation,{source},",
            $"V{s}-Z,Z,organisation,{source},TAKEN-{s}"));
        report.Issues.Select(i => (i.Row, i.Column, i.MessageKey)).ShouldBe(
        [
            (2, "source_system", ExternalReferenceErrors.UnknownSource),
            (3, "source_system", ExternalReferenceErrors.InactiveSource),
            (5, "external_id", ExternalReferenceErrors.DuplicateInFile),
            (6, "external_id", ExternalReferenceErrors.ExternalIdRequired),
            (7, "code", ExternalReferenceErrors.KeyMismatch),
        ], ignoreOrder: true);

        // The existing unit matched by code cannot take another external ID of the same source.
        var other = await ImportAsync(admin, "units", "validate", Csv("code,name_en,level_code,source_system,external_id", $"V{s}-A,A,organisation,{source},OTHER-{s}"));
        other.Issues.Single().MessageKey.ShouldBe(ExternalReferenceErrors.RecordAlreadyReferenced);
    }

    [Fact]
    public async Task Holiday_import_updates_the_referenced_holiday_on_a_rerun()
    {
        var source = await SourceAsync();
        var admin = await api.AdminClientAsync();
        var s = Suffix();
        var file = Csv("name_en,start_date,source_system,external_id", $"Founders {s},2033-02-10,{source},H-{s}");
        var first = await HolidayImportAsync(admin, file);
        (first.Committed, first.Created, first.Updated).ShouldBe((true, 1, 0));
        var second = await HolidayImportAsync(admin, Csv("name_en,start_date,source_system,external_id", $"Founders day {s},2033-02-11,{source},H-{s}"));
        (second.Committed, second.Created, second.Updated, second.Issues.Count).ShouldBe((true, 0, 1, 0));
        var holidays = (await admin.GetFromJsonAsync<List<HolidayDto>>("/api/v1/calendar/holidays?year=2033"))!;
        holidays.Where(h => h.NameEn!.Contains(s, StringComparison.Ordinal)).Select(h => (h.NameEn, h.StartDate)).ShouldBe([($"Founders day {s}", new DateOnly(2033, 2, 11))]);
    }

    [Fact]
    public async Task Report_builder_datasets_offer_source_system_and_external_id_columns_filled_from_the_references()
    {
        var source = await SourceAsync();
        var marker = Suffix();
        var editor = await EditorAsync();
        var item = await ItemAsync(editor, $"Report {marker}");
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", $"RPT-{marker}", item))).IsSuccess.ShouldBeTrue();
        await ItemAsync(editor, $"Report {marker} without reference");

        var datasets = await editor.GetFromJsonAsync<JsonElement>("/api/v1/reporting/datasets");
        var fields = datasets.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "sample.items").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("code").GetString()).ToList();
        fields.ShouldContain("externalSource");
        fields.ShouldContain("externalId");

        var run = await editor.PostAsJsonAsync("/api/v1/reporting/reports/run", new
        {
            definition = new
            {
                dataset = "sample.items",
                columns = new[] { "nameEn", "externalSource", "externalId" },
                filters = new[] { new { field = "nameEn", @operator = "contains", values = new[] { marker } } },
                groupBy = Array.Empty<string>(),
                sort = new[] { new { field = "nameEn", descending = false } },
            },
            language = "en",
        });
        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        var rows = (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList()).ToList();
        rows.Count.ShouldBe(2);
        rows[0].ShouldBe([$"Report {marker}", source, $"RPT-{marker}"]);
        rows[1].ShouldBe([$"Report {marker} without reference", null, null]);

        // Not filterable: the module never sees these columns.
        var filtered = await editor.PostAsJsonAsync("/api/v1/reporting/reports/run", new
        {
            definition = new
            {
                dataset = "sample.items",
                columns = new[] { "nameEn" },
                filters = new[] { new { field = "externalId", @operator = "eq", values = new[] { "x" } } },
                groupBy = Array.Empty<string>(),
                sort = Array.Empty<object>(),
            },
            language = "en",
        });
        filtered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_mcp_tool_finds_a_record_by_external_reference_only_when_the_token_owner_may_read_it()
    {
        var admin = await api.AdminClientAsync();
        var current = (await admin.GetFromJsonAsync<McpSettingsDto>("/api/v1/mcp/settings"))!;
        (await admin.PutAsJsonAsync("/api/v1/mcp/settings", new McpSettingsInput(current.Settings with { Enabled = true }, current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var source = await SourceAsync();
        var marker = Suffix();
        var own = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var alice = await api.Factory.CreateUserAsync(roles: own);
        await api.Factory.GrantSystemAccessAsync("sample", "user", alice.Id);
        var mine = await ItemAsync(await api.Factory.SignInAsync(alice), $"Alice {marker}");
        var theirs = await ItemAsync(await EditorAsync(), $"Admin {marker}");
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", $"M-{marker}", mine))).IsSuccess.ShouldBeTrue();
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", $"T-{marker}", theirs))).IsSuccess.ShouldBeTrue();

        await using var client = await McpAsync(await TokenAsync(alice.Id, TokenScopes.Mcp));
        (await client.ListToolsAsync()).Select(t => t.Name).ShouldContain("find_by_external_reference");
        var found = await client.CallToolAsync("find_by_external_reference",
            new Dictionary<string, object?> { ["source"] = source, ["type"] = "sample.item", ["externalId"] = $"M-{marker}" });
        found.IsError.ShouldNotBe(true, Text(found));
        var data = JsonDocument.Parse(Text(found)).RootElement;
        data.GetProperty("id").GetGuid().ShouldBe(mine);
        data.GetProperty("link").GetString().ShouldBe($"/sample/items/{mine:D}");

        var hidden = await client.CallToolAsync("find_by_external_reference",
            new Dictionary<string, object?> { ["source"] = source, ["type"] = "sample.item", ["externalId"] = $"T-{marker}" });
        hidden.IsError.ShouldBe(true);
        Text(hidden).ShouldContain("not_found");
    }

    private async Task<string> TokenAsync(Guid userId, params string[] scopes)
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        var created = await scope.ServiceProvider.GetRequiredService<AccessTokenService>().CreateAsync(userId, "external refs test", scopes, 1, CancellationToken.None);
        return created.Value.Token;
    }

    private async Task<McpClient> McpAsync(string token)
    {
        var http = api.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static async Task<ImportReportDto> ImportAsync(HttpClient client, string kind, string mode, string content)
    {
        var response = await client.PostAsync($"/api/v1/org/import/{kind}?mode={mode}", Form(content, $"{kind}.csv"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportReportDto>())!;
    }

    private static async Task<HolidayImportDto> HolidayImportAsync(HttpClient client, string content)
    {
        var response = await client.PostAsync("/api/v1/calendar/holidays/import?year=2033&mode=commit", Form(content, "holidays.csv"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonSerializer.Deserialize<HolidayImportDto>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
    }

    private static string Csv(params string[] lines) => string.Join("\r\n", lines);

    private static MultipartFormDataContent Form(string content, string fileName)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Global_search_finds_a_record_by_external_id_only_for_users_who_may_read_it()
    {
        var source = await SourceAsync();
        var own = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var alice = await api.Factory.SignInAsync(await api.Factory.CreateUserAsync(roles: own));
        var bob = await api.Factory.SignInAsync(await api.Factory.CreateUserAsync(roles: own));
        var mine = await ItemAsync(alice, "Ext search A " + Suffix());
        var theirs = await ItemAsync(bob, "Ext search B " + Suffix());
        var mineId = "EXT-" + Suffix();
        var theirsId = "EXT-" + Suffix();
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", mineId, mine))).IsSuccess.ShouldBeTrue();
        (await LinkAsync(new ExternalReferenceInput(source, "sample.item", theirsId, theirs))).IsSuccess.ShouldBeTrue();

        var found = await SearchIdsAsync(alice, mineId);
        found.ShouldContain((mine, "externalId"));
        (await SearchIdsAsync(alice, theirsId)).ShouldBeEmpty("an external ID never reveals a record outside the user's scope");
        (await SearchIdsAsync(bob, theirsId)).ShouldContain((theirs, "externalId"));
    }

    private static async Task<List<(Guid Id, string? MatchedOn)>> SearchIdsAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(term)}&limit=50");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray())
            .Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("matchedOn").GetString())).ToList();
    }
}
