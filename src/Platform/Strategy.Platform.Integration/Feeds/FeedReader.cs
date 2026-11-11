using System.Security.Claims;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>An expansion of one relationship: what to read of the target type, nested.</summary>
public sealed record FeedExpand(
    RecordRelationship Relationship, VisibleRecordType Target, IReadOnlyList<string> Fields, RecordFilter? Filter, IReadOnlyList<RecordSort> OrderBy, int? Top,
    IReadOnlyList<FeedExpand> Expands, IReadOnlySet<string>? Output = null);

/// <summary>A row read for a feed: field values and expanded relations (lists or single rows).</summary>
public sealed class FeedRow
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Expanded { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads feed rows through the module's record source as the caller (every level scoped by its own source) and expands relationships
/// with one query per relationship (<c>In</c> on the join field) — no cross-schema SQL (ADR 0190 / 0192).
/// </summary>
public static class FeedReader
{
    public static async Task<(IReadOnlyList<FeedRow> Rows, long? Total)> ReadAsync(
        VisibleRecordType type, RecordQuery query, IReadOnlyList<FeedExpand> expands, ClaimsPrincipal user, int expandLimit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(expands);
        var fields = query.Fields.Concat(expands.Select(e => e.Relationship.SourceField)).Distinct(StringComparer.Ordinal).ToList();
        var page = await type.Source.QueryAsync(query with { Fields = fields }, user, ct).ConfigureAwait(false);
        var rows = page.Rows.Select(r =>
        {
            var row = new FeedRow();
            for (var i = 0; i < fields.Count; i++)
            {
                row.Values[fields[i]] = r[i];
            }

            return row;
        }).ToList();
        await ExpandAsync(rows, expands, user, expandLimit, ct).ConfigureAwait(false);
        return (rows, page.Total);
    }

    private static async Task ExpandAsync(List<FeedRow> rows, IReadOnlyList<FeedExpand> expands, ClaimsPrincipal user, int expandLimit, CancellationToken ct)
    {
        foreach (var expand in expands)
        {
            var relation = expand.Relationship;
            var keys = rows.Select(r => r.Values.GetValueOrDefault(relation.SourceField)).Where(v => v is not null).Distinct().ToList();
            var children = new List<FeedRow>();
            if (keys.Count > 0)
            {
                var filter = (RecordFilter)new RecordFilterIn(relation.TargetField, keys);
                if (expand.Filter is not null)
                {
                    filter = new RecordFilterAnd(filter, expand.Filter);
                }

                var query = new RecordQuery(expand.Target.Code, expand.Fields.Append(relation.TargetField).Distinct(StringComparer.Ordinal).ToList(), filter,
                    expand.OrderBy, 0, expandLimit + 1);
                var (read, _) = await ReadAsync(expand.Target, query, expand.Expands, user, expandLimit, ct).ConfigureAwait(false);
                if (read.Count > expandLimit)
                {
                    throw new FeedQueryException("expandTooLarge", $"More than {expandLimit} related rows; narrow the query or page the parent.");
                }

                children = [.. read];
            }

            var groups = children.GroupBy(c => c.Values.GetValueOrDefault(relation.TargetField)).ToDictionary(g => g.Key!, g => g.ToList());
            foreach (var row in rows)
            {
                var key = row.Values.GetValueOrDefault(relation.SourceField);
                var matched = key is not null && groups.TryGetValue(key, out var list) ? list : [];
                if (relation.Many)
                {
                    row.Expanded[relation.Name] = expand.Top is { } top ? matched.Take(top).ToList() : matched;
                }
                else
                {
                    row.Expanded[relation.Name] = matched.FirstOrDefault();
                }
            }
        }
    }
}

/// <summary>A feed query the server refuses (400 with an OData error code).</summary>
public sealed class FeedQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
