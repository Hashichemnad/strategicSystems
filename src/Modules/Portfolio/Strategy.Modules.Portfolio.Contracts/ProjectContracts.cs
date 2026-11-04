using System.Security.Claims;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Portfolio.Contracts;

/// <summary>A project's closure was approved (SPEC W5, ADR 0072). Teams also receives <c>LinkedRecordClosed</c>. Delivered at least once.</summary>
public sealed record ProjectClosureApproved(Guid ProjectId, string Number, string? NameAr, string? NameEn, Guid? UnitId, Guid? PmEmployeeId, DateTimeOffset ApprovedAt)
    : IIntegrationEvent;

/// <summary>A project risk with its stored scores (ADR 0074) — the Risk module's future "Project Risks" tab.</summary>
public sealed record ProjectRiskSummary(
    Guid Id,
    Guid ProjectId,
    string ProjectNumber,
    string? ProjectNameAr,
    string? ProjectNameEn,
    string? Title,
    string? CategoryCode,
    int Probability,
    int Impact,
    int? ControlEvaluation,
    int InherentScore,
    string InherentBand,
    decimal ResidualScore,
    string ResidualBand,
    string? Response,
    decimal? ImplementationPct,
    Guid? OwnerEmployeeId,
    string Status,
    string Link);

/// <summary>Project risks for other modules; filtered by the caller's project read scope.</summary>
public interface IProjectRiskDirectory
{
    Task<IReadOnlyList<ProjectRiskSummary>> ListAsync(ClaimsPrincipal user, Guid? projectId = null, string? band = null, CancellationToken cancellationToken = default);
}
