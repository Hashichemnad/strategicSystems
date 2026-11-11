using System.Net;
using System.Security.Claims;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Feeds;
using Strategy.Platform.Integration.Webhooks;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.UnitTests.Integration;

/// <summary>Phase 22 pure rules: OData translation, metadata, cursors, IP allow-list, rate limit, signatures, back-off, CSV, token scopes.</summary>
public class IntegrationFeedRulesTests
{
    private static readonly ReportableRecordType ProjectType = new("portfolio.project", "portfolio", "Projects", "مشاريع", "Projects", "portfolio.project.read",
    [
        new("number", "الرقم", "Number", RecordFieldType.Text),
        new("stage", "المرحلة", "Stage", RecordFieldType.Lookup, Choices: [new("plan", "تخطيط", "Planning")]),
        new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
        new("budget", "الميزانية", "Budget", RecordFieldType.Money),
        new("plannedEnd", "النهاية", "Planned end", RecordFieldType.Date),
        new("closed", "مغلق", "Closed", RecordFieldType.Bool),
    ],
    [new("milestones", "المعالم", "Milestones", "portfolio.milestone", "id", "projectId", Many: true)]);

    private static readonly ReportableRecordType MilestoneType = new("portfolio.milestone", "portfolio", "Milestones", "معالم", "Milestones", "portfolio.project.read",
    [
        new("projectId", "المشروع", "Project", RecordFieldType.Id),
        new("title", "العنوان", "Title", RecordFieldType.Text),
    ], [new("project", "المشروع", "Project", "portfolio.project", "projectId", "id", Many: false)]);

    private static ODataModel Model()
    {
        var project = new VisibleRecordType(ProjectType, null!, ProjectType.Fields, ProjectType.Relationships);
        var milestone = new VisibleRecordType(MilestoneType, null!, MilestoneType.Fields, MilestoneType.Relationships);
        return new ODataModel("portfolio", [project, milestone]);
    }

    private static ODataRequest Parse(string relative)
    {
        var root = new Uri("https://strategy.example/odata/v1/portfolio/");
        return ODataQuery.Parse(Model(), root, new Uri(root, relative));
    }

    [Fact]
    public void Filters_translate_to_the_neutral_tree_with_typed_values()
    {
        var request = Parse("Projects?$filter=budget ge 1000.5 and (stage eq 'plan' or not closed) and plannedEnd lt 2026-12-31 and startswith(number,'PR')");
        request.Type.EntitySet.ShouldBe("Projects");
        var and = request.Filter.ShouldBeOfType<RecordFilterAnd>();
        and.Right.ShouldBe(new RecordFilterText("number", RecordTextMatch.StartsWith, "PR"));
        var leaves = Leaves(request.Filter).ToList();
        leaves.ShouldContain(new RecordFilterCompare("budget", RecordCompare.Ge, 1000.5m));
        leaves.ShouldContain(new RecordFilterCompare("stage", RecordCompare.Eq, "plan"));
        leaves.ShouldContain(new RecordFilterCompare("plannedEnd", RecordCompare.Lt, new DateOnly(2026, 12, 31)));
        Leaves(request.Filter).Count().ShouldBe(5);
        request.Filter.ToString().ShouldContain("RecordFilterNot { Inner = RecordFilterCompare { Field = closed, Operator = Eq, Value = True } }");
    }

    [Fact]
    public void Literal_on_the_left_is_flipped_and_in_and_null_are_supported()
    {
        var flipped = Parse("Projects?$filter=1000 lt budget").Filter.ShouldBeOfType<RecordFilterCompare>();
        flipped.ShouldBe(new RecordFilterCompare("budget", RecordCompare.Gt, 1000m));
        var unit = Guid.Parse("0190a4f0-0000-7000-8000-000000000042");
        var @in = Parse($"Projects?$filter=unit in ({unit},{unit})").Filter.ShouldBeOfType<RecordFilterIn>();
        @in.Values.ShouldAllBe(v => (Guid)v! == unit);
        Parse("Projects?$filter=unit eq null").Filter.ShouldBe(new RecordFilterCompare("unit", RecordCompare.Eq, null));
    }

    [Theory]
    [InlineData("Projects?$apply=aggregate(budget with sum as total)", "notImplemented")]
    [InlineData("Projects?$filter=stageLabel eq 'x'", "notImplemented")]
    [InlineData("Projects?$filter=milestones/any(m: m/title eq 'x')", "notImplemented")]
    [InlineData("Projects?$filter=length(number) eq 3", "notImplemented")]
    [InlineData("Projects?$filter=nope eq 1", "badRequest")]
    [InlineData("Unknown", "badRequest")]
    [InlineData("Projects?$filter=contains(budget,'1')", "badRequest")]
    public void Unsupported_or_invalid_queries_are_refused_with_a_code(string relative, string code) =>
        Should.Throw<FeedQueryException>(() => Parse(relative)).Code.ShouldBe(code);

    [Fact]
    public void Select_keeps_codes_with_their_labels_and_expand_reads_the_join_field()
    {
        var request = Parse("Projects?$select=stageLabel,budget&$orderby=budget desc,number&$top=5&$skip=10&$count=true&$expand=milestones($select=title;$filter=title ne null;$top=3)");
        request.Fields.ShouldBe(["id", "stage", "budget"]);
        request.Output!.ShouldBe(new HashSet<string> { "id", "stage", "stageLabel", "budget" }, ignoreOrder: true);
        request.OrderBy.ShouldBe([new RecordSort("budget", true), new RecordSort("number")]);
        (request.Top, request.Skip, request.Count).ShouldBe((5L, 10L, true));
        var expand = request.Expands.Single();
        expand.Relationship.Name.ShouldBe("milestones");
        expand.Fields.ShouldBe(["id", "title"]);
        expand.Top.ShouldBe(3);
        expand.Filter.ShouldBe(new RecordFilterCompare("title", RecordCompare.Ne, null));
    }

    [Fact]
    public void Key_and_count_segments_are_recognised()
    {
        var id = Guid.Parse("0190a4f0-0000-7000-8000-000000000007");
        Parse($"Projects({id})").Key.ShouldBe(id);
        Parse("Milestones/$count").CountOnly.ShouldBeTrue();
    }

    [Fact]
    public void Metadata_lists_types_keys_labels_and_navigation()
    {
        var csdl = ODataEndpoints.Csdl(Model().Model);
        csdl.ShouldContain("Namespace=\"Strategy.Portfolio\"");
        csdl.ShouldContain("<EntityType Name=\"Project\">");
        csdl.ShouldContain("<PropertyRef Name=\"id\" />");
        csdl.ShouldContain("Name=\"stageLabel\" Type=\"Edm.String\"");
        csdl.ShouldContain("Name=\"unitLabel\"");
        csdl.ShouldContain("Name=\"plannedEnd\" Type=\"Edm.Date\"");
        csdl.ShouldContain("Name=\"modifiedAt\" Type=\"Edm.DateTimeOffset\"");
        csdl.ShouldContain("NavigationProperty Name=\"milestones\" Type=\"Collection(Strategy.Portfolio.Milestone)\"");
        csdl.ShouldContain("encoding=\"utf-8\"");
    }

    [Fact]
    public void Cursors_round_trip_and_garbage_is_refused()
    {
        var at = new DateTimeOffset(2026, 11, 11, 9, 30, 15, 123, TimeSpan.Zero);
        var id = Guid.CreateVersion7();
        Cursor.TryDecode(Cursor.Encode(at, id), out var cursor).ShouldBeTrue();
        cursor.ShouldBe(new RecordCursor(at, id));
        Cursor.TryDecode(null, out var none).ShouldBeTrue();
        none.ShouldBeNull();
        Cursor.TryDecode("bm90LWEtY3Vyc29y", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("10.20.30.0/24", "10.20.30.77", true)]
    [InlineData("10.20.30.0/24", "10.20.31.1", false)]
    [InlineData("192.168.1.5", "192.168.1.5", true)]
    [InlineData("192.168.1.5", "::ffff:192.168.1.5", true)]
    [InlineData("2001:db8::/32", "2001:db8:1::5", true)]
    [InlineData("2001:db8::/32", "2001:db9::5", false)]
    [InlineData("10.0.0.0/33", "10.0.0.1", false)]
    public void Ip_allow_list_matches_addresses_and_cidr_blocks(string entry, string address, bool allowed) =>
        IpAllowList.Allows([entry], IPAddress.Parse(address)).ShouldBe(allowed);

    [Fact]
    public void Empty_allow_list_allows_anyone_and_a_missing_address_is_refused_by_a_list()
    {
        IpAllowList.Allows([], null).ShouldBeTrue();
        IpAllowList.Allows(["10.0.0.0/8"], null).ShouldBeFalse();
        IpAllowList.TryParse("not-an-ip", out _).ShouldBeFalse();
    }

    [Fact]
    public void Rate_limiter_uses_fixed_minute_windows_per_client()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 11, 10, 0, 10, TimeSpan.Zero));
        var limiter = new ClientRateLimiter(clock);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        limiter.TryAcquire(a, 2, out _).ShouldBeTrue();
        limiter.TryAcquire(a, 2, out _).ShouldBeTrue();
        limiter.TryAcquire(a, 2, out var retry).ShouldBeFalse();
        retry.ShouldBe(50);
        limiter.TryAcquire(b, 2, out _).ShouldBeTrue();
        clock.Now = clock.Now.AddSeconds(50);
        limiter.TryAcquire(a, 2, out _).ShouldBeTrue();
    }

    [Fact]
    public void Signatures_verify_with_the_secret_and_a_fresh_timestamp_only()
    {
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        var ts = now.ToUnixTimeSeconds();
        const string body = "{\"type\":\"kpi.result.approved\"}";
        var signature = WebhookSigner.Sign("whsec_abc", ts, body);
        signature.ShouldStartWith("v1=");
        signature.Length.ShouldBe(3 + 64);
        WebhookSigner.Verify("whsec_abc", ts.ToString(System.Globalization.CultureInfo.InvariantCulture), signature, body, now, TimeSpan.FromMinutes(5)).ShouldBeTrue();
        WebhookSigner.Verify("whsec_abc", ts.ToString(System.Globalization.CultureInfo.InvariantCulture), signature, body + " ", now, TimeSpan.FromMinutes(5)).ShouldBeFalse();
        WebhookSigner.Verify("whsec_abc", ts.ToString(System.Globalization.CultureInfo.InvariantCulture), signature, body, now.AddMinutes(6), TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Fact]
    public void Backoff_doubles_from_the_base_and_is_capped_at_six_hours()
    {
        Enumerable.Range(1, 6).Select(n => WebhookSigner.Backoff(n, 60).TotalSeconds).ShouldBe([60, 120, 240, 480, 960, 1920]);
        WebhookSigner.Backoff(15, 60).ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Secrets_have_the_documented_shape_and_parse_back()
    {
        var (key, prefix, hash) = ClientSecrets.NewApiKey();
        key.ShouldStartWith("sik_" + prefix + "_");
        ClientSecrets.TryParse(key, out var parsedPrefix, out var parsedHash).ShouldBeTrue();
        parsedPrefix.ShouldBe(prefix);
        ClientSecrets.Matches(hash, parsedHash).ShouldBeTrue();
        ClientSecrets.TryParse("sik_short_x", out _, out _).ShouldBeFalse();
        ClientSecrets.NewClientId().ShouldStartWith("ic_");
        IntegrationClientKeys.LooksLikeKey(key).ShouldBeTrue();
    }

    [Fact]
    public void Csv_extract_has_a_bom_quotes_and_guards_formulas()
    {
        var page = new ExtractPage("sample", "Items", false,
            [new Dictionary<string, object?> { ["id"] = "1", ["name"] = "=HYPERLINK(\"x\")", ["note"] = "a,b" }, new Dictionary<string, object?> { ["id"] = "2", ["name"] = "-12.5" }],
            false, null, null, ["id", "name", "note"]);
        var csv = FeedService.Csv(page);
        csv[0].ShouldBe('﻿');
        csv.ShouldContain("\"'=HYPERLINK(\"\"x\"\")\"");
        csv.ShouldContain("\"a,b\"");
        csv.ShouldContain("2,-12.5,");
    }

    [Theory]
    [InlineData("GET", "/odata/v1/sample/Items", "feeds", true)]
    [InlineData("GET", "/api/v1/integration/extracts/sample/Items", "feeds", true)]
    [InlineData("GET", "/api/v1/sample/items", "feeds", false)]
    [InlineData("GET", "/api/v1/sample/items", "feeds api.read", true)]
    [InlineData("POST", "/api/v1/sample/items", "feeds api.read", false)]
    [InlineData("GET", "/api/v1/me/profile", "api.read", false)]
    [InlineData("GET", "/api/v1/integration/clients", "api.read", false)]
    [InlineData("POST", "/mcp", "feeds api.read", false)]
    public void Token_scope_rules_for_integration_clients(string method, string path, string scopes, bool allowed)
    {
        var identity = new ClaimsIdentity(AuthSchemes.Integration, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        identity.AddClaim(new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal));
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, AuthMethods.Client));
        identity.AddClaim(new Claim(StrategyClaims.IntegrationClient, "ic_test"));
        identity.AddClaim(new Claim(StrategyClaims.TokenScopes, scopes));
        TokenScopeRules.Allows(new ClaimsPrincipal(identity), method, path).ShouldBe(allowed);
    }

    [Fact]
    public void A_personal_token_never_reads_the_feeds()
    {
        var identity = new ClaimsIdentity(AuthSchemes.PersonalAccessToken, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal));
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, AuthMethods.Token));
        identity.AddClaim(new Claim(StrategyClaims.TokenScopes, "api.read api.write"));
        var principal = new ClaimsPrincipal(identity);
        TokenScopeRules.Allows(principal, "GET", "/odata/v1/sample/Items").ShouldBeFalse();
        TokenScopeRules.Allows(principal, "GET", "/api/v1/integration/extracts/sample/Items").ShouldBeFalse();
        TokenScopeRules.Allows(principal, "GET", "/api/v1/sample/items").ShouldBeTrue();
    }

    [Fact]
    public void Integration_scope_is_read_from_the_claims()
    {
        var unit = Guid.CreateVersion7();
        var identity = new ClaimsIdentity(AuthSchemes.Integration);
        identity.AddClaim(new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal));
        identity.AddClaim(new Claim(StrategyClaims.IntegrationClient, "ic_x"));
        identity.AddClaim(new Claim(StrategyClaims.ActingUnit, unit.ToString()));
        identity.AddClaim(new Claim(StrategyClaims.AllowedSystems, "risk kpi"));
        var scope = new ClaimsPrincipal(identity).IntegrationScopeOf()!;
        scope.ActingUnitId.ShouldBe(unit);
        scope.Systems.ShouldBe(["kpi", "risk"]);
        scope.Key.ShouldEndWith(":kpi,risk");
        new ClaimsPrincipal(new ClaimsIdentity(AuthSchemes.Integration)).IntegrationScopeOf().ShouldBeNull();
    }

    private static IEnumerable<RecordFilter> Leaves(RecordFilter filter) => filter switch
    {
        RecordFilterAnd a => Leaves(a.Left).Concat(Leaves(a.Right)),
        RecordFilterOr o => Leaves(o.Left).Concat(Leaves(o.Right)),
        RecordFilterNot n => Leaves(n.Inner),
        _ => [filter],
    };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;

        public DateTimeOffset UtcNow => Now;
    }
}
