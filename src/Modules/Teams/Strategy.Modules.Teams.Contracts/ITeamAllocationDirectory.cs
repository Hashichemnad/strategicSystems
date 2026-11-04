namespace Strategy.Modules.Teams.Contracts;

/// <summary>
/// Employees' allocation across active teams, for the Portfolio capacity view (SPEC §1.12, ADR 0116). System-level read: the caller filters the
/// employees it shows.
/// </summary>
public interface ITeamAllocationDirectory
{
    Task<IReadOnlyList<TeamAllocation>> ForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>One active membership of an active team; the allocation is a ratio (1.0 = 100 %), null when the team did not set it.</summary>
public sealed record TeamAllocation(Guid EmployeeId, Guid TeamId, string? TeamNumber, string? TeamNameAr, string? TeamNameEn, decimal? AllocationPercent);
