using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Strategy.Platform.Reporting.Records;

/// <summary>
/// Runs a validated <see cref="RecordQuery"/> on a module's already scoped query (ADR 0190): a whitelist of field selectors is the only way a
/// feed touches the data; filters, sorting, keyset / offset paging and counting are translated to SQL. Modules declare one per record type
/// in their <c>Feeds/</c> folder.
/// </summary>
public sealed class RecordTypeQuery<T>
    where T : class
{
    private readonly Dictionary<string, LambdaExpression> _fields = new(StringComparer.Ordinal);
    private readonly Expression<Func<T, Guid>> _id;
    private readonly Expression<Func<T, DateTimeOffset>> _modified;
    private Expression<Func<T, bool>>? _deleted;

    public RecordTypeQuery(Expression<Func<T, Guid>> id, Expression<Func<T, DateTimeOffset>> modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(modifiedAt);
        _id = id;
        _modified = modifiedAt;
        _fields[RecordFields.Id] = id;
        _fields[RecordFields.ModifiedAt] = modifiedAt;
    }

    public IReadOnlyCollection<string> FieldCodes => _fields.Keys;

    /// <summary>Declares field <paramref name="code"/> (must match the <see cref="RecordFieldDefinition"/> code).</summary>
    public RecordTypeQuery<T> Field<TValue>(string code, Expression<Func<T, TValue>> selector)
    {
        _fields[code] = selector;
        return this;
    }

    /// <summary>The "deleted" condition of the type (cancelled / withdrawn / inactive) for the deleted feed.</summary>
    public RecordTypeQuery<T> Deleted(Expression<Func<T, bool>> predicate)
    {
        _deleted = predicate;
        return this;
    }

    public async Task<RecordPage> RunAsync(IQueryable<T> source, RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        var unknown = query.Fields.Concat(query.OrderBy.Select(s => s.Field)).Concat(FilterFields(query.Filter)).FirstOrDefault(f => !_fields.ContainsKey(f));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown record field '{unknown}'.", nameof(query));
        }

        var filtered = query.Mode == RecordQueryMode.Deleted
            ? _deleted is null ? source.Where(_ => false) : source.Where(_deleted)
            : source;
        if (query.ChangedSince is { } since)
        {
            filtered = filtered.Where(Compare(_modified, ExpressionType.GreaterThan, since));
        }

        if (query.Filter is not null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            filtered = filtered.Where(Expression.Lambda<Func<T, bool>>(Build(query.Filter, parameter), parameter));
        }

        long? total = query.IncludeTotal ? await filtered.LongCountAsync(cancellationToken).ConfigureAwait(false) : null;
        IQueryable<T> ordered;
        if (query.After is not null || query.ChangedSince is not null && query.OrderBy.Count == 0)
        {
            if (query.After is { } after)
            {
                filtered = filtered.Where(AfterCursor(after));
            }

            ordered = filtered.OrderBy(_modified).ThenBy(_id);
        }
        else
        {
            IOrderedQueryable<T>? sorted = null;
            foreach (var sort in query.OrderBy)
            {
                sorted = ApplyOrder(sorted ?? filtered, _fields[sort.Field], sort.Descending, sorted is not null);
            }

            // Stable paging: the key always breaks ties.
            ordered = sorted is null ? filtered.OrderBy(_id) : sorted.ThenBy(_id);
        }

        var rows = await ordered.Skip(Math.Max(0, query.Skip)).Take(Math.Max(0, query.Take))
            .Select(Projection(query.Fields)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new RecordPage(query.Fields, rows.Select(r => (IReadOnlyList<object?>)r.Select(Normalise).ToList()).ToList(), total);
    }

    private static IEnumerable<string> FilterFields(RecordFilter? filter) => filter switch
    {
        null => [],
        RecordFilterAnd a => FilterFields(a.Left).Concat(FilterFields(a.Right)),
        RecordFilterOr o => FilterFields(o.Left).Concat(FilterFields(o.Right)),
        RecordFilterNot n => FilterFields(n.Inner),
        RecordFilterCompare c => [c.Field],
        RecordFilterText t => [t.Field],
        RecordFilterIn i => [i.Field],
        _ => throw new ArgumentException("Unknown filter node."),
    };

    private Expression Build(RecordFilter filter, ParameterExpression parameter) => filter switch
    {
        RecordFilterAnd a => Expression.AndAlso(Build(a.Left, parameter), Build(a.Right, parameter)),
        RecordFilterOr o => Expression.OrElse(Build(o.Left, parameter), Build(o.Right, parameter)),
        RecordFilterNot n => Expression.Not(Build(n.Inner, parameter)),
        RecordFilterCompare c => CompareNode(Member(c.Field, parameter), c.Operator, c.Value),
        RecordFilterText t => TextNode(Member(t.Field, parameter), t.Match, t.Value),
        RecordFilterIn i => InNode(Member(i.Field, parameter), i.Values),
        _ => throw new ArgumentException("Unknown filter node."),
    };

    private Expression Member(string field, ParameterExpression parameter) => Rebind(_fields[field], parameter);

    private static Expression CompareNode(Expression member, RecordCompare op, object? value)
    {
        var type = member.Type;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
        if (value is null)
        {
            return op switch
            {
                RecordCompare.Eq => nullable ? Expression.Equal(member, Expression.Constant(null, type)) : Expression.Constant(false),
                RecordCompare.Ne => nullable ? Expression.NotEqual(member, Expression.Constant(null, type)) : Expression.Constant(true),
                _ => Expression.Constant(false), // OData: comparisons with null are false
            };
        }

        var constant = Expression.Convert(Constant(Coerce(value, underlying), underlying), type);
        if (underlying == typeof(string) && op is not (RecordCompare.Eq or RecordCompare.Ne))
        {
            var compare = Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!, member, constant);
            return Binary(op, compare, Expression.Constant(0));
        }

        if (underlying == typeof(Guid) && op is not (RecordCompare.Eq or RecordCompare.Ne))
        {
            throw new ArgumentException("Ids can only be compared for equality.");
        }

        if (underlying == typeof(bool) && op is not (RecordCompare.Eq or RecordCompare.Ne))
        {
            throw new ArgumentException("Booleans can only be compared for equality.");
        }

        if (op == RecordCompare.Ne && nullable)
        {
            // OData: null ne 'x' is true.
            return Expression.OrElse(Expression.NotEqual(member, constant), Expression.Equal(member, Expression.Constant(null, type)));
        }

        return Binary(op, member, constant);
    }

    private static BinaryExpression Binary(RecordCompare op, Expression left, Expression right) => op switch
    {
        RecordCompare.Eq => Expression.Equal(left, right),
        RecordCompare.Ne => Expression.NotEqual(left, right),
        RecordCompare.Gt => Expression.GreaterThan(left, right),
        RecordCompare.Ge => Expression.GreaterThanOrEqual(left, right),
        RecordCompare.Lt => Expression.LessThan(left, right),
        RecordCompare.Le => Expression.LessThanOrEqual(left, right),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static BinaryExpression TextNode(Expression member, RecordTextMatch match, string value)
    {
        if (member.Type != typeof(string))
        {
            throw new ArgumentException("Text functions apply to text fields only.");
        }

        var name = match switch
        {
            RecordTextMatch.Contains => nameof(string.Contains),
            RecordTextMatch.StartsWith => nameof(string.StartsWith),
            RecordTextMatch.EndsWith => nameof(string.EndsWith),
            _ => throw new ArgumentOutOfRangeException(nameof(match)),
        };
        var lowered = Expression.Call(member, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var call = Expression.Call(lowered, typeof(string).GetMethod(name, [typeof(string)])!, Constant(value.ToLowerInvariant(), typeof(string)));
        return Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, typeof(string))), call);
    }

    private static MethodCallExpression InNode(Expression member, IReadOnlyList<object?> values)
    {
        var underlying = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        var listType = typeof(List<>).MakeGenericType(member.Type);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var value in values)
        {
            list.Add(value is null ? null : Coerce(value, underlying));
        }

        var contains = typeof(Enumerable).GetMethods().Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(member.Type);
        return Expression.Call(contains, Constant(list, listType), member);
    }

    private Expression<Func<T, bool>> AfterCursor(RecordCursor after)
    {
        var parameter = _id.Parameters[0];
        var modified = Rebind(_modified, parameter);
        var id = _id.Body;
        var at = Constant(after.ModifiedAt, typeof(DateTimeOffset));
        var compareTo = Expression.Call(id, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, Constant(after.Id, typeof(Guid)));
        var body = Expression.OrElse(
            Expression.GreaterThan(modified, at),
            Expression.AndAlso(Expression.Equal(modified, at), Expression.GreaterThan(compareTo, Expression.Constant(0))));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression<Func<T, bool>> Compare(Expression<Func<T, DateTimeOffset>> selector, ExpressionType type, DateTimeOffset value) =>
        Expression.Lambda<Func<T, bool>>(Expression.MakeBinary(type, selector.Body, Constant(value, typeof(DateTimeOffset))), selector.Parameters[0]);

    /// <summary>The contract's value types converted to the selector's CLR type.</summary>
    internal static object Coerce(object value, Type type)
    {
        if (type.IsInstanceOfType(value))
        {
            return value;
        }

        var invariant = CultureInfo.InvariantCulture;
        try
        {
            return (value, type) switch
            {
                (DateTimeOffset d, _) when type == typeof(DateTime) => d.UtcDateTime,
                (DateTimeOffset d, _) when type == typeof(DateOnly) => DateOnly.FromDateTime(d.UtcDateTime),
                (DateOnly d, _) when type == typeof(DateTimeOffset) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                (DateOnly d, _) when type == typeof(DateTime) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                (string s, _) when type == typeof(Guid) => Guid.Parse(s),
                (string s, _) when type.IsEnum => Enum.Parse(type, s, ignoreCase: true),
                (_, _) when type.IsEnum => Enum.ToObject(type, Convert.ToInt32(value, invariant)),
                (IConvertible, _) when type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double)
                    || type == typeof(short) || type == typeof(float) || type == typeof(string) => Convert.ChangeType(value, type, invariant),
                _ => throw new ArgumentException($"A {value.GetType().Name} value does not fit a {type.Name} field."),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Value '{value}' does not fit a {type.Name} field.", ex);
        }
    }

    private Expression<Func<T, object?[]>> Projection(IReadOnlyList<string> fields)
    {
        var parameter = _id.Parameters[0];
        var values = fields.Select(f => (Expression)Expression.Convert(Rebind(_fields[f], parameter), typeof(object))).ToList();
        return Expression.Lambda<Func<T, object?[]>>(Expression.NewArrayInit(typeof(object), values), parameter);
    }

    private static object? Normalise(object? value) => value switch
    {
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        Enum e => e.ToString(),
        _ => value,
    };

    private static Expression Rebind(LambdaExpression selector, ParameterExpression parameter) =>
        new Replacer(selector.Parameters[0], parameter).Visit(selector.Body);

    private static IOrderedQueryable<T> ApplyOrder(IQueryable<T> source, LambdaExpression selector, bool descending, bool then)
    {
        var name = (then, descending) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending),
        };
        var method = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), selector.ReturnType);
        return (IOrderedQueryable<T>)method.Invoke(null, [source, selector])!;
    }

    /// <summary>A parameterised constant (closure member access), so EF Core sends it as a query parameter.</summary>
    private static MemberExpression Constant(object? value, Type type)
    {
        var holderType = typeof(RecordValueHolder<>).MakeGenericType(type);
        var holder = Activator.CreateInstance(holderType, value);
        return Expression.Property(Expression.Constant(holder), "Value");
    }

    private sealed class Replacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>A closure-like holder so EF Core sends values as query parameters.</summary>
internal sealed class RecordValueHolder<TValue>(TValue value)
{
    public TValue Value { get; } = value;
}
