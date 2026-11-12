using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Integration;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Integration.Sync;

/// <summary>
/// Phase 23 — Salesforce standby sync against a fake Salesforce (docs/design/salesforce-sync.md §14, ADR 0200–0208): connection and key,
/// validation against the live describe, full load with Bulk API 2.0 and incremental REST runs, idempotent re-runs, deletions, exceptions
/// queue with fixes and re-processing, parent-child order, files and approval history, standby read-only on every entry point, failover with the
/// MFA re-prompt, the outage changes report, return to standby with conflict choices, the schedule on the fake clock and alerts.
/// </summary>
public sealed class SalesforceSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Sync = "/api/v1/integration/sync";
    private readonly FakeSalesforce _fake = new();
    private readonly TravellingClock _clock = new();
    private ApiFactory _factory = null!;
    private TestUser _adminUser = null!;
    private HttpClient _admin = null!;
    private Guid _connection;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(_clock);
            services.RemoveAll<ISalesforceTransport>();
            services.AddSingleton<ISalesforceTransport>(_fake);
            // A test-only target on the template module: the same generic writer every module uses (ADR 0202).
            services.AddSyncTarget(SyncTarget.Entity<SampleDbContext, SampleItem>(SampleItem.EntityType, "sample", "عنصر", "Item")
                .Text("number", nameof(SampleItem.Number)).Text("nameEn", nameof(SampleItem.NameEn)).Text("nameAr", nameof(SampleItem.NameAr))
                .Choice("status", nameof(SampleItem.Status), SampleItemStatus.All, required: true)
                .Person("ownerEmployeeId", nameof(SampleItem.OwnerEmployeeId)).Unit("unitId", nameof(SampleItem.UnitId))
                .Number("amount", nameof(SampleItem.Amount)));
            services.AddAppTool<RenameItemTool>();
        });
        _adminUser = await _factory.CreateUserAsync(admin: true);
        var sampleManager = await _factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.All), (SamplePermissions.Update, DataScope.All),
            (SamplePermissions.Submit, DataScope.All));
        await _factory.GrantRoleAsync(_adminUser.Id, sampleManager);
        _admin = await _factory.SignInAsync(_adminUser);
        _connection = (await _admin.GetFromJsonAsync<List<SyncConnectionDto>>($"{Sync}/connections"))!.Single(c => c.SourceSystem == "salesforce").Id;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    // ------------------------------------------------------------------ fake org and pack

    private static readonly string[] Standard = ["Id", "IsDeleted", "SystemModstamp", "CreatedDate", "Name", "CreatedById"];

    /// <summary>Fake objects whose describe has exactly the fields the ADoF pack maps (plus the standard ones) and its picklist values.</summary>
    private async Task<MappingPackDocument> AdofPackAsync()
    {
        var packs = (await _admin.GetFromJsonAsync<List<MappingPackDto>>($"{Sync}/packs"))!;
        var adof = packs.Single(p => p.Code == "adof-salesforce");
        return (await _admin.GetFromJsonAsync<MappingPackDocument>($"{Sync}/packs/{adof.Id}/export", SyncJsonOptions))!;
    }

    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    private void FakeObjectFor(MappingPackObject mapping, params (string Name, string Type)[] extra)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Standard)
        {
            fields[f] = f is "SystemModstamp" or "CreatedDate" ? "datetime" : f == "IsDeleted" ? "boolean" : f.EndsWith("Id", StringComparison.Ordinal) ? "reference" : "string";
        }

        foreach (var f in mapping.Fields.Where(f => f.Kind != FieldMappingKinds.Constant && f.Source is not null && !f.Source.Contains('.', StringComparison.Ordinal)))
        {
            fields[f.Source!] = f.Kind is FieldMappingKinds.Reference or FieldMappingKinds.Person ? "reference" : f.Kind == FieldMappingKinds.Picklist ? "picklist" : "string";
        }

        foreach (var (name, type) in extra)
        {
            fields[name] = type;
        }

        var o = _fake.AddObject(mapping.SourceObject, [.. fields.Select(f => (f.Key, f.Value))]);
        foreach (var f in mapping.Fields.Where(f => f.Kind == FieldMappingKinds.Picklist && f.Values is { Count: > 0 }))
        {
            o.Picklist(f.Source!, [.. f.Values!.Keys.Where(k => k != "*")]);
        }
    }

    /// <summary>The test pack: the ADoF objects used here plus a sample item object, imported through the API and set on the connection.</summary>
    private async Task<Guid> ConfigureAsync(params string[] adofKeys)
    {
        var adof = await AdofPackAsync();
        var objects = adof.Objects.Where(o => adofKeys.Contains(o.Key)).Select(o => o with { Provisional = false }).ToList();
        objects.Add(new MappingPackObject
        {
            Key = "item", SourceObject = "Sample_Item__c", Kind = ObjectMappingKinds.Record, TargetType = SampleItem.EntityType, LoadOrder = 60,
            Fields =
            [
                new FieldMapping { Source = "Name", Target = "number", Kind = FieldMappingKinds.Direct },
                new FieldMapping { Source = "Title__c", Target = "nameEn", Kind = FieldMappingKinds.Direct, Transform = "trim" },
                new FieldMapping { Source = "Title_Ar__c", Target = "nameAr", Kind = FieldMappingKinds.Direct },
                new FieldMapping { Source = "Status__c", Target = "status", Kind = FieldMappingKinds.Picklist, Values = new() { ["Open"] = "active", ["Closed"] = "inactive" } },
                new FieldMapping { Source = "Owner__c", Target = "ownerEmployeeId", Kind = FieldMappingKinds.Person },
                new FieldMapping { Source = "Department__c", Target = "unitId", Kind = FieldMappingKinds.Reference, ReferenceType = "org.unit" },
                new FieldMapping { Source = "Amount__c", Target = "amount", Kind = FieldMappingKinds.Direct },
            ],
        });
        foreach (var o in objects.Where(o => o.Kind is ObjectMappingKinds.Record or ObjectMappingKinds.People))
        {
            FakeObjectFor(o, o.Kind == ObjectMappingKinds.People ? [("Email", "email"), ("EmployeeNumber", "string"), ("FederationIdentifier", "string")] : []);
        }

        _fake.Objects["Department__c"].Reference("Parent_Department__c", "Department__c", "Parent_Department__r");
        _fake.AddObject("ContentVersion", ("Id", "id"), ("ContentDocumentId", "reference"), ("Title", "string"), ("PathOnClient", "string"), ("FileExtension", "string"),
            ("IsLatest", "boolean"), ("SystemModstamp", "datetime"), ("IsDeleted", "boolean"));
        _fake.AddObject("ContentDocumentLink", ("Id", "id"), ("ContentDocumentId", "reference"), ("LinkedEntityId", "reference"), ("SystemModstamp", "datetime"));
        _fake.AddObject("ProcessInstance", ("Id", "id"), ("TargetObjectId", "reference"), ("Status", "picklist"), ("CreatedDate", "datetime"), ("SystemModstamp", "datetime"));
        _fake.AddObject("ProcessInstanceStep", ("Id", "id"), ("ProcessInstanceId", "reference"), ("StepStatus", "picklist"), ("Comments", "textarea"),
            ("CreatedDate", "datetime"));

        var import = await _admin.PostAsJsonAsync($"{Sync}/packs/import", adof with { Code = "tests-pack", Objects = objects });
        import.StatusCode.ShouldBe(HttpStatusCode.OK, await import.Content.ReadAsStringAsync());
        var pack = (await import.Content.ReadFromJsonAsync<MappingPackDto>())!;

        var connection = (await _admin.GetFromJsonAsync<SyncConnectionDto>($"{Sync}/connections/{_connection}"))!;
        (await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}/key", new SyncKeyInput("not a key"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}/key", new SyncKeyInput(_fake.PrivateKeyPem))).StatusCode.ShouldBe(HttpStatusCode.OK);
        connection = (await _admin.GetFromJsonAsync<SyncConnectionDto>($"{Sync}/connections/{_connection}"))!;
        var updated = await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}",
            new SyncConnectionInput(FakeSalesforce.LoginUrl, _fake.ClientId, _fake.Username, FakeSalesforce.ApiVersion, 2, 200, pack.Id, connection.Version));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var tested = (await (await _admin.PostAsync($"{Sync}/connections/{_connection}/test", null)).Content.ReadFromJsonAsync<SyncConnectionDto>())!;
        tested.LastTest!.Ok.ShouldBeTrue(tested.LastTest.MessageKey);
        tested.KeyStored.ShouldBeTrue();
        tested.KeyThumbprint.ShouldNotBeNullOrEmpty();
        tested.InstanceUrl.ShouldBe(FakeSalesforce.InstanceUrl);

        foreach (var mapping in (await _admin.GetFromJsonAsync<List<ObjectMappingDto>>($"{Sync}/packs/{pack.Id}/mappings"))!)
        {
            var validated = (await (await _admin.PostAsync($"{Sync}/connections/{_connection}/mappings/{mapping.Id}/validate", null)).Content
                .ReadFromJsonAsync<MappingValidationDto>())!;
            validated.Ok.ShouldBeTrue(string.Join("; ", validated.Issues.Select(i => i.MessageKey + " " + JsonSerializer.Serialize(i.Params))));
            var current = (await _admin.GetFromJsonAsync<ObjectMappingDto>($"{Sync}/mappings/{mapping.Id}"))!;
            (await _admin.PostAsJsonAsync($"{Sync}/mappings/{mapping.Id}/enabled", new ObjectMappingEnableInput(true, current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await _admin.PostAsync($"{Sync}/connections/{_connection}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return pack.Id;
    }

    private async Task<Guid> UnitAsync(string code)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var unit = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new Strategy.Platform.Organisation.Application.CreateUnit(code, "وحدة", "Unit " + code, "organisation", null, null, null, null, null));
        return unit.Value.Id;
    }

    private async Task<SyncRun> RunAsync(bool full = false)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var run = await scope.ServiceProvider.GetRequiredService<SyncEngine>().RunAsync(_connection, full ? RunTriggers.Full : RunTriggers.Manual, full, null, null, CancellationToken.None);
        run.ShouldNotBeNull();
        return run;
    }

    private static ObjectRunStats Stats(SyncRun run, string key) => JsonSerializer.Deserialize<Dictionary<string, ObjectRunStats>>(run.Objects, SyncJsonOptions)![key];

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        return await work(scope.ServiceProvider);
    }

    private Task<Guid?> RecordOfAsync(string type, string externalId) => InScopeAsync(async sp =>
        (await sp.GetRequiredService<IExternalReferenceStore>().FindAsync("salesforce", type, externalId))?.RecordId);

    private Task<SampleItem?> ItemAsync(Guid id) => InScopeAsync(async sp => await sp.GetRequiredService<SampleDbContext>().Set<SampleItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id));

    /// <summary>Two departments (child listed before its parent: multi-pass), a matched user and two items.</summary>
    private async Task<(string Parent, string Child, string UserId, string ItemA, string ItemB, TestUser Employee)> SeedOrgAsync()
    {
        var employee = await _factory.CreateUserAsync();
        var email = employee.UserName;
        var departments = _fake.Objects["Department__c"];
        var parent = _fake.NewId("a01");
        var child = _fake.NewId("a01");
        departments.Upsert(child, new() { ["Name"] = "قسم الأنظمة", ["Department_Name_English__c"] = "Systems section", ["Short_Code__c"] = "SYS" + parent[^6..], ["Record_Type__c"] = "Section",
            ["Parent_Department__c"] = parent });
        departments.Upsert(parent, new() { ["Name"] = "إدارة التقنية", ["Department_Name_English__c"] = "IT department", ["Short_Code__c"] = "IT" + parent[^6..], ["Record_Type__c"] = "Department" });
        var userId = _fake.NewId("005");
        _fake.Objects["User"].Upsert(userId, new() { ["Name"] = "Test User", ["Email"] = email.ToUpperInvariant() });
        var items = _fake.Objects["Sample_Item__c"];
        var item1 = _fake.NewId("a0X");
        var item2 = _fake.NewId("a0X");
        items.Upsert(item1, new() { ["Name"] = "ITEM-1", ["Title__c"] = " First synced item ", ["Status__c"] = "Open", ["Owner__c"] = userId, ["Department__c"] = child, ["Amount__c"] = 12.5m });
        items.Upsert(item2, new() { ["Name"] = "ITEM-2", ["Title__c"] = "Second", ["Status__c"] = "Closed", ["Department__c"] = parent });
        return (parent, child, userId, item1, item2, employee);
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task Connection_validation_and_permissions_guard_the_configuration()
    {
        var pack = await ConfigureAsync("department", "user");
        var status = (await _admin.GetFromJsonAsync<SyncStatusDto>($"{Sync}/connections/{_connection}/status"))!;
        status.Connection.Mode.ShouldBe(SyncModes.Standby);
        status.Objects.Where(o => o.Enabled).Select(o => o.Key).ShouldBe(["department", "user", "item"], ignoreOrder: true);
        _fake.Requests.ShouldContain(r => r.StartsWith("GET /services/data/v62.0/sobjects/Department__c/describe", StringComparison.Ordinal));
        _fake.TokenRequests.ShouldBeGreaterThan(0);

        // An edit clears the confirmation and disables the object; enabling an unconfirmed object is refused.
        var item = (await _admin.GetFromJsonAsync<List<ObjectMappingDto>>($"{Sync}/packs/{pack}/mappings"))!.Single(m => m.Key == "item");
        var edited = await _admin.PutAsJsonAsync($"{Sync}/mappings/{item.Id}", new ObjectMappingInput(item.TargetType, item.LoadOrder, "Title__c != null", item.WatermarkField,
            item.ExternalIdTemplate, item.UseBulk, [.. item.Fields, new FieldMapping { Source = "Missing__c", Target = "nameAr" }], null, null, null, null, null, item.Version));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var after = (await edited.Content.ReadFromJsonAsync<ObjectMappingDto>())!;
        after.ConfirmedAt.ShouldBeNull();
        after.Enabled.ShouldBeFalse();
        await (await _admin.PostAsJsonAsync($"{Sync}/mappings/{item.Id}/enabled", new ObjectMappingEnableInput(true, after.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "integration.sync.notConfirmed");
        var invalid = (await (await _admin.PostAsync($"{Sync}/connections/{_connection}/mappings/{item.Id}/validate", null)).Content.ReadFromJsonAsync<MappingValidationDto>())!;
        invalid.Ok.ShouldBeFalse();
        invalid.Issues.ShouldContain(i => i.MessageKey == "integration.sync.validation.unknownField" && i.Params!["field"] == "Missing__c");

        // Unsafe filters are refused; the live describe is listed.
        (await _admin.PutAsJsonAsync($"{Sync}/mappings/{item.Id}", new ObjectMappingInput(item.TargetType, 1, "Name = 'x'; DELETE", "SystemModstamp", "{Id}", true, item.Fields,
            null, null, null, null, null, after.Version))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var objects = (await _admin.GetFromJsonAsync<List<SourceObjectDto>>($"{Sync}/connections/{_connection}/source/objects"))!;
        objects.ShouldContain(o => o.Name == "Department__c");

        // Schedule: weekly needs days; the preview lists the next runs.
        (await _admin.GetFromJsonAsync<SyncScheduleDto>($"{Sync}/connections/{_connection}/schedule"))!.TimeZone.ShouldNotBeNullOrEmpty();
        var version = (await _admin.GetFromJsonAsync<SyncConnectionDto>($"{Sync}/connections/{_connection}"))!.Version;
        (await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}/schedule", new SyncScheduleInput(new SyncScheduleSettings { Kind = "weekly", Days = [] }, null, version)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var saved = await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}/schedule",
            new SyncScheduleInput(new SyncScheduleSettings { Kind = "daily", At = "02:30" }, 180, version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync<SyncScheduleDto>())!.NextRuns.Count.ShouldBe(5);

        // Permissions: a signed-in user without the sync roles is refused; the private key is never returned.
        var user = await _factory.CreateSignedInClientAsync();
        (await user.GetAsync($"{Sync}/connections")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync($"{Sync}/connections/{_connection}/make-primary", new MakePrimaryInput("x", "totp", "000000"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _admin.GetStringAsync($"{Sync}/connections/{_connection}")).ShouldNotContain("PRIVATE KEY");
        _fake.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Full_load_and_incremental_runs_are_idempotent_pick_only_changes_and_mark_deletions()
    {
        await ConfigureAsync("department", "user");
        var seed = await SeedOrgAsync();

        var first = await RunAsync();
        first.Status.ShouldBe(RunStatuses.Succeeded, first.MessageKey);
        Stats(first, "department").Created.ShouldBe(2); // the child waited for its parent (multi-pass)
        Stats(first, "department").Api.ShouldBe("bulk"); // initial load: Bulk API 2.0
        Stats(first, "user").Created.ShouldBe(1);
        Stats(first, "item").Created.ShouldBe(2);
        _fake.Requests.ShouldContain(r => r == "POST /services/data/v62.0/jobs/query");

        var parentUnit = (await RecordOfAsync("org.unit", seed.Parent))!.Value;
        var childUnit = (await RecordOfAsync("org.unit", seed.Child))!.Value;
        var units = await InScopeAsync(async sp => await sp.GetRequiredService<IOrgDirectory>().GetUnitAsync(childUnit));
        units!.ParentId.ShouldBe(parentUnit);
        var itemId = (await RecordOfAsync(SampleItem.EntityType, seed.ItemA))!.Value;
        var item = (await ItemAsync(itemId))!;
        item.NameEn.ShouldBe("First synced item");
        item.Status.ShouldBe(SampleItemStatus.Active);
        item.OwnerEmployeeId.ShouldBe(seed.Employee.EmployeeId); // user → employee by e-mail (case-insensitive)
        item.UnitId.ShouldBe(childUnit);
        item.Amount.ShouldBe(12.5m);
        var references = await InScopeAsync(async sp => await sp.GetRequiredService<IExternalReferenceStore>().ListForRecordAsync(SampleItem.EntityType, itemId));
        references.Single().IsOrigin.ShouldBeTrue();

        // Re-run without changes: nothing created, nothing updated.
        var second = await RunAsync();
        Stats(second, "item").Created.ShouldBe(0);
        Stats(second, "item").Updated.ShouldBe(0);
        Stats(second, "department").Created.ShouldBe(0);
        (await InScopeAsync(async sp => await sp.GetRequiredService<SampleDbContext>().Set<SampleItem>().CountAsync(i => i.Number == "ITEM-1"))).ShouldBe(1);

        // A full load again is idempotent too.
        var full = await RunAsync(full: true);
        Stats(full, "item").Created.ShouldBe(0);
        Stats(full, "item").Unchanged.ShouldBe(2);

        // Incremental: only the changed and the new record are read (REST, small delta), then a deletion is marked, not removed.
        _fake.Objects["Sample_Item__c"].Upsert(seed.ItemA, new() { ["Title__c"] = "Renamed in Salesforce" });
        var item3 = _fake.NewId("a0X");
        _fake.Objects["Sample_Item__c"].Upsert(item3, new() { ["Name"] = "ITEM-3", ["Title__c"] = "Third", ["Status__c"] = "Open" });
        var third = await RunAsync();
        Stats(third, "item").Read.ShouldBe(3); // the two changes plus the last record before the watermark (5-minute overlap), unchanged
        Stats(third, "item").Unchanged.ShouldBe(1);
        Stats(third, "item").Updated.ShouldBe(1);
        Stats(third, "item").Created.ShouldBe(1);
        Stats(third, "item").Api.ShouldBe("rest");
        (await ItemAsync(itemId))!.NameEn.ShouldBe("Renamed in Salesforce");

        _fake.Objects["Sample_Item__c"].Delete(seed.ItemB);
        var fourth = await RunAsync();
        Stats(fourth, "item").Deleted.ShouldBe(1);
        var deletedId = (await RecordOfAsync(SampleItem.EntityType, seed.ItemB))!.Value;
        (await ItemAsync(deletedId)).ShouldNotBeNull();
        (await InScopeAsync(async sp => await sp.GetRequiredService<IExternalReferenceStore>().FindAsync("salesforce", SampleItem.EntityType, seed.ItemB)))!
            .SyncStatus.ShouldBe(SyncStatuses.DeletedInSource);
        var state = (await _admin.GetFromJsonAsync<RecordSyncStateDto>($"/api/v1/integration/references/{SampleItem.EntityType}/{deletedId}/sync-state"))!;
        state.DeletedInSource.ShouldBeTrue();

        // Runs are listed with their log; the connector never wrote to Salesforce.
        var runs = (await _admin.GetFromJsonAsync<PagedResult<SyncRunDto>>($"{Sync}/connections/{_connection}/runs"))!;
        runs.TotalCount.ShouldBe(5);
        var detail = (await _admin.GetFromJsonAsync<SyncRunDetailDto>($"{Sync}/connections/{_connection}/runs/{first.Id}"))!;
        detail.Log.ShouldContain(l => l.MessageKey == "integration.sync.log.objectDone" && l.ObjectKey == "item");
        _fake.Violations.ShouldBeEmpty();
        _fake.Requests.Where(r => r.StartsWith("POST", StringComparison.Ordinal))
            .ShouldAllBe(r => r == "POST /services/oauth2/token" || r == "POST /services/data/v62.0/jobs/query");
    }

    [Fact]
    public async Task Unmapped_values_and_unknown_users_go_to_the_exceptions_queue_and_reprocess_after_the_fix()
    {
        await ConfigureAsync("department", "user");
        var seed = await SeedOrgAsync();
        var archived = _fake.NewId("a0X");
        _fake.Objects["Sample_Item__c"].Upsert(archived, new() { ["Name"] = "ITEM-A", ["Title__c"] = "New picklist value", ["Status__c"] = "Archived" });
        var stranger = _fake.NewId("005");
        _fake.Objects["User"].Upsert(stranger, new() { ["Name"] = "Unknown Person", ["Email"] = "nobody@elsewhere.test" });
        var orphan = _fake.NewId("a0X");
        _fake.Objects["Sample_Item__c"].Upsert(orphan, new() { ["Name"] = "ITEM-U", ["Title__c"] = "Owned by a stranger", ["Status__c"] = "Open", ["Owner__c"] = stranger });

        var run = await RunAsync();
        run.Status.ShouldBe(RunStatuses.Partial);
        var open = (await _admin.GetFromJsonAsync<PagedResult<SyncExceptionDto>>($"{Sync}/connections/{_connection}/exceptions"))!;
        var unmapped = open.Items.Single(e => e.ExternalId == archived);
        unmapped.Reason.ShouldBe(ExceptionReasons.UnmappedValue);
        unmapped.Field.ShouldBe("status");
        unmapped.Value.ShouldBe("Archived");
        open.Items.Single(e => e.ExternalId == orphan).Reason.ShouldBe(ExceptionReasons.UnknownUser);
        open.Items.Single(e => e.ExternalId == stranger).Reason.ShouldBe(ExceptionReasons.UnknownUser); // the unmatched user itself
        (await RecordOfAsync(SampleItem.EntityType, archived)).ShouldBeNull();
        var detail = (await _admin.GetFromJsonAsync<SyncExceptionDetailDto>($"{Sync}/exceptions/{unmapped.Id}"))!;
        detail.Payload["Status__c"].ShouldBe("Archived");

        // Fix 1: map the value; the exception is re-processed and the record is created.
        var mapped = await _admin.PostAsJsonAsync($"{Sync}/exceptions/{unmapped.Id}/map-value", new MapValueInput(null, "inactive"));
        mapped.StatusCode.ShouldBe(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        (await mapped.Content.ReadFromJsonAsync<SyncExceptionDto>())!.Status.ShouldBe(ExceptionStatuses.Resolved);
        (await ItemAsync((await RecordOfAsync(SampleItem.EntityType, archived))!.Value))!.Status.ShouldBe(SampleItemStatus.Inactive);

        // Fix 2: match the unknown person by hand, then re-process the record that referenced them.
        var other = await _factory.CreateUserAsync();
        var person = open.Items.Single(e => e.ExternalId == orphan);
        (await _admin.PostAsJsonAsync($"{Sync}/exceptions/{person.Id}/map-person", new MapPersonInput(other.EmployeeId!.Value))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var orphanItem = (await ItemAsync((await RecordOfAsync(SampleItem.EntityType, orphan))!.Value))!;
        orphanItem.OwnerEmployeeId.ShouldBe(other.EmployeeId);

        // The next run keeps the manual match and the value map; the user's own exception is resolved by the manual match too.
        var next = await RunAsync(full: true);
        Stats(next, "item").Failed.ShouldBe(0);
        var reprocess = await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/exceptions/reprocess", new ReprocessInput());
        reprocess.StatusCode.ShouldBe(HttpStatusCode.OK);
        var remaining = (await _admin.GetFromJsonAsync<PagedResult<SyncExceptionDto>>($"{Sync}/connections/{_connection}/exceptions"))!;
        remaining.Items.Where(e => e.SourceObject == "Sample_Item__c").ShouldBeEmpty();
        _ = seed;
    }

    [Fact]
    public async Task Projects_load_parent_before_child_with_work_plan_files_and_approval_history()
    {
        await ConfigureAsync("department", "user", "program-request", "project", "work-plan", "files", "approval-history");
        var seed = await SeedOrgAsync();
        var request = _fake.NewId("a0R");
        _fake.Objects["PMS_Project_Request__c"].Upsert(request, new() { ["Name"] = "PR-0042", ["Status__c"] = "Approved", ["Program_Name__c"] = "منصة البيانات",
            ["Department__c"] = seed.Parent, ["CreatedById"] = seed.UserId, ["Does_it_require_Budget__c"] = "Yes", ["Program_Type__c"] = "Digital" });
        var project = _fake.NewId("a0P");
        _fake.Objects["PMS_Project__c"].Upsert(project, new() { ["Name"] = "PROJ-SPMO-2026-007", ["PMS_Project_Request__c"] = request, ["Project_Name__c"] = "منصة البيانات",
            ["Program_Classification__c"] = "Project", ["Project_Stage__c"] = "Planning", ["Project_Status__c"] = "Active", ["Approval_Status__c"] = "PM Office - Planning",
            ["Project_Manager__c"] = seed.UserId, ["Department_Concerned__c"] = seed.Parent, ["Total_Achieve__c"] = 35.5m, ["Operational_Themes__c"] = "Future Readiness;Quality of Life" });
        // Work plan: the task is listed before its header phase.
        var header = _fake.NewId("a0W");
        var task = _fake.NewId("a0W");
        _fake.Objects["PMS_Work_Plan__c"].Upsert(task, new() { ["Name"] = "1.1", ["PMS_Project__c"] = project, ["Header_Phase__c"] = header, ["Weight__c"] = 40m, ["Status__c"] = "Active" });
        _fake.Objects["PMS_Work_Plan__c"].Upsert(header, new() { ["Name"] = "1", ["PMS_Project__c"] = project, ["Is_this_header_task__c"] = true, ["Status__c"] = "Not Started" });
        var document = _fake.NewId("069");
        var version = _fake.NewId("068");
        _fake.Objects["ContentVersion"].Upsert(version, new() { ["ContentDocumentId"] = document, ["Title"] = "charter", ["PathOnClient"] = "charter.pdf",
            ["FileExtension"] = "pdf", ["IsLatest"] = true, ["__content"] = MinimalPdf() });
        _fake.Objects["ContentDocumentLink"].Upsert(_fake.NewId("06A"), new() { ["ContentDocumentId"] = document, ["LinkedEntityId"] = project });
        var instance = _fake.NewId("04g");
        _fake.Objects["ProcessInstance"].Upsert(instance, new() { ["TargetObjectId"] = project, ["Status"] = "Pending", ["ProcessDefinition.Name"] = "Planning approval" });
        _fake.Objects["ProcessInstanceStep"].Upsert(_fake.NewId("04h"), new() { ["ProcessInstanceId"] = instance, ["ProcessInstance.TargetObjectId"] = project,
            ["ProcessInstance.Status"] = "Pending", ["ProcessInstance.ProcessDefinition.Name"] = "Planning approval", ["StepStatus"] = "Started", ["Actor.Name"] = "Section manager",
            ["Comments"] = "Submitted for planning approval" });

        var run = await RunAsync();
        run.Status.ShouldBe(RunStatuses.Succeeded, run.MessageKey + " " + string.Join(", ",
            (await _admin.GetFromJsonAsync<PagedResult<SyncExceptionDto>>($"{Sync}/connections/{_connection}/exceptions"))!.Items.Select(e => $"{e.SourceObject} {e.Reason} {e.Field} {e.Value} {e.MessageKey}")));
        var projectId = (await RecordOfAsync("portfolio.project", project))!.Value;
        var requestId = (await RecordOfAsync("portfolio.request", request))!.Value;
        var stored = await InScopeAsync(async sp => await sp.GetRequiredService<PortfolioDbContext>().Projects.AsNoTracking().FirstAsync(p => p.Id == projectId));
        stored.Stage.ShouldBe("planning");
        stored.SubState.ShouldBe("planning-approval");
        stored.RequestId.ShouldBe(requestId);
        stored.PmEmployeeId.ShouldBe(seed.Employee.EmployeeId);
        stored.Performance.ShouldBe(0.355m);
        stored.OperationalThemes.ShouldContain("innovative");
        var headerId = (await RecordOfAsync("portfolio.milestone", header))!.Value;
        var taskId = (await RecordOfAsync("portfolio.milestone", task))!.Value;
        var taskRow = await InScopeAsync(async sp => await sp.GetRequiredService<PortfolioDbContext>().Milestones.AsNoTracking().FirstAsync(m => m.Id == taskId));
        taskRow.ParentId.ShouldBe(headerId);
        taskRow.Weight.ShouldBe(0.4m);

        // The project manager gets the record role, so scope-based access works for the synced project.
        var holders = await InScopeAsync(async sp => await sp.GetRequiredService<IRecordRoleService>().GetHoldersAsync("portfolio.project", projectId));
        holders.ShouldContain(h => h.RoleCode == "pm" && h.PrincipalId == seed.Employee.EmployeeId);

        // Files go through the Files service; approval history is read-only source history.
        var files = await InScopeAsync(async sp => await sp.GetRequiredService<IFileService>().ListAsync("portfolio.project", projectId.ToString()));
        files.ShouldContain(f => f.Name == "charter.pdf");
        var history = (await _admin.GetFromJsonAsync<List<SourceHistoryEntryDto>>($"/api/v1/integration/references/portfolio.project/{projectId}/source-history"))!;
        history.ShouldContain(h => h.StepStatus == "Started" && h.ActorName == "Section manager" && h.Comments == "Submitted for planning approval");
        history.ShouldContain(h => h.ProcessStatus == "Pending" && h.ProcessName == "Planning approval");
        (await _admin.GetFromJsonAsync<RecordSyncStateDto>($"/api/v1/integration/references/portfolio.project/{projectId}/sync-state"))!.SourceHistoryCount.ShouldBe(2);

        // A second run imports nothing twice (files by version, history by source id).
        var again = await RunAsync();
        Stats(again, "files").Created.ShouldBe(0);
        (await InScopeAsync(async sp => await sp.GetRequiredService<IFileService>().ListAsync("portfolio.project", projectId.ToString()))).Count.ShouldBe(files.Count);
        _fake.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Standby_makes_synced_records_read_only_on_api_approvals_mcp_import_and_suspends_jobs_and_notifications()
    {
        await ConfigureAsync("department", "user");
        var seed = await SeedOrgAsync();
        (await RunAsync()).Status.ShouldBe(RunStatuses.Succeeded);
        var itemId = (await RecordOfAsync(SampleItem.EntityType, seed.ItemA))!.Value;
        var unitId = (await RecordOfAsync("org.unit", seed.Parent))!.Value;
        var editor = await _factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        await _factory.GrantRoleAsync(seed.Employee.Id, TestIdentity.SampleEditor);
        var owner = await _factory.SignInAsync(seed.Employee); // the synced item's owner (matched from the Salesforce user)
        var dto = (await owner.GetFromJsonAsync<SampleItemDto>($"/api/v1/sample/items/{itemId}"))!;

        // REST API (UI uses the same endpoints): 409 with the read-only code; the badge says so.
        var update = await owner.PutAsJsonAsync($"/api/v1/sample/items/{itemId}", new { nameEn = "Edited here", version = dto.Version });
        await update.ShouldBeProblemAsync(HttpStatusCode.Conflict, StandbyErrors.Code);
        (await _admin.GetFromJsonAsync<RecordSyncStateDto>($"/api/v1/integration/references/{SampleItem.EntityType}/{itemId}/sync-state"))!.ReadOnly.ShouldBeTrue();

        // Approvals: submit is refused before anything is written.
        await (await owner.PostAsJsonAsync($"/api/v1/sample/items/{itemId}/submit", new { comment = "go" })).ShouldBeProblemAsync(HttpStatusCode.Conflict, StandbyErrors.Code);

        // MCP: a write tool on the synced record gets read_only.
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ToolExecutor>();
            var tool = scope.ServiceProvider.GetServices<IAppTool>().OfType<RenameItemTool>().Single();
            var principal = await Strategy.IntegrationTests.Reporting.ReportingEngineTests.PrincipalAsync(_factory, _adminUser.Id);
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.NewGuid().ToString("N"), Channels.Mcp, principal);
            var result = await catalog.InvokeAsync(tool, JsonDocument.Parse($$"""{"id":"{{itemId}}","name":"via mcp"}""").RootElement, new ToolContext(principal, Channels.Mcp, "en"),
                "test-client", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.ErrorCode.ShouldBe(ToolErrors.ReadOnly);
        }

        // Imports: an organisation import that updates a synced unit is refused.
        var unit = await InScopeAsync(async sp => await sp.GetRequiredService<IOrgDirectory>().GetUnitAsync(unitId));
        var csv = $"code,parent_code,name_ar,name_en,level_code\r\n{unit!.Code},,إدارة معدلة,Changed here,department";
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "units.csv");
            var imported = await _admin.PostAsync("/api/v1/org/import/units?mode=commit", form);
            imported.StatusCode.ShouldBe(HttpStatusCode.Conflict, await imported.Content.ReadAsStringAsync());
        }

        // Local-only records stay editable.
        var local = await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Local item" });
        local.StatusCode.ShouldBe(HttpStatusCode.Created);
        var localDto = (await local.Content.ReadFromJsonAsync<SampleItemDto>())!;
        (await editor.PutAsJsonAsync($"/api/v1/sample/items/{localDto.Id}", new { nameEn = "Local item 2", version = localDto.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Jobs of suspended systems are vetoed; notifications of suspended systems are not queued.
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var gate = scope.ServiceProvider.GetRequiredService<StandbyJobGate>();
            (await gate.IsSuspendedAsync("portfolio.monthly-reminders", CancellationToken.None)).ShouldBeTrue();
            (await gate.IsSuspendedAsync("integration.salesforce-sync", CancellationToken.None)).ShouldBeFalse();
            await SaveSyncSettingsAsync(scope.ServiceProvider, s => s with { SuspendedSystems = [.. SyncSettings.DefaultSuspendedSystems, "sample"] });
        }

        InvalidateGuard();
        var before = _factory.Emails.Sent.Count;
        await InScopeAsync(async sp =>
        {
            var sent = await sp.GetRequiredService<INotificationService>().SendAsync(new NotificationRequest(ApiFactory.TestEventTemplate.Key,
                [NotificationRecipient.User(_adminUser.Id)], new Dictionary<string, string> { ["subject"] = "x" }));
            sent.IsSuccess.ShouldBeTrue();
            return true;
        });
        await InScopeAsync(async sp => await sp.GetRequiredService<Strategy.Platform.Scheduling.Application.OutboxDispatcher>().DispatchDueAsync(CancellationToken.None));
        _factory.Emails.Sent.Count.ShouldBe(before);
    }

    private void InvalidateGuard() => _factory.Services.GetRequiredService<SyncStandbyGuard>().Invalidate();

    private static async Task SaveSyncSettingsAsync(IServiceProvider sp, Func<SyncSettings, SyncSettings> change)
    {
        var editor = sp.GetRequiredService<ISettingsEditor>();
        var current = await editor.GetDocumentAsync<SyncSettings>();
        (await editor.SaveAsync(change(current.Value), current.Version, _ => [])).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Failover_unlocks_records_reports_every_change_and_return_to_standby_honours_the_choices()
    {
        await ConfigureAsync("department", "user", "program-request", "project", "approval-history");
        var seed = await SeedOrgAsync();
        var request = _fake.NewId("a0R");
        _fake.Objects["PMS_Project_Request__c"].Upsert(request, new() { ["Name"] = "PR-0099", ["Status__c"] = "Approved", ["Program_Name__c"] = "طلب" });
        var project = _fake.NewId("a0P");
        _fake.Objects["PMS_Project__c"].Upsert(project, new() { ["Name"] = "PROJ-SPMO-2026-099", ["PMS_Project_Request__c"] = request, ["Project_Stage__c"] = "Planning",
            ["Project_Status__c"] = "Active", ["Approval_Status__c"] = "PM Office - Planning" });
        var pending = _fake.NewId("04g");
        _fake.Objects["ProcessInstance"].Upsert(pending, new() { ["TargetObjectId"] = project, ["Status"] = "Pending", ["ProcessDefinition.Name"] = "Planning approval" });
        (await RunAsync()).Status.ShouldBe(RunStatuses.Succeeded);
        var itemA = (await RecordOfAsync(SampleItem.EntityType, seed.ItemA))!.Value;
        var itemB = (await RecordOfAsync(SampleItem.EntityType, seed.ItemB))!.Value;
        var projectId = (await RecordOfAsync("portfolio.project", project))!.Value;

        // Make primary: reason and MFA re-prompt required; a wrong code is refused.
        await (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/make-primary", new MakePrimaryInput("Salesforce outage", "totp", "000000")))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        await (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/make-primary", new MakePrimaryInput("", "totp", await Totp.NextCodeAsync(_adminUser.TotpKey!))))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var primary = await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/make-primary",
            new MakePrimaryInput("Salesforce unavailable since 08:00", "totp", await Totp.NextCodeAsync(_adminUser.TotpKey!)));
        primary.StatusCode.ShouldBe(HttpStatusCode.OK, await primary.Content.ReadAsStringAsync());
        var status = (await primary.Content.ReadFromJsonAsync<SyncStatusDto>())!;
        status.Connection.Mode.ShouldBe(SyncModes.Primary);
        status.Outage!.ResubmitRequired.ShouldBe(1);
        (await _admin.GetFromJsonAsync<RecordSyncStateDto>($"/api/v1/integration/references/portfolio.project/{projectId}/sync-state"))!.ResubmitRequired.ShouldBeTrue();

        // Records unlock, jobs resume; a sync now is refused while primary.
        foreach (var (id, name) in new[] { (itemA, "A changed here"), (itemB, "B changed here") })
        {
            var dto = (await _admin.GetFromJsonAsync<SampleItemDto>($"/api/v1/sample/items/{id}"))!;
            (await _admin.PutAsJsonAsync($"/api/v1/sample/items/{id}", new { nameEn = name, version = dto.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await InScopeAsync(async sp => await sp.GetRequiredService<StandbyJobGate>().IsSuspendedAsync("portfolio.x", CancellationToken.None)))
            .ShouldBeFalse();
        await (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/sync-now", new SyncNowInput()))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "integration.sync.notStandby");

        // The outage changes report lists the changes with old → new values and the Salesforce IDs; Excel export works.
        var report = (await _admin.GetFromJsonAsync<OutageReportDto>($"{Sync}/outages/{status.Outage.Id}/report"))!;
        var sample = report.Systems.Single(s => s.SystemCode == "sample").Records;
        var a = sample.Single(r => r.RecordId == itemA);
        a.SalesforceId.ShouldBe(seed.ItemA);
        a.Changes.ShouldContain(c => c.Field == "NameEn" && c.OldValue == "First synced item" && c.NewValue == "A changed here");
        sample.Single(r => r.RecordId == itemB).SalesforceId.ShouldBe(seed.ItemB);
        report.Systems.SelectMany(s => s.Records).SelectMany(r => r.Changes).ShouldAllBe(c => c.Channel != Channels.Sync);

        // Return to standby needs the report acknowledged; exporting acknowledges it.
        await (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/return-to-standby", new ReturnToStandbyInput(false)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "integration.sync.reportNotAcknowledged");
        var excel = await _admin.GetAsync($"{Sync}/outages/{status.Outage.Id}/report.xlsx?lang=ar");
        excel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await excel.Content.ReadAsByteArrayAsync()).Length.ShouldBeGreaterThan(1000);
        (await _admin.PostAsJsonAsync($"{Sync}/outages/{status.Outage.Id}/records/{SampleItem.EntityType}/{itemA}/re-entered", new ReEnteredInput(true)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var back = await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/return-to-standby", new ReturnToStandbyInput(false));
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
        var reconciling = (await back.Content.ReadFromJsonAsync<SyncStatusDto>())!;
        reconciling.Connection.Mode.ShouldBe(SyncModes.Reconciling);
        reconciling.Outage!.ChangedLocally.ShouldBe(2);
        InvalidateGuard();

        // Salesforce changed both records meanwhile: the sync holds them for a decision instead of overwriting.
        _fake.Objects["Sample_Item__c"].Upsert(seed.ItemA, new() { ["Title__c"] = "A from Salesforce" });
        _fake.Objects["Sample_Item__c"].Upsert(seed.ItemB, new() { ["Title__c"] = "B from Salesforce" });
        var held = await RunAsync();
        Stats(held, "item").Conflicts.ShouldBe(2);
        (await ItemAsync(itemA))!.NameEn.ShouldBe("A changed here");
        var conflicts = (await _admin.GetFromJsonAsync<List<OutageConflictDto>>($"{Sync}/outages/{status.Outage.Id}/conflicts"))!;
        conflicts.Where(c => c.Conflict).Select(c => c.RecordId).ShouldBe([itemA, itemB], ignoreOrder: true);
        conflicts.Single(c => c.RecordId == itemB).Pending!["Title__c"].ShouldBe("B from Salesforce");
        await (await _admin.PostAsync($"{Sync}/connections/{_connection}/close-outage", null)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "integration.sync.conflictsPending");

        // Keep local for A, take Salesforce for B.
        (await _admin.PostAsJsonAsync($"{Sync}/outages/{status.Outage.Id}/conflicts/{itemA}/resolve", new ResolveConflictInput("keep_local"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _admin.PostAsJsonAsync($"{Sync}/outages/{status.Outage.Id}/conflicts/{itemB}/resolve", new ResolveConflictInput("take_source"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ItemAsync(itemB))!.NameEn.ShouldBe("B from Salesforce");
        (await ItemAsync(itemA))!.NameEn.ShouldBe("A changed here");
        _fake.Objects["Sample_Item__c"].Upsert(seed.ItemA, new() { ["Title__c"] = "A from Salesforce again" });
        await RunAsync();
        (await ItemAsync(itemA))!.NameEn.ShouldBe("A changed here"); // kept until the outage closes

        // Closing the outage returns to standby; from then on Salesforce wins again.
        var closed = await _admin.PostAsync($"{Sync}/connections/{_connection}/close-outage", null);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        (await closed.Content.ReadFromJsonAsync<SyncStatusDto>())!.Connection.Mode.ShouldBe(SyncModes.Standby);
        InvalidateGuard();
        _fake.Objects["Sample_Item__c"].Upsert(seed.ItemA, new() { ["Title__c"] = "A final" });
        await RunAsync();
        (await ItemAsync(itemA))!.NameEn.ShouldBe("A final");
        var dtoA = (await _admin.GetFromJsonAsync<SampleItemDto>($"/api/v1/sample/items/{itemA}"))!;
        await (await _admin.PutAsJsonAsync($"/api/v1/sample/items/{itemA}", new { nameEn = "x", version = dtoA.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, StandbyErrors.Code);
        _fake.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scheduler_runs_due_connections_on_the_fake_clock_and_alerts_on_failure_and_delay()
    {
        await ConfigureAsync("department", "user");
        await SeedOrgAsync();
        // The fake clock stays close to real time (the admin's session must stay valid); the schedule maths is unit-tested exactly.
        var now = DateTimeOffset.UtcNow;
        _clock.Frozen = now;
        var version = (await _admin.GetFromJsonAsync<SyncConnectionDto>($"{Sync}/connections/{_connection}"))!.Version;
        var every4h = new SyncScheduleSettings { Kind = "interval", EveryHours = 4, At = "00:00" };
        var saved = await _admin.PutAsJsonAsync($"{Sync}/connections/{_connection}/schedule", new SyncScheduleInput(every4h, 60, version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var schedule = (await saved.Content.ReadFromJsonAsync<SyncScheduleDto>())!;
        var dubai = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var next = SyncSchedule.Next(every4h, now, dubai);
        schedule.NextRunAt.ShouldBe(next);
        schedule.NextRuns[1].ShouldBe(next.AddHours(4));

        async Task<int> DueAsync() => await InScopeAsync(async sp => await sp.GetRequiredService<SyncScheduler>().RunDueAsync(CancellationToken.None));
        (await DueAsync()).ShouldBe(0);
        _clock.Frozen = next.AddSeconds(30);
        (await DueAsync()).ShouldBe(1);
        (await InScopeAsync(async sp => await sp.GetRequiredService<IntegrationDbContext>().SyncConnections.AsNoTracking().FirstAsync(c => c.Id == _connection)))
            .NextRunAt.ShouldBe(next.AddHours(4));
        (await DueAsync()).ShouldBe(0);
        _clock.Frozen = now;

        // Sync now is picked up at once by the scheduler job.
        (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/sync-now", new SyncNowInput(Full: true))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DueAsync()).ShouldBe(1);
        var runs = (await _admin.GetFromJsonAsync<PagedResult<SyncRunDto>>($"{Sync}/connections/{_connection}/runs"))!;
        runs.Items[0].Trigger.ShouldBe(RunTriggers.Full);

        // A failing run alerts the administrators; a late sync alerts once.
        _fake.RejectToken = true;
        (await _admin.PostAsJsonAsync($"{Sync}/connections/{_connection}/sync-now", new SyncNowInput())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DueAsync()).ShouldBe(1);
        var failed = (await InScopeAsync(async sp => await sp.GetRequiredService<SyncAdministration>().StatusAsync(_connection, CancellationToken.None))).Value;
        failed.LastRun!.Status.ShouldBe(RunStatuses.Failed);
        failed.LastRun.MessageKey.ShouldBe("integration.sync.errors.authFailed");
        await InScopeAsync(async sp => await sp.GetRequiredService<Strategy.Platform.Scheduling.Application.OutboxDispatcher>().DispatchDueAsync(CancellationToken.None));
        _factory.Emails.Sent.ShouldContain(m => m.TemplateKey == "integration.sync.failed");
        var count = _factory.Emails.Sent.Count;

        _clock.Frozen = _clock.Frozen.Value.AddHours(3);
        (await InScopeAsync(async sp => await sp.GetRequiredService<SyncScheduler>().MonitorAsync(CancellationToken.None))).ShouldBe(1);
        (await InScopeAsync(async sp => await sp.GetRequiredService<SyncScheduler>().MonitorAsync(CancellationToken.None))).ShouldBe(0);
        await InScopeAsync(async sp => await sp.GetRequiredService<Strategy.Platform.Scheduling.Application.OutboxDispatcher>().DispatchDueAsync(CancellationToken.None));
        _factory.Emails.Sent.ShouldContain(m => m.TemplateKey == "integration.sync.late");

        // The health page reports the failure.
        var checks = await InScopeAsync(async sp => await sp.GetRequiredService<SyncHealthContributor>().CheckAsync(CancellationToken.None));
        checks.ShouldContain(c => c.MessageKey == "health.salesforceSync.failed");
    }

    private static byte[] MinimalPdf() => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 10 10]>>endobj\n" +
        "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000052 00000 n \n0000000101 00000 n \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n160\n%%EOF\n");

    /// <summary>A write tool over the template module (the MCP write path every module's tools take).</summary>
    private sealed class RenameItemTool(IDispatcher dispatcher, SampleDbContext db) : IAppTool
    {
        public ToolDefinition Definition { get; } = new("tests_rename_item", "sample", ToolKind.Write, "إعادة تسمية", "Rename item", "Renames a sample item.",
            ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}"""));

        public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
        {
            var id = Guid.Parse(arguments.Text("id")!);
            var version = await db.Set<SampleItem>().Where(i => i.Id == id).Select(i => i.Version).FirstAsync(cancellationToken);
            var result = await dispatcher.SendAsync(new UpdateSampleItem(id, null, arguments.Text("name"), null, null, version), cancellationToken);
            return result.IsSuccess ? ToolResult.Ok(result.Value) : ToolResult.Fail(ToolErrors.Validation, result.Error!.Code);
        }
    }
}
