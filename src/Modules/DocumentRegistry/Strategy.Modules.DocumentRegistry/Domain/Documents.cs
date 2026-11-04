using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.DocumentRegistry.Domain;

/// <summary>Document statuses (SPEC §6.2). A published document with a version in review shows "under revision" from that version.</summary>
public static class DocumentStatuses
{
    public const string Submitted = "submitted";
    public const string UnderReview = "under_review";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string Published = "published";
    public const string Expired = "expired";
    public const string Withdrawn = "withdrawn";

    public static readonly IReadOnlyList<string> All = [Submitted, UnderReview, Returned, Rejected, Approved, Published, Expired, Withdrawn];

    /// <summary>In the active library.</summary>
    public static bool IsActive(string status) => status is Published or Expired;
}

public static class VersionStatuses
{
    public const string Submitted = "submitted";
    public const string UnderReview = "under_review";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string Current = "current";
    public const string Superseded = "superseded";
    public const string Withdrawn = "withdrawn";
    public const string Cancelled = "cancelled";

    public static bool InProgress(string status) => status is Submitted or UnderReview or Returned or Approved;
}

/// <summary>A controlled document (SPEC §6.3).</summary>
[Audited(EntityType)]
public sealed class Document : AuditableEntity
{
    public const string EntityType = DocumentResource.Type;
    public const int NameMaxLength = 400;
    public const int TextMaxLength = 4000;

    private Document()
    {
    }

    public string? DocumentNo { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public Guid? OwnerUnitId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public string? Level { get; private set; }

    public string? DocType { get; private set; }

    public string? Sensitivity { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public string? CurrentVersionLabel { get; private set; }

    public string Status { get; private set; } = DocumentStatuses.Submitted;

    public DateOnly? EffectiveDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public bool ShareToAll { get; private set; }

    public bool AckRequired { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public string? WithdrawnReason { get; private set; }

    [NotAudited]
    public string? SearchText { get; private set; }

    public static Document Create(string? nameAr, string? nameEn, string? description, Guid? ownerUnitId, Guid? requesterEmployeeId)
    {
        var document = new Document { RequesterEmployeeId = requesterEmployeeId, OwnerEmployeeId = requesterEmployeeId };
        document.Describe(nameAr, nameEn, description, ownerUnitId);
        return document;
    }

    public void Describe(string? nameAr, string? nameEn, string? description, Guid? ownerUnitId)
    {
        NameAr = Clean(nameAr, NameMaxLength);
        NameEn = Clean(nameEn, NameMaxLength);
        Description = Clean(description, TextMaxLength);
        OwnerUnitId = ownerUnitId ?? OwnerUnitId;
        RefreshSearch(null);
    }

    public void SetStatus(string status) => Status = status;

    public void Classify(string level, string docType, string sensitivity)
    {
        Level = level;
        DocType = docType;
        Sensitivity = sensitivity;
    }

    public void AssignNumber(string number)
    {
        DocumentNo ??= number;
        RefreshSearch(null);
    }

    public void SetOwner(Guid? ownerEmployeeId) => OwnerEmployeeId = ownerEmployeeId ?? OwnerEmployeeId;

    public void Publish(Guid versionId, string label, DateOnly effective, DateOnly? expiry, bool shareToAll, bool ackRequired, DateTimeOffset at, string? text)
    {
        CurrentVersionId = versionId;
        CurrentVersionLabel = label;
        EffectiveDate = effective;
        ExpiryDate = expiry;
        ShareToAll = shareToAll;
        AckRequired = ackRequired;
        PublishedAt = at;
        Status = DocumentStatuses.Published;
        RefreshSearch(text);
    }

    public void SetExpiry(DateOnly? expiry)
    {
        ExpiryDate = expiry;
        if (Status == DocumentStatuses.Expired && (expiry is null || expiry > EffectiveDate))
        {
            Status = DocumentStatuses.Published;
        }
    }

    public void Expire() => Status = DocumentStatuses.Expired;

    public void Withdraw(string reason)
    {
        Status = DocumentStatuses.Withdrawn;
        WithdrawnReason = Clean(reason, 2000);
    }

    /// <summary>Normalised text of the current version's file (full-text search, ADR 0083 visibility applies).</summary>
    [NotAudited]
    public string? FileSearchText { get; private set; }

    public void RefreshSearch(string? fileText)
    {
        if (fileText is not null)
        {
            var normalised = Strategy.Platform.Kernel.Text.SearchText.Normalise(fileText);
            FileSearchText = normalised.Length > 200_000 ? normalised[..200_000] : normalised;
        }

        SearchText = Strategy.Platform.Kernel.Text.SearchText.Normalise(DocumentNo, NameAr, NameEn, Description);
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

/// <summary>A version of a document (SPEC §6.3) with its review request and the signed final file.</summary>
[Audited(EntityType)]
[AuditParent(Document.EntityType, nameof(DocumentId))]
public sealed class DocumentVersion : AuditableEntity
{
    public const string EntityType = "docreg.version";

    private DocumentVersion()
    {
    }

    public Guid DocumentId { get; private set; }

    public int Major { get; private set; }

    public int Minor { get; private set; }

    public string? Label { get; private set; }

    public Guid RequestId { get; private set; }

    public string? RequestNo { get; private set; }

    public string? ChangeSummary { get; private set; }

    public string Status { get; private set; } = VersionStatuses.Submitted;

    public Guid? RequestedByEmployeeId { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Guid? PublishedBy { get; private set; }

    public Guid? FinalFileId { get; private set; }

    public string? FinalFileName { get; private set; }

    public bool IsNewDocument { get; private set; }

    /// <summary>The draft file of the review request (owned by the request; read for AI classification and compare).</summary>
    public Guid? DraftFileId { get; private set; }

    [NotAudited]
    public string? TextExtract { get; private set; }

    public string? SummaryAr { get; private set; }

    public string? SummaryEn { get; private set; }

    public static DocumentVersion Create(Guid documentId, Guid requestId, string? requestNo, string? changeSummary, Guid? requester, bool isNew, Guid? draftFileId) => new()
    {
        DraftFileId = draftFileId,
        DocumentId = documentId,
        RequestId = requestId,
        RequestNo = requestNo,
        ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary.Trim(),
        RequestedByEmployeeId = requester,
        IsNewDocument = isNew,
    };

    public void SetChange(string? changeSummary, Guid? draftFileId)
    {
        ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary.Trim();
        DraftFileId = draftFileId ?? DraftFileId;
    }

    public void SetStatus(string status) => Status = status;

    public void Approve(int major, int minor, Guid? reviewer, DateTimeOffset at)
    {
        Major = major;
        Minor = minor;
        Label = VersionNumbers.Label(major, minor);
        ReviewedBy = reviewer;
        ApprovedAt = at;
        Status = VersionStatuses.Approved;
    }

    public void Publish(Guid fileId, string fileName, Guid? by, DateTimeOffset at, string? text)
    {
        FinalFileId = fileId;
        FinalFileName = fileName;
        PublishedBy = by;
        PublishedAt = at;
        Status = VersionStatuses.Current;
        TextExtract = text is { Length: > 100_000 } ? text[..100_000] : text;
    }

    public void SetSummary(string? ar, string? en)
    {
        SummaryAr = ar;
        SummaryEn = en;
    }
}

/// <summary>A read acknowledgement of a version by an employee (SPEC §6.3, optional).</summary>
public sealed class Acknowledgement : Entity
{
    private Acknowledgement()
    {
    }

    public Guid DocumentId { get; private set; }

    public Guid VersionId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateTimeOffset AcknowledgedAt { get; private set; }

    public static Acknowledgement Create(Guid documentId, Guid versionId, Guid employeeId, DateTimeOffset at) =>
        new() { DocumentId = documentId, VersionId = versionId, EmployeeId = employeeId, AcknowledgedAt = at };
}

public static class ValidityEventKinds
{
    public const string Reminder = "reminder";
    public const string Confirmed = "confirmed";
    public const string Expired = "expired";
    public const string ExpiryChanged = "expiry-changed";
}

/// <summary>Validity log (ADR 0084): reminders sent (idempotency of the job), confirmations, expiry, edits.</summary>
public sealed class ValidityEvent : Entity
{
    private ValidityEvent()
    {
    }

    public Guid DocumentId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    /// <summary>Reminders: days before expiry.</summary>
    public int? Days { get; private set; }

    public DateOnly? OldExpiry { get; private set; }

    public DateOnly? NewExpiry { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static ValidityEvent Create(Guid documentId, string kind, int? days, DateOnly? oldExpiry, DateOnly? newExpiry, Guid? actor, string? comment, DateTimeOffset at) =>
        new() { DocumentId = documentId, Kind = kind, Days = days, OldExpiry = oldExpiry, NewExpiry = newExpiry, ActorUserId = actor, Comment = comment, At = at };
}

/// <summary>major.minor version labels (ADR 0082): first approval 1.0, a new version request the next major, a correction the next minor.</summary>
public static class VersionNumbers
{
    public static string Label(int major, int minor) => $"{major}.{minor}";

    public static (int Major, int Minor) Next(IEnumerable<(int Major, int Minor)> existing, bool correction)
    {
        var list = existing.Where(v => v.Major > 0).ToList();
        if (list.Count == 0)
        {
            return (1, 0);
        }

        var top = list.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).First();
        return correction ? (top.Major, top.Minor + 1) : (top.Major + 1, 0);
    }
}

/// <summary>Validity by document type (ADR 0084): until superseded (null) or N months from the effective date.</summary>
public static class ValidityCalculator
{
    public static DateOnly? ExpiryFrom(DateOnly effective, int? months) => months is > 0 and var m ? effective.AddMonths(m) : null;

    /// <summary>Confirmation extends from the current expiry, or from today when the document already expired.</summary>
    public static DateOnly? Extend(DateOnly? currentExpiry, DateOnly today, int? months) =>
        months is not > 0 ? null : (currentExpiry is { } e && e > today ? e : today).AddMonths(months.Value);

    /// <summary>Reminder day counts due today: each configured count once, when the days left reach it (largest first).</summary>
    public static IReadOnlyList<int> DueReminders(DateOnly expiry, DateOnly today, IEnumerable<int> days, IReadOnlyCollection<int> alreadySent)
    {
        var left = expiry.DayNumber - today.DayNumber;
        return left < 0 ? [] : days.Where(d => d > 0 && left <= d && !alreadySent.Contains(d)).OrderByDescending(d => d).ToList();
    }

    public static bool IsExpired(DateOnly? expiry, DateOnly today) => expiry is { } e && today >= e;
}
