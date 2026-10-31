using System.Linq.Expressions;

namespace Strategy.Platform.Access;

/// <summary>Data scope of a role permission (SPEC §0.2, ADR 0015). Stored as the code in <see cref="DataScopes"/>.</summary>
public enum DataScope
{
    Own = 1,
    Team = 2,
    Unit = 3,
    UnitAndBelow = 4,
    All = 5,
}

public static class DataScopes
{
    public static string ToCode(this DataScope scope) => scope switch
    {
        DataScope.Own => "own",
        DataScope.Team => "team",
        DataScope.Unit => "unit",
        DataScope.UnitAndBelow => "unit_and_below",
        DataScope.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public static bool TryParse(string? code, out DataScope scope)
    {
        scope = code switch
        {
            "own" => DataScope.Own,
            "team" => DataScope.Team,
            "unit" => DataScope.Unit,
            "unit_and_below" => DataScope.UnitAndBelow,
            "all" => DataScope.All,
            _ => 0,
        };
        return scope != 0;
    }

    public static readonly IReadOnlyList<string> Codes = ["own", "team", "unit", "unit_and_below", "all"];
}

/// <summary>
/// How a module's entity maps to the data-scope attributes: its id (record roles), owner employee and/or owner user
/// (own / team) and unit (unit / unit and below). Declared once per scoped entity, e.g. <c>SampleScope.Map</c>.
/// </summary>
public sealed class ScopeMap<T>
    where T : class
{
    private readonly Func<T, Guid> _id;
    private readonly Func<T, Guid?>? _ownerEmployee;
    private readonly Func<T, Guid?>? _ownerUser;
    private readonly Func<T, Guid?>? _unit;

    public ScopeMap(
        string resourceType,
        Expression<Func<T, Guid>> id,
        Expression<Func<T, Guid?>>? ownerEmployee = null,
        Expression<Func<T, Guid?>>? ownerUser = null,
        Expression<Func<T, Guid?>>? unit = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentNullException.ThrowIfNull(id);
        ResourceType = resourceType;
        Id = id;
        OwnerEmployee = ownerEmployee;
        OwnerUser = ownerUser;
        Unit = unit;
        _id = id.Compile();
        _ownerEmployee = ownerEmployee?.Compile();
        _ownerUser = ownerUser?.Compile();
        _unit = unit?.Compile();
    }

    public string ResourceType { get; }

    public Expression<Func<T, Guid>> Id { get; }

    public Expression<Func<T, Guid?>>? OwnerEmployee { get; }

    public Expression<Func<T, Guid?>>? OwnerUser { get; }

    public Expression<Func<T, Guid?>>? Unit { get; }

    /// <summary>The record reference with its scope attributes, for <see cref="IAccessService.CanAsync"/>.</summary>
    public ResourceRef RefOf(T entity) =>
        new(ResourceType, _id(entity), _ownerEmployee?.Invoke(entity), _ownerUser?.Invoke(entity), _unit?.Invoke(entity));
}
