using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record AuditUnitDto(Guid Id, string? Code, string? NameAr, string? NameEn);

/// <summary>
/// Loads findings, recommendations and reports the user may read (404 otherwise, ADR 0132). Non-office readers never see findings of draft
/// reports. People / unit names, settings, department ancestor.
/// </summary>
internal sealed class AuditReader(AuditMgmtDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    public async Task<IQueryable<Finding>> FindingsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var scoped = await access.ApplyScopeAsync(tracked ? db.Findings : db.Findings.AsNoTracking(), user, AuditPermissions.Read, AuditScope.Finding, ct).ConfigureAwait(false);
        if (await IsOfficeAsync(user, ct).ConfigureAwait(false))
        {
            return scoped;
        }

        var published = db.Reports.Where(r => r.Status != ReportStatuses.Draft).Select(r => r.Id);
        return scoped.Where(f => published.Contains(f.ReportId));
    }

    public async Task<Finding?> FindingAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await FindingsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(f => f.Id == id, ct).ConfigureAwait(false);

    /// <summary>Reports: the Audit Office (and All readers) see all; others the issued reports of findings they can read.</summary>
    public async Task<IQueryable<AuditReport>> ReportsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Reports : db.Reports.AsNoTracking();
        if (await IsOfficeAsync(user, ct).ConfigureAwait(false))
        {
            return source;
        }

        if (await CanAllAsync(user, AuditPermissions.Read, ct).ConfigureAwait(false))
        {
            return source.Where(r => r.Status != ReportStatuses.Draft);
        }

        var findings = (await FindingsAsync(user, ct, tracked: true).ConfigureAwait(false)).Select(f => f.ReportId);
        return source.Where(r => findings.Contains(r.Id));
    }

    public async Task<AuditReport?> ReportAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await ReportsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public async Task<IQueryable<Recommendation>> RecommendationsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        // The sub-query keeps tracking: AsNoTracking on it would apply to the whole query.
        var findings = (await FindingsAsync(user, ct, tracked: true).ConfigureAwait(false)).Select(f => f.Id);
        return (tracked ? db.Recommendations : db.Recommendations.AsNoTracking()).Where(r => findings.Contains(r.FindingId));
    }

    public async Task<Recommendation?> RecommendationAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await RecommendationsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    /// <summary>The Audit Office: holds the write permission (scope All).</summary>
    public Task<bool> IsOfficeAsync(ClaimsPrincipal user, CancellationToken ct) =>
        access.CanAsync(user, AuditPermissions.Office, new Platform.Access.ResourceRef(Finding.EntityType, Guid.Empty), ct);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, null, ct);

    public Task<bool> CanAllAsync(ClaimsPrincipal user, string permission, CancellationToken ct) =>
        access.CanAsync(user, permission, new Platform.Access.ResourceRef(Finding.EntityType, Guid.Empty), ct);

    public Task<AuditMgmtSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<AuditMgmtSettings>(ct);

    public async Task<Dictionary<Guid, AuditPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new AuditPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, AuditUnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, AuditUnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new AuditUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    public Task<UnitReference?> UnitAsync(Guid id, CancellationToken ct) => org.GetUnitAsync(id, ct);

    public Task<IReadOnlyList<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct) => org.GetSelfAndDescendantIdsAsync(unitId, ct);

    public Task<EmployeeReference?> EmployeeAsync(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);

    public Task<EmployeeReference?> EmployeeByNoAsync(string number, CancellationToken ct) => employees.FindByEmployeeNoAsync(number, ct);

    public Task<Guid?> HeadOfAsync(Guid unitId, CancellationToken ct) => org.GetHeadOfUnitAsync(unitId, null, ct);

    /// <summary>The nearest ancestor (or the unit itself) at <paramref name="levelCode"/>; the unit itself when no ancestor has that level.</summary>
    public async Task<Guid> DepartmentOfAsync(Guid unitId, string levelCode, CancellationToken ct)
    {
        var path = await org.GetPathAsync(unitId, cancellationToken: ct).ConfigureAwait(false);
        return path.LastOrDefault(u => string.Equals(u.LevelCode, levelCode, StringComparison.OrdinalIgnoreCase))?.Id ?? unitId;
    }

    public static AuditPersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, AuditPersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new AuditPersonDto(i, null, null) : null;

    public static AuditUnitDto? Unit(Guid? id, IReadOnlyDictionary<Guid, AuditUnitDto> units) =>
        id is { } i ? units.GetValueOrDefault(i) ?? new AuditUnitDto(i, null, null, null) : null;
}

/// <summary>Today in the installation's time zone and reminder dates in the unit's business hours (ADR 0027 / 0133).</summary>
internal sealed class AuditDeadlines(IBusinessHoursCalculator calculator, IClock clock, ISettingsStore settings)
{
    private const int MaxDaysScanned = 400;

    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Zone(regional.TimeZone)).DateTime);
    }

    /// <summary>The day the reminder for <paramref name="date"/> is due: N working days (of the unit) or N calendar days before it.</summary>
    public async Task<DateOnly> ReminderDayAsync(Guid? unitId, DateOnly date, int days, bool workingDays, CancellationToken ct)
    {
        if (!workingDays || days == 0)
        {
            return date.AddDays(-days);
        }

        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = date;
        var remaining = days;
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(-1);
            if (await calculator.WorkingTimeBetweenAsync(hours.Id, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return day;
    }

    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo Zone(string id)
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
internal sealed class AuditNotifier(INotificationService notifications, IUserDirectory users, IRoleHolderQuery roles, IOrgDirectory org)
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

    public static Dictionary<string, string> Values(string number, string? titleAr, string? titleEn, string? date = null, string? text = null, string? count = null) => new()
    {
        ["number"] = number,
        ["titleAr"] = Short(titleAr ?? titleEn) ?? number,
        ["titleEn"] = Short(titleEn ?? titleAr) ?? number,
        ["date"] = date ?? string.Empty,
        ["actor"] = string.Empty,
        ["text"] = text ?? string.Empty,
        ["count"] = count ?? string.Empty,
    };

    public static string? Short(string? text) => text is null ? null : text.Length <= 160 ? text : text[..157] + "…";
}

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
