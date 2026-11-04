using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>Permission, role, lookup, numbering and file codes (docs/design/requests.md §3).</summary>
public static class DocRegPermissions
{
    public const string SystemCode = "docreg";

    public const string Read = "docreg.document.read";
    public const string Manage = "docreg.document.manage";
    public const string Process = "docreg.request.process";
    public const string DashboardRead = "docreg.dashboard.read";
    public const string SettingsManage = "docreg.settings.manage";
    public const string AiClassify = "docreg.ai.classify";
    public const string AiSummary = "docreg.ai.summary";
    public const string AiDuplicates = "docreg.ai.duplicates";
    public const string AiWhatChanged = "docreg.ai.what-changed";
    public const string AiQa = "docreg.ai.qa";

    /// <summary>The Strategy Office / PM Office role (default reviewer and publisher).</summary>
    public const string OfficeRoleKey = "docreg.office";

    /// <summary>Leadership (seeded clearance for Confidential and Secret).</summary>
    public const string LeadershipRoleKey = "docreg.leadership";

    public const string LevelLookup = "docreg.level";
    public const string TypeLookup = "docreg.type";
    public const string SensitivityLookup = "docreg.sensitivity";

    public const string DocumentNumbering = "docreg.document";
}

/// <summary>Validity and behaviour of a document type (ADR 0084, 0087).</summary>
/// <param name="Code">Lookup code of <c>docreg.type</c>.</param>
/// <param name="NumberCode">Value of <c>{TYPE}</c> in the Document ID.</param>
/// <param name="ValidityMonths">Null: valid until superseded.</param>
/// <param name="CreatesIp">"Creates IP register entry" (SPEC §0.0).</param>
/// <param name="AckRequired">Read acknowledgement required by default on publish.</param>
public sealed record DocTypeRule(string Code, string NumberCode, int? ValidityMonths, bool CreatesIp = false, bool AckRequired = false);

/// <summary>Who is cleared for a sensitivity level (ADR 0083).</summary>
public sealed record SensitivityRule(string Code, bool AllEmployees, bool OwnerUnit, IReadOnlyList<string> RoleKeys, bool AllowShareToAll = true);

/// <summary>Settings → Document Registry (docs/design/requests.md §3.1).</summary>
public sealed record DocRegSettings : ISettingsArea
{
    public static string Key => "docreg";

    public string OrganisationCode { get; init; } = "ORG";

    public IReadOnlyList<DocTypeRule> Types { get; init; } =
    [
        new("policy", "POL", 24), new("procedure", "PRC", 24), new("manual", "MAN", 36), new("legal", "LEG", null), new("strategy", "STR", 36),
        new("form", "FRM", null),
    ];

    public IReadOnlyList<SensitivityRule> Sensitivities { get; init; } =
    [
        new("public", true, false, []),
        new("internal", true, false, []),
        new("confidential", false, true, [DocRegPermissions.OfficeRoleKey, DocRegPermissions.LeadershipRoleKey]),
        new("secret", false, false, [DocRegPermissions.OfficeRoleKey, DocRegPermissions.LeadershipRoleKey], AllowShareToAll: false),
    ];

    public IReadOnlyList<int> ReminderDays { get; init; } = [60, 30];

    public bool Watermark { get; init; } = true;

    public string WatermarkAr { get; init; } = "نسخة غير خاضعة للرقابة عند الطباعة";

    public string WatermarkEn { get; init; } = "UNCONTROLLED WHEN PRINTED";

    public bool PublicLinks { get; init; }

    public DocTypeRule RuleFor(string? docType) =>
        Types.FirstOrDefault(t => t.Code == docType) ?? new DocTypeRule(docType ?? "doc", (docType ?? "DOC").ToUpperInvariant(), null);

    public static List<FieldError> Validate(DocRegSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (string.IsNullOrWhiteSpace(value.OrganisationCode) || value.OrganisationCode.Length > 10 || !value.OrganisationCode.All(char.IsAsciiLetterOrDigit))
        {
            Add("organisationCode", "docreg.errors.code");
        }

        for (var i = 0; i < value.Types.Count; i++)
        {
            var t = value.Types[i];
            if (string.IsNullOrWhiteSpace(t.NumberCode) || t.NumberCode.Length > 10 || !t.NumberCode.All(char.IsAsciiLetterOrDigit))
            {
                Add($"types[{i}].numberCode", "docreg.errors.code");
            }

            if (t.ValidityMonths is < 1 or > 240)
            {
                Add($"types[{i}].validityMonths", "docreg.errors.months");
            }
        }

        if (value.Types.Select(t => t.Code).Distinct().Count() != value.Types.Count || value.Sensitivities.Select(s => s.Code).Distinct().Count() != value.Sensitivities.Count)
        {
            Add("types", "docreg.errors.duplicate");
        }

        if (value.ReminderDays.Count > 5 || value.ReminderDays.Any(d => d is < 1 or > 365))
        {
            Add("reminderDays", "docreg.errors.reminderDays");
        }

        if (value.WatermarkAr.Length > 120 || value.WatermarkEn.Length > 120)
        {
            Add("watermarkEn", "validation.maximumLength");
        }

        return errors;
    }
}

internal sealed class DocRegAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(DocRegPermissions.SystemCode, SystemKind.Module, "سجل الوثائق المؤسسية", "Document Registry", 50);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(DocRegPermissions.Read, "عرض مكتبة الوثائق (حسب مستوى السرية)", "View the document library (by sensitivity clearance)", Scopable: false),
        new(DocRegPermissions.Manage, "إدارة جميع الوثائق (الصلاحية والسحب)", "Manage all documents (validity, withdrawal)", Scopable: false),
        new(DocRegPermissions.Process, "مراجعة ونشر الوثائق", "Review and publish documents", Scopable: false),
        new(DocRegPermissions.DashboardRead, "عرض لوحة الوثائق", "View the document dashboard", Scopable: false),
        new(DocRegPermissions.SettingsManage, "إدارة إعدادات سجل الوثائق", "Manage Document Registry settings", Scopable: false),
        new(DocRegPermissions.AiClassify, "التصنيف الآلي للوثائق", "AI document classification", Scopable: false),
        new(DocRegPermissions.AiSummary, "ملخص الوثيقة بالذكاء الاصطناعي", "AI document summary", Scopable: false),
        new(DocRegPermissions.AiDuplicates, "فحص التكرار والتعارض", "Duplicate / conflict check", Scopable: false),
        new(DocRegPermissions.AiWhatChanged, "ملخص ما تغير بين الإصدارات", "What changed between versions", Scopable: false),
        new(DocRegPermissions.AiQa, "أسئلة وأجوبة على الوثائق المنشورة", "Q&A over published documents", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("reader", "قارئ الوثائق", "Document reader",
            [new(DocRegPermissions.Read), new(DocRegPermissions.AiSummary), new(DocRegPermissions.AiWhatChanged), new(DocRegPermissions.AiQa)],
            AssignToEveryone: true),
        new("office", "مكتب الاستراتيجية / مكتب إدارة المشاريع (الوثائق)", "Strategy Office / PM Office (documents)",
            [new(DocRegPermissions.Read), new(DocRegPermissions.Manage), new(DocRegPermissions.Process), new(DocRegPermissions.DashboardRead),
                new(DocRegPermissions.AiClassify), new(DocRegPermissions.AiSummary), new(DocRegPermissions.AiDuplicates), new(DocRegPermissions.AiWhatChanged),
                new(DocRegPermissions.AiQa)],
            RequiresMfa: true),
        new("leadership", "القيادة (الوثائق)", "Leadership (documents)", [new(DocRegPermissions.Read), new(DocRegPermissions.DashboardRead)]),
        new("administrator", "مسؤول سجل الوثائق", "Document Registry administrator",
            [new(DocRegPermissions.Read), new(DocRegPermissions.SettingsManage), new(DocRegPermissions.DashboardRead)], RequiresMfa: true),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles => [];

    public static readonly NumberingDefinition Numbering = new(DocRegPermissions.DocumentNumbering, DocRegPermissions.SystemCode, "رمز الوثيقة", "Document ID",
        "{ORG}-{UNIT}-{TYPE}-{YYYY}-{SEQ:2}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(DocRegPermissions.LevelLookup, DocRegPermissions.SystemCode, "المستوى الهرمي للوثيقة", "Hierarchical level",
        [
            new("strategic", "استراتيجي", "Strategic"), new("legislative", "تشريعي", "Legislative"), new("operational", "تشغيلي", "Operational"),
        ]),
        new(DocRegPermissions.TypeLookup, DocRegPermissions.SystemCode, "نوع الوثيقة", "Document type",
        [
            new("policy", "سياسة مؤسسية", "Corporate policy"), new("procedure", "إجراء", "Procedure"), new("manual", "دليل", "Manual / guide"),
            new("legal", "وثيقة قانونية", "Legal document"), new("strategy", "استراتيجية", "Strategy"), new("form", "نموذج", "Form"),
        ]),
        new(DocRegPermissions.SensitivityLookup, DocRegPermissions.SystemCode, "مستوى السرية", "Sensitivity",
        [
            new("public", "عام", "Public"), new("internal", "داخلي", "Internal"), new("confidential", "سري", "Confidential"), new("secret", "سري للغاية", "Secret"),
        ]),
    ];
}
