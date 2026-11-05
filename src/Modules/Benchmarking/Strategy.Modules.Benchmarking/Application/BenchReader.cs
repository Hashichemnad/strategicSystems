using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Benchmarking.Application;

public sealed record BenchPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record BenchUnitDto(Guid Id, string? Code, string? NameAr, string? NameEn);

/// <summary>Loads studies the user may read (404 otherwise), names of people and units, settings.</summary>
internal sealed class BenchReader(BenchDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    public async Task<IQueryable<Study>> StudiesAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false, string permission = BenchPermissions.Read) =>
        await access.ApplyScopeAsync(tracked ? db.Studies : db.Studies.AsNoTracking(), user, permission, BenchScope.Study, ct).ConfigureAwait(false);

    public async Task<Study?> StudyAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await StudiesAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, Study study, CancellationToken ct) =>
        access.CanAsync(user, permission, BenchScope.Study.RefOf(study), ct);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, null, ct);

    public Task<bool> CanAllAsync(ClaimsPrincipal user, string permission, CancellationToken ct) =>
        access.CanAsync(user, permission, new ResourceRef(Study.EntityType, Guid.Empty), ct);

    public Task<BenchSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<BenchSettings>(ct);

    public async Task<Dictionary<Guid, BenchPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new BenchPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, BenchUnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, BenchUnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new BenchUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    public Task<UnitReference?> UnitAsync(Guid id, CancellationToken ct) => org.GetUnitAsync(id, ct);

    public Task<IReadOnlyList<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct) => org.GetSelfAndDescendantIdsAsync(unitId, ct);

    public Task<Guid?> HeadOfUnitAsync(Guid unitId, CancellationToken ct) => org.GetHeadOfUnitAsync(unitId, null, ct);

    public Task<IReadOnlyList<UnitReference>> PathAsync(Guid unitId, CancellationToken ct) => org.GetPathAsync(unitId, null, ct);

    public Task<EmployeeReference?> EmployeeAsync(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);

    public static BenchPersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, BenchPersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new BenchPersonDto(i, null, null) : null;

    public static BenchUnitDto? Unit(Guid? id, IReadOnlyDictionary<Guid, BenchUnitDto> units) =>
        id is { } i ? units.GetValueOrDefault(i) ?? new BenchUnitDto(i, null, null, null) : null;
}

/// <summary>Working-time moments (ADR 0027 / 0127): the pre-quarter search notice in the responsible unit's business hours.</summary>
internal sealed class BenchCalendar(IBusinessHoursCalculator calculator, IClock clock, ISettingsStore settings)
{
    private const int MaxDaysScanned = 400;

    /// <summary>
    /// The notice day (ADR 0127): the first day of the target quarter minus <paramref name="days"/> calendar days (or working days), moved back to
    /// the previous working day of the unit; the notice is due from the start of that day in the unit's time zone.
    /// </summary>
    public async Task<(DateOnly Day, DateTimeOffset From)> SearchNoticeAsync(Guid? unitId, int year, int quarter, int days, bool workingDays, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var start = Quarters.Start(year, quarter);
        DateOnly day;
        if (workingDays)
        {
            day = start;
            var remaining = days;
            for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
            {
                day = day.AddDays(-1);
                if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
                {
                    remaining--;
                }
            }
        }
        else
        {
            day = start.AddDays(-days);
        }

        for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
        {
            day = day.AddDays(-1);
        }

        return (day, Midnight(zone, day));
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
internal sealed class BenchNotifier(INotificationService notifications, IUserDirectory users, IRoleHolderQuery roles, IOrgDirectory org)
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

    public static Dictionary<string, string> Values(Study s, string? date = null, string? actor = null, string? text = null, string? count = null) => new()
    {
        ["number"] = s.Number,
        ["titleAr"] = s.TitleAr ?? s.TitleEn ?? s.Number,
        ["titleEn"] = s.TitleEn ?? s.TitleAr ?? s.Number,
        ["date"] = date ?? string.Empty,
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["count"] = count ?? string.Empty,
    };

    public static string Link(Study s, string? tab = null) => tab is null ? $"/bench/studies/{s.Id:D}" : $"/bench/studies/{s.Id:D}?tab={tab}";
}
