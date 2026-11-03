using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Portfolio.Domain;

public static class ProjectStages
{
    public const string Initiation = "initiation";
    public const string Planning = "planning";
    public const string Execution = "execution";
    public const string Closure = "closure";
    public const string Impact = "impact";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> ProjectPath = [Initiation, Planning, Execution, Closure, Impact, Completed];

    /// <summary>Q-P2 default: an initiative skips charter, closure approval and Impact.</summary>
    public static readonly IReadOnlyList<string> InitiativePath = [Planning, Execution, Completed];
}

public static class ProjectStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string OnHold = "on-hold";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
}

/// <summary>
/// A Project or Initiative (SPEC §1.4 B, <c>PROJ-…</c>), created by the request's final approval with the request's data (ADR 0049). The
/// lifecycle after creation (Take Ownership, charter, planning…) is Phase 9.
/// </summary>
[Audited(EntityType)]
public sealed class Project : AuditableEntity
{
    public const string EntityType = "portfolio.project";

    private Project()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string Classification { get; private set; } = Classifications.Project;

    public Guid RequestId { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public string? ScopeOfWork { get; private set; }

    public string Stage { get; private set; } = ProjectStages.Initiation;

    public string Status { get; private set; } = ProjectStatuses.Active;

    public Guid? UnitId { get; private set; }

    public Guid? SectionUnitId { get; private set; }

    public string? StrategicObjectiveCode { get; private set; }

    public string? ProgramTypeCode { get; private set; }

    public decimal? Budget { get; private set; }

    public decimal? Year1PlannedCost { get; private set; }

    public decimal? Year2PlannedCost { get; private set; }

    public decimal? Year3PlannedCost { get; private set; }

    public decimal? Year4PlannedCost { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    /// <summary>Project Owner: the department director of the unit at approval (SPEC §1.9).</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public Guid? RequesterUserId { get; private set; }

    public static Project FromRequest(string number, ProgramRequest request, Guid? ownerEmployeeId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classification = request.EffectiveClassification;
        return new Project
        {
            Number = number,
            Classification = classification,
            RequestId = request.Id,
            NameAr = request.NameAr,
            NameEn = request.NameEn,
            Description = request.Description,
            ScopeOfWork = request.ScopeOfWork,
            Stage = classification == Classifications.Project ? ProjectStages.Initiation : ProjectStages.Planning,
            Status = ProjectStatuses.Active,
            UnitId = request.UnitId,
            SectionUnitId = request.SectionUnitId,
            StrategicObjectiveCode = request.StrategicObjectiveCode,
            ProgramTypeCode = request.ProgramTypeCode,
            Budget = request.RequiresBudget == true ? request.ApprovedBudget ?? request.EstimatedBudget : null,
            Year1PlannedCost = request.Year1Cost,
            Year2PlannedCost = request.Year2Cost,
            Year3PlannedCost = request.Year3Cost,
            Year4PlannedCost = request.Year4Cost,
            PlannedStart = request.ExpectedStartDate,
            PlannedEnd = request.ExpectedEndDate,
            OwnerEmployeeId = ownerEmployeeId,
            RequesterEmployeeId = request.RequesterEmployeeId,
            RequesterUserId = request.RequesterUserId,
        };
    }
}

/// <summary>One immutable version of the AI Proposal Assessment of a request (SPEC §1.15 A5–A6, ADR 0050).</summary>
[Audited(EntityType)]
[AuditParent(ProgramRequest.EntityType, nameof(RequestId))]
public sealed class ProposalAssessment : AuditableEntity
{
    public const string EntityType = "portfolio.assessment";

    private ProposalAssessment()
    {
    }

    public Guid RequestId { get; private set; }

    public int VersionNo { get; private set; }

    /// <summary>jsonb: the validated assessment (docs/design/portfolio.md §8.2) plus the server-computed similar records.</summary>
    public string Content { get; private set; } = "{}";

    public string? Language { get; private set; }

    /// <summary>jsonb array of the document names read.</summary>
    public string Documents { get; private set; } = "[]";

    public Guid? UsageId { get; private set; }

    public Guid? GeneratedByUserId { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public static ProposalAssessment Create(Guid requestId, int versionNo, string content, string? language, string documents, Guid? usageId, Guid? by, DateTimeOffset at) =>
        new()
        {
            RequestId = requestId, VersionNo = versionNo, Content = content, Language = language, Documents = documents, UsageId = usageId,
            GeneratedByUserId = by, GeneratedAt = at,
        };
}
