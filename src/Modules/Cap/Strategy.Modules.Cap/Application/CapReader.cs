using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Cap.Application;

public sealed record CapPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record CapUnitDto(Guid Id, string? Code, string? NameAr, string? NameEn);

/// <summary>Loads remarks and actions the user may read (404 otherwise), names of people and units, settings (ADR 0125).</summary>
internal sealed class CapReader(CapDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    public async Task<IQueryable<Remark>> RemarksAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false, string permission = CapPermissions.Read) =>
        await access.ApplyScopeAsync(tracked ? db.Remarks : db.Remarks.AsNoTracking(), user, permission, CapScope.Remark, ct).ConfigureAwait(false);

    public async Task<Remark?> RemarkAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await RemarksAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    /// <summary>Actions readable through any readable remark, or as their assignee.</summary>
    public async Task<IQueryable<CapAction>> ActionsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        // The sub-query must not carry AsNoTracking: EF applies it to the whole query, and tracked loads would come back detached.
        var remarks = (await RemarksAsync(user, ct, tracked: true).ConfigureAwait(false)).Select(r => r.Id);
        var linked = db.RemarkActions.Where(l => remarks.Contains(l.RemarkId)).Select(l => l.ActionId);
        var me = user.EmployeeIdOf();
        var source = tracked ? db.Actions : db.Actions.AsNoTracking();
        return source.Where(a => linked.Contains(a.Id) || (me != null && a.AssignedEmployeeId == me));
    }

    public async Task<CapAction?> ActionAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await ActionsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);

    /// <summary>Sources: the PM Office (manage) sees all; others the sources of remarks they can read.</summary>
    public async Task<IQueryable<CapSource>> SourcesAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Sources : db.Sources.AsNoTracking();
        if (await access.CanAsync(user, CapPermissions.SourceManage, null, ct).ConfigureAwait(false)
            && await access.CanAsync(user, CapPermissions.Read, new ResourceRef(Remark.EntityType, Guid.Empty), ct).ConfigureAwait(false))
        {
            return source;
        }

        var remarks = (await RemarksAsync(user, ct, tracked: true).ConfigureAwait(false)).Select(r => r.SourceId);
        return source.Where(s => remarks.Contains(s.Id));
    }

    public async Task<CapSource?> SourceAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await SourcesAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, Remark remark, CancellationToken ct) =>
        access.CanAsync(user, permission, CapScope.Remark.RefOf(remark), ct);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, null, ct);

    /// <summary>A permission with an unscoped-but-typed reference (Office / All checks for lists).</summary>
    public Task<bool> CanAllAsync(ClaimsPrincipal user, string permission, CancellationToken ct) =>
        access.CanAsync(user, permission, new ResourceRef(Remark.EntityType, Guid.Empty), ct);

    public Task<CapSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<CapSettings>(ct);

    public async Task<Dictionary<Guid, CapPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new CapPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, CapUnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, CapUnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new CapUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    public Task<UnitReference?> UnitAsync(Guid id, CancellationToken ct) => org.GetUnitAsync(id, ct);

    public Task<IReadOnlyList<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct) => org.GetSelfAndDescendantIdsAsync(unitId, ct);

    public Task<EmployeeReference?> EmployeeAsync(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);

    /// <summary>Default remark owner: head of the section, else of the department (SPEC §9.3 B).</summary>
    public async Task<Guid?> DefaultOwnerAsync(Guid departmentUnitId, Guid? sectionUnitId, CancellationToken ct)
    {
        if (sectionUnitId is { } section && await org.GetHeadOfUnitAsync(section, null, ct).ConfigureAwait(false) is { } sectionHead)
        {
            return sectionHead;
        }

        return await org.GetHeadOfUnitAsync(departmentUnitId, null, ct).ConfigureAwait(false);
    }

    public static CapPersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, CapPersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new CapPersonDto(i, null, null) : null;

    public static CapUnitDto? Unit(Guid? id, IReadOnlyDictionary<Guid, CapUnitDto> units) =>
        id is { } i ? units.GetValueOrDefault(i) ?? new CapUnitDto(i, null, null, null) : null;
}

/// <summary>Working-time deadlines (ADR 0022 / 0027): due dates by priority, late, escalation moments in the unit's business hours.</summary>
internal sealed class CapDeadlines(IBusinessHoursCalculator calculator, IClock clock, ISettingsStore settings)
{
    private const int MaxDaysScanned = 800;

    /// <summary>
    /// Remark due date (ADR 0122): <paramref name="from"/> + <paramref name="days"/> calendar days rolled forward to a working day of the unit, or
    /// <paramref name="days"/> working days when <paramref name="workingDays"/>; due at the end of working time that day.
    /// </summary>
    public async Task<(DateOnly Date, DateTimeOffset DueAt)> DueAsync(Guid? unitId, DateOnly from, int days, bool workingDays, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        DateOnly day;
        if (workingDays)
        {
            day = from;
            var remaining = days;
            for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
            {
                day = day.AddDays(1);
                if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
                {
                    remaining--;
                }
            }
        }
        else
        {
            day = from.AddDays(days);
            for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
            {
                day = day.AddDays(1);
            }
        }

        var working = await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false);
        var due = working > TimeSpan.Zero
            ? await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, day), working, ct).ConfigureAwait(false)
            : Midnight(zone, day.AddDays(1));
        return (day, due);
    }

    /// <summary>The start of the working day <paramref name="days"/> working days after the moment's day.</summary>
    public async Task<DateTimeOffset> ShiftWorkingDaysAsync(Guid? unitId, DateTimeOffset from, int days, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime);
        var remaining = Math.Abs(days);
        var step = days < 0 ? -1 : 1;
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(step);
            if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return Midnight(zone, day);
    }

    /// <summary>End of the given date (local midnight after it) in the unit's time zone.</summary>
    public async Task<DateTimeOffset> EndOfDayAsync(Guid? unitId, DateOnly day, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        return Midnight(Zone(hours.TimeZone), day.AddDays(1));
    }

    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Zone(regional.TimeZone)).DateTime);
    }

    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private Task<TimeSpan> WorkingOnAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct) =>
        calculator.WorkingTimeBetweenAsync(hoursId, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct);

    internal static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    internal static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>Bilingual notifications to employees and to holders of a role (direct users + heads of assigned units).</summary>
internal sealed class CapNotifier(INotificationService notifications, IUserDirectory users, IRoleHolderQuery roles, IOrgDirectory org)
{
    public async Task SendAsync(
        NotificationTemplateDefinition template, IEnumerable<Guid?> employeeIds, IReadOnlyDictionary<string, string> values, string link, object? transactionContext,
        CancellationToken ct, IEnumerable<string>? roleKeys = null)
    {
        var ids = employeeIds.Where(i => i is not null).Select(i => i!.Value).ToList();
        var recipients = new List<Guid>();
        foreach (var key in roleKeys ?? [])
        {
            var holders = await roles.GetHoldersAsync(key, ct).ConfigureAwait(false);
            recipients.AddRange(holders.UserIds);
            foreach (var unit in holders.UnitIds)
            {
                if (await org.GetHeadOfUnitAsync(unit, null, ct).ConfigureAwait(false) is { } head)
                {
                    ids.Add(head);
                }
            }
        }

        ids = ids.Distinct().ToList();
        if (ids.Count > 0)
        {
            recipients.AddRange((await users.GetByEmployeesAsync(ids, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id));
        }

        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(template.Key, recipients.Distinct().Select(NotificationRecipient.User).ToList(), values, link),
            transactionContext, ct).ConfigureAwait(false);
    }

    public static Dictionary<string, string> Values(Remark remark, string? date = null, string? actor = null, string? text = null, string? count = null) => new()
    {
        ["number"] = remark.Number,
        ["titleAr"] = Short(remark.TextAr ?? remark.TextEn) ?? remark.Number,
        ["titleEn"] = Short(remark.TextEn ?? remark.TextAr) ?? remark.Number,
        ["date"] = date ?? CapDeadlines.Date(remark.DueOn),
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["count"] = count ?? string.Empty,
    };

    public static Dictionary<string, string> Values(CapAction action, string? date = null, string? actor = null, string? text = null) => new()
    {
        ["number"] = action.Number,
        ["titleAr"] = Short(action.DetailsAr ?? action.DetailsEn) ?? action.Number,
        ["titleEn"] = Short(action.DetailsEn ?? action.DetailsAr) ?? action.Number,
        ["date"] = date ?? CapDeadlines.Date(action.EffectiveEnd),
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["count"] = string.Empty,
    };

    public static string? Short(string? text) => text is null ? null : text.Length <= 160 ? text : text[..157] + "…";
}
