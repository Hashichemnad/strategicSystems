using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;

namespace Strategy.UnitTests.Integration;

/// <summary>Pure rules of the Salesforce standby sync (docs/design/salesforce-sync.md, ADR 0200–0207).</summary>
public sealed class SalesforceSyncRulesTests
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

    // ---- Mode lifecycle: every allowed and every forbidden transition ----

    public static TheoryData<string, string, string?> Transitions()
    {
        var data = new TheoryData<string, string, string?>();
        var allowed = new Dictionary<(string, string), string>
        {
            [(SyncModes.Disabled, SyncModeActions.Activate)] = SyncModes.Standby,
            [(SyncModes.Standby, SyncModeActions.Deactivate)] = SyncModes.Disabled,
            [(SyncModes.Standby, SyncModeActions.MakePrimary)] = SyncModes.Primary,
            [(SyncModes.Primary, SyncModeActions.ReturnToStandby)] = SyncModes.Reconciling,
            [(SyncModes.Reconciling, SyncModeActions.MakePrimary)] = SyncModes.Primary,
            [(SyncModes.Reconciling, SyncModeActions.CloseOutage)] = SyncModes.Standby,
        };
        foreach (var mode in SyncModes.All)
        {
            foreach (var action in SyncModeActions.All)
            {
                data.Add(mode, action, allowed.TryGetValue((mode, action), out var to) ? to : null);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Mode_transitions_follow_the_table(string from, string action, string? to) => SyncModeLifecycle.Next(from, action).ShouldBe(to);

    [Fact]
    public void Standby_covers_standby_and_reconciling_only()
    {
        SyncModes.All.Where(SyncModes.IsStandby).ShouldBe([SyncModes.Standby, SyncModes.Reconciling]);
        SyncModeLifecycle.ActionsFrom(SyncModes.Primary).ShouldBe([SyncModeActions.ReturnToStandby]);
    }

    // ---- Schedule on a fake clock (installation time zone) ----

    [Theory]
    [InlineData("interval", 4, "00:00", "2026-11-16T05:59:00Z", "2026-11-16T08:00:00Z")] // 09:59 Dubai → 12:00
    [InlineData("interval", 4, "00:00", "2026-11-16T08:00:00Z", "2026-11-16T12:00:00Z")] // strictly after
    [InlineData("interval", 6, "01:30", "2026-11-16T00:00:00Z", "2026-11-16T03:30:00Z")] // 04:00 Dubai → 07:30 (anchored at 01:30)
    [InlineData("interval", 24, "02:00", "2026-11-16T00:00:00Z", "2026-11-16T22:00:00Z")] // next 02:00 Dubai
    [InlineData("daily", 0, "02:30", "2026-11-15T23:00:00Z", "2026-11-16T22:30:00Z")] // 03:00 Dubai has passed → tomorrow 02:30
    [InlineData("daily", 0, "02:30", "2026-11-15T22:00:00Z", "2026-11-15T22:30:00Z")]
    public void Next_run_of_interval_and_daily_schedules(string kind, int hours, string at, string after, string expected) =>
        SyncSchedule.Next(new SyncScheduleSettings { Kind = kind, EveryHours = hours, At = at }, DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture), Dubai)
            .ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Weekly_schedule_runs_on_the_chosen_days_only()
    {
        var weekly = new SyncScheduleSettings { Kind = ScheduleKinds.Weekly, At = "06:00", Days = [1, 4] }; // Monday, Thursday
        var friday = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        var monday = SyncSchedule.Next(weekly, friday, Dubai);
        monday.ShouldBe(new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.Zero));
        SyncSchedule.Next(weekly, monday, Dubai).ShouldBe(new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero));
        SyncSchedule.IsoDay(new DateOnly(2026, 11, 15)).ShouldBe(7);
    }

    [Fact]
    public void Invalid_schedules_are_reported_with_message_keys()
    {
        SyncSchedule.Validate(new SyncScheduleSettings()).ShouldBeEmpty();
        SyncSchedule.Validate(new SyncScheduleSettings { Kind = "hourly" }).Select(e => e.Field).ShouldContain("kind");
        SyncSchedule.Validate(new SyncScheduleSettings { EveryHours = 25 }).Select(e => e.MessageKey).ShouldBe(["integration.sync.errors.everyHours"]);
        SyncSchedule.Validate(new SyncScheduleSettings { At = "25:00" }).Select(e => e.Field).ShouldBe(["at"]);
        SyncSchedule.Validate(new SyncScheduleSettings { Kind = ScheduleKinds.Weekly, Days = [0, 8] }).Select(e => e.Field).ShouldBe(["days"]);
    }

    // ---- Field mapping ----

    private sealed class Resolver : IMappingResolver
    {
        public Dictionary<string, Guid> References { get; } = [];

        public Dictionary<string, Guid> People { get; } = [];

        public HashSet<string> ActiveCodes { get; } = ["digital", "innovative", "sustainability"];

        public Task<Guid?> ReferenceAsync(string targetType, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(References.TryGetValue(externalId, out var id) ? id : (Guid?)null);

        public Task<Guid?> PersonAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(People.TryGetValue(externalId, out var id) ? id : (Guid?)null);

        public Task<bool> IsActiveLookupCodeAsync(string lookupType, string code, CancellationToken cancellationToken) => Task.FromResult(ActiveCodes.Contains(code));
    }

    private static readonly SyncTargetDefinition Target = new SyncTargetDefinition("tests.thing", "tests", "شيء", "Thing")
        .Text("name", "Name", required: true, maxLength: 10).Choice("stage", "Stage", ["planning", "execution"]).Lookup("type", "Type", "tests.type")
        .Codes("themes", "Themes", "tests.theme").Person("ownerId", "OwnerId").Reference("parentId", "ParentId", "tests.thing").Date("start", "Start")
        .Timestamp("at", "At").Ratio("progress", "Progress").WholeNumber("count", "Count").Bool("flag", "Flag").Default("stage", "planning");

    private static SourceRecord Record(params (string Key, string? Value)[] values) => new(values.ToDictionary(v => v.Key, v => v.Value));

    [Fact]
    public async Task Maps_values_with_kinds_value_maps_transforms_and_conversions()
    {
        var resolver = new Resolver();
        var owner = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();
        resolver.People["005A"] = owner;
        resolver.References["a01P"] = parent;
        List<FieldMapping> fields =
        [
            new() { Source = "Name", Target = "name", Transform = "trim" },
            new() { Source = "Stage__c", Target = "stage", Kind = FieldMappingKinds.Picklist, Values = new() { ["Execution"] = "execution" } },
            new() { Source = "Type__c", Target = "type", Kind = FieldMappingKinds.Picklist, Values = new() { ["Digital"] = "digital" } },
            new() { Source = "Themes__c", Target = "themes", Kind = FieldMappingKinds.Picklist, Values = new() { ["Future Readiness"] = "innovative", ["Green"] = "sustainability" } },
            new() { Source = "Owner__c", Target = "ownerId", Kind = FieldMappingKinds.Person },
            new() { Source = "Parent__c", Target = "parentId", Kind = FieldMappingKinds.Reference },
            new() { Source = "Start__c", Target = "start" },
            new() { Source = "At__c", Target = "at" },
            new() { Source = "Progress__c", Target = "progress", Transform = "percentToRatio" },
            new() { Source = "Count__c", Target = "count" },
            new() { Source = "Flag__c", Target = "flag", Transform = "yesNo" },
        ];
        var record = Record(("Name", "  Data  "), ("Stage__c", "execution"), ("Type__c", "Digital"), ("Themes__c", "Future Readiness;Green;Green"), ("Owner__c", "005A"),
            ("Parent__c", "a01P"), ("Start__c", "2026-03-01"), ("At__c", "2026-03-01T10:15:00.000+0000"), ("Progress__c", "35.5"), ("Count__c", "3.0"), ("Flag__c", "Yes"));
        var mapped = await FieldMapper.MapAsync(record, fields, new Dictionary<string, JsonElement>(), Target, resolver, CancellationToken.None);
        mapped.Ok.ShouldBeTrue(string.Join(", ", mapped.Issues.Select(i => i.Reason + " " + i.Field)));
        mapped.Values["name"].ShouldBe("Data");
        mapped.Values["stage"].ShouldBe("execution"); // value maps match case-insensitively
        mapped.Values["type"].ShouldBe("digital");
        mapped.Values["themes"].ShouldBe(new[] { "innovative", "sustainability" });
        mapped.Values["ownerId"].ShouldBe(owner);
        mapped.Values["parentId"].ShouldBe(parent);
        mapped.Values["start"].ShouldBe(new DateOnly(2026, 3, 1));
        mapped.Values["at"].ShouldBe(new DateTimeOffset(2026, 3, 1, 10, 15, 0, TimeSpan.Zero));
        mapped.Values["progress"].ShouldBe(0.355m);
        mapped.Values["count"].ShouldBe(3);
        mapped.Values["flag"].ShouldBe(true);
    }

    [Fact]
    public async Task Issues_become_exception_reasons_and_defaults_fill_empty_fields()
    {
        var resolver = new Resolver();
        List<FieldMapping> fields =
        [
            new() { Source = "Name", Target = "name" },
            new() { Source = "Stage__c", Target = "stage", Kind = FieldMappingKinds.Picklist, Values = new() { ["Execution"] = "execution" } },
            new() { Source = "Owner__c", Target = "ownerId", Kind = FieldMappingKinds.Person },
            new() { Source = "Parent__c", Target = "parentId", Kind = FieldMappingKinds.Reference },
            new() { Source = "Type__c", Target = "type", Kind = FieldMappingKinds.Picklist, Values = new() { ["Paper"] = "paper" } },
            new() { Source = "Start__c", Target = "start" },
        ];
        var mapped = await FieldMapper.MapAsync(Record(("Name", "Too long a name"), ("Stage__c", "Closure"), ("Owner__c", "005X"), ("Parent__c", "a01X"),
            ("Type__c", "Paper"), ("Start__c", "1 March")), fields, new Dictionary<string, JsonElement>(), Target, resolver, CancellationToken.None);
        mapped.Issues.Select(i => (i.Reason, i.Field)).ShouldBe(
        [
            (ExceptionReasons.InvalidValue, "name"), (ExceptionReasons.UnmappedValue, "stage"), (ExceptionReasons.UnknownUser, "ownerId"),
            (ExceptionReasons.MissingParent, "parentId"), (ExceptionReasons.InvalidValue, "type"), (ExceptionReasons.InvalidValue, "start"),
        ]);

        var empty = await FieldMapper.MapAsync(Record(("Name", null)), [new() { Source = "Name", Target = "name" }], new Dictionary<string, JsonElement>(), Target, resolver,
            CancellationToken.None);
        empty.Issues.Single().Reason.ShouldBe(ExceptionReasons.RequiredMissing);
        var defaults = JsonDocument.Parse("""{"stage":"planning"}""").RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var withDefault = await FieldMapper.MapAsync(Record(("Name", "x")), [new() { Source = "Name", Target = "name" }], defaults, Target, resolver, CancellationToken.None);
        withDefault.Values["stage"].ShouldBe("planning");
        var fallback = new FieldMapping { Kind = FieldMappingKinds.Picklist, Values = new() { ["A"] = "a", ["*"] = "other" } };
        FieldMapper.MapValue(fallback, "Z").ShouldBe("other");
    }

    [Fact]
    public async Task The_hash_changes_with_the_mapped_values_and_with_the_mapping_only()
    {
        var resolver = new Resolver();
        List<FieldMapping> fields = [new() { Source = "Name", Target = "name" }];
        var none = new Dictionary<string, JsonElement>();
        var a = await FieldMapper.MapAsync(Record(("Name", "x"), ("Other__c", "1")), fields, none, Target, resolver, CancellationToken.None);
        var b = await FieldMapper.MapAsync(Record(("Name", "x"), ("Other__c", "2")), fields, none, Target, resolver, CancellationToken.None);
        var c = await FieldMapper.MapAsync(Record(("Name", "y")), fields, none, Target, resolver, CancellationToken.None);
        var d = await FieldMapper.MapAsync(Record(("Name", "x")), [new() { Source = "Name", Target = "name", Transform = "upper" }], none, Target, resolver, CancellationToken.None);
        b.Hash.ShouldBe(a.Hash); // unmapped source fields do not matter
        c.Hash.ShouldNotBe(a.Hash);
        d.Hash.ShouldNotBe(a.Hash); // a mapping change re-applies records
    }

    [Theory]
    [InlineData("{Id}", "a01", "a01")]
    [InlineData("{Id}-{year:End_Date__c}", "a01", "a01-2025")]
    [InlineData("{Id}-{year:Missing__c}", "a01", null)]
    public void External_id_templates(string template, string id, string? expected) =>
        FieldMapper.ExternalId(template, Record(("Id", id), ("End_Date__c", "2025-12-31"))).ShouldBe(expected);

    [Theory]
    [InlineData("trim", "  a ", "a")]
    [InlineData("upper", "ab", "AB")]
    [InlineData("percentToRatio", "50", "0.5")]
    [InlineData("dateOnly", "2026-01-02T10:00:00.000+0000", "2026-01-02")]
    [InlineData("htmlToText", "<p>Line&amp;1</p><b>x</b>", "Line&1\nx")]
    [InlineData("truncate:3", "abcdef", "abc")]
    [InlineData("yesNo", "No", "false")]
    [InlineData("yesNo", "TBD", null)]
    public void Transforms(string transform, string raw, string? expected) => FieldMapper.Transform(transform, raw).ShouldBe(expected);

    [Fact]
    public void Salesforce_timestamps_parse_in_every_format()
    {
        FieldMapper.TryParseDateTime("2026-11-01T08:00:00.000+0000", out var a).ShouldBeTrue();
        FieldMapper.TryParseDateTime("2026-11-01T12:00:00+04:00", out var b).ShouldBeTrue();
        FieldMapper.TryParseDateTime("2026-11-01T08:00:00Z", out var c).ShouldBeTrue();
        a.ShouldBe(b);
        a.ShouldBe(c);
        FieldMapper.IsKnownTransform("truncate:0").ShouldBeFalse();
        FieldMapper.IsKnownTransform("eval").ShouldBeFalse();
    }

    // ---- Connector: SOQL, CSV, JWT and the read-only guard ----

    [Fact]
    public void Soql_is_built_from_checked_names_with_the_watermark_overlap()
    {
        Soql.Build("PMS_Project__c", ["Id", "Name", "RecordType.DeveloperName", "Name"], "Status__c = 'Active'", "SystemModstamp",
                new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero))
            .ShouldBe("SELECT Id, Name, RecordType.DeveloperName FROM PMS_Project__c WHERE (Status__c = 'Active') AND SystemModstamp >= 2026-11-01T08:00:00Z ORDER BY SystemModstamp ASC");
        Soql.Count("User", null, "SystemModstamp", null).ShouldBe("SELECT COUNT() FROM User");
        Should.Throw<SourceConnectorException>(() => Soql.Build("Bad Object", ["Id"], null, "SystemModstamp", null));
        Should.Throw<SourceConnectorException>(() => Soql.Build("User", ["Id, Password"], null, "SystemModstamp", null));
        Soql.IsSafeCondition("Status__c = 'A' AND IsLatest = true").ShouldBeTrue();
        Soql.IsSafeCondition("Id IN (SELECT Id FROM User)").ShouldBeFalse();
        Soql.IsSafeCondition("Name = 'x'; DELETE").ShouldBeFalse();
        Soql.IsSafeCondition("Name = 'x").ShouldBeFalse();
        Soql.Quote("O'Brien").ShouldBe("'O\\'Brien'");
    }

    [Fact]
    public void Bulk_csv_is_parsed_with_quotes_commas_and_empty_values()
    {
        var rows = Csv.Parse("\"Id\",\"Name\",\"Notes\"\n\"a01\",\"Smith, J\",\"He said \"\"hi\"\"\nnext line\"\n\"a02\",\"\",\"x\"\n").ToList();
        rows.Count.ShouldBe(2);
        rows[0]["Name"].ShouldBe("Smith, J");
        rows[0]["Notes"].ShouldBe("He said \"hi\"\nnext line");
        rows[1]["Name"].ShouldBeNull();
    }

    [Fact]
    public void Jwt_assertion_is_signed_with_the_private_key_and_carries_the_claims()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var assertion = SalesforceJwt.CreateAssertion("consumer", "integration@example", "https://login.salesforce.com/", pem, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var parts = assertion.Split('.');
        rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        payload.RootElement.GetProperty("iss").GetString().ShouldBe("consumer");
        payload.RootElement.GetProperty("sub").GetString().ShouldBe("integration@example");
        payload.RootElement.GetProperty("aud").GetString().ShouldBe("https://login.salesforce.com");
        payload.RootElement.GetProperty("exp").GetInt64().ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 3, 0, TimeSpan.Zero).ToUnixTimeSeconds());

        SalesforceJwt.Thumbprint(pem).ShouldNotBeNull();
        SalesforceJwt.Thumbprint(rsa.ExportSubjectPublicKeyInfoPem()).ShouldBeNull(); // a public key is not enough
        SalesforceJwt.Thumbprint("garbage").ShouldBeNull();
        using var small = RSA.Create(1024);
        SalesforceJwt.Thumbprint(small.ExportPkcs8PrivateKeyPem()).ShouldBeNull();
    }

    [Theory]
    [InlineData("GET", "https://x.my.salesforce.com/services/data/v62.0/queryAll?q=SELECT", true)]
    [InlineData("POST", "https://login.salesforce.com/services/oauth2/token", true)]
    [InlineData("POST", "https://x.my.salesforce.com/services/data/v62.0/jobs/query", true)]
    [InlineData("POST", "https://x.my.salesforce.com/services/data/v62.0/jobs/ingest", false)]
    [InlineData("POST", "https://x.my.salesforce.com/services/data/v62.0/sobjects/Account", false)]
    [InlineData("PATCH", "https://x.my.salesforce.com/services/data/v62.0/sobjects/Account/001", false)]
    [InlineData("DELETE", "https://x.my.salesforce.com/services/data/v62.0/sobjects/Account/001", false)]
    [InlineData("PATCH", "https://x.my.salesforce.com/services/data/v62.0/jobs/query/750x", false)]
    public void The_connector_only_ever_reads(string method, string url, bool allowed)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        SalesforceSession.IsReadOnlyRequest(request).ShouldBe(allowed);
    }

    private static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
