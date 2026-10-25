using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Organisation.Domain;

public static class DelegationStatus
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Active, Cancelled];
}

/// <summary>
/// An employee hands their approvals to another for a date range (SPEC §0.1, leave cover). <see cref="Scope"/> lists
/// system codes or approval process keys; empty means everything. Consumed by the approval engine (Phase 4).
/// </summary>
public sealed class Delegation : AuditableEntity
{
    public const int ReasonMaxLength = 1000;
    public const int ScopeItemMaxLength = 100;

    private Delegation()
    {
    }

    public Guid DelegatorEmployeeId { get; private set; }

    public Guid DelegateEmployeeId { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    public List<string> Scope { get; private set; } = [];

    public string? Reason { get; private set; }

    public string Status { get; private set; } = DelegationStatus.Active;

    public string? CancelReason { get; private set; }

    public bool CoversAll => Scope.Count == 0;

    /// <summary>
    /// Creates a delegation after checking it against the delegator's and the delegate's active delegations.
    /// </summary>
    public static Result<Delegation> Create(
        Guid delegatorId,
        Guid delegateId,
        bool delegateActive,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string> scope,
        string? reason,
        DateOnly today,
        IEnumerable<Delegation> existing)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(existing);
        if (delegatorId == delegateId)
        {
            return OrgErrors.DelegationSelf;
        }

        if (!delegateActive)
        {
            return OrgErrors.DelegateInactive;
        }

        if (to < today)
        {
            return OrgErrors.DelegationInPast;
        }

        var candidate = new Delegation
        {
            DelegatorEmployeeId = delegatorId,
            DelegateEmployeeId = delegateId,
            FromDate = from,
            ToDate = to,
            Scope = scope.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            Reason = Text.Clean(reason),
        };

        foreach (var other in existing.Where(d => d.Status == DelegationStatus.Active && d.Overlaps(candidate)))
        {
            if (other.DelegatorEmployeeId == delegatorId && other.ScopeIntersects(candidate))
            {
                return OrgErrors.DelegationOverlap;
            }

            if (other.DelegatorEmployeeId == delegateId && other.DelegateEmployeeId == delegatorId)
            {
                return OrgErrors.DelegationCycle;
            }
        }

        return candidate;
    }

    public Result Cancel(string reason)
    {
        if (Status != DelegationStatus.Active)
        {
            return OrgErrors.DelegationNotActive;
        }

        Status = DelegationStatus.Cancelled;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Does this delegation let the delegate act for the delegator on <paramref name="date"/> for <paramref name="scopeKey"/>?</summary>
    public bool Covers(DateOnly date, string? scopeKey) =>
        Status == DelegationStatus.Active
        && FromDate <= date && date <= ToDate
        && (CoversAll || scopeKey is null || Scope.Any(s => scopeKey == s || scopeKey.StartsWith(s + ".", StringComparison.Ordinal)));

    public bool Overlaps(Delegation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return FromDate <= other.ToDate && other.FromDate <= ToDate;
    }

    private bool ScopeIntersects(Delegation other) =>
        CoversAll || other.CoversAll || Scope.Intersect(other.Scope, StringComparer.Ordinal).Any();
}
