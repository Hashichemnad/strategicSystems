using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;

namespace Strategy.UnitTests.Requests;

/// <summary>Form schema and data validation (docs/design/requests.md §2.2): every field type, conditions, tables, drafts vs submission.</summary>
public class FormValidationTests
{
    private static readonly Guid KnownEmployee = Guid.CreateVersion7();
    private static readonly Guid KnownUnit = Guid.CreateVersion7();
    private static readonly Guid KnownFile = Guid.CreateVersion7();
    private static readonly Guid ReadableRecord = Guid.CreateVersion7();

    private sealed class Resolver : IFormReferenceResolver
    {
        public Task<bool> IsActiveLookupAsync(string lookupType, string code, CancellationToken ct) => Task.FromResult(lookupType == "colours" && code is "red" or "blue");

        public Task<bool> EmployeeExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(id == KnownEmployee);

        public Task<bool> UnitExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(id == KnownUnit);

        public Task<bool> IsRequestFileAsync(Guid fileId, CancellationToken ct) => Task.FromResult(fileId == KnownFile);

        public Task<bool> CanReadRecordAsync(string targetType, Guid id, CancellationToken ct) => Task.FromResult(targetType == "docreg.document" && id == ReadableRecord);
    }

    private static FormField F(string code, string type, bool required = false) => new() { Code = code, Type = type, LabelEn = code, Required = required };

    private static readonly FormSchema Schema = FormSchema.Of(
        F("title", FieldTypes.Text, true) with { MaxLength = 10 },
        F("notes", FieldTypes.Textarea),
        F("amount", FieldTypes.Currency, true) with { Min = 0, Max = 1000 },
        F("count", FieldTypes.Number) with { Decimals = 0 },
        F("when", FieldTypes.Date),
        F("urgent", FieldTypes.Checkbox),
        F("reason", FieldTypes.Textarea) with { VisibleWhen = new FieldCondition("urgent", "eq", true), RequiredWhen = new FieldCondition("urgent", "eq", true) },
        F("colour", FieldTypes.Picklist) with { LookupType = "colours" },
        F("colours", FieldTypes.MultiPicklist) with { LookupType = "colours" },
        F("person", FieldTypes.Employee),
        F("unit", FieldTypes.Unit),
        F("attachment", FieldTypes.File),
        F("source", FieldTypes.Record) with { TargetType = "docreg.document" },
        F("eid", FieldTypes.Secret) with { Format = SecretFormats.EmiratesId },
        F("rows", FieldTypes.Table) with
        {
            MaxRows = 2,
            Columns = [F("name", FieldTypes.Text, true), F("share", FieldTypes.Number) with { Min = 1, Max = 100 }],
        },
        F("agree", FieldTypes.Checkbox, true));

    private static Task<FormValidationResult> ValidateAsync(object values, bool submitting = true) =>
        FormDataValidator.ValidateAsync(Schema, JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(values))!.AsObject(), submitting, new Resolver(), CancellationToken.None);

    private static Dictionary<string, object?> Valid(Action<Dictionary<string, object?>>? change = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["title"] = " Hello ",
            ["amount"] = 12.5m,
            ["count"] = 3,
            ["when"] = "2030-01-31",
            ["urgent"] = false,
            ["colour"] = "red",
            ["colours"] = new[] { "red", "blue", "red" },
            ["person"] = KnownEmployee,
            ["unit"] = KnownUnit,
            ["attachment"] = KnownFile,
            ["source"] = ReadableRecord,
            ["eid"] = "784-1990-1234567-1",
            ["rows"] = new object[] { new { name = "A", share = 50 } },
            ["agree"] = true,
        };
        change?.Invoke(values);
        return values;
    }

    [Fact]
    public async Task Valid_values_are_normalised()
    {
        var result = await ValidateAsync(Valid());
        result.Errors.ShouldBeEmpty();
        result.Values["title"]!.GetValue<string>().ShouldBe("Hello");
        result.Values["colours"]!.AsArray().Count.ShouldBe(2);
        result.Values.ContainsKey("reason").ShouldBeFalse();
    }

    [Theory]
    [InlineData("title", "far too long a title", "validation.maximumLength")]
    [InlineData("amount", 1000.5, "requests.errors.range")]
    [InlineData("amount", 1.234, "requests.errors.decimals")]
    [InlineData("count", "abc", "requests.errors.number")]
    [InlineData("when", "31/01/2030", "requests.errors.date")]
    [InlineData("colour", "green", "validation.unknownCode")]
    [InlineData("eid", "784-12-1", "requests.errors.emiratesId")]
    [InlineData("urgent", "yes", "requests.errors.type")]
    public async Task Wrong_values_are_refused(string field, object value, string key)
    {
        var result = await ValidateAsync(Valid(v => v[field] = value));
        result.Errors.ShouldContain(e => e.Field == $"fields.{field}" && e.MessageKey == key);
    }

    [Fact]
    public async Task References_must_exist_and_be_readable()
    {
        var result = await ValidateAsync(Valid(v =>
        {
            v["person"] = Guid.CreateVersion7();
            v["unit"] = Guid.CreateVersion7();
            v["attachment"] = Guid.CreateVersion7();
            v["source"] = Guid.CreateVersion7();
        }));
        result.Errors.Select(e => e.Field).ShouldBe(["fields.person", "fields.unit", "fields.attachment", "fields.source"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_keys_are_refused()
    {
        var result = await ValidateAsync(Valid(v => v["admin"] = true));
        result.Errors.ShouldContain(e => e.Field == "fields.admin" && e.MessageKey == "requests.errors.unknownField");
    }

    [Fact]
    public async Task Required_when_and_hidden_fields()
    {
        (await ValidateAsync(Valid(v => v["urgent"] = true))).Errors.ShouldContain(e => e.Field == "fields.reason" && e.MessageKey == "validation.notEmpty");
        var hidden = await ValidateAsync(Valid(v => v["reason"] = "dropped because not visible"));
        hidden.Errors.ShouldBeEmpty();
        hidden.Values.ContainsKey("reason").ShouldBeFalse();
    }

    [Fact]
    public async Task Drafts_skip_required_checks_but_not_types()
    {
        (await ValidateAsync(new { title = "Hi" }, submitting: false)).Errors.ShouldBeEmpty();
        (await ValidateAsync(new { title = "Hi", amount = "x" }, submitting: false)).Errors.ShouldContain(e => e.Field == "fields.amount");
        var submit = await ValidateAsync(new { title = "Hi" });
        submit.Errors.Select(e => e.Field).ShouldContain("fields.amount");
        submit.Errors.ShouldContain(e => e.Field == "fields.agree" && e.MessageKey == "validation.notEmpty");
        (await ValidateAsync(Valid(v => v["agree"] = false))).Errors.ShouldContain(e => e.Field == "fields.agree" && e.MessageKey == "requests.errors.mustConfirm");
    }

    [Fact]
    public async Task Table_rows_are_validated_per_cell_and_counted()
    {
        var result = await ValidateAsync(Valid(v => v["rows"] = new object[] { new { name = "", share = 500 }, new { name = "B" }, new { name = "C" } }));
        result.Errors.ShouldContain(e => e.Field == "fields.rows" && e.MessageKey == "requests.errors.rows");
        var cells = await ValidateAsync(Valid(v => v["rows"] = new object[] { new { name = "", share = 500 } }));
        cells.Errors.Select(e => e.Field).ShouldBe(["fields.rows[0].name", "fields.rows[0].share"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("eq", true, true)]
    [InlineData("ne", true, false)]
    [InlineData("filled", null, true)]
    [InlineData("empty", null, false)]
    public void Conditions(string op, bool? value, bool expected)
    {
        var condition = new FieldCondition("x", op, value is null ? null : JsonValue.Create(value.Value));
        FormDataValidator.Holds(condition, new JsonObject { ["x"] = true }, []).ShouldBe(expected);
    }

    [Fact]
    public void In_condition_matches_any_listed_value()
    {
        var condition = new FieldCondition("x", "in", new JsonArray("a", "b"));
        FormDataValidator.Holds(condition, [], new JsonObject { ["x"] = "b" }).ShouldBeTrue();
        FormDataValidator.Holds(condition, [], new JsonObject { ["x"] = "c" }).ShouldBeFalse();
    }

    [Fact]
    public void Schema_validation_catches_builder_mistakes()
    {
        var bad = new FormSchema
        {
            Sections =
            [
                new FormSection
                {
                    Code = "s",
                    Fields =
                    [
                        F("a", FieldTypes.Text) with { VisibleWhen = new FieldCondition("b", "filled") },
                        F("b", "spreadsheet"),
                        F("a", FieldTypes.Text),
                        F("1bad", FieldTypes.Text),
                        new FormField { Code = "nolabel", Type = FieldTypes.Text },
                        F("pick", FieldTypes.Picklist),
                        F("rec", FieldTypes.Record) with { TargetType = "unknown.type" },
                        F("tbl", FieldTypes.Table) with { Columns = [F("inner", FieldTypes.Table)] },
                        F("range", FieldTypes.Number) with { Min = 5, Max = 1 },
                    ],
                },
            ],
        };
        var errors = FormSchemaValidator.Validate(bad, knownTargetTypes: new HashSet<string> { "docreg.document" }).Select(e => e.Code).ToList();
        errors.ShouldContain("requests.errors.condition");
        errors.ShouldContain("requests.errors.fieldType");
        errors.ShouldContain("requests.errors.duplicateCode");
        errors.ShouldContain("requests.errors.code");
        errors.ShouldContain("requests.errors.label");
        errors.ShouldContain("validation.notEmpty");
        errors.ShouldContain("validation.unknownCode");
        errors.ShouldContain("requests.errors.fieldOptions");
        FormSchemaValidator.Validate(Schema).ShouldBeEmpty();
    }
}

/// <summary>Request lifecycle table (every action from every status) and stage order.</summary>
public class RequestLifecycleTests
{
    public static TheoryData<string, string, bool> Table()
    {
        var allowed = new HashSet<(string, string)>
        {
            (RequestStatuses.Draft, LifecycleActions.Save), (RequestStatuses.Draft, LifecycleActions.Submit), (RequestStatuses.Draft, LifecycleActions.Cancel),
            (RequestStatuses.Returned, LifecycleActions.Save), (RequestStatuses.Returned, LifecycleActions.Submit), (RequestStatuses.Returned, LifecycleActions.Cancel),
            (RequestStatuses.InReview, LifecycleActions.Approved), (RequestStatuses.InReview, LifecycleActions.Rejected), (RequestStatuses.InReview, LifecycleActions.Returned),
            (RequestStatuses.InReview, LifecycleActions.Cancel), (RequestStatuses.InProgress, LifecycleActions.StageAction), (RequestStatuses.InProgress, LifecycleActions.Cancel),
        };
        var data = new TheoryData<string, string, bool>();
        foreach (var status in RequestStatuses.All)
        {
            foreach (var action in LifecycleActions.All)
            {
                data.Add(status, action, allowed.Contains((status, action)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Every_transition(string status, string action, bool allowed) => RequestLifecycle.Allows(status, action).ShouldBe(allowed);

    [Fact]
    public void Stages_follow_their_order_and_kinds()
    {
        IReadOnlyList<StageDefinition> stages =
        [
            new() { Code = "review", Kind = StageKinds.Approval, ProcessKey = "p" },
            new() { Code = "processing", Kind = StageKinds.Processing },
        ];
        RequestLifecycle.After(stages, null)!.Code.ShouldBe("review");
        RequestLifecycle.After(stages, "review")!.Code.ShouldBe("processing");
        RequestLifecycle.After(stages, "processing").ShouldBeNull();
        RequestLifecycle.StatusOf(stages[0]).ShouldBe(RequestStatuses.InReview);
        RequestLifecycle.StatusOf(stages[1]).ShouldBe(RequestStatuses.InProgress);
        RequestLifecycle.StatusOf(null).ShouldBe(RequestStatuses.Completed);
    }
}

/// <summary>Secret values (ADR 0086): encrypted at rest, masked out, the mask keeps the stored value.</summary>
public class SecretValuesTests
{
    private static readonly FormSchema Schema = FormSchema.Of(
        new FormField { Code = "eid", Type = FieldTypes.Secret, Format = SecretFormats.EmiratesId, LabelEn = "EID" },
        new FormField
        {
            Code = "rows", Type = FieldTypes.Table, LabelEn = "Rows",
            Columns = [new FormField { Code = "eid", Type = FieldTypes.Secret, Format = SecretFormats.EmiratesId, LabelEn = "EID" }],
        });

    private static string Protect(string plain) => "enc:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));

    private static string? Unprotect(string cipher) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipher[4..]));

    [Fact]
    public void Stored_values_never_contain_the_plain_value_and_round_trip()
    {
        var plain = new JsonObject { ["eid"] = "784-1990-1234567-1", ["rows"] = new JsonArray(new JsonObject { ["eid"] = "784-2000-7654321-2" }) };
        var stored = SecretValues.ToStored(Schema, plain, Protect);
        stored.ToJsonString().ShouldNotContain("1234567");
        stored.ToJsonString().ShouldNotContain("7654321");
        var masked = SecretValues.ToMasked(Schema, stored);
        masked["eid"]!.GetValue<string>().ShouldBe("784-****-*******-1");
        masked["rows"]![0]!["eid"]!.GetValue<string>().ShouldBe("784-****-*******-2");
        SecretValues.ToPlain(Schema, stored, Unprotect).ToJsonString().ShouldBe(plain.ToJsonString());
    }

    [Fact]
    public void A_mask_sent_back_keeps_the_stored_value_and_an_unknown_mask_is_dropped()
    {
        var current = new JsonObject { ["eid"] = "784-1990-1234567-1" };
        var restored = SecretValues.RestoreMasked(Schema, new JsonObject { ["eid"] = "784-****-*******-1" }, current);
        restored["eid"]!.GetValue<string>().ShouldBe("784-1990-1234567-1");
        SecretValues.RestoreMasked(Schema, new JsonObject { ["eid"] = "784-****-*******-5" }, current).ContainsKey("eid").ShouldBeFalse();
    }
}

/// <summary>PDF template merge (ADR 0088): values encoded, loops, unknown paths empty.</summary>
public class TemplateRendererTests
{
    [Fact]
    public void Values_are_encoded_and_rows_loop()
    {
        var model = new TemplateModel(
            new Dictionary<string, string?> { ["field.title"] = "<script>alert(1)</script>", ["org.name"] = "الجهة" },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["people"] = [new Dictionary<string, string?> { ["name"] = "A & B" }, new Dictionary<string, string?> { ["name"] = "C" }],
            });
        var html = TemplateRenderer.Render("<h1>{{field.title}}</h1>{{org.name}}{{#rows people}}<p>{{row.index}}. {{name}}</p>{{/rows}}{{missing.path}}", model);
        html.ShouldBe("<h1>&lt;script&gt;alert(1)&lt;/script&gt;</h1>الجهة<p>1. A &amp; B</p><p>2. C</p>");
        TemplateRenderer.Placeholders("{{a.b}} {{ c }}").ShouldBe(["a.b", "c"]);
    }
}
