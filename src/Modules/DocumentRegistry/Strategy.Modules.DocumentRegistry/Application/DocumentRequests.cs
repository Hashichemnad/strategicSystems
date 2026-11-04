using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>Built-in request types of the Document Registry (docs/design/requests.md §3.3) and their approval processes.</summary>
public static class DocumentRequestTypes
{
    public const string Submission = "docreg.document";
    public const string Withdrawal = "docreg.withdrawal";

    public const string ReviewProcess = "docreg.review";
    public const string WithdrawalProcess = "docreg.withdrawal";

    public const string ReviewStage = "review";
    public const string PublishStage = "publish";
    public const string PublishAction = "publish";
    public const string ReturnAction = "return";

    private static FormField F(string code, string type, string ar, string en, bool required = false) =>
        new() { Code = code, Type = type, LabelAr = ar, LabelEn = en, Required = required, System = true };

    public static readonly RequestTypeRegistration SubmissionType = new(
        Submission, DocRegPermissions.SystemCode, "تقديم وثيقة", "Document submission",
        "تقديم وثيقة جديدة أو إصدار جديد من وثيقة قائمة للمراجعة والاعتماد والنشر.",
        "Submit a new document, or a new version of an existing document, for review, approval and publication.",
        "DOCREQ", "description",
        new FormSchema
        {
            Sections =
            [
                new FormSection
                {
                    Code = "document", TitleAr = "الوثيقة", TitleEn = "Document", System = true,
                    Fields =
                    [
                        F("isNewVersion", FieldTypes.Checkbox, "إصدار جديد من وثيقة قائمة؟", "Is it a new version of an existing document?") with
                        {
                            HelpAr = "اختر الوثيقة القائمة ليصبح هذا الطلب إصداراً جديداً منها.", HelpEn = "Pick the existing document: this request becomes its new version.",
                        },
                        F("existingDocument", FieldTypes.Record, "الوثيقة القائمة", "Existing document") with
                        {
                            TargetType = DocumentResource.Type,
                            VisibleWhen = new FieldCondition("isNewVersion", "eq", true),
                            RequiredWhen = new FieldCondition("isNewVersion", "eq", true),
                        },
                        F("nameAr", FieldTypes.Text, "اسم الوثيقة (عربي)", "Document name (Arabic)", true) with { MaxLength = Document.NameMaxLength, TitleOf = "ar" },
                        F("nameEn", FieldTypes.Text, "اسم الوثيقة (إنجليزي)", "Document name (English)", true) with { MaxLength = Document.NameMaxLength, TitleOf = "en" },
                        F("ownerUnit", FieldTypes.Unit, "الوحدة المالكة", "Owner unit", true) with { RequestUnit = true },
                        F("description", FieldTypes.Textarea, "الوصف", "Description") with { MaxLength = Document.TextMaxLength },
                        F("draftFile", FieldTypes.File, "مسودة الوثيقة", "Draft file", true),
                        F("changeReason", FieldTypes.Textarea, "سبب التغيير", "Reason for change") with
                        {
                            VisibleWhen = new FieldCondition("isNewVersion", "eq", true),
                            RequiredWhen = new FieldCondition("isNewVersion", "eq", true),
                            MaxLength = 4000,
                        },
                    ],
                },
            ],
        },
        [
            new StageDefinition
            {
                Code = ReviewStage, Kind = StageKinds.Approval, NameAr = "المراجعة والتصنيف", NameEn = "Review and classification", ProcessKey = ReviewProcess, System = true,
                ApproveForm = FormSchema.Of(
                    F("level", FieldTypes.Picklist, "المستوى الهرمي", "Hierarchical level", true) with { LookupType = DocRegPermissions.LevelLookup },
                    F("docType", FieldTypes.Picklist, "نوع الوثيقة", "Document type", true) with { LookupType = DocRegPermissions.TypeLookup },
                    F("sensitivity", FieldTypes.Picklist, "مستوى السرية", "Sensitivity", true) with { LookupType = DocRegPermissions.SensitivityLookup }),
            },
            new StageDefinition
            {
                Code = PublishStage, Kind = StageKinds.Processing, NameAr = "النشر", NameEn = "Publication", System = true, SlaHours = 24,
                ProcessorRoleKeys = [DocRegPermissions.OfficeRoleKey],
                Actions =
                [
                    new StageAction
                    {
                        Code = PublishAction, Kind = ActionKinds.Custom, LabelAr = "نشر الوثيقة", LabelEn = "Publish",
                        Form = FormSchema.Of(
                            F("finalFile", FieldTypes.File, "النسخة النهائية الموقعة (PDF)", "Signed final file (PDF)", true) with { Accept = ["pdf"] },
                            F("shareToAll", FieldTypes.Checkbox, "مشاركة مع جميع الموظفين؟", "Share to all employees?") with
                            {
                                HelpAr = "إذا لم تحدد، تتبع الرؤية مستوى السرية.", HelpEn = "Otherwise visibility follows the sensitivity.",
                            },
                            F("effectiveDate", FieldTypes.Date, "تاريخ السريان", "Effective date"),
                            F("expiryDate", FieldTypes.Date, "تاريخ الانتهاء", "Expiry date") with
                            {
                                HelpAr = "يقترح من قاعدة نوع الوثيقة إذا ترك فارغاً.", HelpEn = "Proposed from the document type's rule when empty.",
                            },
                            F("ackRequired", FieldTypes.Checkbox, "يتطلب إقرار الاطلاع", "Read acknowledgement required")),
                    },
                    new StageAction { Code = ReturnAction, Kind = ActionKinds.Return, LabelAr = "إعادة لمقدم الطلب", LabelEn = "Return to requester", Tone = "secondary" },
                ],
            },
        ],
        Category: "documents", Sort: 10);

    public static readonly RequestTypeRegistration WithdrawalType = new(
        Withdrawal, DocRegPermissions.SystemCode, "سحب / إلغاء وثيقة", "Document withdrawal",
        "طلب سحب وثيقة منشورة من المكتبة النشطة مع ذكر السبب.", "Ask to withdraw a published document from the active library, with the reason.",
        "DOCWD", "archive",
        FormSchema.Of(
            F("document", FieldTypes.Record, "الوثيقة", "Document", true) with { TargetType = DocumentResource.Type },
            F("reason", FieldTypes.Textarea, "سبب السحب", "Reason for withdrawal", true) with { MaxLength = 2000 }),
        [
            new StageDefinition
            {
                Code = ReviewStage, Kind = StageKinds.Approval, NameAr = "اعتماد السحب", NameEn = "Withdrawal approval", ProcessKey = WithdrawalProcess, System = true,
            },
        ],
        Category: "documents", Sort: 11);

    private static ApprovalProcessDefinition Process(string key, string nameAr, string nameEn) => new(
        key, DocRegPermissions.SystemCode, nameAr, nameEn, RequestResource.Type, RequestResource.ReadPermission, RequestResource.LinkTemplate, [],
        [
            new ApprovalStepDefinition("office", "مكتب الاستراتيجية / مكتب إدارة المشاريع", "Strategy Office / PM Office",
                new ApproverRule(ApproverRuleKinds.Role, RoleKey: DocRegPermissions.OfficeRoleKey), SlaHours: 24),
        ],
        RequestResource.ApproverRole,
        "خطوة واحدة افتراضياً (مكتب الاستراتيجية / مكتب إدارة المشاريع)، قابلة للتعديل.", "One step by default (Strategy Office / PM Office), editable.");

    public static readonly ApprovalProcessDefinition ReviewDefinition = Process(ReviewProcess, "مراجعة واعتماد وثيقة", "Document review and approval");

    public static readonly ApprovalProcessDefinition WithdrawalDefinition = Process(WithdrawalProcess, "اعتماد سحب وثيقة", "Document withdrawal approval");
}

/// <summary>
/// The register side of <c>docreg.document</c> (inside the framework's transaction): document and version on submit, classification, Document ID
/// and version on approval, publication with supersede, validity and the <see cref="DocumentPublished"/> event (ADR 0082–0084, 0087).
/// </summary>
internal sealed class DocumentSubmissionHandler(
    DocRegDbContext db,
    DocumentVisibility visibility,
    ISettingsStore settings,
    INumberingService numbering,
    IOrgDirectory org,
    IFileService files,
    IIntegrationEventPublisher publisher,
    DocRegNotifier notifier,
    DocRegCalendar calendar,
    IClock clock) : IRequestTypeHandler
{
    public string TypeCode => DocumentRequestTypes.Submission;

    public async Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, bool submitting, CancellationToken cancellationToken)
    {
        var data = context.Request.Data;
        if (!data.Flag("isNewVersion") || data.Id("existingDocument") is not { } existingId)
        {
            return [];
        }

        var errors = new List<FieldError>();
        var document = await visibility.FindAsync(existingId, context.User, cancellationToken).ConfigureAwait(false);
        if (document is null || !DocumentStatuses.IsActive(document.Status) || !await visibility.IsOwnerSideAsync(document, context.User, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new FieldError("fields.existingDocument", "docreg.errors.notRevisable", "docreg.errors.notRevisable"));
        }
        else if (submitting && await db.Versions.AnyAsync(v => v.DocumentId == existingId && v.RequestId != context.Request.Id
                     && (v.Status == VersionStatuses.Submitted || v.Status == VersionStatuses.UnderReview || v.Status == VersionStatuses.Returned
                         || v.Status == VersionStatuses.Approved), cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new FieldError("fields.existingDocument", "docreg.errors.versionInProgress", "docreg.errors.versionInProgress"));
        }

        return errors;
    }

    public Task<IReadOnlyCollection<string>?> AvailableActionsAsync(RequestHandlerContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>?>(null);

    public async Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var request = transition.Request;
        var version = await db.Versions.FirstOrDefaultAsync(v => v.RequestId == request.Id, cancellationToken).ConfigureAwait(false);
        var document = version is null ? null : await db.Documents.FirstAsync(d => d.Id == version.DocumentId, cancellationToken).ConfigureAwait(false);
        switch (transition.Kind)
        {
            case TransitionKinds.Submitted:
                return await SubmittedAsync(request, version, document, cancellationToken).ConfigureAwait(false);

            case TransitionKinds.Approved when version is not null && document is not null:
                return await ApprovedAsync(transition, version, document, cancellationToken).ConfigureAwait(false);

            case TransitionKinds.Action when transition.ActionCode == DocumentRequestTypes.PublishAction && version is not null && document is not null:
                return await PublishAsync(transition, version, document, cancellationToken).ConfigureAwait(false);

            case TransitionKinds.Returned or TransitionKinds.Action when version is not null && document is not null:
                version.SetStatus(VersionStatuses.Returned);
                SetNewDocumentStatus(version, document, DocumentStatuses.Returned);
                break;

            case TransitionKinds.Rejected when version is not null && document is not null:
                version.SetStatus(VersionStatuses.Rejected);
                SetNewDocumentStatus(version, document, DocumentStatuses.Rejected);
                break;

            case TransitionKinds.Cancelled when version is not null && document is not null:
                version.SetStatus(VersionStatuses.Cancelled);
                SetNewDocumentStatus(version, document, DocumentStatuses.Withdrawn);
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RequestTransitionOutcome.None;
    }

    private async Task<Result<RequestTransitionOutcome>> SubmittedAsync(RequestSnapshot request, DocumentVersion? version, Document? document, CancellationToken ct)
    {
        var data = request.Data;
        var isNew = !data.Flag("isNewVersion");
        var ownerUnit = data.Id("ownerUnit") ?? request.UnitId;
        if (version is null)
        {
            if (isNew)
            {
                document = Document.Create(data.Text("nameAr"), data.Text("nameEn"), data.Text("description"), ownerUnit, request.RequesterEmployeeId);
                db.Documents.Add(document);
            }
            else
            {
                document = await db.Documents.FirstAsync(d => d.Id == data.Id("existingDocument")!.Value, ct).ConfigureAwait(false);
            }

            version = DocumentVersion.Create(document.Id, request.Id, request.Number, data.Text("changeReason"), request.RequesterEmployeeId, isNew,
                FirstId(data, "draftFile"));
            db.Versions.Add(version);
        }
        else
        {
            version.SetChange(data.Text("changeReason"), FirstId(data, "draftFile"));
            if (isNew)
            {
                document!.Describe(data.Text("nameAr"), data.Text("nameEn"), data.Text("description"), ownerUnit);
            }
        }

        version.SetStatus(VersionStatuses.UnderReview);
        SetNewDocumentStatus(version, document!, DocumentStatuses.UnderReview);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RequestTransitionOutcome(DocumentVersion.EntityType, version.Id);
    }

    private async Task<Result<RequestTransitionOutcome>> ApprovedAsync(RequestTransition transition, DocumentVersion version, Document document, CancellationToken ct)
    {
        var values = transition.ActionValues;
        var level = values.Text("level")!;
        var docType = values.Text("docType")!;
        var sensitivity = values.Text("sensitivity")!;
        document.Classify(level, docType, sensitivity);
        if (document.DocumentNo is null)
        {
            var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
            var unitCode = document.OwnerUnitId is { } unit ? (await org.GetUnitAsync(unit, ct).ConfigureAwait(false))?.Code : null;
            var number = await numbering.NextAsync(db, DocRegPermissions.DocumentNumbering,
                new NumberingContext(clock.Today, unitCode ?? "GEN", configuration.RuleFor(docType).NumberCode, configuration.OrganisationCode), ct).ConfigureAwait(false);
            document.AssignNumber(number);
        }

        var existing = await db.Versions.Where(v => v.DocumentId == document.Id && v.Id != version.Id && v.Major > 0).Select(v => new { v.Major, v.Minor })
            .ToListAsync(ct).ConfigureAwait(false);
        var (major, minor) = VersionNumbers.Next(existing.Select(v => (v.Major, v.Minor)), correction: false);
        version.Approve(major, minor, transition.ActorUserId, clock.UtcNow);
        SetNewDocumentStatus(version, document, DocumentStatuses.Approved);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RequestTransitionOutcome(Outcome: "approved");
    }

    private async Task<Result<RequestTransitionOutcome>> PublishAsync(RequestTransition transition, DocumentVersion version, Document document, CancellationToken ct)
    {
        var values = transition.ActionValues;
        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        var shareToAll = values.Flag("shareToAll");
        if (shareToAll && configuration.Sensitivities.FirstOrDefault(s => s.Code == document.Sensitivity) is { AllowShareToAll: false })
        {
            return Error.Validation([new FieldError("form.shareToAll", "docreg.errors.shareNotAllowed", "docreg.errors.shareNotAllowed")]);
        }

        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        var effective = values.Date("effectiveDate") ?? today;
        var rule = configuration.RuleFor(document.DocType);
        var expiry = values.Date("expiryDate") ?? ValidityCalculator.ExpiryFrom(effective, rule.ValidityMonths);
        if (expiry is { } e && e <= effective)
        {
            return Error.Validation([new FieldError("form.expiryDate", "docreg.errors.expiryBeforeEffective", "docreg.errors.expiryBeforeEffective")]);
        }

        // The signed file stays where it was uploaded (the request's files); the version points at it and the Document Registry checks visibility
        // itself on download. Copying it here would write on another connection while this transaction holds the audit chain (ADR 0018).
        if (FirstId(values, "finalFile") is not { } fileId)
        {
            return Error.Validation([new FieldError("form.finalFile", "validation.notEmpty", "validation.notEmpty")]);
        }

        var source = await files.OpenAsync(fileId, auditDownload: false, cancellationToken: ct).ConfigureAwait(false);
        if (source.IsFailure)
        {
            return source.Error;
        }

        byte[] bytes;
        string name;
        await using (source.Value.Stream)
        {
            using var buffer = new MemoryStream();
            await source.Value.Stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            bytes = buffer.ToArray();
            name = source.Value.FileName;
        }

        if (source.Value.ContentType != "application/pdf")
        {
            return Error.Validation([new FieldError("form.finalFile", "docreg.errors.finalPdf", "docreg.errors.finalPdf")]);
        }

        var text = await ExtractAsync(bytes, name, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var previous = await db.Versions.Where(v => v.DocumentId == document.Id && v.Status == VersionStatuses.Current).ToListAsync(ct).ConfigureAwait(false);
        foreach (var old in previous)
        {
            old.SetStatus(VersionStatuses.Superseded);
        }

        if (!version.IsNewDocument)
        {
            var data = transition.Request.Data;
            document.Describe(data.Text("nameAr"), data.Text("nameEn"), data.Text("description"), data.Id("ownerUnit"));
        }

        version.Publish(fileId, name, transition.ActorUserId, now, text);
        document.Publish(version.Id, version.Label!, effective, expiry, shareToAll, values.Flag("ackRequired") || rule.AckRequired, now, text);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await publisher.PublishAsync(new DocumentPublished(document.Id, version.Id, document.DocumentNo!, version.Label!, document.NameAr, document.NameEn, document.Description,
            document.DocType, document.OwnerUnitId, document.OwnerEmployeeId, version.RequestedByEmployeeId ?? document.RequesterEmployeeId, rule.CreatesIp, now), db, ct)
            .ConfigureAwait(false);
        await notifier.PublishedAsync(document, version, db, ct).ConfigureAwait(false);
        return new RequestTransitionOutcome(Outcome: "published");
    }

    private static Guid? FirstId(FormValues values, string code) => values.Ids(code) is [var first, ..] ? first : null;

    private static async Task<string?> ExtractAsync(byte[] bytes, string name, CancellationToken ct)
    {
        var text = await DocumentTextExtractor.ExtractAsync(bytes, name, null, ct).ConfigureAwait(false);
        return text.IsSuccess ? text.Value.Text : null;
    }

    /// <summary>A new document follows its first version's review; a revised document stays published until the new version is published.</summary>
    private static void SetNewDocumentStatus(DocumentVersion version, Document document, string status)
    {
        if (version.IsNewDocument && !DocumentStatuses.IsActive(document.Status))
        {
            document.SetStatus(status);
        }
    }

    internal static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The register side of <c>docreg.withdrawal</c>: on approval the document leaves the active library (archive).</summary>
internal sealed class DocumentWithdrawalHandler(DocRegDbContext db, DocumentVisibility visibility, DocRegNotifier notifier, IClock clock) : IRequestTypeHandler
{
    public string TypeCode => DocumentRequestTypes.Withdrawal;

    public async Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, bool submitting, CancellationToken cancellationToken)
    {
        if (context.Request.Data.Id("document") is not { } id)
        {
            return [];
        }

        var document = await visibility.FindAsync(id, context.User, cancellationToken).ConfigureAwait(false);
        return document is null || !DocumentStatuses.IsActive(document.Status) || !await visibility.IsOwnerSideAsync(document, context.User, cancellationToken).ConfigureAwait(false)
            ? [new FieldError("fields.document", "docreg.errors.notWithdrawable", "docreg.errors.notWithdrawable")]
            : [];
    }

    public Task<IReadOnlyCollection<string>?> AvailableActionsAsync(RequestHandlerContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>?>(null);

    public async Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var id = transition.Request.Data.Id("document");
        if (id is null)
        {
            return RequestTransitionOutcome.None;
        }

        if (transition.Kind == TransitionKinds.Submitted)
        {
            return new RequestTransitionOutcome(Document.EntityType, id);
        }

        if (transition.Kind != TransitionKinds.Approved)
        {
            return RequestTransitionOutcome.None;
        }

        var document = await db.Documents.FirstAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
        var reason = transition.Request.Data.Text("reason") ?? string.Empty;
        document.Withdraw(reason);
        foreach (var version in await db.Versions.Where(v => v.DocumentId == document.Id && v.Status == VersionStatuses.Current).ToListAsync(cancellationToken)
                     .ConfigureAwait(false))
        {
            version.SetStatus(VersionStatuses.Withdrawn);
        }

        db.ValidityEvents.Add(ValidityEvent.Create(document.Id, "withdrawn", null, document.ExpiryDate, null, transition.ActorUserId, reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await notifier.WithdrawnAsync(document, reason, db, cancellationToken).ConfigureAwait(false);
        return new RequestTransitionOutcome(Outcome: "withdrawn");
    }
}
