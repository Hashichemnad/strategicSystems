using System.Security.Claims;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Risk.Contracts;

/// <summary>A score and its band under the current risk framework (ADR 0100 / 0101).</summary>
public sealed record SharedRiskScore(decimal InherentScore, string InherentBand, decimal ResidualScore, string ResidualBand, int FrameworkVersion);

/// <summary>
/// The shared scoring service (SPEC §3.3, Q-P6): the same scales, method and bands for enterprise and project risks, so the same ratings
/// always give the same score. Registered only while the Risk module is enabled.
/// </summary>
public interface IRiskScoring
{
    /// <summary>
    /// A Portfolio project risk: probability and impact 1–5, existing control evaluation 1–10 (none = no reduction). Residual follows the
    /// framework's project residual rule.
    /// </summary>
    Task<SharedRiskScore> ScoreProjectRiskAsync(int probability, int impact, int? controlEvaluation, CancellationToken cancellationToken = default);
}

/// <summary>A risk as other modules see it (CAP Phase 15, Audit Phase 16).</summary>
public sealed record RiskSummary(
    Guid Id, string Number, string? TitleAr, string? TitleEn, string Status, Guid UnitId, Guid? OwnerEmployeeId, decimal? ResidualScore, string? ResidualBand,
    string Link);

/// <summary>A control of a risk as other modules see it (Audit Phase 16). Effectiveness is a framework level code.</summary>
public sealed record RiskControlSummary(Guid Id, Guid RiskId, string? NameAr, string? NameEn, string? EffectivenessCode, string Status);

/// <summary>A logged risk event / incident (SPEC §3.4 F).</summary>
public sealed record RiskEventSummary(
    Guid Id, string Number, DateOnly OccurredOn, string? Description, decimal? FinancialImpact, bool NearMiss, Guid? UnitId, IReadOnlyList<Guid> RiskIds, Guid? CapRemarkId,
    string Link);

/// <summary>Risks and events for other modules. Reads are filtered by the caller's risk read scope.</summary>
public interface IRiskDirectory
{
    Task<RiskSummary?> GetRiskAsync(Guid riskId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskSummary>> SearchAsync(string? text, ClaimsPrincipal user, int take = 20, CancellationToken cancellationToken = default);

    Task<RiskEventSummary?> GetEventAsync(Guid eventId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>CAP (Phase 15) records the remark it raised for an event (shown on the event). Idempotent; false when the event does not exist.</summary>
    Task<bool> LinkCapRemarkAsync(Guid eventId, Guid remarkId, CancellationToken cancellationToken = default);

    /// <summary>Audit (Phase 16): a finding was linked — flag the risk for an unscheduled review. False when the risk does not exist.</summary>
    Task<bool> FlagForReassessmentAsync(Guid riskId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Audit (Phase 16): the active controls of a risk the user may read (empty when the risk is not readable).</summary>
    Task<IReadOnlyList<RiskControlSummary>> ListControlsAsync(Guid riskId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Audit (Phase 16, ADR 0135): an audit finding lowers the control's effectiveness one level on the framework scale (audited) and flags the
    /// risk for reassessment. Returns the new level, or null when the control does not exist or is already at the lowest level.
    /// </summary>
    Task<string?> LowerControlEffectivenessAsync(Guid controlId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>Recalculation choice when a framework version is saved (ADR 0100).</summary>
public static class RiskRecalculation
{
    public const string Now = "now";
    public const string NextReview = "next-review";

    public static readonly IReadOnlyList<string> All = [Now, NextReview];
}

/// <summary>A new framework version was saved (or seeded). Portfolio re-rates project risks when <c>Recalculation</c> is <c>now</c>.</summary>
public sealed record RiskFrameworkChanged(Guid VersionId, int VersionNo, string Recalculation, DateTimeOffset At) : IIntegrationEvent;

/// <summary>A risk event / incident was logged (CAP raises a remark in Phase 15).</summary>
public sealed record RiskEventLogged(
    Guid EventId, string Number, DateOnly OccurredOn, string? Description, bool NearMiss, decimal? FinancialImpact, Guid? UnitId, IReadOnlyList<Guid> RiskIds,
    DateTimeOffset At) : IIntegrationEvent;

/// <summary>A control was rated with the lowest effectiveness level (CAP raises a remark in Phase 15).</summary>
public sealed record RiskControlRatedIneffective(
    Guid ControlId, Guid RiskId, string RiskNumber, string? ControlNameAr, string? ControlNameEn, Guid UnitId, Guid? OwnerEmployeeId, DateTimeOffset At) : IIntegrationEvent;

/// <summary>An approved KPI result breached a key risk indicator threshold (ADR 0104).</summary>
public sealed record RiskKriBreached(
    Guid RiskId, string RiskNumber, Guid KpiCardId, Guid KpiResultId, decimal Value, decimal Threshold, string Direction, DateTimeOffset At) : IIntegrationEvent;
