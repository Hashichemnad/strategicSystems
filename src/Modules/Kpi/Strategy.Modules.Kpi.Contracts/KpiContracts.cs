using System.Security.Claims;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Kpi.Contracts;

/// <summary>Performance bands (SPEC §2.7) applied to capped performance; <c>none</c> = not reported or cannot be calculated.</summary>
public static class RagCodes
{
    public const string Green = "green";
    public const string Amber = "amber";
    public const string Red = "red";
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = [Green, Amber, Red, None];
}

/// <summary>A KPI card as other modules see it (Portfolio "Existing KPI", Risk KRIs). docs/design/kpi.md §1.</summary>
public sealed record KpiCardSummary(
    Guid Id,
    string Code,
    string? NameAr,
    string? NameEn,
    string Status,
    string UnitCode,
    string MeasuringPeriod,
    string Direction,
    Guid? ResponsibleUnitId,
    Guid? ResponsibleEmployeeId,
    string Link);

/// <summary>Year-to-date performance of a card's annual plan (approved results only).</summary>
public sealed record KpiYearPerformance(Guid CardId, int Year, decimal? Achieved, decimal? Actual, decimal? Capped, string Rag, int ApprovedPeriods, int TotalPeriods);

/// <summary>How another module reads the KPI catalog. Every call is scope-filtered for <c>user</c> (cards they may read).</summary>
public interface IKpiCatalog
{
    Task<IReadOnlyList<KpiCardSummary>> SearchAsync(string? text, ClaimsPrincipal user, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>The card, or null when it does not exist or the user may not read it.</summary>
    Task<KpiCardSummary?> GetAsync(Guid cardId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<KpiYearPerformance?> GetPerformanceAsync(Guid cardId, int year, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

/// <summary>The K5 calculator and the configured RAG bands, for modules that compute their own KPI status (Q-P6 project KPI goal status).</summary>
public interface IKpiPerformance
{
    Performance Calculate(string targetType, string? direction, decimal? target, decimal? rangeMin, decimal? rangeMax, decimal? achieve);

    /// <summary>The band of a capped performance with the installation's thresholds (or the given overrides).</summary>
    Task<string> BandAsync(decimal? capped, decimal? greenFrom = null, decimal? amberFrom = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implemented by a consumer that links its records to KPI cards (Portfolio project KPIs in Phase 9, Risk KRIs in Phase 13). The card page
/// lists the references through the Links registry, which shows a live badge only for records the viewer may read (SPEC §2.3 D).
/// </summary>
public interface IKpiLinkedRecordSource
{
    /// <summary>Linkable type of the records (e.g. <c>portfolio.project</c>); declared in the Links registry by the consumer.</summary>
    string ResourceType { get; }

    Task<IReadOnlyList<Guid>> GetLinkedRecordIdsAsync(Guid cardId, CancellationToken cancellationToken);
}

/// <summary>A result was finally approved (SPEC §2.4 K4; Risk uses it for key risk indicators). Delivered at least once.</summary>
public sealed record KpiResultApproved(
    Guid ResultId,
    Guid CardId,
    string CardCode,
    int Year,
    string PeriodCode,
    decimal? Achieve,
    decimal? Actual,
    decimal? Capped,
    string Rag,
    bool NotApplicable,
    Guid? ResponsibleUnitId,
    DateTimeOffset ApprovedAt) : IIntegrationEvent;

/// <summary>
/// An approved result is below target in the configured band (red; amber optional) — SPEC §2.8 KPI missed → CAP. <c>ConsecutiveMissed</c> counts
/// the approved periods in a row, newest included, in that band.
/// </summary>
public sealed record KpiTargetMissed(
    Guid ResultId,
    Guid CardId,
    string CardCode,
    string? CardNameAr,
    string? CardNameEn,
    int Year,
    string PeriodCode,
    decimal? Achieve,
    decimal? Target,
    decimal? Capped,
    string Rag,
    int ConsecutiveMissed,
    Guid? ResponsibleUnitId,
    Guid? ResponsibleEmployeeId,
    DateTimeOffset ApprovedAt) : IIntegrationEvent;

/// <summary>A strategic objective of the strategy hierarchy (SPEC §2.7) as other modules use it (ADR 0075). Codes are stable references.</summary>
public sealed record StrategicObjective(string Code, string? NameAr, string? NameEn, bool Active, int Sort);

/// <summary>
/// The strategy hierarchy's objectives — the single source of strategic objectives for every system (ADR 0075). Reference data: not
/// scope-filtered (objectives are public within the installation).
/// </summary>
public interface IKpiStrategyCatalog
{
    /// <summary>Objectives ordered by sort then code; inactive ones only when asked (to label stored codes).</summary>
    Task<IReadOnlyList<StrategicObjective>> ListObjectivesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>Creates the objectives whose code does not exist yet (as top-level objectives); never changes existing elements. Returns the number created.</summary>
    Task<int> EnsureObjectivesAsync(IReadOnlyList<StrategicObjective> objectives, CancellationToken cancellationToken = default);
}

/// <summary>
/// Updates the benchmark description / result of a KPI card from an approved benchmarking analysis (SPEC §8.3, ADR 0128). An audited
/// change of those two fields only. Returns false when the card does not exist.
/// </summary>
public interface IKpiBenchmarkWriter
{
    Task<bool> UpdateBenchmarkAsync(Guid cardId, string? description, decimal? result, object? transactionContext = null, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IKpiResultFeed.ProposeAsync"/>.</summary>
public static class KpiFeedOutcomes
{
    public const string Drafted = "drafted";
    public const string NoOpenResult = "no-open-result";
    public const string Unchanged = "unchanged";
    public const string Failed = "failed";
}

/// <summary>
/// Another module proposes a measured value for a KPI card (Audit Phase 16, ADR 0135). The value is saved as a machine draft on the card's latest
/// result that is open for update and whose period ended on or before <c>asOf</c>; the KPI owner reviews and submits it through the normal
/// approval. Never submits or approves.
/// </summary>
public interface IKpiResultFeed
{
    Task<string> ProposeAsync(Guid cardId, DateOnly asOf, decimal achieve, string? note, CancellationToken cancellationToken = default);
}
