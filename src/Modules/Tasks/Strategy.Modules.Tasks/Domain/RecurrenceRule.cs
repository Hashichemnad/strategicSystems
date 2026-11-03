using System.Text.Json;

namespace Strategy.Modules.Tasks.Domain;

public static class RecurrenceFrequencies
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Daily, Weekly, Monthly];
}

/// <summary>
/// Recurrence of a task (ADR 0038): every <see cref="Interval"/> days / weeks / months, until a date or for a number of occurrences.
/// Pure and deterministic; dates are calendar dates (the due date of each occurrence moves to a working day when set).
/// </summary>
public sealed record RecurrenceRule(string Frequency, int Interval = 1, DateOnly? Until = null, int? Count = null)
{
    public const int MaxCount = 366;

    public bool IsValid =>
        RecurrenceFrequencies.All.Contains(Frequency) && Interval is >= 1 and <= 12 && (Count is null or (>= 2 and <= MaxCount));

    /// <summary>The date of occurrence <paramref name="occurrence"/> (1 = the first task) from the series anchor.</summary>
    public DateOnly DateOf(DateOnly anchor, int occurrence)
    {
        var steps = (occurrence - 1) * Interval;
        return Frequency switch
        {
            RecurrenceFrequencies.Daily => anchor.AddDays(steps),
            RecurrenceFrequencies.Weekly => anchor.AddDays(7 * steps),
            _ => anchor.AddMonths(steps), // the 31st becomes the last day of shorter months
        };
    }

    /// <summary>The date of occurrence <paramref name="occurrence"/>, or null when the series has ended by count or date.</summary>
    public DateOnly? Next(DateOnly anchor, int occurrence)
    {
        if (Count is { } count && occurrence > count)
        {
            return null;
        }

        var date = DateOf(anchor, occurrence);
        return Until is { } until && date > until ? null : date;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static RecurrenceRule? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RecurrenceRule>(json, JsonSerializerOptions.Web);
}
