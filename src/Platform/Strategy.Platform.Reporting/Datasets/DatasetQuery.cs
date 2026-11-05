using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Strategy.Platform.Reporting.Datasets;

/// <summary>
/// Runs a validated <see cref="ReportQuery"/> on a module's already scoped query (ADR 0144): a whitelist of field selectors is the only
/// way a report touches the data — filters, sorting and paging are translated to SQL; grouping is done on the filtered rows (bounded by
/// the export row limit). Modules build one per dataset in their <c>Reporting/</c> folder.
/// </summary>
public sealed class DatasetQuery<T>
    where T : class
{
    private readonly Dictionary<string, LambdaExpression> _fields = new(StringComparer.Ordinal);
    private readonly Expression<Func<T, Guid>> _id;

    public DatasetQuery(Expression<Func<T, Guid>> id)
    {
        _id = id;
    }

    /// <summary>Maximum rows read for a grouped result.</summary>
    public int GroupRowLimit { get; init; } = 50_000;

    public IReadOnlyCollection<string> FieldCodes => _fields.Keys;

    /// <summary>Declares field <paramref name="code"/> (must match the <see cref="DatasetField"/> code).</summary>
    public DatasetQuery<T> Field<TValue>(string code, Expression<Func<T, TValue>> selector)
    {
        _fields[code] = selector;
        return this;
    }

    public async Task<ReportResult> RunAsync(IQueryable<T> source, ReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        var unknown = query.Columns.Concat(query.GroupBy).Concat(query.Filters.Select(f => f.Field)).Concat(query.Sort.Select(s => s.Field))
            .FirstOrDefault(f => !_fields.ContainsKey(f));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown dataset field '{unknown}'.", nameof(query));
        }

        var filtered = source;
        foreach (var filter in query.Filters)
        {
            filtered = filtered.Where(BuildFilter(filter));
        }

        if (query.GroupBy.Count > 0)
        {
            return await GroupAsync(filtered, query, cancellationToken).ConfigureAwait(false);
        }

        var total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        IOrderedQueryable<T>? ordered = null;
        foreach (var sort in query.Sort)
        {
            ordered = ApplyOrder(ordered ?? (IQueryable<T>)filtered, _fields[sort.Field], sort.Descending, ordered is not null);
        }

        var paged = (ordered ?? filtered.OrderBy(_id)).Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Take, 1, GroupRowLimit));
        var rows = await paged.Select(Projection(query.Columns)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ReportResult(
            query.Columns,
            rows.Select(r => (IReadOnlyList<object?>)r.Values.Select(Normalise).ToList()).ToList(),
            total,
            rows.Select(r => r.Id).ToList());
    }

    private async Task<ReportResult> GroupAsync(IQueryable<T> filtered, ReportQuery query, CancellationToken cancellationToken)
    {
        var numeric = query.Columns.Where(c => !query.GroupBy.Contains(c, StringComparer.Ordinal) && IsNumeric(_fields[c].ReturnType)).ToList();
        var needed = query.GroupBy.Concat(numeric).ToList();
        var rows = await filtered.OrderBy(_id).Take(GroupRowLimit).Select(Projection(needed)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var groups = rows
            .GroupBy(r => string.Join('\u001f', r.Values.Take(query.GroupBy.Count).Select(v => Convert.ToString(Normalise(v), CultureInfo.InvariantCulture))))
            .Select(g =>
            {
                var first = g.First().Values;
                var values = new List<object?>();
                values.AddRange(first.Take(query.GroupBy.Count).Select(Normalise));
                values.Add(g.Count());
                for (var i = 0; i < numeric.Count; i++)
                {
                    var index = query.GroupBy.Count + i;
                    values.Add(g.Sum(r => r.Values[index] is null ? 0m : Convert.ToDecimal(r.Values[index], CultureInfo.InvariantCulture)));
                }

                return (IReadOnlyList<object?>)values;
            })
            .ToList();

        IEnumerable<IReadOnlyList<object?>> sorted = groups;
        var groupSort = query.Sort.FirstOrDefault(s => query.GroupBy.Contains(s.Field, StringComparer.Ordinal));
        if (groupSort is not null)
        {
            var index = query.GroupBy.ToList().IndexOf(groupSort.Field);
            sorted = groupSort.Descending ? groups.OrderByDescending(g => g[index], Comparer<object?>.Create(CompareValues)) : groups.OrderBy(g => g[index], Comparer<object?>.Create(CompareValues));
        }
        else
        {
            sorted = groups.OrderByDescending(g => (int)g[query.GroupBy.Count]!);
        }

        var columns = query.GroupBy.Concat(["count"]).Concat(numeric).ToList();
        var list = sorted.ToList();
        return new ReportResult(columns, list.Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Take, 1, GroupRowLimit)).ToList(), list.Count);
    }

    private static int CompareValues(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (IComparable x, _) when a.GetType() == b.GetType() => x.CompareTo(b),
        _ => string.CompareOrdinal(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture)),
    };

    private sealed class Row
    {
        public Guid Id { get; init; }

        public object?[] Values { get; init; } = [];
    }

    private Expression<Func<T, Row>> Projection(IReadOnlyList<string> columns)
    {
        var parameter = _id.Parameters[0];
        var values = columns.Select(c => (Expression)Expression.Convert(Rebind(_fields[c], parameter), typeof(object))).ToList();
        var body = Expression.MemberInit(
            Expression.New(typeof(Row)),
            Expression.Bind(typeof(Row).GetProperty(nameof(Row.Id))!, _id.Body),
            Expression.Bind(typeof(Row).GetProperty(nameof(Row.Values))!, Expression.NewArrayInit(typeof(object), values)));
        return Expression.Lambda<Func<T, Row>>(body, parameter);
    }

    private static object? Normalise(object? value) => value switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        _ => value,
    };

    private static bool IsNumeric(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(float) || t == typeof(short);
    }

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

    private Expression<Func<T, bool>> BuildFilter(ReportFilter filter)
    {
        var selector = _fields[filter.Field];
        var parameter = Expression.Parameter(typeof(T), "x");
        var member = Rebind(selector, parameter);
        var type = member.Type;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        Expression Value(int index) => Expression.Convert(Constant(Parse(filter.Values.ElementAtOrDefault(index), underlying), underlying), type);
        Expression body = filter.Operator switch
        {
            ReportOperators.IsEmpty => IsEmpty(member),
            ReportOperators.IsNotEmpty => Expression.Not(IsEmpty(member)),
            ReportOperators.Equal => Expression.Equal(member, Value(0)),
            ReportOperators.NotEqual => Expression.Or(Expression.NotEqual(member, Value(0)), NullCheck(member)),
            ReportOperators.GreaterOrEqual => Expression.GreaterThanOrEqual(member, Value(0)),
            ReportOperators.LessOrEqual => Expression.LessThanOrEqual(member, Value(0)),
            ReportOperators.Between => Expression.AndAlso(Expression.GreaterThanOrEqual(member, Value(0)), Expression.LessThanOrEqual(member, Value(1))),
            ReportOperators.In => In(member, underlying, filter.Values),
            ReportOperators.Contains when underlying == typeof(string) => Contains(member, filter.Values.Count > 0 ? filter.Values[0] : string.Empty),
            _ => throw new ArgumentException($"Operator '{filter.Operator}' is not valid for field '{filter.Field}'."),
        };
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression NullCheck(Expression member) =>
        member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null
            ? Expression.Constant(false)
            : Expression.Equal(member, Expression.Constant(null, member.Type));

    private static Expression IsEmpty(Expression member) =>
        member.Type == typeof(string)
            ? Expression.OrElse(Expression.Equal(member, Expression.Constant(null, typeof(string))), Expression.Equal(member, Expression.Constant(string.Empty)))
            : NullCheck(member);

    private static BinaryExpression Contains(Expression member, string text)
    {
        var lowered = Expression.Call(member, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var call = Expression.Call(lowered, typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!, Constant(text.ToLowerInvariant(), typeof(string)));
        return Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, typeof(string))), call);
    }

    private static MethodCallExpression In(Expression member, Type underlying, IReadOnlyList<string> values)
    {
        var listType = typeof(List<>).MakeGenericType(member.Type);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var value in values)
        {
            list.Add(Parse(value, underlying));
        }

        var contains = typeof(Enumerable).GetMethods().Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(member.Type);
        return Expression.Call(contains, Constant(list, listType), member);
    }

    /// <summary>A parameterised constant (closure member access), so EF Core sends it as a query parameter.</summary>
    private static MemberExpression Constant(object? value, Type type)
    {
        var holderType = typeof(Holder<>).MakeGenericType(type);
        var holder = Activator.CreateInstance(holderType, value);
        return Expression.Property(Expression.Constant(holder), "Value");
    }

    private sealed class Holder<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    internal static object? Parse(string? value, Type type)
    {
        if (value is null)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var invariant = CultureInfo.InvariantCulture;
        try
        {
            return type switch
            {
                _ when type == typeof(string) => value,
                _ when type == typeof(Guid) => Guid.Parse(value),
                _ when type == typeof(bool) => bool.Parse(value),
                _ when type == typeof(int) => int.Parse(value, invariant),
                _ when type == typeof(long) => long.Parse(value, invariant),
                _ when type == typeof(decimal) => decimal.Parse(value, NumberStyles.Number, invariant),
                _ when type == typeof(double) => double.Parse(value, NumberStyles.Float, invariant),
                _ when type == typeof(DateOnly) => DateOnly.Parse(value, invariant),
                _ when type == typeof(DateTimeOffset) => DateTimeOffset.Parse(value, invariant, DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                _ when type == typeof(DateTime) => DateTime.SpecifyKind(DateTime.Parse(value, invariant), DateTimeKind.Utc),
                _ when type.IsEnum => Enum.Parse(type, value, ignoreCase: true),
                _ => throw new ArgumentException($"Unsupported field type {type.Name}."),
            };
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Value '{value}' is not a valid {type.Name}.", ex);
        }
    }

    private sealed class Replacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
