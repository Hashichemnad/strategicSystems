namespace Strategy.Platform.Organisation.Contracts;

/// <summary>A unit as other modules see it.</summary>
public sealed record UnitReference(Guid Id, string Code, string? NameAr, string? NameEn, string LevelCode, Guid? ParentId, bool Active);

/// <summary>An employee as other modules see it.</summary>
public sealed record EmployeeReference(
    Guid Id,
    string EmployeeNo,
    string? NameAr,
    string? NameEn,
    string Email,
    Guid UnitId,
    Guid? ManagerEmployeeId,
    string Status);

/// <summary>Organisation structure reads for other modules and the approval engine (SPEC §0.1 approver resolution).</summary>
public interface IOrgDirectory
{
    Task<UnitReference?> GetUnitAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>The unit's chain from the root as the structure stood on <paramref name="asOf"/> (default today).</summary>
    Task<IReadOnlyList<UnitReference>> GetPathAsync(Guid unitId, DateOnly? asOf = null, CancellationToken cancellationToken = default);

    /// <summary>The unit and every unit below it (current structure), for "unit and below" scopes.</summary>
    Task<IReadOnlyList<Guid>> GetSelfAndDescendantIdsAsync(Guid unitId, CancellationToken cancellationToken = default);

    Task<Guid?> GetHeadOfUnitAsync(Guid unitId, DateOnly? asOf = null, CancellationToken cancellationToken = default);
}

public interface IEmployeeDirectory
{
    Task<EmployeeReference?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, EmployeeReference>> GetManyAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Ids of employees whose number, names or email match <paramref name="search"/> (Arabic-normalised), at most 500.</summary>
    Task<IReadOnlyList<Guid>> SearchAsync(string search, CancellationToken cancellationToken = default);

    Task<EmployeeReference?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<EmployeeReference?> FindByEmployeeNoAsync(string employeeNo, CancellationToken cancellationToken = default);

    /// <summary>The employee's manager as of a date, from the placement history.</summary>
    Task<Guid?> GetManagerAsync(Guid employeeId, DateOnly? asOf = null, CancellationToken cancellationToken = default);
}

/// <summary>Who may act for an employee on a date (consumed by the approval engine, Phase 4).</summary>
public interface IDelegationQuery
{
    Task<IReadOnlyList<Guid>> GetActiveDelegatesAsync(Guid employeeId, DateOnly date, string? scopeKey, CancellationToken cancellationToken = default);
}

/// <summary>Implemented by other platform services that react to employee lifecycle changes (Identity deactivates the user).</summary>
public interface IEmployeeLifecycleHook
{
    Task EmployeeLeftAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>Optional source for AD / HR synchronisation; the scheduled job arrives in Phase 3 (ADR 0014).</summary>
public interface IDirectorySyncSource
{
    string Name { get; }

    IAsyncEnumerable<EmployeeReference> ReadEmployeesAsync(CancellationToken cancellationToken);
}
