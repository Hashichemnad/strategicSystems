using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

public static class AssignScopes
{
    public const string Anyone = "anyone";
    public const string Unit = "unit";
    public const string DirectReports = "direct-reports";

    public static readonly IReadOnlyList<string> All = [Anyone, Unit, DirectReports];
}

public static class DigestModes
{
    public const string Off = "off";
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = [Off, Daily, Weekly];
}

/// <summary>Settings → Tasks (docs/design/tasks.md §2). Defaults are the SPEC / Appendix A build defaults; nothing organisation-specific in code.</summary>
public sealed record TaskSettings(
    IReadOnlyDictionary<string, string>? TypeTemplates = null,
    IReadOnlyList<string>? ConfidentialCategories = null,
    string AssignScope = AssignScopes.Anyone,
    bool SecretaryMayCreateOnBehalf = true,
    int FeedbackScaleMax = 5,
    int ReminderDaysBeforeDue = 2,
    bool OverdueNotice = true,
    int EscalateAfterOverdueDays = 3,
    int AcceptReminderHours = 8,
    string Digest = DigestModes.Off,
    int DigestWeekday = 1,
    int WorkloadThreshold = 10) : ISettingsArea
{
    public static string Key => "tasks";

    /// <summary>Seeded type → template map (SPEC §11.3: personal to-do = Simple; everything else Full, §11.12).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultTypeTemplates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["personal"] = TaskTemplates.Simple,
    };

    public string TemplateOf(string typeCode) =>
        (TypeTemplates ?? DefaultTypeTemplates).TryGetValue(typeCode, out var template) && TaskTemplates.All.Contains(template) ? template : TaskTemplates.Full;

    public bool IsConfidentialCategory(string? categoryCode) =>
        categoryCode is not null && (ConfidentialCategories ?? []).Contains(categoryCode, StringComparer.Ordinal);

    public static List<FieldError> Validate(TaskSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        void Range(string field, int actual, int min, int max)
        {
            if (actual < min || actual > max)
            {
                errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = min, ["to"] = max }));
            }
        }

        if (!AssignScopes.All.Contains(value.AssignScope))
        {
            errors.Add(new FieldError("assignScope", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!DigestModes.All.Contains(value.Digest))
        {
            errors.Add(new FieldError("digest", "validation.unknownCode", "validation.unknownCode"));
        }

        if ((value.TypeTemplates ?? new Dictionary<string, string>()).Values.Any(t => !TaskTemplates.All.Contains(t)))
        {
            errors.Add(new FieldError("typeTemplates", "validation.unknownCode", "validation.unknownCode"));
        }

        Range("feedbackScaleMax", value.FeedbackScaleMax, 3, 10);
        Range("reminderDaysBeforeDue", value.ReminderDaysBeforeDue, 0, 30);
        Range("escalateAfterOverdueDays", value.EscalateAfterOverdueDays, 0, 60);
        Range("acceptReminderHours", value.AcceptReminderHours, 0, 240);
        Range("digestWeekday", value.DigestWeekday, 1, 7);
        Range("workloadThreshold", value.WorkloadThreshold, 1, 500);
        return errors;
    }
}
