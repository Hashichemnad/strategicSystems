using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.IpRegistry.Application;

/// <summary>Permission, role, lookup and numbering codes (docs/design/requests.md §4.8).</summary>
public static class IpPermissions
{
    public const string SystemCode = "ip";

    public const string Read = "ip.record.read";
    public const string Manage = "ip.record.manage";
    public const string Process = "ip.request.process";
    public const string Sensitive = "ip.contributor.sensitive";
    public const string DashboardRead = "ip.dashboard.read";
    public const string SettingsManage = "ip.settings.manage";
    public const string AiSuggest = "ip.ai.suggest";
    public const string AiSimilarity = "ip.ai.similarity";
    public const string AiMoet = "ip.ai.moet";

    public const string OfficeRoleKey = "ip.office";

    public const string PropertyTypeLookup = "ip.property-type";
    public const string ProtectionTypeLookup = "ip.protection-type";
    public const string ContributorRoleLookup = "ip.contributor-role";

    public const string RecordNumbering = "ip.record";
    public const string InternalNumbering = "ip.internal";

    /// <summary>Property type of records created from documents (ADR 0087).</summary>
    public const string DocumentPropertyType = "document";
}

public static class IpScope
{
    /// <summary>Own = requester; unit = owner unit. Contributors read their records through the reader (not a data scope).</summary>
    public static readonly ScopeMap<IpRecord> Map = new(IpRecord.EntityType, x => x.Id, ownerEmployee: x => x.RequesterEmployeeId, unit: x => x.OwnerUnitId);
}

/// <summary>From which date a protection term counts (ADR 0087).</summary>
public static class TermBases
{
    /// <summary>The registration date (patents, designs, trademarks, utility models).</summary>
    public const string Registration = "registration";

    /// <summary>The first publication of the work (copyright of works owned by the organisation).</summary>
    public const string Publication = "publication";

    public static readonly IReadOnlyList<string> All = [Registration, Publication];
}

/// <summary>Term and handling per protection type (ADR 0087): years (null: no expiry), the date the term counts from, internal-only.</summary>
public sealed record ProtectionRule(string Code, int? TermYears, bool InternalOnly, string TermFrom = TermBases.Registration);

/// <summary>Settings → IP (docs/design/requests.md §4.1).</summary>
public sealed record IpSettings : ISettingsArea
{
    public static string Key => "ip";

    public IReadOnlyList<ProtectionRule> Protections { get; init; } =
    [
        new("copyright", 50, false, TermBases.Publication), new("industrial-design", 20, false), new("patent", 20, false), new("utility-model", 10, false),
        new("trademark", 10, false), new("trade-secret", null, true),
    ];

    public IReadOnlyList<int> ReminderMonths { get; init; } = [6, 3];

    public string? MoetHeaderAr { get; init; } = "نموذج طلب تسجيل مصنف — وزارة الاقتصاد";

    public string? MoetHeaderEn { get; init; } = "Application to register a work — Ministry of Economy";

    public ProtectionRule RuleFor(string? code) => Protections.FirstOrDefault(p => p.Code == code) ?? new ProtectionRule(code ?? string.Empty, 20, false);

    public static List<FieldError> Validate(IpSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        for (var i = 0; i < value.Protections.Count; i++)
        {
            if (value.Protections[i].TermYears is < 1 or > 100)
            {
                errors.Add(new FieldError($"protections[{i}].termYears", "ip.errors.term", "ip.errors.term"));
            }

            if (!TermBases.All.Contains(value.Protections[i].TermFrom))
            {
                errors.Add(new FieldError($"protections[{i}].termFrom", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        if (value.Protections.Select(p => p.Code).Distinct().Count() != value.Protections.Count)
        {
            errors.Add(new FieldError("protections", "ip.errors.duplicate", "ip.errors.duplicate"));
        }

        if (value.ReminderMonths.Count > 5 || value.ReminderMonths.Any(m => m is < 1 or > 60))
        {
            errors.Add(new FieldError("reminderMonths", "ip.errors.reminderMonths", "ip.errors.reminderMonths"));
        }

        return errors;
    }
}

internal sealed class IpAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(IpPermissions.SystemCode, SystemKind.Module, "سجل الملكية الفكرية", "Intellectual Property Registry", 51);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(IpPermissions.Read, "عرض سجلات الملكية الفكرية", "View IP records"),
        new(IpPermissions.Manage, "إدارة سجلات الملكية الفكرية (التجديد والمساهمين)", "Manage IP records (renewal, contributors)", Scopable: false),
        new(IpPermissions.Process, "مراجعة طلبات الملكية الفكرية وتسجيلها", "Review and register IP requests", Scopable: false),
        new(IpPermissions.Sensitive, "عرض رقم الهوية الإماراتية ونموذج الوزارة", "See Emirates ID numbers and the MOET form", Scopable: false),
        new(IpPermissions.DashboardRead, "عرض لوحة الملكية الفكرية", "View the IP dashboard", Scopable: false),
        new(IpPermissions.SettingsManage, "إدارة إعدادات الملكية الفكرية", "Manage IP settings", Scopable: false),
        new(IpPermissions.AiSuggest, "اقتراح نوع الملكية والحماية", "Suggest property and protection type", Scopable: false),
        new(IpPermissions.AiSimilarity, "فحص التشابه", "Similarity check", Scopable: false),
        new(IpPermissions.AiMoet, "تعبئة وصف نموذج الوزارة", "Pre-fill the MOET description", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("requester", "مقدم طلب ملكية فكرية", "IP requester", [new(IpPermissions.Read, DataScope.Own), new(IpPermissions.AiSuggest)], AssignToEveryone: true),
        new("office", "مكتب الاستراتيجية (الملكية الفكرية)", "Strategy Office (IP)",
            [new(IpPermissions.Read, DataScope.All), new(IpPermissions.Manage), new(IpPermissions.Process), new(IpPermissions.DashboardRead),
                new(IpPermissions.AiSuggest), new(IpPermissions.AiSimilarity), new(IpPermissions.AiMoet)],
            RequiresMfa: true),
        new("administrator", "مسؤول الملكية الفكرية", "IP administrator",
            [new(IpPermissions.Read, DataScope.All), new(IpPermissions.Manage), new(IpPermissions.Sensitive), new(IpPermissions.DashboardRead),
                new(IpPermissions.SettingsManage), new(IpPermissions.AiMoet)],
            RequiresMfa: true),
        new("viewer", "مستعرض الملكية الفكرية", "IP viewer", [new(IpPermissions.Read, DataScope.All), new(IpPermissions.DashboardRead)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles => [];

    public static readonly NumberingDefinition RecordNumbering = new(IpPermissions.RecordNumbering, IpPermissions.SystemCode, "رقم سجل الملكية الفكرية", "IP number",
        "IP-Registry-{SEQ:4}");

    public static readonly NumberingDefinition InternalNumbering = new(IpPermissions.InternalNumbering, IpPermissions.SystemCode, "رقم التسجيل الداخلي",
        "Internal registration number", "IP-INT-{YYYY}-{SEQ:4}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(IpPermissions.PropertyTypeLookup, IpPermissions.SystemCode, "نوع الملكية", "Property type",
        [
            new("methodology", "منهجية", "Methodology"), new("software", "برمجيات / نظام", "Software / system"), new("literary", "مصنف أدبي", "Literary work"),
            new("design", "تصميم", "Design"), new("invention", "اختراع", "Invention"), new("trademark", "علامة تجارية", "Trademark"),
            new(IpPermissions.DocumentPropertyType, "وثيقة مؤسسية", "Corporate document"),
        ]),
        new(IpPermissions.ProtectionTypeLookup, IpPermissions.SystemCode, "نوع الحماية", "Protection type",
        [
            new("copyright", "حق المؤلف", "Copyright"), new("industrial-design", "تصميم صناعي", "Industrial design"), new("patent", "براءة اختراع", "Patent"),
            new("utility-model", "نموذج منفعة", "Utility model"), new("trademark", "علامة تجارية", "Trademark"), new("trade-secret", "سر تجاري / تسجيل داخلي", "Trade secret / internal registration"),
        ]),
        new(IpPermissions.ContributorRoleLookup, IpPermissions.SystemCode, "دور المساهم", "Contributor role",
        [
            new("author", "مؤلف", "Author"), new("inventor", "مخترع", "Inventor"), new("designer", "مصمم", "Designer"), new("developer", "مطور", "Developer"),
            new("contributor", "مساهم", "Contributor"),
        ]),
    ];
}
