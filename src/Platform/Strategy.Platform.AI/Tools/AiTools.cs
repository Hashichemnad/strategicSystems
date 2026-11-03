using System.Text.Json;
using Strategy.Platform.AI.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Settings;

namespace Strategy.Platform.AI.Tools;

/// <summary><c>search_records</c>: full-text search over every record type the caller may read (scope applied in the database).</summary>
internal sealed class SearchRecordsTool(IEnumerable<IRecordSearchSource> sources, IAccessService access, ISystemState systems) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "search_records", "ai", ToolKind.Read, "البحث في السجلات", "Search records",
        "Searches the records the signed-in user may see (by number or title, Arabic or English). Returns type, id, number, titles, status and a link.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "query":{"type":"string","description":"Words or a record number to look for","maxLength":200},
              "types":{"type":"array","items":{"type":"string"},"description":"Optional record types, e.g. sample.item"},
              "limit":{"type":"integer","minimum":1,"maximum":25}},
             "required":["query"]}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.Text("query")?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length > 200)
        {
            return ToolResult.Fail(ToolErrors.Validation, "query is required (at most 200 characters).");
        }

        var limit = Math.Clamp(arguments.Number("limit") ?? 10, 1, 25);
        var types = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).ToHashSet(StringComparer.Ordinal)
            : null;
        var hits = new List<RecordSearchHit>();
        foreach (var source in sources.Where(s => types is null || types.Contains(s.ResourceType)))
        {
            if (await systems.IsEnabledAsync(source.SystemCode, cancellationToken).ConfigureAwait(false)
                && await access.CanAsync(context.User, source.ReadPermission, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                hits.AddRange(await source.SearchAsync(query, context.User, limit, cancellationToken).ConfigureAwait(false));
            }
        }

        return ToolResult.Ok(new { results = hits.OrderByDescending(h => h.Score).Take(limit).ToList() });
    }
}

/// <summary><c>get_record</c>: a record's fields for a caller who may read it; fields excluded from AI in Settings → AI are left out.</summary>
internal sealed class GetRecordTool(IEnumerable<IAiRecordSource> sources, ISystemState systems, ISettingsStore settings) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "get_record", "ai", ToolKind.Read, "عرض سجل", "Get a record",
        "Returns the fields of one record the signed-in user may see. Use search_records first to find its type and id.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "resourceType":{"type":"string","description":"Record type, e.g. sample.item"},
              "id":{"type":"string","description":"Record id (GUID)"}},
             "required":["resourceType","id"]}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var source = sources.FirstOrDefault(s => s.ResourceType == arguments.Text("resourceType"));
        if (source is null || arguments.Id("id") is not { } id)
        {
            return ToolResult.NotFound();
        }

        var record = await source.GetAsync(id, context.User, cancellationToken).ConfigureAwait(false);
        if (record is null || !await systems.IsEnabledAsync(record.SystemCode, cancellationToken).ConfigureAwait(false))
        {
            return ToolResult.NotFound();
        }

        var sharing = (await settings.GetAsync<AiSettings>(cancellationToken).ConfigureAwait(false)).Sharing.System(record.SystemCode);
        if (!sharing.Allowed)
        {
            return ToolResult.Fail(ToolErrors.Forbidden, "Data of this system is not shared with AI clients.");
        }

        return ToolResult.Ok(new
        {
            record.ResourceType,
            record.Id,
            record.Number,
            record.TitleAr,
            record.TitleEn,
            record.Link,
            fields = record.Fields.Where(f => !sharing.Excludes(f.Name)).Select(f => new { f.Name, label = context.Language == "ar" ? f.LabelAr : f.LabelEn, f.Value }),
        });
    }
}
