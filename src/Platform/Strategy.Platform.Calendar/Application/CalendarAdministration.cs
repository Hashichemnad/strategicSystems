using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Calendar.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Calendar.Application;

public sealed record DayDto(int Day, string Mode, string? Start, string? End);

public sealed record BusinessHoursDto(
    Guid Id, string? NameAr, string? NameEn, string TimeZone, bool Active, bool IsDefault, IReadOnlyList<DayDto> Days, int UnitCount, uint Version);

public sealed record BusinessHoursInput(string? NameAr, string? NameEn, string? TimeZone, IReadOnlyList<DayDto>? Days, uint Version = 0);

public sealed record VersionInput(uint Version);

public sealed record CalculateInput(DateTimeOffset? Start, decimal? Hours);

public sealed record CalculateDto(DateTimeOffset Start, decimal Hours, DateTimeOffset Due, string TimeZone);

public sealed record HolidayDto(
    Guid Id, string? NameAr, string? NameEn, DateOnly StartDate, DateOnly EndDate, bool AllDay, string? StartTime, string? EndTime, bool Recurring,
    bool AppliesToAll, IReadOnlyList<Guid> BusinessHoursIds, bool Active, DateOnly? OccursOn, uint Version);

public sealed record HolidayInput(
    string? NameAr, string? NameEn, DateOnly? StartDate, DateOnly? EndDate, bool AllDay, string? StartTime, string? EndTime, bool Recurring,
    bool AppliesToAll, IReadOnlyList<Guid>? BusinessHoursIds, uint Version = 0);

public sealed record UnitAssignmentDto(Guid UnitId, string? UnitCode, string? UnitNameAr, string? UnitNameEn, Guid? BusinessHoursId, uint Version);

public sealed record UnitAssignmentInput(Guid? BusinessHoursId);

public sealed record EffectiveBusinessHoursDto(
    BusinessHoursReference BusinessHours, Guid? FromUnitId, string? FromUnitNameAr, string? FromUnitNameEn, bool Inherited);

/// <summary>Settings → Business hours and Settings → Holidays (ADR 0027): validation, default rules, unit assignments, holiday overlaps.</summary>
internal sealed class CalendarAdministration(CalendarDbContext db, BusinessHoursCalculator calculator, IOrgDirectory org, ISettingsStore settings)
{
    public const int MaxHolidayDays = 366;
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss"];

    // ---------------------------------------------------------------- business hours

    public async Task<IReadOnlyList<BusinessHoursDto>> ListAsync(CancellationToken ct)
    {
        var records = await db.BusinessHours.AsNoTracking().OrderByDescending(b => b.IsDefault).ThenByDescending(b => b.Active).ThenBy(b => b.NameEn)
            .ToListAsync(ct).ConfigureAwait(false);
        var counts = await db.UnitBusinessHours.AsNoTracking().Where(a => a.BusinessHoursId != null).GroupBy(a => a.BusinessHoursId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        return records.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Result<BusinessHoursDto>> GetAsync(Guid id, CancellationToken ct) =>
        await db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false) is { } record
            ? ToDto(record, await db.UnitBusinessHours.CountAsync(a => a.BusinessHoursId == id, ct).ConfigureAwait(false))
            : Error.NotFound();

    public async Task<Result<BusinessHoursDto>> CreateAsync(BusinessHoursInput input, CancellationToken ct)
    {
        var parsed = await ParseAsync(input, ct).ConfigureAwait(false);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var first = !await db.BusinessHours.AnyAsync(ct).ConfigureAwait(false);
        var record = BusinessHours.Create(input.NameAr, input.NameEn, parsed.Value.TimeZone, parsed.Value.Days, isDefault: first);
        db.BusinessHours.Add(record);
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(record, 0);
    }

    public Task<Result<BusinessHoursDto>> UpdateAsync(Guid id, BusinessHoursInput input, CancellationToken ct) =>
        ChangeAsync(id, input.Version, async record =>
        {
            var parsed = await ParseAsync(input, ct).ConfigureAwait(false);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            record.Update(input.NameAr, input.NameEn, parsed.Value.TimeZone, parsed.Value.Days);
            return Result.Success();
        }, ct);

    /// <summary>Makes the record the default; the previous default stops being one in the same transaction.</summary>
    public async Task<Result<BusinessHoursDto>> MakeDefaultAsync(Guid id, VersionInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var previous = await db.BusinessHours.FirstOrDefaultAsync(b => b.IsDefault && b.Id != id, ct).ConfigureAwait(false);
        if (previous is not null)
        {
            previous.SetDefault(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false); // the unique "one default" index sees the old default leave first
        }

        var result = await ChangeAsync(id, input.Version, record =>
        {
            if (!record.Active)
            {
                return Task.FromResult<Result>(Error.Rule("calendar.businessHours.inactive", "calendar.errors.inactiveCannotBeDefault"));
            }

            record.SetDefault(true);
            return Task.FromResult(Result.Success());
        }, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return result;
    }

    public Task<Result<BusinessHoursDto>> SetActiveAsync(Guid id, bool active, VersionInput input, CancellationToken ct) =>
        ChangeAsync(id, input.Version, record =>
        {
            if (!active && record.IsDefault)
            {
                return Task.FromResult<Result>(Error.Rule("calendar.businessHours.default", "calendar.errors.defaultCannotBeDeactivated"));
            }

            record.SetActive(active);
            return Task.FromResult(Result.Success());
        }, ct);

    public async Task<Result<CalculateDto>> CalculateAsync(Guid id, CalculateInput input, CancellationToken ct)
    {
        if (await calculator.GetAsync(id, ct).ConfigureAwait(false) is not { } record)
        {
            return Error.NotFound();
        }

        if (input.Start is not { } start || input.Hours is not { } hours || hours < 0 || hours > 10_000)
        {
            return Error.Validation([Field("hours", "validation.inclusiveBetween", ("from", 0), ("to", 10_000))]);
        }

        var due = await calculator.AddWorkingTimeAsync(id, start, TimeSpan.FromHours((double)hours), ct).ConfigureAwait(false);
        return new CalculateDto(start, hours, due, record.TimeZone);
    }

    private async Task<Result<BusinessHoursDto>> ChangeAsync(Guid id, uint version, Func<BusinessHours, Task<Result>> change, CancellationToken ct)
    {
        var record = await db.BusinessHours.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound();
        }

        db.Entry(record).Property(x => x.Version).OriginalValue = version;
        var outcome = await change(record).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        try
        {
            await SaveAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(record, await db.UnitBusinessHours.CountAsync(a => a.BusinessHoursId == id, ct).ConfigureAwait(false));
    }

    private async Task<Result<(string TimeZone, IReadOnlyList<StoredDay> Days)>> ParseAsync(BusinessHoursInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(Field("nameAr", "validation.atLeastOneLanguage"));
        }

        foreach (var (value, name) in new[] { (input.NameAr, "nameAr"), (input.NameEn, "nameEn") })
        {
            if (value is { Length: > 200 })
            {
                errors.Add(Field(name, "validation.maximumLength", ("maxLength", 200)));
            }
        }

        var zone = string.IsNullOrWhiteSpace(input.TimeZone)
            ? (await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone
            : input.TimeZone.Trim();
        if (Schedules.FindZone(zone) is null)
        {
            errors.Add(Field("timeZone", "calendar.errors.unknownTimeZone"));
        }

        var days = new List<StoredDay>();
        var given = input.Days ?? [];
        for (var day = 1; day <= 7; day++)
        {
            var match = given.FirstOrDefault(d => d.Day == day);
            var at = $"days[{day - 1}]";
            if (match is null || !DayModes.All.Contains(match.Mode))
            {
                errors.Add(Field($"{at}.mode", "validation.unknownReference"));
                continue;
            }

            if (match.Mode != DayModes.Hours)
            {
                days.Add(new StoredDay(day, match.Mode, null, null));
                continue;
            }

            var start = ParseTime(match.Start);
            var end = ParseTime(match.End);
            if (start is null)
            {
                errors.Add(Field($"{at}.start", "calendar.errors.timeRequired"));
            }

            if (end is null)
            {
                errors.Add(Field($"{at}.end", "calendar.errors.timeRequired"));
            }
            else if (start is not null && end <= start)
            {
                errors.Add(Field($"{at}.end", "calendar.errors.endBeforeStart"));
            }

            days.Add(new StoredDay(day, DayModes.Hours, start?.ToString("HH:mm", CultureInfo.InvariantCulture), end?.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }

        if (given.Count > 0 && given.All(d => d.Mode == DayModes.Closed))
        {
            errors.Add(Field("days", "calendar.errors.noWorkingDay"));
        }

        return errors.Count > 0 ? Error.Validation(errors) : (zone, days);
    }

    private static BusinessHoursDto ToDto(BusinessHours record, int units) => new(
        record.Id, record.NameAr, record.NameEn, record.TimeZone, record.Active, record.IsDefault,
        record.ReadDays().Select(d => new DayDto(d.Day, d.Mode, d.Start, d.End)).ToList(), units, record.Version);

    // ---------------------------------------------------------------- unit assignments

    public async Task<IReadOnlyList<UnitAssignmentDto>> UnitsAsync(CancellationToken ct)
    {
        var assignments = await db.UnitBusinessHours.AsNoTracking().Where(a => a.BusinessHoursId != null).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<UnitAssignmentDto>();
        foreach (var assignment in assignments)
        {
            var unit = await org.GetUnitAsync(assignment.UnitId, ct).ConfigureAwait(false);
            result.Add(new UnitAssignmentDto(assignment.UnitId, unit?.Code, unit?.NameAr, unit?.NameEn, assignment.BusinessHoursId, assignment.Version));
        }

        return result.OrderBy(r => r.UnitCode, StringComparer.Ordinal).ToList();
    }

    /// <summary>Sets a unit's business hours, or "inherit" with null (no delete).</summary>
    public async Task<Result<UnitAssignmentDto>> SetUnitAsync(Guid unitId, UnitAssignmentInput input, CancellationToken ct)
    {
        var unit = await org.GetUnitAsync(unitId, ct).ConfigureAwait(false);
        if (unit is null)
        {
            return Error.NotFound();
        }

        if (input.BusinessHoursId is { } id && !await db.BusinessHours.AnyAsync(b => b.Id == id && b.Active, ct).ConfigureAwait(false))
        {
            return Error.Validation([Field("businessHoursId", "validation.unknownReference")]);
        }

        var assignment = await db.UnitBusinessHours.FirstOrDefaultAsync(a => a.UnitId == unitId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            assignment = UnitBusinessHours.Create(unitId, input.BusinessHoursId);
            db.UnitBusinessHours.Add(assignment);
        }
        else
        {
            assignment.Set(input.BusinessHoursId);
        }

        await SaveAsync(ct).ConfigureAwait(false);
        return new UnitAssignmentDto(unitId, unit.Code, unit.NameAr, unit.NameEn, assignment.BusinessHoursId, assignment.Version);
    }

    public async Task<Result<EffectiveBusinessHoursDto>> EffectiveAsync(Guid unitId, CancellationToken ct)
    {
        if (await org.GetUnitAsync(unitId, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var (record, from) = await calculator.ResolveWithSourceAsync(unitId, ct).ConfigureAwait(false);
        var source = from is { } fromId ? await org.GetUnitAsync(fromId, ct).ConfigureAwait(false) : null;
        return new EffectiveBusinessHoursDto(record, from, source?.NameAr, source?.NameEn, from != unitId);
    }

    // ---------------------------------------------------------------- holidays

    public async Task<IReadOnlyList<HolidayDto>> HolidaysAsync(int? year, bool includeRemoved, CancellationToken ct)
    {
        var holidays = await db.Holidays.AsNoTracking().Where(h => includeRemoved || h.Active).ToListAsync(ct).ConfigureAwait(false);
        return holidays
            .Select(h => (Holiday: h, On: year is { } y ? OccurrenceIn(h.ToRule(), y) : (DateOnly?)h.StartDate))
            .Where(x => year is null || x.On is not null)
            .OrderBy(x => x.On ?? x.Holiday.StartDate)
            .Select(x => ToDto(x.Holiday, x.On)).ToList();
    }

    public async Task<Result<HolidayDto>> CreateHolidayAsync(HolidayInput input, CancellationToken ct)
    {
        var values = await ValidateHolidayAsync(input, null, ct).ConfigureAwait(false);
        if (values.IsFailure)
        {
            return values.Error;
        }

        var holiday = Holiday.Create(values.Value);
        db.Holidays.Add(holiday);
        await SaveAsync(ct).ConfigureAwait(false);
        return ToDto(holiday, holiday.StartDate);
    }

    public async Task<Result<HolidayDto>> UpdateHolidayAsync(Guid id, HolidayInput input, CancellationToken ct)
    {
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id && h.Active, ct).ConfigureAwait(false);
        if (holiday is null)
        {
            return Error.NotFound();
        }

        var values = await ValidateHolidayAsync(input, id, ct).ConfigureAwait(false);
        if (values.IsFailure)
        {
            return values.Error;
        }

        db.Entry(holiday).Property(x => x.Version).OriginalValue = input.Version;
        holiday.Update(values.Value);
        try
        {
            await SaveAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(holiday, holiday.StartDate);
    }

    public async Task<Result<HolidayDto>> RemoveHolidayAsync(Guid id, VersionInput input, CancellationToken ct)
    {
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id && h.Active, ct).ConfigureAwait(false);
        if (holiday is null)
        {
            return Error.NotFound();
        }

        db.Entry(holiday).Property(x => x.Version).OriginalValue = input.Version;
        holiday.Remove();
        try
        {
            await SaveAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(holiday, holiday.StartDate);
    }

    /// <summary>Validates a holiday against the rules and against the other active holidays sharing a record (ADR 0027).</summary>
    public async Task<Result<HolidayValues>> ValidateHolidayAsync(
        HolidayInput input, Guid? exceptId, CancellationToken ct, IReadOnlyList<HolidayValues>? alsoPending = null, string prefix = "")
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(Field(prefix + "nameAr", "validation.atLeastOneLanguage"));
        }

        foreach (var (value, name) in new[] { (input.NameAr, "nameAr"), (input.NameEn, "nameEn") })
        {
            if (value is { Length: > 200 })
            {
                errors.Add(Field(prefix + name, "validation.maximumLength", ("maxLength", 200)));
            }
        }

        if (input.StartDate is not { } start)
        {
            errors.Add(Field(prefix + "startDate", "validation.notEmpty"));
            return Error.Validation(errors);
        }

        var end = input.EndDate ?? start;
        if (end < start)
        {
            errors.Add(Field(prefix + "endDate", "calendar.errors.endBeforeStart"));
        }
        else if (end.DayNumber - start.DayNumber + 1 > MaxHolidayDays)
        {
            errors.Add(Field(prefix + "endDate", "calendar.errors.holidayTooLong", ("max", MaxHolidayDays)));
        }

        if (input.Recurring && end.Year != start.Year)
        {
            errors.Add(Field(prefix + "endDate", "calendar.errors.recurringAcrossYear"));
        }

        TimeOnly? startTime = null, endTime = null;
        if (!input.AllDay)
        {
            startTime = ParseTime(input.StartTime);
            endTime = ParseTime(input.EndTime);
            if (startTime is null)
            {
                errors.Add(Field(prefix + "startTime", "calendar.errors.timeRequired"));
            }

            if (endTime is null)
            {
                errors.Add(Field(prefix + "endTime", "calendar.errors.timeRequired"));
            }
            else if (startTime is not null && endTime <= startTime)
            {
                errors.Add(Field(prefix + "endTime", "calendar.errors.endBeforeStart"));
            }
        }

        var ids = input.BusinessHoursIds?.Distinct().ToList() ?? [];
        if (!input.AppliesToAll)
        {
            var known = await db.BusinessHours.AsNoTracking().CountAsync(b => ids.Contains(b.Id) && b.Active, ct).ConfigureAwait(false);
            if (ids.Count == 0 || known != ids.Count)
            {
                errors.Add(Field(prefix + "businessHoursIds", "calendar.errors.holidayRecords"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var values = new HolidayValues(input.NameAr, input.NameEn, start, end, input.AllDay, startTime, endTime, input.Recurring, input.AppliesToAll, ids);
        var others = (await db.Holidays.AsNoTracking().Where(h => h.Active && h.Id != exceptId).ToListAsync(ct).ConfigureAwait(false))
            .Select(h => new HolidayValues(h.NameAr, h.NameEn, h.StartDate, h.EndDate, h.AllDay, h.StartTime, h.EndTime, h.Recurring, h.AppliesToAll, h.BusinessHoursIds))
            .Concat(alsoPending ?? []);
        foreach (var other in others)
        {
            if (SharesRecord(values, other) && FirstOverlap(values, other) is { } date)
            {
                return Error.Validation([Field(prefix + "startDate", "calendar.errors.holidayOverlap",
                    ("nameAr", other.NameAr ?? other.NameEn), ("nameEn", other.NameEn ?? other.NameAr), ("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))]);
            }
        }

        return values;
    }

    private static bool SharesRecord(HolidayValues a, HolidayValues b) =>
        a.AppliesToAll || b.AppliesToAll || a.BusinessHoursIds.Intersect(b.BusinessHoursIds).Any();

    /// <summary>The first date both holidays fall on with overlapping hours, or null.</summary>
    internal static DateOnly? FirstOverlap(HolidayValues a, HolidayValues b)
    {
        if (!a.AllDay && !b.AllDay && (a.EndTime <= b.StartTime || b.EndTime <= a.StartTime))
        {
            return null;
        }

        var ruleA = new HolidayRule(a.StartDate, a.EndDate, a.Recurring);
        var ruleB = new HolidayRule(b.StartDate, b.EndDate, b.Recurring);
        // One-time dates fix the years to look at; two recurring holidays are compared in a leap year.
        var years = new[] { a, b }.Where(h => !h.Recurring).SelectMany(h => Enumerable.Range(h.StartDate.Year, h.EndDate.Year - h.StartDate.Year + 1))
            .DefaultIfEmpty(2028).Distinct();
        foreach (var year in years)
        {
            for (var date = new DateOnly(year, 1, 1); date.Year == year; date = date.AddDays(1))
            {
                if (ruleA.Covers(date) && ruleB.Covers(date))
                {
                    return date;
                }
            }
        }

        return null;
    }

    /// <summary>The first date of the holiday in <paramref name="year"/> (recurring: projected), or null.</summary>
    internal static DateOnly? OccurrenceIn(HolidayRule rule, int year)
    {
        for (var date = new DateOnly(year, 1, 1); date.Year == year; date = date.AddDays(1))
        {
            if (rule.Covers(date))
            {
                return date;
            }
        }

        return null;
    }

    private static HolidayDto ToDto(Holiday h, DateOnly? on) => new(
        h.Id, h.NameAr, h.NameEn, h.StartDate, h.EndDate, h.AllDay, h.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        h.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture), h.Recurring, h.AppliesToAll, h.BusinessHoursIds, h.Active, on, h.Version);

    // ---------------------------------------------------------------- helpers

    internal static TimeOnly? ParseTime(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TimeOnly.TryParseExact(value.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    internal async Task SaveAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        CalendarChangeSignal.Raise();
    }

    internal static FieldError Field(string field, string key, params (string Key, object? Value)[] parameters) =>
        new(field, key, key, parameters.Length == 0 ? null : parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
}
