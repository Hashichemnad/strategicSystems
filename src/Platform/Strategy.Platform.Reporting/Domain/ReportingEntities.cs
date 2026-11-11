using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Reporting.Domain;

/// <summary>
/// One cached aggregate (ADR 0140 / 0141): derived data written by the refresh job from module metric sources — not business data,
/// not entity-audited (refresh runs are logged), replaced wholesale per metric and month.
/// </summary>
public sealed class MetricFactRow
{
    public long Id { get; set; }

    public string Metric { get; set; } = string.Empty;

    public DateOnly Month { get; set; }

    public Guid? UnitId { get; set; }

    public string Partition { get; set; } = string.Empty;

    /// <summary>Dimension values as a JSON object (codes).</summary>
    public string DimsJson { get; set; } = "{}";

    public decimal Value { get; set; }

    public decimal Weight { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>Log of refresh runs (job dashboard detail and the "as of" time per system).</summary>
public sealed class RefreshLog
{
    public long Id { get; set; }

    public string SystemCode { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public DateOnly FromMonth { get; set; }

    public DateOnly ToMonth { get; set; }

    public int Facts { get; set; }

    public int DurationMs { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset At { get; set; }
}

public static class DashboardLevels
{
    public const string System = "system";
    public const string Executive = "executive";
    public const string Role = "role";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [System, Executive, Role, Custom];
}

public static class DashboardStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>
/// A dashboard definition (ADR 0143): widgets and grid layout as JSON (validated against the catalogue on save), never data. Seeded
/// definitions carry a <see cref="SeedHash"/>; re-seeding replaces them only while nobody edited them.
/// </summary>
public sealed class Dashboard : AuditableEntity
{
    private Dashboard()
    {
    }

    public Dashboard(string code, string titleAr, string titleEn, string level, string? systemCode, Guid? ownerUserId, string definitionJson)
    {
        Code = code;
        TitleAr = titleAr;
        TitleEn = titleEn;
        Level = level;
        SystemCode = systemCode;
        OwnerUserId = ownerUserId;
        DefinitionJson = definitionJson;
        Status = DashboardStatuses.Draft;
    }

    public string Code { get; private set; } = string.Empty;

    public string TitleAr { get; private set; } = string.Empty;

    public string TitleEn { get; private set; } = string.Empty;

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string Level { get; private set; } = DashboardLevels.Custom;

    public string? SystemCode { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string Status { get; private set; } = DashboardStatuses.Draft;

    /// <summary>Published definition (what viewers see).</summary>
    public string DefinitionJson { get; private set; } = "{}";

    /// <summary>Pending edits of a published dashboard; replaces the definition on publish.</summary>
    public string? DraftJson { get; private set; }

    public int PublishedVersion { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public string? SeedHash { get; private set; }

    public void Describe(string titleAr, string titleEn, string? descriptionAr, string? descriptionEn)
    {
        TitleAr = titleAr;
        TitleEn = titleEn;
        DescriptionAr = descriptionAr;
        DescriptionEn = descriptionEn;
    }

    public void SaveDraft(string definitionJson)
    {
        if (Status == DashboardStatuses.Published)
        {
            DraftJson = definitionJson;
        }
        else
        {
            DefinitionJson = definitionJson;
        }
    }

    public void Publish(DateTimeOffset at)
    {
        if (DraftJson is not null)
        {
            DefinitionJson = DraftJson;
            DraftJson = null;
        }

        Status = DashboardStatuses.Published;
        PublishedVersion++;
        PublishedAt = at;
    }

    public void Archive() => Status = DashboardStatuses.Archived;

    public void Seeded(string definitionJson, string hash, DateTimeOffset at)
    {
        DefinitionJson = definitionJson;
        DraftJson = null;
        SeedHash = hash;
        if (Status != DashboardStatuses.Published)
        {
            Publish(at);
        }
    }

    /// <summary>An edit by a person: the seed no longer owns the definition.</summary>
    public void Edited() => SeedHash = null;
}

/// <summary>A default dashboard of a role (one default per role; a user with several roles gets each default as a tab).</summary>
public sealed class DashboardRole : AuditableEntity
{
    private DashboardRole()
    {
    }

    public DashboardRole(Guid dashboardId, string roleKey, int sort, bool isDefault)
    {
        DashboardId = dashboardId;
        RoleKey = roleKey;
        Sort = sort;
        IsDefault = isDefault;
    }

    public Guid DashboardId { get; private set; }

    /// <summary>The role as <c>system.code</c> (stable across installations, like the role keys of approval steps).</summary>
    public string RoleKey { get; private set; } = string.Empty;

    public int Sort { get; private set; }

    public bool IsDefault { get; private set; }
}

public static class SharePrincipalKinds
{
    public const string User = "user";
    public const string Role = "role";
    public const string Unit = "unit";

    public static readonly IReadOnlyList<string> All = [User, Role, Unit];
}

/// <summary>A dashboard or report shared with a user, role or unit (and below).</summary>
public sealed class Share : AuditableEntity
{
    private Share()
    {
    }

    public Share(string targetKind, Guid targetId, string principalKind, string principalRef)
    {
        TargetKind = targetKind;
        TargetId = targetId;
        PrincipalKind = principalKind;
        PrincipalRef = principalRef;
    }

    /// <summary><c>dashboard</c> or <c>report</c>.</summary>
    public string TargetKind { get; private set; } = string.Empty;

    public Guid TargetId { get; private set; }

    public string PrincipalKind { get; private set; } = SharePrincipalKinds.User;

    /// <summary>User id, unit id (and below) or role key <c>system.code</c>.</summary>
    public string PrincipalRef { get; private set; } = string.Empty;
}

/// <summary>A user's favourite (dashboard, widget of a dashboard, report).</summary>
public sealed class UserPin : AuditableEntity
{
    private UserPin()
    {
    }

    public UserPin(Guid userId, string kind, Guid refId, string? widgetId, int sort)
    {
        UserId = userId;
        Kind = kind;
        RefId = refId;
        WidgetId = widgetId;
        Sort = sort;
    }

    public Guid UserId { get; private set; }

    /// <summary><c>dashboard</c>, <c>widget</c> or <c>report</c>.</summary>
    public string Kind { get; private set; } = string.Empty;

    public Guid RefId { get; private set; }

    public string? WidgetId { get; private set; }

    public int Sort { get; private set; }
}

/// <summary>A named filter set (period + filters) a user saved on a dashboard.</summary>
public sealed class SavedFilter : AuditableEntity
{
    private SavedFilter()
    {
    }

    public SavedFilter(Guid userId, Guid dashboardId, string name, string filtersJson, bool isDefault)
    {
        UserId = userId;
        DashboardId = dashboardId;
        Name = name;
        FiltersJson = filtersJson;
        IsDefault = isDefault;
    }

    public Guid UserId { get; private set; }

    public Guid DashboardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string FiltersJson { get; private set; } = "{}";

    public bool IsDefault { get; private set; }

    public void MakeDefault(bool value) => IsDefault = value;
}

/// <summary>A user's My Work card order and hidden cards (ADR 0149).</summary>
public sealed class MyWorkLayout : AuditableEntity
{
    private MyWorkLayout()
    {
    }

    public MyWorkLayout(Guid userId, string cardsJson)
    {
        UserId = userId;
        CardsJson = cardsJson;
    }

    public Guid UserId { get; private set; }

    /// <summary><c>[{"code":"tasks.my-tasks","hidden":false}, …]</c> in display order.</summary>
    public string CardsJson { get; private set; } = "[]";

    public void Update(string cardsJson) => CardsJson = cardsJson;
}

/// <summary>A saved report-builder definition (ADR 0144). Runs always with the runner's rights.</summary>
public sealed class SavedReport : AuditableEntity
{
    private SavedReport()
    {
    }

    public SavedReport(Guid ownerUserId, string name, string dataset, string definitionJson)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Dataset = dataset;
        DefinitionJson = definitionJson;
        Status = "active";
    }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Dataset { get; private set; } = string.Empty;

    /// <summary>Columns, filters, grouping, sort.</summary>
    public string DefinitionJson { get; private set; } = "{}";

    /// <summary><c>active</c> or <c>archived</c> (no hard delete).</summary>
    public string Status { get; private set; } = "active";

    /// <summary>The folder (ADR 0183); null = "My reports" root.</summary>
    public Guid? FolderId { get; private set; }

    public void MoveTo(Guid? folderId) => FolderId = folderId;

    public void Update(string name, string? description, string dataset, string definitionJson)
    {
        Name = name;
        Description = description;
        Dataset = dataset;
        DefinitionJson = definitionJson;
    }

    public void Archive() => Status = "archived";
}

public static class ScheduleFrequencies
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Daily, Weekly, Monthly];
}

/// <summary>Scheduled delivery of a dashboard or report by email (ADR 0145).</summary>
public sealed class Schedule : AuditableEntity
{
    private Schedule()
    {
    }

    public Schedule(Guid ownerUserId, string targetKind, Guid targetId, string name)
    {
        OwnerUserId = ownerUserId;
        TargetKind = targetKind;
        TargetId = targetId;
        Name = name;
        Active = true;
    }

    public Guid OwnerUserId { get; private set; }

    /// <summary><c>dashboard</c> or <c>report</c>.</summary>
    public string TargetKind { get; private set; } = string.Empty;

    public Guid TargetId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Frequency { get; private set; } = ScheduleFrequencies.Weekly;

    /// <summary>Local time of day (installation time zone).</summary>
    public TimeOnly TimeOfDay { get; private set; } = new(7, 0);

    /// <summary>Weekly: 0 = Sunday … 6 = Saturday. Monthly: day of month 1–28.</summary>
    public int Day { get; private set; } = 1;

    /// <summary><c>previous-month</c>, <c>current-month</c>, <c>previous-quarter</c>, <c>current-quarter</c>, <c>current-year</c>, <c>previous-year</c>.</summary>
    public string PeriodRule { get; private set; } = "previous-month";

    /// <summary><c>pdf</c>, <c>xlsx</c> or <c>link</c>.</summary>
    public string Format { get; private set; } = "pdf";

    /// <summary><c>[{"kind":"user|role|unit","id":"…"}]</c>, expanded at send time.</summary>
    public string RecipientsJson { get; private set; } = "[]";

    public bool Active { get; private set; }

    /// <summary>Dossier schedules (ADR 0186): record filters, format, language mode (jsonb); null for dashboards and reports.</summary>
    public string? OptionsJson { get; private set; }

    public void SetOptions(string? optionsJson) => OptionsJson = optionsJson;

    public DateTimeOffset? NextRunAt { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public void Configure(string name, string frequency, TimeOnly timeOfDay, int day, string periodRule, string format, string recipientsJson, bool active)
    {
        Name = name;
        Frequency = frequency;
        TimeOfDay = timeOfDay;
        Day = day;
        PeriodRule = periodRule;
        Format = format;
        RecipientsJson = recipientsJson;
        Active = active;
    }

    public void Planned(DateTimeOffset? next) => NextRunAt = next;

    public void Ran(DateTimeOffset at, DateTimeOffset? next)
    {
        LastRunAt = at;
        NextRunAt = next;
    }
}

/// <summary>One delivery to one recipient for one period; unique (schedule, recipient, period key) makes the job idempotent.</summary>
public sealed class Delivery : AuditableEntity
{
    private Delivery()
    {
    }

    public Delivery(Guid scheduleId, Guid userId, string periodKey, string status, string? format, int rows, string? detail, Guid? fileId)
    {
        ScheduleId = scheduleId;
        UserId = userId;
        PeriodKey = periodKey;
        Status = status;
        Format = format;
        Rows = rows;
        Detail = detail;
        FileId = fileId;
    }

    public Guid ScheduleId { get; private set; }

    public Guid UserId { get; private set; }

    public string PeriodKey { get; private set; } = string.Empty;

    /// <summary><c>sent</c>, <c>skipped</c> (nothing visible) or <c>failed</c>.</summary>
    public string Status { get; private set; } = string.Empty;

    public string? Format { get; private set; }

    public int Rows { get; private set; }

    public string? Detail { get; private set; }

    public Guid? FileId { get; private set; }

    public void FileRemoved() => FileId = null;

    public void Complete(string status, string? format, int rows, string? detail, Guid? fileId)
    {
        Status = status;
        Format = format;
        Rows = rows;
        Detail = detail;
        FileId = fileId;
    }
}

/// <summary>An executive brief a user saved after reviewing the AI draft (ADR 0147).</summary>
public sealed class ExecutiveBrief : AuditableEntity
{
    private ExecutiveBrief()
    {
    }

    public ExecutiveBrief(string periodKey, string scopeDescription, string contentJson, bool aiGenerated)
    {
        PeriodKey = periodKey;
        ScopeDescription = scopeDescription;
        ContentJson = contentJson;
        AiGenerated = aiGenerated;
    }

    public string PeriodKey { get; private set; } = string.Empty;

    public string ScopeDescription { get; private set; } = string.Empty;

    /// <summary>The (edited) brief: improved / attention / decisions with AR / EN text and evidence references.</summary>
    public string ContentJson { get; private set; } = "{}";

    /// <summary>Saved unchanged from the AI draft (the label stays until a person edits it).</summary>
    public bool AiGenerated { get; private set; }
}

public static class DefinitionStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
}

/// <summary>
/// A report type (ADR 0181): primary record type + related types along declared relationships. Seeded types carry a <see cref="SeedHash"/>;
/// re-seeding replaces them only while nobody edited them.
/// </summary>
public sealed class ReportType : AuditableEntity
{
    private ReportType()
    {
    }

    public ReportType(string code, string systemCode, string labelAr, string labelEn, string definitionJson)
    {
        Code = code;
        SystemCode = systemCode;
        LabelAr = labelAr;
        LabelEn = labelEn;
        DefinitionJson = definitionJson;
        Status = DefinitionStatuses.Active;
    }

    public string Code { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string LabelAr { get; private set; } = string.Empty;

    public string LabelEn { get; private set; } = string.Empty;

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string DefinitionJson { get; private set; } = "{}";

    public string Status { get; private set; } = DefinitionStatuses.Active;

    public string? SeedHash { get; private set; }

    public void Update(string systemCode, string labelAr, string labelEn, string? descriptionAr, string? descriptionEn, string definitionJson, bool active)
    {
        SystemCode = systemCode;
        LabelAr = labelAr;
        LabelEn = labelEn;
        DescriptionAr = descriptionAr;
        DescriptionEn = descriptionEn;
        DefinitionJson = definitionJson;
        Status = active ? DefinitionStatuses.Active : DefinitionStatuses.Inactive;
    }

    public void Seeded(string hash) => SeedHash = hash;

    public void Edited() => SeedHash = null;
}

public static class FolderKinds
{
    public const string Private = "private";
    public const string Shared = "shared";
}

/// <summary>A report folder (ADR 0183): private (owner only) or shared (users / roles / units through <see cref="Share"/>).</summary>
public sealed class ReportFolder : AuditableEntity
{
    private ReportFolder()
    {
    }

    public ReportFolder(Guid ownerUserId, string name, string kind)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Kind = kind;
        Status = DefinitionStatuses.Active;
    }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Kind { get; private set; } = FolderKinds.Private;

    public string Status { get; private set; } = DefinitionStatuses.Active;

    public void Rename(string name) => Name = name;

    public void Archive() => Status = DefinitionStatuses.Inactive;
}

/// <summary>A record dossier template (ADR 0184): record type, cover, header / footer and ordered sections (jsonb).</summary>
public sealed class DossierTemplate : AuditableEntity
{
    private DossierTemplate()
    {
    }

    public DossierTemplate(string code, string dataset, string labelAr, string labelEn, string definitionJson)
    {
        Code = code;
        Dataset = dataset;
        LabelAr = labelAr;
        LabelEn = labelEn;
        DefinitionJson = definitionJson;
        Status = DefinitionStatuses.Active;
    }

    public string Code { get; private set; } = string.Empty;

    /// <summary>The record type (a dataset code).</summary>
    public string Dataset { get; private set; } = string.Empty;

    public string LabelAr { get; private set; } = string.Empty;

    public string LabelEn { get; private set; } = string.Empty;

    public string DefinitionJson { get; private set; } = "{}";

    public string Status { get; private set; } = DefinitionStatuses.Active;

    public string? SeedHash { get; private set; }

    public void Update(string dataset, string labelAr, string labelEn, string definitionJson, bool active)
    {
        Dataset = dataset;
        LabelAr = labelAr;
        LabelEn = labelEn;
        DefinitionJson = definitionJson;
        Status = active ? DefinitionStatuses.Active : DefinitionStatuses.Inactive;
    }

    public void Seeded(string hash) => SeedHash = hash;

    public void Edited() => SeedHash = null;
}
