using System.Globalization;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>One row a module returned for one node: its raw values by field code and its row id (record link).</summary>
public sealed class NodeRow(IReadOnlyDictionary<string, object?> values, Guid? rowId)
{
    public IReadOnlyDictionary<string, object?> Values { get; } = values;

    public Guid? RowId { get; } = rowId;

    public object? this[string field] => Values.TryGetValue(field, out var value) ? value : null;
}

/// <summary>A composed (flattened) report row: one node row (or none) per alias.</summary>
public sealed class FlatRow
{
    private readonly Dictionary<string, NodeRow> _parts;

    public FlatRow(Dictionary<string, NodeRow> parts) => _parts = parts;

    public IReadOnlyDictionary<string, NodeRow> Parts => _parts;

    public object? Value(string alias, string field) => _parts.TryGetValue(alias, out var row) ? row[field] : null;

    public object? Value(string qualified) => ReportTypeRules.Split(qualified) is { } p ? Value(p.Alias, p.Field) : null;

    internal FlatRow With(string alias, NodeRow row) => new(new Dictionary<string, NodeRow>(_parts, StringComparer.Ordinal) { [alias] = row });

    internal FlatRow Merge(FlatRow other)
    {
        var parts = new Dictionary<string, NodeRow>(_parts, StringComparer.Ordinal);
        foreach (var (alias, row) in other._parts)
        {
            parts[alias] = row;
        }

        return new FlatRow(parts);
    }
}

/// <summary>A node as the composer sees it: its relationship fields and the child rows the runner may see, by parent key.</summary>
public sealed record ComposerNode(string Alias, string? Parent, string Join, string? ParentField, string? ChildField, IReadOnlyList<NodeRow> Rows)
{
    private ILookup<string, NodeRow>? _byKey;

    /// <summary>Rows by the (normalised) value of the child field.</summary>
    public ILookup<string, NodeRow> ByKey => _byKey ??= Rows.Where(r => ChildField is not null && r[ChildField] is not null)
        .ToLookup(r => ReportComposer.Key(r[ChildField!]), StringComparer.Ordinal);
}

/// <summary>
/// Joins the node rows in the reporting layer (ADR 0181; pure, unit-tested). A primary row is repeated once per related row; siblings (e.g.
/// risks and KPIs of a project) are listed one after the other — never multiplied with each other. "With" (inner) drops a parent without
/// related rows; "with or without" (outer) keeps it with empty related columns. Related rows are only those the module returned for the
/// runner, so a row the runner may not see never joins.
/// </summary>
public static class ReportComposer
{
    public static string Key(object? value) => value switch
    {
        null => string.Empty,
        Guid g => g.ToString("D"),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public static List<FlatRow> Compose(IReadOnlyList<ComposerNode> nodes, int limit, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var root = nodes[0];
        var children = nodes.Skip(1).ToLookup(n => n.Parent!, StringComparer.Ordinal);
        var result = new List<FlatRow>();
        truncated = false;
        foreach (var row in root.Rows)
        {
            foreach (var flat in Expand(root, row, children))
            {
                if (result.Count >= limit)
                {
                    truncated = true;
                    return result;
                }

                result.Add(flat);
            }
        }

        return result;
    }

    /// <summary>The flattened rows below <paramref name="row"/> of <paramref name="node"/> (empty = the row is dropped by an inner join).</summary>
    private static List<FlatRow> Expand(ComposerNode node, NodeRow row, ILookup<string, ComposerNode> children)
    {
        var self = new FlatRow(new Dictionary<string, NodeRow>(StringComparer.Ordinal) { [node.Alias] = row });
        var kids = children[node.Alias].ToList();
        if (kids.Count == 0)
        {
            return [self];
        }

        var blocks = new List<FlatRow>();
        foreach (var child in kids)
        {
            var key = Key(row[child.ParentField!]);
            var matches = key.Length == 0 ? [] : child.ByKey[key].ToList();
            var expanded = matches.SelectMany(m => Expand(child, m, children)).ToList();
            if (expanded.Count == 0 && child.Join == JoinKinds.Inner)
            {
                return [];
            }

            blocks.AddRange(expanded.Select(self.Merge));
        }

        return blocks.Count == 0 ? [self] : blocks;
    }
}
