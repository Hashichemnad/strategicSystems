using Strategy.Modules.IpRegistry.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.IpRegistry.Domain;

/// <summary>IP statuses (SPEC §7.2).</summary>
public static class IpStatuses
{
    public const string Submitted = "submitted";
    public const string UnderReview = "under_review";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string ApprovedToFile = "approved_to_file";
    public const string Filed = "filed";
    public const string Registered = "registered";
    public const string RejectedByMoet = "rejected_by_moet";
    public const string Expired = "expired";
    public const string Renewed = "renewed";
    public const string Withdrawn = "withdrawn";

    public static readonly IReadOnlyList<string> All =
        [Submitted, UnderReview, Returned, Rejected, ApprovedToFile, Filed, Registered, RejectedByMoet, Expired, Renewed, Withdrawn];

    /// <summary>Protected now (a registration period is running).</summary>
    public static bool IsProtected(string status) => status is Registered or Renewed;
}

public static class IpSources
{
    public const string Request = "request";
    public const string Document = "document";
}

/// <summary>An intellectual property record (SPEC §7.3).</summary>
[Audited(EntityType)]
public sealed class IpRecord : AuditableEntity
{
    public const string EntityType = IpResource.Type;
    public const int NameMaxLength = 400;
    public const int TextMaxLength = 8000;

    private IpRecord()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string? PropertyType { get; private set; }

    public string? ProtectionType { get; private set; }

    public string Source { get; private set; } = IpSources.Request;

    public Guid? RequestId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? DocumentVersionId { get; private set; }

    public string? DocumentNo { get; private set; }

    public Guid? OwnerUnitId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public string Status { get; private set; } = IpStatuses.Submitted;

    public DateOnly? FilingDate { get; private set; }

    public string? ApplicationNo { get; private set; }

    public string? RegistrationNo { get; private set; }

    public DateOnly? RegistrationDate { get; private set; }

    /// <summary>First publication of the work (copyright terms count from it, ADR 0087).</summary>
    public DateOnly? FirstPublicationDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public Guid? CertificateFileId { get; private set; }

    public bool Internal { get; private set; }

    public string? Notes { get; private set; }

    public string? MoetRejectionReason { get; private set; }

    [NotAudited]
    public string? SearchText { get; private set; }

    public static IpRecord FromRequest(string number, Guid requestId, string? nameAr, string? nameEn, string? description, Guid? ownerUnit, Guid? requester)
    {
        var record = new IpRecord { Number = number, RequestId = requestId, Source = IpSources.Request, OwnerUnitId = ownerUnit, RequesterEmployeeId = requester };
        record.Describe(nameAr, nameEn, description);
        return record;
    }

    public static IpRecord FromDocument(string number, Guid documentId, Guid versionId, string documentNo, string? nameAr, string? nameEn, string? description,
        Guid? ownerUnit, Guid? requester, string propertyType, DateOnly? firstPublication)
    {
        var record = new IpRecord
        {
            Number = number,
            Source = IpSources.Document,
            DocumentId = documentId,
            DocumentVersionId = versionId,
            DocumentNo = documentNo,
            OwnerUnitId = ownerUnit,
            RequesterEmployeeId = requester,
            PropertyType = propertyType,
            Status = IpStatuses.ApprovedToFile,
            FirstPublicationDate = firstPublication,
        };
        record.Describe(nameAr, nameEn, description);
        return record;
    }

    public void Describe(string? nameAr, string? nameEn, string? description)
    {
        NameAr = Clean(nameAr, NameMaxLength);
        NameEn = Clean(nameEn, NameMaxLength);
        Description = Clean(description, TextMaxLength);
        SearchText = Platform.Kernel.Text.SearchText.Normalise(Number, NameAr, NameEn, Description, DocumentNo);
    }

    public void SetStatus(string status) => Status = status;

    public void Classify(string propertyType, string protectionType, string? notes, bool internalOnly)
    {
        PropertyType = propertyType;
        ProtectionType = protectionType;
        Notes = Clean(notes, TextMaxLength) ?? Notes;
        Internal = internalOnly;
        Status = IpStatuses.ApprovedToFile;
    }

    /// <summary>The MOET description texts (AI pre-fill accepted by the office).</summary>
    public void SetMoetDescriptions(string? ar, string? en)
    {
        DescriptionAr = Clean(ar, TextMaxLength);
        DescriptionEn = Clean(en, TextMaxLength);
    }

    public void File(DateOnly filingDate, string applicationNo)
    {
        FilingDate = filingDate;
        ApplicationNo = applicationNo.Trim();
        MoetRejectionReason = null;
        Status = IpStatuses.Filed;
    }

    public void RejectByMoet(string reason)
    {
        MoetRejectionReason = Clean(reason, 2000);
        Status = IpStatuses.RejectedByMoet;
    }

    public void Register(string registrationNo, DateOnly registrationDate, DateOnly? expiry, Guid? certificateFileId, bool internalOnly, DateOnly? firstPublication = null)
    {
        FirstPublicationDate = firstPublication ?? FirstPublicationDate;
        RegistrationNo = registrationNo.Trim();
        RegistrationDate = registrationDate;
        ExpiryDate = expiry;
        CertificateFileId = certificateFileId ?? CertificateFileId;
        Internal = internalOnly;
        Status = IpStatuses.Registered;
    }

    public void Renew(string registrationNo, DateOnly to, Guid? certificateFileId)
    {
        RegistrationNo = registrationNo.Trim();
        ExpiryDate = to;
        CertificateFileId = certificateFileId ?? CertificateFileId;
        Status = IpStatuses.Renewed;
    }

    public void Expire() => Status = IpStatuses.Expired;

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

public static class SignatureStatuses
{
    public const string Pending = "pending";
    public const string SignedOnPaper = "signed";
    public const string Uploaded = "uploaded";
}

/// <summary>A transferring party / contributor (SPEC §7.3). The Emirates ID is encrypted (ADR 0086) and never audited in clear.</summary>
[Audited(EntityType)]
[AuditParent(IpRecord.EntityType, nameof(IpRecordId))]
public sealed class Contributor : AuditableEntity
{
    public const string EntityType = "ip.contributor";

    private Contributor()
    {
    }

    public Guid IpRecordId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string? ExternalName { get; private set; }

    public string? ExternalOrganisation { get; private set; }

    [AuditMasked]
    public string? EmiratesIdEncrypted { get; private set; }

    public string? EmiratesIdLast { get; private set; }

    public string? Role { get; private set; }

    /// <summary>Contribution as a ratio (1.0 = 100 %).</summary>
    public decimal Share { get; private set; }

    public string SignatureStatus { get; private set; } = SignatureStatuses.Pending;

    public DateTimeOffset? CertificateSentAt { get; private set; }

    public int Sort { get; private set; }

    public static Contributor Create(Guid recordId, Guid? employeeId, string? externalName, string? externalOrganisation, string? role, decimal share, int sort) => new()
    {
        IpRecordId = recordId,
        EmployeeId = employeeId,
        ExternalName = string.IsNullOrWhiteSpace(externalName) ? null : externalName.Trim(),
        ExternalOrganisation = string.IsNullOrWhiteSpace(externalOrganisation) ? null : externalOrganisation.Trim(),
        Role = role,
        Share = share,
        Sort = sort,
    };

    public void SetEmiratesId(string? encrypted, string? last)
    {
        EmiratesIdEncrypted = encrypted;
        EmiratesIdLast = last;
    }

    public void Edit(Guid? employeeId, string? externalName, string? externalOrganisation, string? role, decimal share, int sort)
    {
        EmployeeId = employeeId;
        ExternalName = string.IsNullOrWhiteSpace(externalName) ? null : externalName.Trim();
        ExternalOrganisation = string.IsNullOrWhiteSpace(externalOrganisation) ? null : externalOrganisation.Trim();
        Role = role;
        Share = share;
        Sort = sort;
    }

    public void SetSignature(string status) => SignatureStatus = status;

    public void MarkCertificateSent(DateTimeOffset at) => CertificateSentAt = at;

    public string? EmiratesIdMasked => EmiratesIdLast is null ? null : $"784-****-*******-{EmiratesIdLast}";
}

/// <summary>A registration period (initial or renewal, SPEC §7.3).</summary>
[Audited(EntityType)]
[AuditParent(IpRecord.EntityType, nameof(IpRecordId))]
public sealed class RegistrationPeriod : AuditableEntity
{
    public const string EntityType = "ip.registration-period";

    private RegistrationPeriod()
    {
    }

    public Guid IpRecordId { get; private set; }

    public string Kind { get; private set; } = "initial";

    public DateOnly From { get; private set; }

    public DateOnly? To { get; private set; }

    public string RegistrationNo { get; private set; } = string.Empty;

    public Guid? CertificateFileId { get; private set; }

    public static RegistrationPeriod Create(Guid recordId, string kind, DateOnly from, DateOnly? to, string registrationNo, Guid? certificate) =>
        new() { IpRecordId = recordId, Kind = kind, From = from, To = to, RegistrationNo = registrationNo.Trim(), CertificateFileId = certificate };
}

/// <summary>Renewal reminders sent (idempotency of the job, ADR 0087).</summary>
public sealed class ReminderEvent : Entity
{
    private ReminderEvent()
    {
    }

    public Guid IpRecordId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public int? Months { get; private set; }

    public DateOnly Expiry { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static ReminderEvent Create(Guid recordId, string kind, int? months, DateOnly expiry, DateTimeOffset at) =>
        new() { IpRecordId = recordId, Kind = kind, Months = months, Expiry = expiry, At = at };
}

/// <summary>Pure IP rules (unit-tested): contribution shares, expiry proposals, reminder months, Emirates ID mask.</summary>
public static class IpRules
{
    /// <summary>Shares are percentages on the form; they must total 100 (± 0.01).</summary>
    public static bool SharesTotal100(IEnumerable<decimal> percentages) => Math.Abs(percentages.Sum() - 100m) <= 0.01m;

    public static DateOnly? ProposedExpiry(DateOnly registrationDate, int? termYears) => termYears is > 0 and var t ? registrationDate.AddYears(t) : null;

    /// <summary>
    /// The expiry a protection rule gives (ADR 0087): term years from the registration date, or from the first publication for publication-based
    /// terms (null when that date is unknown — the office must give it or an explicit expiry).
    /// </summary>
    public static DateOnly? ProposedExpiry(DateOnly registrationDate, DateOnly? firstPublication, int? termYears, string termFrom) =>
        termFrom == "publication"
            ? firstPublication is { } published ? ProposedExpiry(published, termYears) : null
            : ProposedExpiry(registrationDate, termYears);

    /// <summary>Reminder month counts due today (each once per expiry), largest first.</summary>
    public static IReadOnlyList<int> DueReminders(DateOnly expiry, DateOnly today, IEnumerable<int> months, IReadOnlyCollection<int> alreadySent) =>
        today >= expiry ? [] : months.Where(m => m > 0 && today >= expiry.AddMonths(-m) && !alreadySent.Contains(m)).OrderByDescending(m => m).ToList();

    public static string MaskEmiratesId(string? plain) => plain is { Length: > 0 } ? $"784-****-*******-{plain[^1]}" : string.Empty;
}
