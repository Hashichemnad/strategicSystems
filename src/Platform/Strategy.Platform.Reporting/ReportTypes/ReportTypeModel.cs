namespace Strategy.Platform.Reporting.ReportTypes;

public static class JoinKinds
{
    /// <summary>"With": a parent row appears only when it has at least one visible related row.</summary>
    public const string Inner = "inner";

    /// <summary>"With or without": a parent row appears with empty related columns when it has none.</summary>
    public const string Outer = "outer";
}

/// <summary>
/// One object of a report type (ADR 0181). The first node is the primary object (no relationship, no parent); every other node follows a
/// declared <see cref="DatasetRelationship"/> from its parent node. <see cref="Fields"/>: the fields offered (null = every field of the
/// dataset); the runner's own field permissions apply on top at every run.
/// </summary>
public sealed record ReportTypeNode
{
    /// <summary>Short alias used to qualify columns (<c>a.number</c>, <c>b.title</c>): lowercase letter(s) unique in the type.</summary>
    public string Alias { get; init; } = string.Empty;

    public string Dataset { get; init; } = string.Empty;

    public string? Relationship { get; init; }

    public string? Parent { get; init; }

    public string Join { get; init; } = JoinKinds.Outer;

    public List<string>? Fields { get; init; }
}

/// <summary>The stored shape of a report type: the primary node first, then related nodes (parents before children).</summary>
public sealed record ReportTypeDefinition
{
    public List<ReportTypeNode> Nodes { get; init; } = [];
}

/// <summary>Validation of a report-type definition against the reportable metadata (pure; unit-tested).</summary>
public static class ReportTypeRules
{
    /// <summary>Related levels below the primary object (Projects → Milestones → Tasks → … ).</summary>
    public const int MaxDepth = 3;

    public const int MaxNodes = 6;

    public const string ImplicitPrefix = "dataset:";

    /// <summary>A single-object type for every dataset (<c>dataset:&lt;code&gt;</c>), so the builder treats one object and many alike.</summary>
    public static ReportTypeDefinition Implicit(string dataset) => new() { Nodes = [new ReportTypeNode { Alias = "a", Dataset = dataset }] };

    /// <summary>Message keys of the problems found (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(ReportTypeDefinition definition, IReportableMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(metadata);
        var errors = new List<string>();
        if (definition.Nodes is null || definition.Nodes.Count == 0)
        {
            return ["reporting.errors.reportType.primary"];
        }

        if (definition.Nodes.Count > MaxNodes)
        {
            errors.Add("reporting.errors.reportType.tooMany");
        }

        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var datasets = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < definition.Nodes.Count; i++)
        {
            var node = definition.Nodes[i];
            if (string.IsNullOrWhiteSpace(node.Alias) || node.Alias.Length > 3 || !node.Alias.All(char.IsAsciiLetterLower) || depth.ContainsKey(node.Alias))
            {
                errors.Add("reporting.errors.reportType.alias");
                continue;
            }

            var dataset = metadata.Dataset(node.Dataset);
            if (dataset is null)
            {
                errors.Add("reporting.errors.reportType.dataset");
                continue;
            }

            if (i == 0)
            {
                if (node.Relationship is not null || node.Parent is not null || dataset.JoinOnly)
                {
                    errors.Add("reporting.errors.reportType.primary");
                }

                depth[node.Alias] = 0;
            }
            else
            {
                if (node.Parent is null || !depth.TryGetValue(node.Parent, out var parentDepth))
                {
                    errors.Add("reporting.errors.reportType.parent");
                    continue;
                }

                var relationship = metadata.Relationships.FirstOrDefault(r => r.Code == node.Relationship);
                if (relationship is null || relationship.ParentDataset != datasets[node.Parent] || relationship.ChildDataset != node.Dataset)
                {
                    errors.Add("reporting.errors.reportType.relationship");
                    continue;
                }

                if (node.Join is not (JoinKinds.Inner or JoinKinds.Outer))
                {
                    errors.Add("reporting.errors.reportType.join");
                }

                if (parentDepth + 1 > MaxDepth)
                {
                    errors.Add("reporting.errors.reportType.depth");
                }

                depth[node.Alias] = parentDepth + 1;
            }

            datasets[node.Alias] = node.Dataset;
            if (node.Fields is { } fields
                && (fields.Count == 0 || fields.Any(f => !dataset.Fields.Any(d => d.Code == f && d.Type != DatasetFieldType.Reference))))
            {
                errors.Add("reporting.errors.reportType.fields");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Splits <c>b.title</c> into (<c>b</c>, <c>title</c>); null when not qualified.</summary>
    public static (string Alias, string Field)? Split(string column)
    {
        var dot = column?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        return dot is > 0 && dot < column!.Length - 1 ? (column[..dot], column[(dot + 1)..]) : null;
    }
}
