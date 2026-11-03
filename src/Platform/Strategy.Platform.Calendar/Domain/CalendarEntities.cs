using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Calendar.Domain;

/// <summary>One weekday as stored (JSON): ISO day number 1 = Monday … 7 = Sunday.</summary>
public sealed record StoredDay(int Day, string Mode, string? Start, string? End);

/// <summary>A business-hours record (ADR 0027). Never deleted; exactly one is the default.</summary>
[Audited(EntityType)]
public sealed class BusinessHours : AuditableEntity
{
    public const string EntityType = "calendar.business-hours";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private BusinessHours()
    {
    }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string TimeZone { get; private set; } = "Asia/Dubai";

    public bool Active { get; private set; } = true;

    public bool IsDefault { get; private set; }

    /// <summary>Seven <see cref="StoredDay"/> entries as JSON.</summary>
    public string Days { get; private set; } = "[]";

    public static BusinessHours Create(string? nameAr, string? nameEn, string timeZone, IReadOnlyList<StoredDay> days, bool isDefault)
    {
        var record = new BusinessHours { IsDefault = isDefault };
        record.Update(nameAr, nameEn, timeZone, days);
        return record;
    }

    public void Update(string? nameAr, string? nameEn, string timeZone, IReadOnlyList<StoredDay> days)
    {
        NameAr = Clean(nameAr);
        NameEn = Clean(nameEn);
        TimeZone = timeZone.Trim();
        Days = JsonSerializer.Serialize(days.OrderBy(d => d.Day).ToList(), Json);
    }

    public IReadOnlyList<StoredDay> ReadDays() => JsonSerializer.Deserialize<List<StoredDay>>(Days, Json) ?? [];

    public void SetDefault(bool value) => IsDefault = value;

    public void SetActive(bool value) => Active = value;

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A holiday (ADR 0027): one-time or yearly, all day or some hours, for all records or chosen ones. Removed = inactive.</summary>
[Audited(EntityType)]
public sealed class Holiday : AuditableEntity
{
    public const string EntityType = "calendar.holiday";

    private Holiday()
    {
    }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public bool AllDay { get; private set; } = true;

    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    public bool Recurring { get; private set; }

    public bool AppliesToAll { get; private set; } = true;

    public List<Guid> BusinessHoursIds { get; private set; } = [];

    public bool Active { get; private set; } = true;

    public static Holiday Create(HolidayValues values)
    {
        var holiday = new Holiday();
        holiday.Update(values);
        return holiday;
    }

    public void Update(HolidayValues values)
    {
        NameAr = BusinessHours.Clean(values.NameAr);
        NameEn = BusinessHours.Clean(values.NameEn);
        StartDate = values.StartDate;
        EndDate = values.EndDate;
        AllDay = values.AllDay;
        StartTime = values.AllDay ? null : values.StartTime;
        EndTime = values.AllDay ? null : values.EndTime;
        Recurring = values.Recurring;
        AppliesToAll = values.AppliesToAll;
        BusinessHoursIds = values.AppliesToAll ? [] : [.. values.BusinessHoursIds.Distinct().Order()];
    }

    public void Remove() => Active = false;

    public bool AppliesTo(Guid businessHoursId) => AppliesToAll || BusinessHoursIds.Contains(businessHoursId);

    public HolidayRule ToRule() => new(StartDate, EndDate, Recurring, AllDay ? null : StartTime, AllDay ? null : EndTime);
}

/// <summary>Validated holiday values (create, update, import).</summary>
public sealed record HolidayValues(
    string? NameAr, string? NameEn, DateOnly StartDate, DateOnly EndDate, bool AllDay, TimeOnly? StartTime, TimeOnly? EndTime, bool Recurring,
    bool AppliesToAll, IReadOnlyList<Guid> BusinessHoursIds);

/// <summary>The business hours a unit uses (null = inherit from the parent unit, then the default). Never deleted.</summary>
[Audited(EntityType)]
public sealed class UnitBusinessHours : AuditableEntity
{
    public const string EntityType = "calendar.unit-business-hours";

    private UnitBusinessHours()
    {
    }

    public Guid UnitId { get; private set; }

    public Guid? BusinessHoursId { get; private set; }

    public static UnitBusinessHours Create(Guid unitId, Guid? businessHoursId) => new() { UnitId = unitId, BusinessHoursId = businessHoursId };

    public void Set(Guid? businessHoursId) => BusinessHoursId = businessHoursId;
}
