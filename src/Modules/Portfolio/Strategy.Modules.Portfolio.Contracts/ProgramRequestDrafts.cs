namespace Strategy.Modules.Portfolio.Contracts;

/// <summary>
/// A draft Program Request another module asks Portfolio to open (CAP budget routing, ADR 0124). Values pre-fill the wizard; the requester
/// completes and submits it in Portfolio.
/// </summary>
public sealed record ProgramRequestDraft(
    string? NameAr,
    string? NameEn,
    string? Description,
    string? Justifications,
    Guid RequesterEmployeeId,
    Guid? UnitId,
    Guid? SectionUnitId,
    decimal? EstimatedBudget,
    DateOnly? ExpectedStartDate,
    DateOnly? ExpectedEndDate,
    string SuggestionSourceCode);

public sealed record ProgramRequestDraftRef(Guid Id, string Number);

/// <summary>Status of a request and, once approved, of the project created from it (progress as a ratio 0–1).</summary>
public sealed record ProgramRequestProgress(Guid RequestId, string Number, string Status, Guid? ProjectId, string? ProjectNumber, string? ProjectStage, decimal? ProjectProgress);

/// <summary>Suggestion-source codes other modules use (seeded lookup values of <c>portfolio.suggestion-source</c>).</summary>
public static class PortfolioSuggestionSources
{
    public const string ActionPlan = "action-plan";

    public const string Benchmarking = "benchmarking";
}

/// <summary>Creates draft Program Requests for other modules (ADR 0124). Not scope-filtered: the caller decides who may trigger it.</summary>
public interface IProgramRequestDrafts
{
    /// <summary>
    /// Creates a draft request (requester record role for <see cref="ProgramRequestDraft.RequesterEmployeeId"/>). With
    /// <paramref name="transactionContext"/> (a DbContext with an open transaction) Portfolio joins the caller's transaction (ADR 0023).
    /// </summary>
    Task<ProgramRequestDraftRef> CreateDraftAsync(ProgramRequestDraft draft, object? transactionContext = null, CancellationToken cancellationToken = default);

    Task<ProgramRequestProgress?> GetProgressAsync(Guid requestId, CancellationToken cancellationToken = default);
}
