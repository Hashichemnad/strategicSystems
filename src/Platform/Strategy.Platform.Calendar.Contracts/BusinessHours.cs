namespace Strategy.Platform.Calendar;

/// <summary>A business-hours record as other components see it.</summary>
public sealed record BusinessHoursReference(Guid Id, string? NameAr, string? NameEn, string TimeZone, bool IsDefault, bool Active);

/// <summary>
/// Working-time arithmetic over business hours and holidays (ADR 0027), shared by every deadline: approval SLAs now, KPI windows, CAP and task due
/// dates later. Times are instants (UTC offsets allowed); each record converts them with its own time zone.
/// </summary>
public interface IBusinessHoursCalculator
{
    Task<BusinessHoursReference> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>The record, or null when it does not exist.</summary>
    Task<BusinessHoursReference?> GetAsync(Guid businessHoursId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessHoursReference>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>The unit's own record, else the nearest ancestor's, else the default (inactive records are skipped).</summary>
    Task<BusinessHoursReference> ResolveForUnitAsync(Guid? unitId, CancellationToken cancellationToken = default);

    /// <summary>The moment <paramref name="duration"/> of working time after <paramref name="start"/>.</summary>
    Task<DateTimeOffset> AddWorkingTimeAsync(Guid businessHoursId, DateTimeOffset start, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Working time between two moments (zero when <paramref name="to"/> is not after <paramref name="from"/>).</summary>
    Task<TimeSpan> WorkingTimeBetweenAsync(Guid businessHoursId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<bool> IsWorkingTimeAsync(Guid businessHoursId, DateTimeOffset at, CancellationToken cancellationToken = default);
}
