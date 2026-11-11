using Microsoft.OData;
using Microsoft.OData.UriParser;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>
/// A parsed OData request (ADR 0192). <see cref="Output"/>: the properties to write (null = every property of the type: fields, labels).
/// </summary>
public sealed record ODataRequest(
    VisibleRecordType Type,
    Guid? Key,
    bool CountOnly,
    IReadOnlyList<string> Fields,
    IReadOnlySet<string>? Output,
    RecordFilter? Filter,
    IReadOnlyList<RecordSort> OrderBy,
    long? Top,
    long? Skip,
    bool Count,
    IReadOnlyList<FeedExpand> Expands);

/// <summary>Parses OData URLs with Microsoft.OData.Core and translates them to the neutral record query (only the supported subset).</summary>
public static class ODataQuery
{
    public static ODataRequest Parse(ODataModel model, Uri serviceRoot, Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            var parser = new ODataUriParser(model.Model, serviceRoot, requestUri)
            {
                Resolver = new ODataUriResolver { EnableCaseInsensitive = false },
            };
            foreach (var unsupported in new[] { "$apply", "$search", "$compute" })
            {
                if (requestUri.Query.Contains(unsupported, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FeedQueryException("notImplemented", $"{unsupported} is not supported.");
                }
            }

            var path = parser.ParsePath();
            VisibleRecordType? type = null;
            Guid? key = null;
            var countOnly = false;
            foreach (var segment in path)
            {
                switch (segment)
                {
                    case EntitySetSegment set when type is null:
                        type = model.FindSet(set.EntitySet.Name);
                        break;
                    case KeySegment keySegment when type is not null && key is null:
                        key = keySegment.Keys.Single().Value is Guid id ? id : throw new FeedQueryException("badRequest", "The key must be a GUID.");
                        break;
                    case CountSegment when type is not null && key is null:
                        countOnly = true;
                        break;
                    default:
                        throw new FeedQueryException("notImplemented", "Only entity sets, single entities and $count are supported.");
                }
            }

            if (type is null)
            {
                throw new FeedQueryException("notFound", "Unknown entity set.");
            }

            var (fields, output, expands) = Selection(model, type, parser.ParseSelectAndExpand());
            var filter = parser.ParseFilter() is { } clause ? Translate(type, clause.Expression) : null;
            var order = parser.ParseOrderBy() is { } orderBy ? Order(type, orderBy) : [];
            return new ODataRequest(type, key, countOnly, fields, output, filter, order, parser.ParseTop(), parser.ParseSkip(), parser.ParseCount() ?? false, expands);
        }
        catch (ODataException ex)
        {
            throw new FeedQueryException("badRequest", ex.Message);
        }
    }

    private static (IReadOnlyList<string> Fields, IReadOnlySet<string>? Output, IReadOnlyList<FeedExpand> Expands) Selection(
        ODataModel model, VisibleRecordType type, SelectExpandClause? clause)
    {
        var expands = new List<FeedExpand>();
        HashSet<string>? output = null;
        if (clause is not null)
        {
            foreach (var item in clause.SelectedItems)
            {
                switch (item)
                {
                    case PathSelectItem path when path.SelectedPath.FirstSegment is PropertySegment property:
                        output ??= new HashSet<string>(StringComparer.Ordinal);
                        output.Add(property.Property.Name);
                        break;
                    case WildcardSelectItem:
                        break;
                    case ExpandedNavigationSelectItem expanded when expanded.PathToNavigationProperty.FirstSegment is NavigationPropertySegment navigation:
                        expands.Add(Expand(model, type, navigation.NavigationProperty.Name, expanded));
                        break;
                    default:
                        throw new FeedQueryException("notImplemented", "This $select / $expand form is not supported.");
                }
            }

            if (clause.AllSelected)
            {
                output = null;
            }
        }

        var fields = type.AllFields.Select(f => f.Code)
            .Where(code => output is null || output.Contains(code) || output.Contains(FeedLabels.LabelProperty(code)) || code == RecordFields.Id)
            .ToList();
        if (output is not null)
        {
            // Codes are always included with their labels; the key is always written.
            foreach (var code in fields)
            {
                output.Add(code);
            }
        }

        return (fields, output, expands);
    }

    private static FeedExpand Expand(ODataModel model, VisibleRecordType type, string name, ExpandedNavigationSelectItem item)
    {
        var relation = type.Relationships.FirstOrDefault(r => r.Name == name) ?? throw new FeedQueryException("badRequest", $"Unknown relationship {name}.");
        var target = model.Types.First(t => t.Code == relation.TargetType);
        if (item.SkipOption is not null || item.CountOption is not null || item.SearchOption is not null || item.LevelsOption is not null)
        {
            throw new FeedQueryException("notImplemented", "Only $select, $filter, $orderby, $top and $expand are supported inside $expand.");
        }

        var (fields, output, nested) = Selection(model, target, item.SelectAndExpand);
        var filter = item.FilterOption is { } clause ? Translate(target, clause.Expression) : null;
        var order = item.OrderByOption is { } orderBy ? Order(target, orderBy) : [];
        return new FeedExpand(relation, target, fields, filter, order, item.TopOption is { } top ? (int)Math.Min(top, int.MaxValue) : null, nested, output);
    }

    private static List<RecordSort> Order(VisibleRecordType type, OrderByClause clause)
    {
        var result = new List<RecordSort>();
        for (var current = clause; current is not null; current = current.ThenBy)
        {
            result.Add(new RecordSort(FieldOf(type, current.Expression).Code, current.Direction == OrderByDirection.Descending));
        }

        return result;
    }

    internal static RecordFilter Translate(VisibleRecordType type, SingleValueNode node) => node switch
    {
        ConvertNode convert => Translate(type, convert.Source),
        BinaryOperatorNode { OperatorKind: BinaryOperatorKind.And } and => new RecordFilterAnd(Translate(type, and.Left), Translate(type, and.Right)),
        BinaryOperatorNode { OperatorKind: BinaryOperatorKind.Or } or => new RecordFilterOr(Translate(type, or.Left), Translate(type, or.Right)),
        UnaryOperatorNode { OperatorKind: UnaryOperatorKind.Not } not => new RecordFilterNot(Translate(type, not.Operand)),
        BinaryOperatorNode binary => Comparison(type, binary),
        SingleValueFunctionCallNode call => Function(type, call),
        InNode @in => In(type, @in),
        _ => throw new FeedQueryException("notImplemented", $"This filter expression ({node.Kind}) is not supported."),
    };

    private static RecordFilterCompare Comparison(VisibleRecordType type, BinaryOperatorNode node)
    {
        var op = node.OperatorKind switch
        {
            BinaryOperatorKind.Equal => RecordCompare.Eq,
            BinaryOperatorKind.NotEqual => RecordCompare.Ne,
            BinaryOperatorKind.GreaterThan => RecordCompare.Gt,
            BinaryOperatorKind.GreaterThanOrEqual => RecordCompare.Ge,
            BinaryOperatorKind.LessThan => RecordCompare.Lt,
            BinaryOperatorKind.LessThanOrEqual => RecordCompare.Le,
            _ => throw new FeedQueryException("notImplemented", $"Operator {node.OperatorKind} is not supported."),
        };
        var left = Unwrap(node.Left);
        var right = Unwrap(node.Right);
        if (left is ConstantNode && right is not ConstantNode)
        {
            (left, right) = (right, left);
            op = op switch
            {
                RecordCompare.Gt => RecordCompare.Lt,
                RecordCompare.Ge => RecordCompare.Le,
                RecordCompare.Lt => RecordCompare.Gt,
                RecordCompare.Le => RecordCompare.Ge,
                _ => op,
            };
        }

        if (right is not ConstantNode constant)
        {
            throw new FeedQueryException("notImplemented", "Comparisons must be between a property and a literal.");
        }

        var field = FieldOf(type, left);
        return new RecordFilterCompare(field.Code, op, Value(field, constant.Value));
    }

    private static RecordFilterText Function(VisibleRecordType type, SingleValueFunctionCallNode call)
    {
        var match = call.Name switch
        {
            "contains" => RecordTextMatch.Contains,
            "startswith" => RecordTextMatch.StartsWith,
            "endswith" => RecordTextMatch.EndsWith,
            _ => throw new FeedQueryException("notImplemented", $"Function {call.Name} is not supported."),
        };
        var arguments = call.Parameters.Select(p => p as SingleValueNode).ToList();
        if (arguments.Count != 2 || arguments[0] is null || Unwrap(arguments[1]!) is not ConstantNode { Value: string text })
        {
            throw new FeedQueryException("notImplemented", $"{call.Name} needs a text property and a text literal.");
        }

        var field = FieldOf(type, arguments[0]!);
        if (field.Type is not (RecordFieldType.Text or RecordFieldType.Lookup))
        {
            throw new FeedQueryException("badRequest", $"{call.Name} applies to text properties only.");
        }

        return new RecordFilterText(field.Code, match, text);
    }

    private static RecordFilterIn In(VisibleRecordType type, InNode node)
    {
        var field = FieldOf(type, node.Left);
        if (node.Right is not CollectionConstantNode collection)
        {
            throw new FeedQueryException("notImplemented", "in needs a list of literals.");
        }

        return new RecordFilterIn(field.Code, collection.Collection.Select(c => Value(field, c.Value)).ToList());
    }

    private static SingleValueNode Unwrap(SingleValueNode node) => node is ConvertNode convert ? Unwrap(convert.Source) : node;

    private static RecordFieldDefinition FieldOf(VisibleRecordType type, SingleValueNode node)
    {
        if (Unwrap(node) is not SingleValuePropertyAccessNode property)
        {
            throw new FeedQueryException("notImplemented", "Only properties of the entity can be filtered and sorted.");
        }

        if (property.Source is not ResourceRangeVariableReferenceNode)
        {
            throw new FeedQueryException("notImplemented", "Filters on related entities are not supported.");
        }

        return type.Field(property.Property.Name)
            ?? throw new FeedQueryException("notImplemented", $"{property.Property.Name} cannot be filtered or sorted (labels are for display).");
    }

    /// <summary>The literal as the contract's value type for the field.</summary>
    internal static object? Value(RecordFieldDefinition field, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            return field.Type switch
            {
                RecordFieldType.Id or RecordFieldType.Unit or RecordFieldType.Employee => value is Guid g ? g : Guid.Parse(Convert.ToString(value, invariant)!),
                RecordFieldType.Text or RecordFieldType.Lookup => Convert.ToString(value, invariant),
                RecordFieldType.Number or RecordFieldType.Money or RecordFieldType.Ratio => Convert.ToDecimal(value, invariant),
                RecordFieldType.Bool => value is bool b ? b : throw new FormatException(),
                RecordFieldType.Date => value switch
                {
                    Microsoft.OData.Edm.Date d => new DateOnly(d.Year, d.Month, d.Day),
                    DateTimeOffset o => DateOnly.FromDateTime(o.UtcDateTime),
                    _ => throw new FormatException(),
                },
                RecordFieldType.DateTime => value switch
                {
                    DateTimeOffset o => o.ToUniversalTime(),
                    Microsoft.OData.Edm.Date d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
                    _ => throw new FormatException(),
                },
                _ => throw new FormatException(),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new FeedQueryException("badRequest", $"The literal does not fit {field.Code}.");
        }
    }
}
