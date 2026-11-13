using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;

namespace Strategy.UnitTests.Integration;

/// <summary>
/// Fixes of release 1.0.1 found against a real Salesforce sandbox (ADR 0210–0213): /limits refused, error details, picklist → number and
/// SOQL that only selects fields the object has.
/// </summary>
public sealed class SalesforceSandboxFixesTests
{
    private const string Instance = "https://adof.my.salesforce.test";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Answers by path; records every request; adds the usage header like Salesforce.</summary>
    private sealed class ScriptedTransport : ISalesforceTransport
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);

        public List<HttpRequestMessage> Requests { get; } = [];

        public string? UsageHeader { get; set; } = "api-usage=120/15000";

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/services/oauth2/token")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"access_token":"secret-token-value","instance_url":"{{Instance}}"}"""));
            }

            var response = Routes.TryGetValue(path, out var route) ? route() : Json(HttpStatusCode.NotFound, """[{"errorCode":"NOT_FOUND","message":"The requested resource does not exist"}]""");
            if (UsageHeader is not null)
            {
                response.Headers.TryAddWithoutValidation("Sforce-Limit-Info", UsageHeader);
            }

            return Task.FromResult(response);
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly string Pem = CreatePem();

    private static string CreatePem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static SalesforceSession Session(ScriptedTransport transport) =>
        new(transport, new SalesforceSessionOptions("https://login.salesforce.test", "client", "integration@example", Pem, "v62.0", 200));

    // ---- 1. /limits refused → warning, usage from the header ----

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "INSUFFICIENT_ACCESS")]
    [InlineData(HttpStatusCode.BadRequest, "FUNCTIONALITY_NOT_ENABLED")]
    [InlineData(HttpStatusCode.Unauthorized, "INVALID_SESSION_ID")]
    [InlineData(HttpStatusCode.BadRequest, "INSUFFICIENT_ACCESS")]
    public async Task Limits_refused_for_lack_of_permission_returns_the_refusal_and_usage_comes_from_the_header(HttpStatusCode status, string errorCode)
    {
        var transport = new ScriptedTransport();
        transport.Routes["/services/data/v62.0/limits"] = () => Json(status, $$"""[{"errorCode":"{{errorCode}}","message":"Insufficient privileges"}]""");
        var session = Session(transport);

        var (limits, refused) = await session.TryLimitsAsync(CancellationToken.None);

        limits.ShouldBeNull();
        refused.ShouldNotBeNull();
        refused.Status.ShouldBe((int)status);
        refused.Call.ShouldBe("GET /limits");
        refused.ErrorCode.ShouldBe(errorCode);
        session.ApiUsage.ShouldNotBeNull();
        var current = session.CurrentLimits(null)!;
        current.Source.ShouldBe(ApiLimitSources.Header);
        current.DailyApiMax.ShouldBe(15000);
        current.DailyApiRemaining.ShouldBe(14880);
    }

    [Fact]
    public async Task Other_limits_failures_are_not_swallowed()
    {
        var transport = new ScriptedTransport();
        transport.Routes["/services/data/v62.0/limits"] = () => Json(HttpStatusCode.BadRequest, """[{"errorCode":"MALFORMED_QUERY","message":"x"}]""");
        var ex = await Should.ThrowAsync<SourceConnectorException>(() => Session(transport).TryLimitsAsync(CancellationToken.None));
        ex.Error!.ErrorCode.ShouldBe("MALFORMED_QUERY");
    }

    [Fact]
    public async Task Limits_answered_are_used_until_a_newer_header_arrives_and_the_probe_reads_the_header()
    {
        var transport = new ScriptedTransport { UsageHeader = null };
        transport.Routes["/services/data/v62.0/limits"] = () => Json(HttpStatusCode.OK,
            """{"DailyApiRequests":{"Max":100000,"Remaining":95000},"DailyBulkV2QueryJobs":{"Max":10000,"Remaining":9990}}""");
        transport.Routes["/services/data/v62.0"] = () => Json(HttpStatusCode.OK, """{"sobjects":"/services/data/v62.0/sobjects"}""");
        var session = Session(transport);
        var (limits, refused) = await session.TryLimitsAsync(CancellationToken.None);
        refused.ShouldBeNull();
        limits!.Source.ShouldBe(ApiLimitSources.Limits);
        session.CurrentLimits(limits).ShouldBe(limits);

        transport.UsageHeader = "api-usage=99000/100000; per-app-api-usage=17/250(appName=Strategy)";
        await session.ProbeAsync(CancellationToken.None);
        transport.Requests[^1].RequestUri!.AbsolutePath.ShouldBe("/services/data/v62.0");
        var current = session.CurrentLimits(limits)!;
        current.Source.ShouldBe(ApiLimitSources.Header);
        current.DailyApiRemaining.ShouldBe(1000);
        current.IsBelowReserve(10).ShouldBeTrue();
    }

    [Theory]
    [InlineData("api-usage=18/15000", 18, 15000)]
    [InlineData("api-usage=18/15000;per-app-api-usage=2/250(appName=x)", 18, 15000)]
    [InlineData("per-app-api-usage=2/250(appName=x); api-usage=7/100", 7, 100)]
    [InlineData("per-app-api-usage=2/250(appName=x)", null, null)]
    [InlineData("api-usage=x/y", null, null)]
    [InlineData("api-usage=5/0", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void Usage_header_parsing(string? header, int? used, int? max)
    {
        var usage = ApiUsage.Parse(header, DateTimeOffset.UnixEpoch);
        usage?.Used.ShouldBe(used!.Value);
        usage?.Max.ShouldBe(max!.Value);
        if (used is null)
        {
            usage.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(100000, 9999, 10, true)]
    [InlineData(100000, 10000, 10, false)]
    [InlineData(100000, 0, 0, false)]
    [InlineData(100000, 50000, 95, true)] // the reserve is capped at 90 %
    [InlineData(null, 5, 10, false)] // unknown limits never stop a run
    [InlineData(100000, null, 10, false)]
    public void Reserve_rule(int? max, int? remaining, int percent, bool below) =>
        new ApiLimits(max, remaining, null, null, DateTimeOffset.UnixEpoch).IsBelowReserve(percent).ShouldBe(below);

    [Fact]
    public void Limits_stored_by_1_0_0_without_a_source_still_read()
    {
        var limits = JsonSerializer.Deserialize<ApiLimits>("""{"dailyApiMax":100,"dailyApiRemaining":50,"at":"2026-11-12T00:00:00Z"}""",
            WebJson)!;
        limits.Source.ShouldBeNull();
        limits.DailyApiRemaining.ShouldBe(50);
    }

    // ---- 2. Error details ----

    [Fact]
    public async Task Http_errors_carry_status_sanitised_call_error_code_and_message_but_never_the_token_or_filter_literals()
    {
        var transport = new ScriptedTransport();
        transport.Routes["/services/data/v62.0/queryAll"] = () => Json(HttpStatusCode.BadRequest,
            """[{"message":"\nSELECT Id, IsDeleted FROM User\n          ^\nERROR at Row:1:Column:12\nNo such column 'IsDeleted' on entity 'User'.","errorCode":"INVALID_FIELD"}]""");
        var session = Session(transport);
        var ex = await Should.ThrowAsync<SourceConnectorException>(async () =>
        {
            await foreach (var _ in session.QueryAsync("SELECT Id, IsDeleted FROM User WHERE Email = 'secret@example.com' ORDER BY SystemModstamp ASC", CancellationToken.None))
            {
            }
        });

        ex.MessageKey.ShouldBe("integration.sync.errors.http");
        var error = ex.Error!;
        error.Status.ShouldBe(400);
        error.ErrorCode.ShouldBe("INVALID_FIELD");
        error.Call.ShouldBe("GET /queryAll: SELECT Id, IsDeleted FROM User WHERE …");
        error.Message!.ShouldContain("No such column 'IsDeleted' on entity 'User'.");
        error.Message!.ShouldNotContain("\n");
        var parameters = ex.Params()!;
        parameters["status"].ShouldBe("400");
        parameters["errorCode"].ShouldBe("INVALID_FIELD");
        var all = string.Join(' ', parameters.Values);
        all.ShouldNotContain("secret-token-value");
        all.ShouldNotContain("Bearer");
        all.ShouldNotContain("secret@example.com");
    }

    [Fact]
    public async Task Sign_in_failures_carry_the_oauth_error()
    {
        var transport = new ScriptedTransport();
        var session = new SalesforceSession(new TokenRefusal(), new SalesforceSessionOptions("https://login.salesforce.test", "client", "user", Pem, "v62.0", 200));
        var ex = await Should.ThrowAsync<SourceConnectorException>(() => session.AuthenticateAsync(CancellationToken.None));
        ex.MessageKey.ShouldBe("integration.sync.errors.authFailed");
        ex.Error!.Status.ShouldBe(400);
        ex.Error.Call.ShouldBe("POST /services/oauth2/token");
        ex.Error.ErrorCode.ShouldBe("invalid_grant");
        ex.Error.Message.ShouldBe("user hasn't approved this consumer");
        transport.Requests.ShouldBeEmpty();
    }

    private sealed class TokenRefusal : ISalesforceTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}"""));

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("""[{"errorCode":"INSUFFICIENT_ACCESS","message":"no"}]""", "INSUFFICIENT_ACCESS", "no")]
    [InlineData("""{"error":"invalid_client_id","error_description":"client identifier invalid"}""", "invalid_client_id", "client identifier invalid")]
    [InlineData("""{"errorMessage":"InvalidJob : the query is invalid"}""", null, "InvalidJob : the query is invalid")]
    [InlineData("<html>Service unavailable</html>", null, "<html>Service unavailable</html>")]
    [InlineData("[not json", null, "[not json")]
    [InlineData("", null, null)]
    public void Error_bodies_are_read_in_every_shape(string body, string? code, string? message)
    {
        var error = SalesforceError.FromBody(500, "GET /limits", body);
        error.ErrorCode.ShouldBe(code);
        error.Message.ShouldBe(message);
    }

    [Fact]
    public void Long_messages_are_truncated()
    {
        var error = SalesforceError.FromBody(400, "GET /limits", $$"""[{"errorCode":"X","message":"{{new string('a', 2000)}}"}]""");
        error.Message!.Length.ShouldBe(SalesforceError.MaxMessage + 1);
    }

    [Theory]
    [InlineData(401, null, true)]
    [InlineData(403, null, true)]
    [InlineData(400, "FUNCTIONALITY_NOT_ENABLED", true)]
    [InlineData(400, "INSUFFICIENT_ACCESS", true)]
    [InlineData(400, "MALFORMED_QUERY", false)]
    [InlineData(500, null, false)]
    public void Permission_refusals(int status, string? code, bool refusal) =>
        new SalesforceError(status, "GET /limits", code, null).IsPermissionRefusal.ShouldBe(refusal);

    // ---- 3. Picklist → number ----

    private sealed class NoLookups : IMappingResolver
    {
        public Task<Guid?> ReferenceAsync(string targetType, string externalId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<Guid?> PersonAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsActiveLookupCodeAsync(string lookupType, string code, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static readonly SyncTargetDefinition Risk = new SyncTargetDefinition("tests.risk", "tests", "خطر", "Risk")
        .WholeNumber("probability", "Probability", required: true).Number("score", "Score").Ratio("share", "Share").Codes("tags", "Tags", "tests.tag");

    private static readonly Dictionary<string, string> Levels = new() { ["1"] = "1", ["2"] = "2", ["3"] = "3", ["4"] = "4", ["5"] = "5" };

    [Fact]
    public async Task Picklist_values_map_into_whole_numbers_numbers_and_ratios()
    {
        List<FieldMapping> fields =
        [
            new() { Source = "Probability_Level__c", Target = "probability", Kind = FieldMappingKinds.Picklist, Values = Levels },
            new() { Source = "Score__c", Target = "score", Kind = FieldMappingKinds.Picklist, Values = new() { ["High"] = "4.5" } },
            new() { Source = "Share__c", Target = "share", Kind = FieldMappingKinds.Picklist, Values = new() { ["Half"] = "0.5" } },
            new() { Source = "Tags__c", Target = "tags", Kind = FieldMappingKinds.Picklist, Values = new() { ["A"] = "a", ["B"] = "b" } },
        ];
        var record = new SourceRecord(new Dictionary<string, string?> { ["Probability_Level__c"] = "3", ["Score__c"] = "High", ["Share__c"] = "Half", ["Tags__c"] = "A;B" });
        var mapped = await FieldMapper.MapAsync(record, fields, new Dictionary<string, JsonElement>(), Risk, new NoLookups(), CancellationToken.None);
        mapped.Ok.ShouldBeTrue(string.Join(", ", mapped.Issues.Select(i => i.Reason + " " + i.Field)));
        mapped.Values["probability"].ShouldBe(3);
        mapped.Values["score"].ShouldBe(4.5m);
        mapped.Values["share"].ShouldBe(0.5m);
        mapped.Values["tags"].ShouldBe(new[] { "a", "b" }); // multi-select codes unchanged
    }

    [Fact]
    public async Task A_mapped_value_that_is_not_a_number_and_an_unmapped_value_become_exceptions()
    {
        List<FieldMapping> fields = [new() { Source = "Probability_Level__c", Target = "probability", Kind = FieldMappingKinds.Picklist, Values = new() { ["High"] = "high" } }];
        var notNumber = await FieldMapper.MapAsync(new SourceRecord(new Dictionary<string, string?> { ["Probability_Level__c"] = "High" }), fields,
            new Dictionary<string, JsonElement>(), Risk, new NoLookups(), CancellationToken.None);
        notNumber.Issues.Single().Reason.ShouldBe(ExceptionReasons.InvalidValue);

        var unmapped = await FieldMapper.MapAsync(new SourceRecord(new Dictionary<string, string?> { ["Probability_Level__c"] = "6" }),
            [fields[0] with { Values = Levels }], new Dictionary<string, JsonElement>(), Risk, new NoLookups(), CancellationToken.None);
        unmapped.Issues.Single().Reason.ShouldBe(ExceptionReasons.UnmappedValue);
    }

    // ---- 4. SOQL only selects fields the object has ----

    private static SourceObjectDescribe Describe(string name, params string[] fields) =>
        new(name, name, null, [.. fields.Select(f => new SourceFieldInfo(f, f, "string", 0, true, [], null, []))]);

    private static readonly SourceObjectDescribe User = Describe("User", "Id", "Name", "Email", "IsActive", "EmployeeNumber", "FederationIdentifier", "SystemModstamp");

    private static readonly SourceObjectDescribe Contact = Describe("Contact", "Id", "IsDeleted", "Name", "Email", "SystemModstamp", "Employee_Number__c");

    private static SyncObjectMapping People(string sourceObject) => new() { SourceObject = sourceObject, Kind = ObjectMappingKinds.People, Key = sourceObject.ToLowerInvariant() };

    [Fact]
    public void People_options_have_no_user_only_defaults()
    {
        var defaults = new PeopleOptions();
        defaults.EmployeeNoField.ShouldBeNull();
        defaults.FederationIdField.ShouldBeNull();
        defaults.EmailField.ShouldBe("Email");
        SyncQueries.PeopleFields(defaults).ShouldBe(["Email", "Name"]);
    }

    [Fact]
    public void User_queries_never_select_IsDeleted_and_contact_queries_only_existing_people_fields()
    {
        var userOptions = new PeopleOptions { EmployeeNoField = "EmployeeNumber", FederationIdField = "FederationIdentifier" };
        var userFields = Soql.FieldsOf(People("User"), [], userOptions, User);
        userFields.ShouldNotContain("IsDeleted");
        userFields.ShouldContain("EmployeeNumber");
        userFields.ShouldContain("FederationIdentifier");
        Soql.Build("User", userFields, null, "SystemModstamp", null).ShouldNotContain("IsDeleted");

        var inheritedDefaults = Soql.FieldsOf(People("Contact"), [], new PeopleOptions(), Contact);
        inheritedDefaults.ShouldBe(["Id", "IsDeleted", "SystemModstamp", "Email", "Name"]);

        var configured = new PeopleOptions { MatchOrder = [MatchedBy.Email, MatchedBy.EmployeeNo], EmployeeNoField = "Employee_Number__c" };
        Soql.FieldsOf(People("Contact"), [], configured, Contact).ShouldContain("Employee_Number__c");

        // A people field the object lacks is skipped at run time and reported.
        var wrong = new PeopleOptions { EmployeeNoField = "EmployeeNumber", FederationIdField = "FederationIdentifier" };
        var fields = Soql.FieldsOf(People("Contact"), [], wrong, Contact);
        fields.ShouldNotContain("EmployeeNumber");
        fields.ShouldNotContain("FederationIdentifier");
        SyncQueries.MissingPeopleFields(wrong, Contact).ShouldBe(["EmployeeNumber", "FederationIdentifier"]);
        SyncQueries.IsNeverDeleted("User").ShouldBeTrue();
        SyncQueries.IsNeverDeleted("Contact").ShouldBeFalse();
    }

    [Fact]
    public void Validate_sees_every_field_the_queries_select()
    {
        var record = new SyncObjectMapping { SourceObject = "PMS_Project__c", Kind = ObjectMappingKinds.Record, ExternalIdTemplate = "{Name}-{year:Start__c}", Key = "project" };
        var all = SyncQueries.All(record, [new FieldMapping { Source = "Title__c", Target = "title" }, new FieldMapping { Target = "x", Kind = FieldMappingKinds.Constant, Value = "1" }], null);
        all.Select(q => (q.Field, q.Usage, q.Optional)).ShouldBe(
        [
            ("Id", SyncQueries.UsageSystem, false),
            ("IsDeleted", SyncQueries.UsageSystem, true),
            ("SystemModstamp", SyncQueries.UsageWatermark, false),
            ("Title__c", SyncQueries.UsageMapped, false),
            ("Name", SyncQueries.UsageExternalId, false),
            ("Start__c", SyncQueries.UsageExternalId, false),
        ]);

        var files = SyncQueries.All(new SyncObjectMapping { SourceObject = "ContentDocumentLink", Kind = ObjectMappingKinds.Files, Key = "files" }, [], null);
        files.Select(q => q.SourceObject).Distinct().ShouldBe(["ContentVersion", "ContentDocumentLink"]);
        files.ShouldContain(q => q.SourceObject == "ContentVersion" && q.Field == "SystemModstamp" && q.Usage == SyncQueries.UsageWatermark);

        var approvals = SyncQueries.All(new SyncObjectMapping { SourceObject = "ProcessInstance", Kind = ObjectMappingKinds.Approvals, Key = "a" }, [], null);
        approvals.ShouldContain(q => q.SourceObject == "ProcessInstanceStep" && q.Field == "ProcessInstance.ProcessDefinition.Name");
        approvals.ShouldContain(q => q.SourceObject == "ProcessInstanceStep" && q.Field == "Actor.Name");

        var history = SyncQueries.All(new SyncObjectMapping { SourceObject = "PMS_Project__History", Kind = ObjectMappingKinds.FieldHistory, WatermarkField = "CreatedDate", Key = "h" }, [], null);
        history.ShouldContain(q => q.Field == "CreatedBy.Name" && q.Usage == SyncQueries.UsageHistory);
    }

    // ---- The shipped ADoF pack ----

    [Fact]
    public async Task The_adof_pack_maps_risk_levels_as_picklists_to_numbers_and_matches_contacts_by_employee_number()
    {
        var pack = await ShippedMappingPacks.AdofSalesforceAsync();
        pack.Version.ShouldBe(2);
        var risk = pack.Objects.Single(o => o.Key == "project-risk");
        foreach (var source in new[] { "Probability_Level__c", "Impact_Level__c" })
        {
            var field = risk.Fields.Single(f => f.Source == source);
            field.Kind.ShouldBe(FieldMappingKinds.Picklist);
            field.Values!.Keys.ShouldBe(["1", "2", "3", "4", "5"], ignoreOrder: true);
        }

        var contact = JsonSerializer.Deserialize<PeopleOptions>(pack.Objects.Single(o => o.Key == "contact").Options!.Value.GetRawText(),
            WebJson)!;
        contact.MatchOrder.ShouldBe([MatchedBy.Email, MatchedBy.EmployeeNo]);
        contact.EmployeeNoField.ShouldBe("Employee_Number__c");
        contact.FederationIdField.ShouldBeNull();

        var user = JsonSerializer.Deserialize<PeopleOptions>(pack.Objects.Single(o => o.Key == "user").Options!.Value.GetRawText(),
            WebJson)!;
        user.EmployeeNoField.ShouldBe("EmployeeNumber");
        user.FederationIdField.ShouldBe("FederationIdentifier");
    }
}
