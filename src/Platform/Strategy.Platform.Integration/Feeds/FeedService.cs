using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Strategy.Platform.Audit;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Reporting;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>The OData feeds and the incremental extracts (docs/design/integrations.md §3–4) for one request.</summary>
public sealed class FeedService(FeedCatalog catalog, FeedLabels labels, ISettingsStore settings, IAuditLog audit, IHttpContextAccessor http)
{
    public const string ODataVersion = "4.0";

    private IReadOnlyCollection<string> AllowedRecordTypes =>
        http.HttpContext?.Items[IntegrationClientAuthority.CallContextKey] is ClientCallContext call ? call.AllowedRecordTypes : [];

    /// <summary>The model of a system for the caller, or null when the caller may read nothing of it.</summary>
    public async Task<ODataModel?> ModelAsync(ClaimsPrincipal user, string systemCode, CancellationToken ct)
    {
        var types = await catalog.VisibleAsync(user, systemCode, AllowedRecordTypes, ct).ConfigureAwait(false);
        return types.Count == 0 ? null : new ODataModel(systemCode, types);
    }

    public async Task<IReadOnlyList<VisibleRecordType>> VisibleAsync(ClaimsPrincipal user, string? systemCode, CancellationToken ct) =>
        await catalog.VisibleAsync(user, systemCode, AllowedRecordTypes, ct).ConfigureAwait(false);

    /// <summary>Executes an OData collection / entity / count request and writes the JSON (or plain-text count) response.</summary>
    public async Task WriteODataAsync(HttpContext context, ODataModel model, ODataRequest request, string serviceRoot, string lang, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var options = await settings.GetAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        var filter = request.Filter;
        if (request.Key is { } key)
        {
            var byKey = new RecordFilterCompare(RecordFields.Id, RecordCompare.Eq, key);
            filter = filter is null ? byKey : new RecordFilterAnd(byKey, filter);
        }

        if (request.CountOnly)
        {
            var counted = await request.Type.Source.QueryAsync(
                new RecordQuery(request.Type.Code, [RecordFields.Id], filter, [], 0, 0, IncludeTotal: true), context.User, ct).ConfigureAwait(false);
            await AuditAsync(model.SystemCode, request.Type, 0, "count", ct).ConfigureAwait(false);
            context.Response.Headers["OData-Version"] = ODataVersion;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync((counted.Total ?? 0).ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            return;
        }

        var skip = (int)Math.Clamp(request.Skip ?? 0, 0, int.MaxValue);
        var pageSize = Math.Clamp(options.ODataPageSize, 1, 5000);
        var take = request.Top is { } top ? (int)Math.Min(top, pageSize) : pageSize;
        var query = new RecordQuery(request.Type.Code, request.Fields, filter, request.OrderBy, skip, take + 1, request.Count && request.Key is null);
        var (rows, total) = await FeedReader.ReadAsync(request.Type, query, request.Expands, context.User, options.ExpandLimit, ct).ConfigureAwait(false);
        var more = rows.Count > take && (request.Top is null || request.Top > take);
        var page = rows.Take(take).ToList();
        await PrepareLabelsAsync(request.Type, request.Expands, page, ct).ConfigureAwait(false);
        context.Items[IntegrationGateMiddleware.RowsKey] = page.Count;
        await AuditAsync(model.SystemCode, request.Type, page.Count, request.Key is null ? "collection" : "entity", ct).ConfigureAwait(false);

        if (request.Key is not null && page.Count == 0)
        {
            await ODataEndpoints.ErrorAsync(context, StatusCodes.Status404NotFound, "notFound", "No such entity.").ConfigureAwait(false);
            return;
        }

        context.Response.Headers["OData-Version"] = ODataVersion;
        context.Response.ContentType = "application/json; odata.metadata=minimal; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        if (request.Key is not null)
        {
            writer.WriteString("@odata.context", $"{serviceRoot}$metadata#{request.Type.EntitySet}/$entity");
            WriteRowBody(writer, request.Type, request.Output, request.Expands, page[0], lang);
            writer.WriteEndObject();
            await writer.FlushAsync(ct).ConfigureAwait(false);
            return;
        }

        writer.WriteString("@odata.context", $"{serviceRoot}$metadata#{request.Type.EntitySet}");
        if (total is { } count)
        {
            writer.WriteNumber("@odata.count", count);
        }

        writer.WriteStartArray("value");
        foreach (var row in page)
        {
            writer.WriteStartObject();
            WriteRowBody(writer, request.Type, request.Output, request.Expands, row, lang);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (more)
        {
            writer.WriteString("@odata.nextLink", NextLink(context.Request, skip + take, request.Top is { } t ? t - take : null));
        }

        writer.WriteEndObject();
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An incremental extract page (changed or deleted records since a time, keyset cursor), every field the caller may read with labels.
    /// </summary>
    public async Task<ExtractPage> ExtractAsync(
        ClaimsPrincipal user, VisibleRecordType type, RecordQueryMode mode, DateTimeOffset? changedSince, RecordCursor? cursor, int? pageSize, string lang,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(type);
        var options = await settings.GetAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        var size = Math.Clamp(pageSize ?? options.ExtractMaxPageSize, 1, options.ExtractMaxPageSize);
        var fields = type.AllFields.Select(f => f.Code).ToList();
        var page = await type.Source.QueryAsync(
            new RecordQuery(type.Code, fields, null, [], 0, size + 1, ChangedSince: changedSince ?? DateTimeOffset.UnixEpoch, After: cursor, Mode: mode),
            user, ct).ConfigureAwait(false);
        var rows = page.Rows.Take(size).Select(r =>
        {
            var row = new FeedRow();
            for (var i = 0; i < fields.Count; i++)
            {
                row.Values[fields[i]] = r[i];
            }

            return row;
        }).ToList();
        await PrepareLabelsAsync(type, [], rows, ct).ConfigureAwait(false);
        var items = rows.Select(row =>
        {
            var item = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in type.AllFields)
            {
                var value = row.Values.GetValueOrDefault(field.Code);
                item[field.Code] = JsonValue(value);
                if (FeedLabels.HasLabel(field))
                {
                    item[FeedLabels.LabelProperty(field.Code)] = labels.Label(field, value, lang);
                }
            }

            return item;
        }).ToList();
        var last = rows.LastOrDefault();
        var nextCursor = last is null ? null : Cursor.Encode((DateTimeOffset)last.Values[RecordFields.ModifiedAt]!, (Guid)last.Values[RecordFields.Id]!);
        var watermark = rows.Count == 0 ? changedSince : rows.Max(r => (DateTimeOffset)r.Values[RecordFields.ModifiedAt]!);
        http.HttpContext?.Items.TryAdd(IntegrationGateMiddleware.RowsKey, items.Count);
        await AuditAsync(type.SystemCode, type, items.Count, mode == RecordQueryMode.Deleted ? "extract.deleted" : "extract.changed", ct).ConfigureAwait(false);
        return new ExtractPage(type.SystemCode, type.EntitySet, mode == RecordQueryMode.Deleted, items, page.Rows.Count > size, nextCursor, watermark,
            [.. type.AllFields.SelectMany(f => FeedLabels.HasLabel(f) ? new[] { f.Code, FeedLabels.LabelProperty(f.Code) } : [f.Code])]);
    }

    public static string Csv(ExtractPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var builder = new StringBuilder();
        builder.Append('﻿');
        builder.AppendJoin(',', page.Columns.Select(CsvCell)).Append("\r\n");
        foreach (var item in page.Items)
        {
            builder.AppendJoin(',', page.Columns.Select(c => CsvCell(Convert.ToString(item.GetValueOrDefault(c), CultureInfo.InvariantCulture)))).Append("\r\n");
        }

        return builder.ToString();
    }

    private static string CsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Formula injection guard for spreadsheets (=, +, -, @ at the start).
        if (value[0] is '=' or '+' or '-' or '@' && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private async Task PrepareLabelsAsync(VisibleRecordType type, IReadOnlyList<FeedExpand> expands, IReadOnlyList<FeedRow> rows, CancellationToken ct)
    {
        await labels.PrepareAsync(type.AllFields.Where(FeedLabels.HasLabel).Select(f => (f, rows.Select(r => r.Values.GetValueOrDefault(f.Code)))).ToList(), ct)
            .ConfigureAwait(false);
        foreach (var expand in expands)
        {
            var children = rows.SelectMany(r => r.Expanded.GetValueOrDefault(expand.Relationship.Name) switch
            {
                List<FeedRow> list => list,
                FeedRow single => [single],
                _ => [],
            }).ToList();
            await PrepareLabelsAsync(expand.Target, expand.Expands, children, ct).ConfigureAwait(false);
        }
    }

    private void WriteRowBody(Utf8JsonWriter writer, VisibleRecordType type, IReadOnlySet<string>? output, IReadOnlyList<FeedExpand> expands, FeedRow row, string lang)
    {
        foreach (var field in type.AllFields)
        {
            var value = row.Values.GetValueOrDefault(field.Code);
            if (output is null || output.Contains(field.Code))
            {
                writer.WritePropertyName(field.Code);
                WriteValue(writer, value);
            }

            var label = FeedLabels.LabelProperty(field.Code);
            if (FeedLabels.HasLabel(field) && (output is null || output.Contains(label)))
            {
                writer.WriteString(label, labels.Label(field, value, lang));
            }
        }

        foreach (var expand in expands)
        {
            writer.WritePropertyName(expand.Relationship.Name);
            switch (row.Expanded.GetValueOrDefault(expand.Relationship.Name))
            {
                case List<FeedRow> list:
                    writer.WriteStartArray();
                    foreach (var child in list)
                    {
                        writer.WriteStartObject();
                        WriteRowBody(writer, expand.Target, expand.Output, expand.Expands, child, lang);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    break;
                case FeedRow single:
                    writer.WriteStartObject();
                    WriteRowBody(writer, expand.Target, expand.Output, expand.Expands, single, lang);
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }

    internal static object? JsonValue(object? value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        _ => value,
    };

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case DateOnly or DateTimeOffset:
                writer.WriteStringValue((string)JsonValue(value)!);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static string NextLink(HttpRequest request, int skip, long? top)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(q => q.Key is not "$skip" and not "$top")
            .Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString()))
            .Append(new KeyValuePair<string, string?>("$skip", skip.ToString(CultureInfo.InvariantCulture)));
        if (top is { } remaining)
        {
            query = query.Append(new KeyValuePair<string, string?>("$top", remaining.ToString(CultureInfo.InvariantCulture)));
        }

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}", query);
    }

    private Task AuditAsync(string system, VisibleRecordType type, int rows, string kind, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent("integration.feed.query", true, system, null, null, new { type = type.Code, kind, rows }), ct);
}

/// <summary>One extract page: <c>Items</c> (codes + labels), <c>NextCursor</c> to continue, <c>Watermark</c> = next run's <c>changedSince</c>.</summary>
public sealed record ExtractPage(
    string System, string EntitySet, bool Deleted, IReadOnlyList<Dictionary<string, object?>> Items, bool HasMore, string? NextCursor, DateTimeOffset? Watermark,
    IReadOnlyList<string> Columns);

/// <summary>Opaque keyset cursor <c>base64url(modifiedAt ticks|id)</c>.</summary>
public static class Cursor
{
    public static string Encode(DateTimeOffset modifiedAt, Guid id) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            Encoding.ASCII.GetBytes(modifiedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + id.ToString("N")));

    public static bool TryDecode(string? text, out RecordCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        try
        {
            var parts = Encoding.ASCII.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(text)).Split('|');
            if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && Guid.TryParseExact(parts[1], "N", out var id)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks)
            {
                cursor = new RecordCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
