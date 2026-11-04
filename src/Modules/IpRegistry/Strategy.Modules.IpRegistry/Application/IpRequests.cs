using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.IpRegistry.Application;

/// <summary>Loads IP records within the caller's visibility: read scope (own = requester, unit = owner unit), contributors, or office (All).</summary>
internal sealed class IpReader(IpDbContext db, IAccessService access, ISystemState systems)
{
    public async Task<IQueryable<IpRecord>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Records : db.Records.AsNoTracking();
        if (!await systems.IsEnabledAsync(IpPermissions.SystemCode, ct).ConfigureAwait(false))
        {
            return source.Where(_ => false);
        }

        var scoped = await access.ApplyScopeAsync(tracked ? db.Records : db.Records.AsNoTracking(), user, IpPermissions.Read, IpScope.Map, ct).ConfigureAwait(false);
        var ids = scoped.Select(r => r.Id);
        var me = user.EmployeeIdOf();
        var contributed = db.Contributors.Where(c => me != null && c.EmployeeId == me).Select(c => c.IpRecordId);
        return source.Where(r => ids.Contains(r.Id) || contributed.Contains(r.Id) || (me != null && r.RequesterEmployeeId == me));
    }

    public async Task<IpRecord?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, cancellationToken: ct);
}

/// <summary>Encrypts Emirates IDs of contributors (ADR 0086, purpose <c>ip.contributor.emirates-id.v1</c>).</summary>
internal sealed class EmiratesIds(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("ip.contributor.emirates-id.v1");

    public (string? Encrypted, string? Last) Protect(string? plain) =>
        string.IsNullOrWhiteSpace(plain) ? (null, null) : (_protector.Protect(plain.Trim()), plain.Trim()[^1..]);

    public string? Unprotect(string? encrypted)
    {
        if (encrypted is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>The built-in request type <c>ip.registration</c> (docs/design/requests.md §4.3) and its approval process.</summary>
public static class IpRequestTypes
{
    public const string Registration = "ip.registration";
    public const string ReviewProcess = "ip.review";
    public const string ReviewStage = "review";

    private static FormField F(string code, string type, string ar, string en, bool required = false) =>
        new() { Code = code, Type = type, LabelAr = ar, LabelEn = en, Required = required, System = true };

    public static readonly RequestTypeRegistration RegistrationType = new(
        Registration, IpPermissions.SystemCode, "تسجيل ملكية فكرية", "Intellectual property registration",
        "تسجيل عمل أنتجه موظفون لصالح الجهة (منهجية، نظام، تصميم، مصنف…) مع المساهمين ونسب مساهمتهم.",
        "Register a work created for the organisation (methodology, system, design, literary work…) with its contributors and their shares.",
        "IPREQ", "lightbulb",
        new FormSchema
        {
            Sections =
            [
                new FormSection
                {
                    Code = "work", TitleAr = "العمل", TitleEn = "The work", System = true,
                    Fields =
                    [
                        F("nameAr", FieldTypes.Text, "اسم العمل (عربي)", "Name of the work (Arabic)", true) with { MaxLength = IpRecord.NameMaxLength, TitleOf = "ar" },
                        F("nameEn", FieldTypes.Text, "اسم العمل (إنجليزي)", "Name of the work (English)", true) with { MaxLength = IpRecord.NameMaxLength, TitleOf = "en" },
                        F("description", FieldTypes.Textarea, "وصف العمل", "Description of the work", true) with { MaxLength = IpRecord.TextMaxLength },
                        F("files", FieldTypes.File, "ملفات داعمة", "Supporting files") with { Multiple = true },
                    ],
                },
                new FormSection
                {
                    Code = "contributors", TitleAr = "الأطراف المتنازلة / المساهمون", TitleEn = "Transferring parties / contributors", System = true,
                    Fields =
                    [
                        F("contributors", FieldTypes.Table, "المساهمون", "Contributors", true) with
                        {
                            MinRows = 1,
                            MaxRows = 20,
                            Columns =
                            [
                                new FormField { Code = "employee", Type = FieldTypes.Employee, LabelAr = "الموظف", LabelEn = "Employee" },
                                new FormField { Code = "externalName", Type = FieldTypes.Text, LabelAr = "الاسم (من خارج الجهة)", LabelEn = "Name (external)", MaxLength = 300 },
                                new FormField { Code = "externalOrganisation", Type = FieldTypes.Text, LabelAr = "الجهة (من خارج الجهة)", LabelEn = "Organisation (external)", MaxLength = 300 },
                                new FormField
                                {
                                    Code = "emiratesId", Type = FieldTypes.Secret, Format = SecretFormats.EmiratesId, LabelAr = "رقم الهوية الإماراتية", LabelEn = "Emirates ID",
                                    HelpAr = "784-YYYY-NNNNNNN-N — يحفظ مشفراً ويعرض مخفياً", HelpEn = "784-YYYY-NNNNNNN-N — stored encrypted, shown masked",
                                },
                                new FormField { Code = "role", Type = FieldTypes.Picklist, LookupType = IpPermissions.ContributorRoleLookup, LabelAr = "الدور", LabelEn = "Role", Required = true },
                                new FormField { Code = "share", Type = FieldTypes.Number, LabelAr = "نسبة المساهمة %", LabelEn = "Contribution %", Required = true, Min = 0.01m, Max = 100, Decimals = 2 },
                            ],
                        },
                        F("declaration", FieldTypes.Checkbox, "أقر بأن حقوق هذا العمل متنازل عنها للجهة", "I declare that the rights to this work are assigned to the organisation", true),
                    ],
                },
            ],
        },
        [
            new StageDefinition
            {
                Code = ReviewStage, Kind = StageKinds.Approval, NameAr = "مراجعة مكتب الاستراتيجية", NameEn = "Strategy Office review", ProcessKey = ReviewProcess, System = true,
                ApproveForm = FormSchema.Of(
                    F("propertyType", FieldTypes.Picklist, "نوع الملكية", "Property type", true) with { LookupType = IpPermissions.PropertyTypeLookup },
                    F("protectionType", FieldTypes.Picklist, "نوع الحماية", "Protection type", true) with { LookupType = IpPermissions.ProtectionTypeLookup },
                    F("notes", FieldTypes.Textarea, "ملاحظات", "Notes") with { MaxLength = 4000 }),
            },
        ],
        Category: "intellectual-property", Sort: 20);

    public static readonly ApprovalProcessDefinition ReviewDefinition = new(
        ReviewProcess, IpPermissions.SystemCode, "مراجعة طلب ملكية فكرية", "Intellectual property review", RequestResource.Type, RequestResource.ReadPermission,
        RequestResource.LinkTemplate, [],
        [
            new ApprovalStepDefinition("office", "مكتب الاستراتيجية / مكتب إدارة المشاريع", "Strategy Office / PM Office",
                new ApproverRule(ApproverRuleKinds.Role, RoleKey: IpPermissions.OfficeRoleKey), SlaHours: 24),
        ],
        RequestResource.ApproverRole,
        "خطوة واحدة افتراضياً (مكتب الاستراتيجية)، قابلة للتعديل.", "One step by default (Strategy Office), editable.");
}

/// <summary>
/// The register side of <c>ip.registration</c> (inside the framework's transaction): IP record on submission; property / protection type and the
/// contributors (Emirates IDs re-encrypted for the IP register) on approval; filing and registration continue on the IP record (ADR 0087).
/// </summary>
internal sealed class IpRegistrationHandler(IpDbContext db, INumberingService numbering, ISettingsStore settings, EmiratesIds ids, IClock clock) : IRequestTypeHandler
{
    public string TypeCode => IpRequestTypes.Registration;

    public Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, bool submitting, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var rows = context.Request.Data.Rows("contributors");
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id("employee") is null && rows[i].Text("externalName") is null)
            {
                errors.Add(new FieldError($"fields.contributors[{i}].employee", "ip.errors.contributorWho", "ip.errors.contributorWho"));
            }
        }

        if (submitting && rows.Count > 0 && !IpRules.SharesTotal100(rows.Select(r => r.Number("share") ?? 0)))
        {
            errors.Add(new FieldError("fields.contributors", "ip.errors.sharesTotal", "ip.errors.sharesTotal"));
        }

        return Task.FromResult<IReadOnlyList<FieldError>>(errors);
    }

    public Task<IReadOnlyCollection<string>?> AvailableActionsAsync(RequestHandlerContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>?>(null);

    public async Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var request = transition.Request;
        var record = await db.Records.FirstOrDefaultAsync(r => r.RequestId == request.Id, cancellationToken).ConfigureAwait(false);
        var data = request.Data;
        switch (transition.Kind)
        {
            case TransitionKinds.Submitted:
                if (record is null)
                {
                    var number = await numbering.NextAsync(db, IpPermissions.RecordNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
                    record = IpRecord.FromRequest(number, request.Id, data.Text("nameAr"), data.Text("nameEn"), data.Text("description"), request.UnitId,
                        request.RequesterEmployeeId);
                    db.Records.Add(record);
                }
                else
                {
                    record.Describe(data.Text("nameAr"), data.Text("nameEn"), data.Text("description"));
                }

                record.SetStatus(IpStatuses.UnderReview);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new RequestTransitionOutcome(IpRecord.EntityType, record.Id);

            case TransitionKinds.Approved when record is not null:
                {
                    var values = transition.ActionValues;
                    var rule = (await settings.GetAsync<IpSettings>(cancellationToken).ConfigureAwait(false)).RuleFor(values.Text("protectionType"));
                    record.Classify(values.Text("propertyType")!, values.Text("protectionType")!, values.Text("notes"), rule.InternalOnly);
                    var sort = 0;
                    foreach (var row in data.Rows("contributors"))
                    {
                        var contributor = Contributor.Create(record.Id, row.Id("employee"), row.Text("externalName"), row.Text("externalOrganisation"), row.Text("role"),
                            (row.Number("share") ?? 0) / 100m, sort++);
                        var (encrypted, last) = ids.Protect(row.Text("emiratesId"));
                        contributor.SetEmiratesId(encrypted, last);
                        db.Contributors.Add(contributor);
                    }

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return new RequestTransitionOutcome(Outcome: "approved");
                }

            case TransitionKinds.Returned when record is not null:
                record.SetStatus(IpStatuses.Returned);
                break;
            case TransitionKinds.Rejected when record is not null:
                record.SetStatus(IpStatuses.Rejected);
                break;
            case TransitionKinds.Cancelled when record is not null:
                record.SetStatus(IpStatuses.Withdrawn);
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RequestTransitionOutcome.None;
    }
}
