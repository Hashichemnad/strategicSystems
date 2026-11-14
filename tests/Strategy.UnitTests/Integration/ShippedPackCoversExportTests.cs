using System.Text.Json;
using Strategy.Platform.Integration.Sync;

namespace Strategy.UnitTests.Integration;

/// <summary>
/// Release 1.0.2 (ADR 0215): this installation is the ADoF standby for Salesforce and holds every Salesforce data field. The shipped pack is
/// checked against the owner's export (<c>docs/salesforce/adof-export.json</c>, from <c>Object Data.xlsx</c> by
/// <c>tools/salesforce/export_snapshot.py</c>): every object and field the pack names exists, every data field is mapped or ignored with a
/// reason, picklists are mapped as picklists and every active picklist value has a code. System fields belong to the sync; formula and
/// roll-up fields are recalculated here.
/// </summary>
public sealed class ShippedPackCoversExportTests
{
    private static readonly Lazy<JsonDocument> Export = new(() =>
    {
        using var stream = typeof(ShippedPackCoversExportTests).Assembly.GetManifestResourceStream("adof-export.json")!;
        return JsonDocument.Parse(stream);
    });

    /// <summary>Objects outside the export the pack may use: people matching, files, approvals and field history (standard objects).</summary>
    private static readonly string[] StandardObjects = ["User", "Contact", "ContentDocumentLink", "ContentVersion", "Attachment", "ProcessInstance"];

    public static TheoryData<string> ExportObjects()
    {
        var data = new TheoryData<string>();
        foreach (var o in Export.Value.RootElement.GetProperty("objects").EnumerateObject())
        {
            data.Add(o.Name);
        }

        return data;
    }

    private static JsonElement Fields(string sourceObject) => Export.Value.RootElement.GetProperty("objects").GetProperty(sourceObject).GetProperty("fields");

    private static async Task<List<MappingPackObject>> RecordMappingsAsync(string sourceObject) =>
        [.. (await ShippedMappingPacks.AdofSalesforceAsync()).Objects.Where(o => o.Kind == ObjectMappingKinds.Record && o.SourceObject == sourceObject)];

    /// <summary>The field name a mapping source reads (<c>RecordType.DeveloperName</c> → <c>RecordTypeId</c>, <c>Owner.Email</c> → <c>OwnerId</c>).</summary>
    private static string SourceField(string source)
    {
        var dot = source.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return source;
        }

        var relationship = source[..dot];
        return relationship.EndsWith("__r", StringComparison.Ordinal) ? relationship[..^3] + "__c" : relationship + "Id";
    }

    [Fact]
    public async Task Every_object_of_the_pack_exists_in_salesforce()
    {
        var pack = await ShippedMappingPacks.AdofSalesforceAsync();
        var objects = Export.Value.RootElement.GetProperty("objects");
        var unknown = pack.Objects
            .Where(o => o.Kind is ObjectMappingKinds.Record or ObjectMappingKinds.People)
            .Where(o => !StandardObjects.Contains(o.SourceObject) && !objects.TryGetProperty(o.SourceObject, out _))
            .Select(o => $"{o.Key} → {o.SourceObject}").ToList();
        unknown.ShouldBeEmpty("Pack objects whose Salesforce object does not exist in the owner's org");
    }

    [Theory]
    [MemberData(nameof(ExportObjects))]
    public async Task Every_data_field_of_the_object_is_held_and_every_mapped_field_exists(string sourceObject)
    {
        var mappings = await RecordMappingsAsync(sourceObject);
        mappings.ShouldNotBeEmpty($"{sourceObject} is not synced by any pack object");
        var fields = Fields(sourceObject);
        var mapped = mappings.SelectMany(m => m.Fields).Where(f => f.Source is not null).Select(f => SourceField(f.Source!)).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var name in mapped.Where(n => !fields.TryGetProperty(n, out _)))
        {
            problems.Add($"mapped field {name} does not exist");
        }

        foreach (var template in mappings.Select(m => m.ExternalIdTemplate))
        {
            foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(template, "\\{(?:year:)?([A-Za-z0-9_]+)\\}"))
            {
                if (!fields.TryGetProperty(token.Groups[1].Value, out _))
                {
                    problems.Add($"external-ID template field {token.Groups[1].Value} does not exist");
                }
            }
        }

        var ignored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in mappings.SelectMany(m => m.Ignore))
        {
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            ignored[(colon < 0 ? entry : entry[..colon]).Trim()] = colon < 0 ? string.Empty : entry[(colon + 1)..].Trim();
        }

        foreach (var field in fields.EnumerateObject())
        {
            if (field.Value.GetProperty("category").GetString() != "data" || mapped.Contains(field.Name))
            {
                continue;
            }

            if (!ignored.TryGetValue(field.Name, out var reason))
            {
                problems.Add($"data field {field.Name} ({field.Value.GetProperty("sfType").GetString()}) is not held");
            }
            else if (reason.Length == 0)
            {
                problems.Add($"data field {field.Name} is ignored without a reason (\"{field.Name}: reason\")");
            }
        }

        problems.ShouldBeEmpty($"{sourceObject}: every Salesforce data field is held (ADR 0215)");
    }

    [Theory]
    [MemberData(nameof(ExportObjects))]
    public async Task Picklists_are_mapped_as_picklists_and_every_active_value_has_a_code(string sourceObject)
    {
        var fields = Fields(sourceObject);
        var problems = new List<string>();
        foreach (var mapping in (await RecordMappingsAsync(sourceObject)).SelectMany(m => m.Fields.Select(f => (m.Key, Field: f))))
        {
            if (mapping.Field.Source is not { } source || source.Contains('.', StringComparison.Ordinal) || !fields.TryGetProperty(source, out var described))
            {
                continue;
            }

            var type = described.GetProperty("type").GetString();
            if (type is not ("picklist" or "multipicklist"))
            {
                continue;
            }

            if (mapping.Field.Kind != FieldMappingKinds.Picklist)
            {
                problems.Add($"{mapping.Key}: {source} is a {type} mapped as {mapping.Field.Kind}");
                continue;
            }

            var values = mapping.Field.Values ?? [];
            if (values.ContainsKey("*") || !described.TryGetProperty("values", out var listed))
            {
                continue;
            }

            foreach (var value in listed.EnumerateArray().Where(v => v.GetProperty("active").GetBoolean()).Select(v => v.GetProperty("value").GetString()!))
            {
                if (!values.ContainsKey(value))
                {
                    problems.Add($"{mapping.Key}: active value “{value}” of {source} has no code");
                }
            }
        }

        problems.ShouldBeEmpty($"{sourceObject}: picklists keep their values as codes (ADR 0216)");
    }
}
