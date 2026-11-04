using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Risk.Application;

public sealed record RiskPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record RiskUnitDto(Guid Id, string? Code, string? NameAr, string? NameEn);

/// <summary>Loads risks the user may read (404 otherwise), names of people and units, settings.</summary>
internal sealed class RiskReader(RiskDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    public async Task<IQueryable<RiskRecord>> RisksAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false, string permission = RiskPermissions.Read) =>
        await access.ApplyScopeAsync(tracked ? db.Risks : db.Risks.AsNoTracking(), user, permission, RiskScope.Risk, ct).ConfigureAwait(false);

    public async Task<RiskRecord?> RiskAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await RisksAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, RiskRecord risk, CancellationToken ct) =>
        access.CanAsync(user, permission, RiskScope.Risk.RefOf(risk), ct);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, null, ct);

    /// <summary>A permission whose check uses a unit (create a risk in a register the user may write to).</summary>
    public Task<bool> CanInUnitAsync(ClaimsPrincipal user, string permission, Guid unitId, Guid? ownerEmployeeId, CancellationToken ct) =>
        access.CanAsync(user, permission, new ResourceRef(RiskRecord.EntityType, Guid.Empty, ownerEmployeeId, null, unitId), ct);

    public Task<RiskSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<RiskSettings>(ct);

    public async Task<Dictionary<Guid, RiskPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new RiskPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, RiskUnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, RiskUnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new RiskUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    public Task<UnitReference?> UnitAsync(Guid id, CancellationToken ct) => org.GetUnitAsync(id, ct);

    public Task<IReadOnlyList<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct) => org.GetSelfAndDescendantIdsAsync(unitId, ct);

    public Task<Guid?> HeadOfUnitAsync(Guid unitId, CancellationToken ct) => org.GetHeadOfUnitAsync(unitId, null, ct);

    public Task<EmployeeReference?> EmployeeAsync(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);

    public static RiskPersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, RiskPersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new RiskPersonDto(i, null, null) : null;

    public static RiskUnitDto? Unit(Guid? id, IReadOnlyDictionary<Guid, RiskUnitDto> units) =>
        id is { } i ? units.GetValueOrDefault(i) ?? new RiskUnitDto(i, null, null, null) : null;
}

/// <summary>Working-time deadlines (ADR 0027): review due dates and reminder moments in the owning unit's business hours.</summary>
internal sealed class RiskDeadlines(IBusinessHoursCalculator calculator, IClock clock, ISettingsStore settings)
{
    private const int MaxDaysScanned = 400;

    /// <summary>
    /// The next review (design §5.3): <paramref name="from"/> + the band's frequency in months, rolled forward to the next working day of the
    /// unit; due at the end of working time that day.
    /// </summary>
    public async Task<(DateOnly Date, DateTimeOffset DueAt)> NextReviewAsync(Guid unitId, DateOnly from, int months, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = from.AddMonths(months);
        for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
        {
            day = day.AddDays(1);
        }

        var working = await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false);
        var due = working > TimeSpan.Zero
            ? await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, day), working, ct).ConfigureAwait(false)
            : Midnight(zone, day.AddDays(1));
        return (day, due);
    }

    /// <summary>The start of the working day <paramref name="days"/> working days before (negative) or after the moment's day.</summary>
    public async Task<DateTimeOffset> ShiftWorkingDaysAsync(Guid? unitId, DateTimeOffset from, int days, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime);
        var step = days < 0 ? -1 : 1;
        var remaining = Math.Abs(days);
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(step);
            if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return days < 0 ? Midnight(zone, day) : Midnight(zone, day.AddDays(1));
    }

    /// <summary>End of the planned end date (local midnight after it) in the unit's time zone.</summary>
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
internal sealed class RiskNotifier(INotificationService notifications, IUserDirectory users, IRoleHolderQuery roles, IOrgDirectory org)
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

    public static Dictionary<string, string> Values(RiskRecord risk, string? date = null, string? actor = null, string? text = null, string? number = null) => new()
    {
        ["number"] = number ?? risk.Number,
        ["riskAr"] = risk.TitleAr ?? risk.TitleEn ?? risk.Number,
        ["riskEn"] = risk.TitleEn ?? risk.TitleAr ?? risk.Number,
        ["date"] = date ?? string.Empty,
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["band"] = risk.ResidualBand,
        ["count"] = string.Empty,
    };

    public static Dictionary<string, string> Values(TreatmentAction action, string? date) => new()
    {
        ["number"] = action.Number,
        ["riskAr"] = action.TitleAr ?? action.TitleEn ?? action.Number,
        ["riskEn"] = action.TitleEn ?? action.TitleAr ?? action.Number,
        ["date"] = date ?? string.Empty,
        ["actor"] = string.Empty,
        ["text"] = string.Empty,
        ["band"] = string.Empty,
        ["count"] = string.Empty,
    };
}
