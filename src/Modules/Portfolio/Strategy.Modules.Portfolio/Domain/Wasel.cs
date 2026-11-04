using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Domain;

// Wasel vendor portal (SPEC §1.18, docs/design/portfolio.md §46–§57, ADRs 0110–0115).

public static class VendorStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Blacklisted = "blacklisted";

    public static readonly IReadOnlyList<string> All = [Active, Suspended, Blacklisted];
}

public static class VendorUserStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Dormant = "dormant";
}

public static class VendorRoles
{
    public const string Lead = "vendor-lead";
    public const string Contributor = "vendor-contributor";

    public static readonly IReadOnlyList<string> All = [Lead, Contributor];
}

public static class AssignmentStatuses
{
    public const string Active = "active";
    public const string Ended = "ended";
    public const string Revoked = "revoked";
}

public static class VendorMemberStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class ExpiryRules
{
    public const string ContractEnd = "contract-end";
    public const string ProjectClosure = "project-closure";
    public const string Date = "date";

    public static readonly IReadOnlyList<string> All = [ContractEnd, ProjectClosure, Date];
}

public static class UpdateModes
{
    public const string PmOnly = "pm-only";
    public const string VendorThenPm = "vendor-then-pm";

    public static readonly IReadOnlyList<string> All = [PmOnly, VendorThenPm];
}

public static class PendingSources
{
    public const string Pm = "pm";
    public const string Vendor = "vendor";
    public const string VendorEdited = "vendor-edited";
}

/// <summary>Vendor organisation in the shared supplier register (ADR 0112).</summary>
[Audited(EntityType)]
public sealed class Vendor : AuditableEntity
{
    public const string EntityType = "portfolio.vendor";

    private Vendor()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? TradeLicenceNo { get; private set; }

    public DateOnly? LicenceExpiry { get; private set; }

    public string? CountryCode { get; private set; }

    public string? Address { get; private set; }

    public string? ContactPerson { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string Status { get; private set; } = VendorStatuses.Active;

    public string? StatusReason { get; private set; }

    public string? Notes { get; private set; }

    public static Vendor Create(string number, VendorValues values)
    {
        var vendor = new Vendor { Id = Guid.CreateVersion7(), Number = number };
        vendor.Edit(values);
        return vendor;
    }

    public void Edit(VendorValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = Clean(v.NameAr);
        NameEn = Clean(v.NameEn);
        TradeLicenceNo = Clean(v.TradeLicenceNo);
        LicenceExpiry = v.LicenceExpiry;
        CountryCode = Clean(v.CountryCode);
        Address = Clean(v.Address);
        ContactPerson = Clean(v.ContactPerson);
        Email = Clean(v.Email)?.ToLowerInvariant();
        Phone = Clean(v.Phone);
        Notes = Clean(v.Notes);
    }

    public void SetStatus(string status, string? reason)
    {
        Status = status;
        StatusReason = Clean(reason);
    }

    public bool IsActive => Status == VendorStatuses.Active;

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record VendorValues(
    string? NameAr, string? NameEn, string? TradeLicenceNo, DateOnly? LicenceExpiry, string? CountryCode, string? Address, string? ContactPerson, string? Email,
    string? Phone, string? Notes);

/// <summary>A person of a vendor with a Wasel account (identity user of type vendor).</summary>
[Audited(EntityType)]
[AuditParent(Vendor.EntityType, nameof(VendorId))]
public sealed class VendorUser : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-user";

    private VendorUser()
    {
    }

    public Guid VendorId { get; private set; }

    public Guid UserId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? Language { get; private set; }

    public string Status { get; private set; } = VendorUserStatuses.Invited;

    public DateTimeOffset InvitedAt { get; private set; }

    public Guid? InvitedByUserId { get; private set; }

    public string? DisabledReason { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public static VendorUser Create(Guid vendorId, Guid userId, string email, string? name, string? language, DateTimeOffset at, Guid? by) => new()
    {
        Id = Guid.CreateVersion7(),
        VendorId = vendorId,
        UserId = userId,
        Email = email.Trim().ToLowerInvariant(),
        Name = Vendor.Clean(name),
        Language = language,
        InvitedAt = at,
        InvitedByUserId = by,
    };

    public bool MayEnter => Status is VendorUserStatuses.Invited or VendorUserStatuses.Active;

    public void MarkActive()
    {
        if (Status == VendorUserStatuses.Invited)
        {
            Status = VendorUserStatuses.Active;
        }
    }

    public void Disable(string status, string? reason, DateTimeOffset at)
    {
        Status = status;
        DisabledReason = Vendor.Clean(reason);
        DisabledAt = at;
    }

    public void Reactivate()
    {
        Status = VendorUserStatuses.Active;
        DisabledReason = null;
        DisabledAt = null;
    }
}

/// <summary>A vendor working on a project, with the date its access ends (ADR 0115).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorAssignment : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-assignment";

    private VendorAssignment()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public Guid? ContractId { get; private set; }

    public DateOnly? AccessUntil { get; private set; }

    public string ExpiryRule { get; private set; } = ExpiryRules.ContractEnd;

    public string Status { get; private set; } = AssignmentStatuses.Active;

    public string? EndedReason { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public static VendorAssignment Create(Guid projectId, Guid vendorId, Guid? contractId, DateOnly? accessUntil, string rule) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        ContractId = contractId,
        AccessUntil = accessUntil,
        ExpiryRule = rule,
    };

    public bool IsActive => Status == AssignmentStatuses.Active;

    public void Extend(DateOnly? accessUntil, Guid? contractId)
    {
        AccessUntil = accessUntil;
        ContractId = contractId ?? ContractId;
    }

    public void End(string status, string? reason, DateTimeOffset at)
    {
        Status = status;
        EndedReason = Vendor.Clean(reason);
        EndedAt = at;
    }
}

/// <summary>A vendor user on one project with a vendor role; mirrors the record role grant (ADR 0111).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorMember : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-member";

    private VendorMember()
    {
    }

    public Guid AssignmentId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid VendorUserId { get; private set; }

    public Guid UserId { get; private set; }

    public string Role { get; private set; } = VendorRoles.Contributor;

    public string Status { get; private set; } = VendorMemberStatuses.Active;

    public DateTimeOffset? ValidTo { get; private set; }

    public string? RevokedReason { get; private set; }

    public static VendorMember Create(VendorAssignment assignment, VendorUser user, string role, DateTimeOffset? validTo) => new()
    {
        Id = Guid.CreateVersion7(),
        AssignmentId = assignment.Id,
        ProjectId = assignment.ProjectId,
        VendorUserId = user.Id,
        UserId = user.UserId,
        Role = role,
        ValidTo = validTo,
    };

    public bool IsActive => Status == VendorMemberStatuses.Active;

    public void End(string status, string? reason)
    {
        Status = status;
        RevokedReason = Vendor.Clean(reason);
    }

    public void SetValidTo(DateTimeOffset? validTo) => ValidTo = validTo;
}

public static class ProposalStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Returned = "returned";
    public const string Accepted = "accepted";
    public const string Withdrawn = "withdrawn";

    public static bool Open(string status) => status is Draft or Submitted or Returned;
}

public static class ProposalActions
{
    public const string Edit = "edit";
    public const string Submit = "submit";
    public const string Accept = "accept";
    public const string Return = "return";
    public const string Withdraw = "withdraw";

    public static readonly IReadOnlyList<string> All = [Edit, Submit, Accept, Return, Withdraw];
}

/// <summary>Plan proposal lifecycle (docs/design/portfolio.md §51) — a transition table, unit-tested.</summary>
public static class PlanProposalLifecycle
{
    private static readonly Dictionary<(string, string), string> Table = new()
    {
        [(ProposalStatuses.Draft, ProposalActions.Edit)] = ProposalStatuses.Draft,
        [(ProposalStatuses.Returned, ProposalActions.Edit)] = ProposalStatuses.Draft,
        [(ProposalStatuses.Draft, ProposalActions.Submit)] = ProposalStatuses.Submitted,
        [(ProposalStatuses.Submitted, ProposalActions.Accept)] = ProposalStatuses.Accepted,
        [(ProposalStatuses.Submitted, ProposalActions.Return)] = ProposalStatuses.Returned,
        [(ProposalStatuses.Draft, ProposalActions.Withdraw)] = ProposalStatuses.Withdrawn,
        [(ProposalStatuses.Returned, ProposalActions.Withdraw)] = ProposalStatuses.Withdrawn,
    };

    public static Result<string> Next(string status, string action) =>
        Table.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.wasel.proposalTransition", "portfolio.errors.waselTransition", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });
}

/// <summary>A vendor's proposed detailed plan (W3.2).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class PlanProposal : AuditableEntity
{
    public const string EntityType = "portfolio.plan-proposal";

    private PlanProposal()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public int Sequence { get; private set; }

    public string Status { get; private set; } = ProposalStatuses.Draft;

    /// <summary><c>waterfall</c> (phases / milestones / tasks) or <c>agile</c> (epics / stories).</summary>
    public string Kind { get; private set; } = Methodologies.Waterfall;

    public string Items { get; private set; } = "[]";

    public string? AcceptedItems { get; private set; }

    public string? Diff { get; private set; }

    public string? VendorNote { get; private set; }

    public string? PmComment { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public static PlanProposal Create(Guid projectId, Guid vendorId, int sequence, string kind) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        Sequence = sequence,
        Kind = kind,
    };

    public Result Move(string action, DateTimeOffset at, Guid? by)
    {
        var next = PlanProposalLifecycle.Next(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        switch (action)
        {
            case ProposalActions.Submit:
                SubmittedAt = at;
                SubmittedByUserId = by;
                PmComment = null;
                break;
            case ProposalActions.Accept:
            case ProposalActions.Return:
                DecidedAt = at;
                DecidedByUserId = by;
                break;
        }

        return Result.Success();
    }

    public void SetItems(string items, string? note)
    {
        Items = items;
        VendorNote = Vendor.Clean(note);
    }

    public void Decide(string? comment, string? acceptedItems, string? diff)
    {
        PmComment = Vendor.Clean(comment);
        AcceptedItems = acceptedItems;
        Diff = diff;
    }
}

public static class SubmissionStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Returned = "returned";
    public const string Accepted = "accepted";
    public const string Included = "included";

    public static bool Editable(string status) => status is Draft or Returned;
}

public static class SubmissionActions
{
    public const string Edit = "edit";
    public const string Submit = "submit";
    public const string Review = "review";
    public const string AllAccepted = "all-accepted";
    public const string SomeReturned = "some-returned";
    public const string Include = "include";

    public static readonly IReadOnlyList<string> All = [Edit, Submit, Review, AllAccepted, SomeReturned, Include];
}

/// <summary>Vendor monthly submission lifecycle (§52) — a transition table, unit-tested.</summary>
public static class VendorSubmissionLifecycle
{
    private static readonly Dictionary<(string, string), string> Table = new()
    {
        [(SubmissionStatuses.Draft, SubmissionActions.Edit)] = SubmissionStatuses.Draft,
        [(SubmissionStatuses.Returned, SubmissionActions.Edit)] = SubmissionStatuses.Returned,
        [(SubmissionStatuses.Draft, SubmissionActions.Submit)] = SubmissionStatuses.Submitted,
        [(SubmissionStatuses.Returned, SubmissionActions.Submit)] = SubmissionStatuses.Submitted,
        [(SubmissionStatuses.Submitted, SubmissionActions.Review)] = SubmissionStatuses.Submitted,
        [(SubmissionStatuses.Submitted, SubmissionActions.AllAccepted)] = SubmissionStatuses.Accepted,
        [(SubmissionStatuses.Submitted, SubmissionActions.SomeReturned)] = SubmissionStatuses.Returned,
        [(SubmissionStatuses.Accepted, SubmissionActions.Include)] = SubmissionStatuses.Included,
    };

    public static Result<string> Next(string status, string action) =>
        Table.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.wasel.submissionTransition", "portfolio.errors.waselTransition", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });
}

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorSubmission : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-submission";

    private VendorSubmission()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public Guid MonthlyUpdateId { get; private set; }

    public string Period { get; private set; } = string.Empty;

    public string Status { get; private set; } = SubmissionStatuses.Draft;

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? SharedOutcome { get; private set; }

    public string? SharedNote { get; private set; }

    public DateTimeOffset? SharedAt { get; private set; }

    public static VendorSubmission Create(Guid projectId, Guid vendorId, Guid monthlyUpdateId, string period) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        MonthlyUpdateId = monthlyUpdateId,
        Period = period,
    };

    public Result Move(string action, DateTimeOffset at, Guid? by)
    {
        var next = VendorSubmissionLifecycle.Next(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        if (action == SubmissionActions.Submit)
        {
            SubmittedAt = at;
            SubmittedByUserId = by;
        }
        else if (action is SubmissionActions.AllAccepted or SubmissionActions.SomeReturned)
        {
            ReviewedAt = at;
        }

        return Result.Success();
    }

    public void Share(string? outcome, string? note, DateTimeOffset at)
    {
        SharedOutcome = outcome;
        SharedNote = Vendor.Clean(note);
        SharedAt = at;
    }
}

public static class ItemReviewStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Edited = "edited";
    public const string Returned = "returned";
}

/// <summary>One milestone of a vendor submission: the vendor's values, the PM's decision and the diff (§52).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorSubmissionItem : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-submission-item";

    private VendorSubmissionItem()
    {
    }

    public Guid SubmissionId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid MilestoneId { get; private set; }

    public decimal Achieve { get; private set; }

    public DateOnly? ActualStart { get; private set; }

    public DateOnly? ActualEnd { get; private set; }

    public string? ProgressNote { get; private set; }

    public string? Causality { get; private set; }

    public string? CorrectiveActions { get; private set; }

    public string? Previous { get; private set; }

    public string ReviewStatus { get; private set; } = ItemReviewStatuses.Pending;

    public decimal? FinalAchieve { get; private set; }

    public DateOnly? FinalActualStart { get; private set; }

    public DateOnly? FinalActualEnd { get; private set; }

    public string? FinalCausality { get; private set; }

    public string? FinalCorrectiveActions { get; private set; }

    public string? Diff { get; private set; }

    public string? PmComment { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public static VendorSubmissionItem Create(VendorSubmission submission, Guid milestoneId) => new()
    {
        Id = Guid.CreateVersion7(),
        SubmissionId = submission.Id,
        ProjectId = submission.ProjectId,
        MilestoneId = milestoneId,
    };

    public bool Locked => ReviewStatus is ItemReviewStatuses.Accepted or ItemReviewStatuses.Edited;

    public void SetValues(VendorProgressValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Achieve = v.Achieve;
        ActualStart = v.ActualStart;
        ActualEnd = v.ActualEnd;
        ProgressNote = Vendor.Clean(v.ProgressNote);
        Causality = Vendor.Clean(v.Causality);
        CorrectiveActions = Vendor.Clean(v.CorrectiveActions);
        if (ReviewStatus == ItemReviewStatuses.Returned)
        {
            ReviewStatus = ItemReviewStatuses.Pending;
        }
    }

    public void SnapshotPrevious(string previous) => Previous = previous;

    public VendorProgressValues Values => new(Achieve, ActualStart, ActualEnd, ProgressNote, Causality, CorrectiveActions);

    public void Accept(VendorProgressValues final, string? diff, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(final);
        ReviewStatus = diff is null ? ItemReviewStatuses.Accepted : ItemReviewStatuses.Edited;
        FinalAchieve = final.Achieve;
        FinalActualStart = final.ActualStart;
        FinalActualEnd = final.ActualEnd;
        FinalCausality = Vendor.Clean(final.Causality);
        FinalCorrectiveActions = Vendor.Clean(final.CorrectiveActions);
        Diff = diff;
        PmComment = null;
        ReviewedByUserId = by;
        ReviewedAt = at;
    }

    public void Return(string comment, Guid? by, DateTimeOffset at)
    {
        ReviewStatus = ItemReviewStatuses.Returned;
        PmComment = Vendor.Clean(comment);
        ReviewedByUserId = by;
        ReviewedAt = at;
    }

    public void ResetForResubmit()
    {
        if (ReviewStatus == ItemReviewStatuses.Returned)
        {
            ReviewStatus = ItemReviewStatuses.Pending;
        }
    }
}

public sealed record VendorProgressValues(
    decimal Achieve, DateOnly? ActualStart, DateOnly? ActualEnd, string? ProgressNote, string? Causality, string? CorrectiveActions);

public static class DeliverableStatuses
{
    public const string Submitted = "submitted";
    public const string Accepted = "accepted";
    public const string Returned = "returned";
}

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorDeliverable : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-deliverable";

    private VendorDeliverable()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Status { get; private set; } = DeliverableStatuses.Submitted;

    public string? PmComment { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public static VendorDeliverable Create(Guid projectId, Guid vendorId, Guid? milestoneId, string title, string? description) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        MilestoneId = milestoneId,
        Title = title.Trim(),
        Description = Vendor.Clean(description),
    };

    public Result Decide(bool accept, string? comment, Guid? by, DateTimeOffset at)
    {
        if (Status != DeliverableStatuses.Submitted)
        {
            return Error.Rule("portfolio.wasel.decided", "portfolio.errors.waselAlreadyDecided");
        }

        Status = accept ? DeliverableStatuses.Accepted : DeliverableStatuses.Returned;
        PmComment = Vendor.Clean(comment);
        DecidedAt = at;
        DecidedByUserId = by;
        return Result.Success();
    }
}

public static class VendorItemKinds
{
    public const string Issue = "issue";
    public const string Challenge = "challenge";
    public const string Risk = "risk";

    public static readonly IReadOnlyList<string> All = [Issue, Challenge, Risk];
}

public static class VendorItemStatuses
{
    public const string Proposed = "proposed";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

/// <summary>An issue, challenge or risk proposed by the vendor as a draft for the PM (SPEC §1.18).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorItem : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-item";

    private VendorItem()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public string Kind { get; private set; } = VendorItemKinds.Issue;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Impact { get; private set; }

    public int? Probability { get; private set; }

    public int? ImpactLevel { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public string Status { get; private set; } = VendorItemStatuses.Proposed;

    public string? PmComment { get; private set; }

    public Guid? CreatedRecordId { get; private set; }

    public static VendorItem Create(Guid projectId, Guid vendorId, VendorItemValues v) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        Kind = v.Kind,
        Title = v.Title.Trim(),
        Description = Vendor.Clean(v.Description),
        Impact = Vendor.Clean(v.Impact),
        Probability = v.Probability,
        ImpactLevel = v.ImpactLevel,
        MilestoneId = v.MilestoneId,
    };

    public Result Decide(bool accept, string? comment, Guid? recordId)
    {
        if (Status != VendorItemStatuses.Proposed)
        {
            return Error.Rule("portfolio.wasel.decided", "portfolio.errors.waselAlreadyDecided");
        }

        Status = accept ? VendorItemStatuses.Accepted : VendorItemStatuses.Rejected;
        PmComment = Vendor.Clean(comment);
        CreatedRecordId = recordId;
        return Result.Success();
    }
}

public sealed record VendorItemValues(string Kind, string Title, string? Description, string? Impact, int? Probability, int? ImpactLevel, Guid? MilestoneId);

public static class CommentSubjects
{
    public const string Milestone = "milestone";
    public const string Submission = "submission";
    public const string Proposal = "proposal";
    public const string Deliverable = "deliverable";

    public static readonly IReadOnlyList<string> All = [Milestone, Submission, Proposal, Deliverable];
}

/// <summary>Append-only comment between the vendor and the PM on one subject.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class VendorComment : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-comment";
    public const int MaxLength = 4000;

    private VendorComment()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid VendorId { get; private set; }

    public string SubjectType { get; private set; } = CommentSubjects.Milestone;

    public Guid SubjectId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string AuthorKind { get; private set; } = "vendor";

    public string? AuthorName { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public static VendorComment Create(Guid projectId, Guid vendorId, string subjectType, Guid subjectId, Guid author, string kind, string? authorName, string body) => new()
    {
        Id = Guid.CreateVersion7(),
        ProjectId = projectId,
        VendorId = vendorId,
        SubjectType = subjectType,
        SubjectId = subjectId,
        AuthorUserId = author,
        AuthorKind = kind,
        AuthorName = authorName,
        Body = body.Trim(),
    };
}

/// <summary>The PM's rating of a vendor on a project (closure) or one milestone (ADR 0115).</summary>
[Audited(EntityType)]
[AuditParent(Vendor.EntityType, nameof(VendorId))]
public sealed class VendorRating : AuditableEntity
{
    public const string EntityType = "portfolio.vendor-rating";

    private VendorRating()
    {
    }

    public Guid VendorId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public int Timeliness { get; private set; }

    public int Quality { get; private set; }

    public int Responsiveness { get; private set; }

    public int Reporting { get; private set; }

    public decimal Overall { get; private set; }

    public string? Comment { get; private set; }

    public Guid? RatedByUserId { get; private set; }

    public DateTimeOffset RatedAt { get; private set; }

    public static VendorRating Create(Guid vendorId, Guid projectId, Guid? milestoneId, VendorRatingValues v, Guid? by, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        VendorId = vendorId,
        ProjectId = projectId,
        MilestoneId = milestoneId,
        Timeliness = v.Timeliness,
        Quality = v.Quality,
        Responsiveness = v.Responsiveness,
        Reporting = v.Reporting,
        Overall = VendorRatingCalculator.Overall(v),
        Comment = Vendor.Clean(v.Comment),
        RatedByUserId = by,
        RatedAt = at,
    };
}

public sealed record VendorRatingValues(int Timeliness, int Quality, int Responsiveness, int Reporting, string? Comment);

/// <summary>Pure rating rules (ADR 0115).</summary>
public static class VendorRatingCalculator
{
    public static bool Valid(VendorRatingValues v) =>
        v is not null && new[] { v.Timeliness, v.Quality, v.Responsiveness, v.Reporting }.All(x => x is >= 1 and <= 5);

    public static decimal Overall(VendorRatingValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return decimal.Round((v.Timeliness + v.Quality + v.Responsiveness + v.Reporting) / 4m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Impact "vendor satisfaction" (1–5) from closure ratings: rounded average of the overall scores, null when none.</summary>
    public static int? Satisfaction(IReadOnlyCollection<decimal> overalls) =>
        overalls.Count == 0 ? null : (int)decimal.Round(overalls.Average(), 0, MidpointRounding.AwayFromZero);
}

/// <summary>One field the PM changed before accepting a vendor value.</summary>
public sealed record DiffEntry(string Field, string? Vendor, string? Pm);

/// <summary>Field-by-field difference between the vendor's and the PM's milestone values (§52).</summary>
public static class VendorDiff
{
    public static IReadOnlyList<DiffEntry> Of(VendorProgressValues vendor, VendorProgressValues pm)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(pm);
        var diff = new List<DiffEntry>();
        void Add(string field, string? v, string? p)
        {
            if (!string.Equals(v, p, StringComparison.Ordinal))
            {
                diff.Add(new DiffEntry(field, v, p));
            }
        }

        Add("achieve", Number(vendor.Achieve), Number(pm.Achieve));
        Add("actualStart", vendor.ActualStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            pm.ActualStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add("actualEnd", vendor.ActualEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            pm.ActualEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add("causality", Vendor.Clean(vendor.Causality), Vendor.Clean(pm.Causality));
        Add("correctiveActions", Vendor.Clean(vendor.CorrectiveActions), Vendor.Clean(pm.CorrectiveActions));
        return diff;
    }

    private static string Number(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Server-computed checks on a vendor submission item for the PM (§57) — no model involved.</summary>
public static class VendorInconsistencies
{
    public const decimal DefaultJumpThreshold = 0.30m;

    public static IReadOnlyList<string> Flags(
        VendorProgressValues v, decimal weight, decimal approved, DateOnly? plannedStart, DateOnly? plannedEnd, DateOnly today, int evidenceFiles, DateOnly? accessFrom,
        DateOnly? accessUntil, decimal jumpThreshold = DefaultJumpThreshold)
    {
        ArgumentNullException.ThrowIfNull(v);
        var flags = new List<string>();
        if (weight > 0 && v.Achieve >= weight && evidenceFiles == 0)
        {
            flags.Add("completeWithoutEvidence");
        }

        if (weight > 0 && (v.Achieve - approved) / weight > jumpThreshold)
        {
            flags.Add("progressJump");
        }

        foreach (var date in new[] { v.ActualStart, v.ActualEnd })
        {
            if (date is { } d && ((accessUntil is { } until && d > until) || (accessFrom is { } from && d < from)))
            {
                flags.Add("dateOutsideContract");
                break;
            }
        }

        if (v.ActualEnd is { } end && v.ActualStart is { } start && end < start)
        {
            flags.Add("endBeforeStart");
        }

        if (v.ActualStart is { } s && plannedStart is { } ps && s < ps.AddDays(-30))
        {
            flags.Add("startBeforePlan");
        }

        if (plannedEnd is { } pe && today > pe && v.Achieve < weight && string.IsNullOrWhiteSpace(v.Causality))
        {
            flags.Add("delayedWithoutCausality");
        }

        if (v.ActualEnd is { } e2 && e2 > today)
        {
            flags.Add("endInFuture");
        }

        return flags;
    }
}

// ---- Portfolio completion (SPEC §1.12, ADR 0116) ----

public static class ReviewDecisions
{
    public const string Continue = "continue";
    public const string Rescope = "re-scope";
    public const string Hold = "hold";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [Continue, Rescope, Hold, Cancel];

    /// <summary>The change request a decision starts (null = none); never a direct change of the project.</summary>
    public static string? ChangeTypeFor(string decision) => decision switch
    {
        Hold => ChangeTypes.Hold,
        Cancel => ChangeTypes.Cancellation,
        Rescope => ChangeTypes.Details,
        _ => null,
    };

    /// <summary>Hold and cancel are submitted into the approval at once; re-scope stays a draft for the PM.</summary>
    public static bool SubmitsAtOnce(string decision) => decision is Hold or Cancel;
}

public static class ReviewStatuses
{
    public const string Draft = "draft";
    public const string Completed = "completed";
}

/// <summary>Quarterly portfolio review (SPEC §1.12).</summary>
[Audited(EntityType)]
public sealed class PortfolioReview : AuditableEntity
{
    public const string EntityType = "portfolio.review";

    private PortfolioReview()
    {
    }

    public string Period { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public DateOnly? ReviewDate { get; private set; }

    public string? Notes { get; private set; }

    public string Status { get; private set; } = ReviewStatuses.Draft;

    public DateTimeOffset? CompletedAt { get; private set; }

    public static PortfolioReview Create(string period, string? title, DateOnly? date, string? notes)
    {
        var review = new PortfolioReview { Id = Guid.CreateVersion7(), Period = period };
        review.Edit(title, date, notes);
        return review;
    }

    public void Edit(string? title, DateOnly? date, string? notes)
    {
        Title = Vendor.Clean(title);
        ReviewDate = date;
        Notes = Vendor.Clean(notes);
    }

    public void Complete(DateTimeOffset at)
    {
        Status = ReviewStatuses.Completed;
        CompletedAt = at;
    }

    public static bool ValidPeriod(string? period) =>
        period is { Length: 7 } p && int.TryParse(p[..4], out var year) && year is >= 2000 and <= 2100 && p[4] == '-' && p[5] == 'Q' && p[6] is >= '1' and <= '4';
}

[Audited(EntityType)]
[AuditParent(PortfolioReview.EntityType, nameof(ReviewId))]
public sealed class ReviewDecision : AuditableEntity
{
    public const string EntityType = "portfolio.review-decision";

    private ReviewDecision()
    {
    }

    public Guid ReviewId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Decision { get; private set; } = ReviewDecisions.Continue;

    public string? Comment { get; private set; }

    public Guid? ChangeRequestId { get; private set; }

    public static ReviewDecision Create(Guid reviewId, Guid projectId) => new() { Id = Guid.CreateVersion7(), ReviewId = reviewId, ProjectId = projectId };

    public void Set(string decision, string? comment)
    {
        Decision = decision;
        Comment = Vendor.Clean(comment);
    }

    public void LinkChangeRequest(Guid id) => ChangeRequestId = id;
}

/// <summary>Employee allocation across projects and teams (SPEC §1.12 capacity view, ADR 0116).</summary>
public static class CapacityCalculator
{
    public const decimal DefaultThreshold = 1m;

    /// <summary>Total allocation (ratio) of one employee; allocations without a value count 0.</summary>
    public static decimal Total(IEnumerable<decimal?> allocations) => allocations.Sum(a => a ?? 0m);

    public static bool OverAllocated(decimal total, decimal threshold) => total > threshold;

    /// <summary>Proposed charter members count only for projects with no formed team (the team's own allocation is used otherwise).</summary>
    public static bool CountProposedMember(Guid? projectTeamId) => projectTeamId is null;
}
