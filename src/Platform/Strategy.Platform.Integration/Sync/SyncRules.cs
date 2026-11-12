using System.Globalization;

namespace Strategy.Platform.Integration.Sync;

public static class SyncModeActions
{
    public const string Activate = "activate";
    public const string Deactivate = "deactivate";
    public const string MakePrimary = "makePrimary";
    public const string ReturnToStandby = "returnToStandby";
    public const string CloseOutage = "closeOutage";

    public static readonly IReadOnlyList<string> All = [Activate, Deactivate, MakePrimary, ReturnToStandby, CloseOutage];
}

/// <summary>Connection mode state machine (ADR 0207, docs/design/salesforce-sync.md §6). Guards (tested connection, acknowledged report,
/// no pending conflict, MFA) are checked by the service; this table says which transitions exist at all.</summary>
public static class SyncModeLifecycle
{
    private static readonly Dictionary<(string From, string Action), string> Transitions = new()
    {
        [(SyncModes.Disabled, SyncModeActions.Activate)] = SyncModes.Standby,
        [(SyncModes.Standby, SyncModeActions.Deactivate)] = SyncModes.Disabled,
        [(SyncModes.Standby, SyncModeActions.MakePrimary)] = SyncModes.Primary,
        [(SyncModes.Primary, SyncModeActions.ReturnToStandby)] = SyncModes.Reconciling,
        [(SyncModes.Reconciling, SyncModeActions.MakePrimary)] = SyncModes.Primary,
        [(SyncModes.Reconciling, SyncModeActions.CloseOutage)] = SyncModes.Standby,
    };

    public static string? Next(string from, string action) => Transitions.TryGetValue((from, action), out var to) ? to : null;

    public static bool CanApply(string from, string action) => Next(from, action) is not null;

    public static IReadOnlyList<string> ActionsFrom(string from) => [.. SyncModeActions.All.Where(a => CanApply(from, a))];
}

/// <summary>Next run of a schedule (§4). Pure; times are computed in the installation time zone and returned in UTC.</summary>
public static class SyncSchedule
{
    public const int MaxEveryHours = 24;

    /// <summary>Message keys of invalid settings (empty = valid).</summary>
    public static IReadOnlyList<(string Field, string MessageKey)> Validate(SyncScheduleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<(string, string)>();
        if (!ScheduleKinds.All.Contains(settings.Kind))
        {
            errors.Add(("kind", "integration.sync.errors.scheduleKind"));
        }

        if (settings.Kind == ScheduleKinds.Interval && settings.EveryHours is < 1 or > MaxEveryHours)
        {
            errors.Add(("everyHours", "integration.sync.errors.everyHours"));
        }

        if (!TryParseTime(settings.At, out _))
        {
            errors.Add(("at", "integration.sync.errors.time"));
        }

        if (settings.Kind == ScheduleKinds.Weekly && (settings.Days.Count == 0 || settings.Days.Any(d => d is < 1 or > 7)))
        {
            errors.Add(("days", "integration.sync.errors.days"));
        }

        return errors;
    }

    /// <summary>The first scheduled time strictly after <paramref name="after"/>.</summary>
    public static DateTimeOffset Next(SyncScheduleSettings settings, DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(zone);
        var at = TryParseTime(settings.At, out var time) ? time : TimeOnly.MinValue;
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        for (var i = 0; i < 15; i++, day = day.AddDays(1))
        {
            IEnumerable<TimeOnly> slots = settings.Kind switch
            {
                ScheduleKinds.Interval => IntervalSlots(at, Math.Clamp(settings.EveryHours, 1, MaxEveryHours)),
                ScheduleKinds.Weekly => settings.Days.Contains(IsoDay(day)) ? [at] : [],
                _ => [at],
            };
            foreach (var slot in slots)
            {
                var candidate = ToUtc(day, slot, zone);
                if (candidate > after)
                {
                    return candidate;
                }
            }
        }

        return after.AddDays(1);
    }

    /// <summary>Slots of a day for "every N hours", anchored at <paramref name="anchor"/> so runs do not drift.</summary>
    private static IEnumerable<TimeOnly> IntervalSlots(TimeOnly anchor, int everyHours)
    {
        var start = anchor.ToTimeSpan();
        while (start.TotalHours >= everyHours)
        {
            start -= TimeSpan.FromHours(everyHours);
        }

        for (var t = start; t < TimeSpan.FromDays(1); t += TimeSpan.FromHours(everyHours))
        {
            yield return TimeOnly.FromTimeSpan(t);
        }
    }

    public static int IsoDay(DateOnly day) => day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;

    private static DateTimeOffset ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30); // DST gap
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
