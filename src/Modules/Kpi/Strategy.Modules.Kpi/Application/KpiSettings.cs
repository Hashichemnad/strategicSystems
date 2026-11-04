using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

/// <summary>A verification checklist item (SPEC K4.3, ADR 0064).</summary>
public sealed record ChecklistItem(string Code, string LabelAr, string LabelEn, bool Required = true);

/// <summary>A Measuring Method code mapped to its aggregation rule (SPEC K5).</summary>
public sealed record MeasuringMethodRule(string Code, string Rule);

/// <summary>Settings → KPI (docs/design/kpi.md §4, §5, §7; ADR 0060 / 0064).</summary>
public sealed record KpiSettings : ISettingsArea
{
    public static string Key => "kpi";

    public IReadOnlyList<WindowRule> WindowRules { get; init; } = Domain.WindowRules.Defaults;

    public int ReminderWorkingDaysBefore { get; init; } = 3;

    public int EscalationWorkingDaysAfter { get; init; } = 2;

    public bool RequireEvidence { get; init; } = true;

    public bool RequirePlanConfirmation { get; init; } = true;

    public bool ManualCodes { get; init; }

    public decimal GreenFrom { get; init; } = RagBands.DefaultGreen;

    public decimal AmberFrom { get; init; } = RagBands.DefaultAmber;

    /// <summary>Publish <c>KpiTargetMissed</c> for amber results too (red always).</summary>
    public bool MissedIncludesAmber { get; init; }

    public decimal AnomalyJumpRatio { get; init; } = 0.5m;

    public IReadOnlyList<MeasuringMethodRule> MeasuringMethods { get; init; } =
        [new("cumulative", AggregationRules.Sum), new("average", AggregationRules.Average)];

    public IReadOnlyList<ChecklistItem> Checklist { get; init; } =
    [
        new("evidence-readable", "الدليل مرفق ومقروء", "Evidence is attached and readable"),
        new("evidence-supports", "الدليل يدعم القيمة المدخلة", "Evidence supports the value"),
        new("equation-followed", "الحساب يتبع معادلة القياس", "The calculation follows the measuring equation"),
        new("correct-period", "النتيجة تخص الفترة الصحيحة", "The result is for the correct period"),
        new("justification-adequate", "الأسباب والإجراءات التصحيحية كافية", "Causality and corrective actions are adequate", Required: false),
    ];

    public string RuleFor(string? measuringMethodCode) =>
        MeasuringMethods.FirstOrDefault(m => m.Code == measuringMethodCode)?.Rule ?? AggregationRules.Sum;

    public static List<FieldError> Validate(KpiSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        void Add(string field, string key, Dictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        void Range(string field, int actual, int min, int max)
        {
            if (actual < min || actual > max)
            {
                Add(field, "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = min, ["to"] = max });
            }
        }

        Range("reminderWorkingDaysBefore", value.ReminderWorkingDaysBefore, 0, 30);
        Range("escalationWorkingDaysAfter", value.EscalationWorkingDaysAfter, 0, 60);
        if (value.GreenFrom is <= 0 or > 2 || value.AmberFrom <= 0 || value.AmberFrom >= value.GreenFrom)
        {
            Add("amberFrom", "kpi.errors.bands");
        }

        if (value.AnomalyJumpRatio is <= 0 or > 10)
        {
            Add("anomalyJumpRatio", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0.01, ["to"] = 10 });
        }

        foreach (var code in PeriodCodes.All)
        {
            var rules = value.WindowRules.Where(r => r.PeriodCode == code).ToList();
            if (rules.Count != 1 || rules[0].OpensDaysAfterEnd is < 0 or > 365 || rules[0].DueDaysAfterOpen is < 0 or > 365)
            {
                Add($"windowRules.{code}", "kpi.errors.windowRule");
            }
        }

        if (value.MeasuringMethods.Any(m => string.IsNullOrWhiteSpace(m.Code) || !AggregationRules.All.Contains(m.Rule))
            || value.MeasuringMethods.Select(m => m.Code).Distinct().Count() != value.MeasuringMethods.Count)
        {
            Add("measuringMethods", "kpi.errors.measuringMethods");
        }

        if (value.Checklist.Count > 20 || value.Checklist.Any(c => string.IsNullOrWhiteSpace(c.Code) || string.IsNullOrWhiteSpace(c.LabelAr) || string.IsNullOrWhiteSpace(c.LabelEn))
            || value.Checklist.Select(c => c.Code).Distinct().Count() != value.Checklist.Count)
        {
            Add("checklist", "kpi.errors.checklist");
        }

        return errors;
    }
}

/// <summary>Bilingual templates of Performance Management (editable in Settings → Notifications).</summary>
public static class KpiNotifications
{
    private static readonly string[] Common = ["name", "kpiAr", "kpiEn", "code", "period", "year", "date", "count", "actor", "text"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, KpiPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition WindowOpened = T(
        "kpi.windowOpened", "فتح فترة إدخال النتائج", "Result window opened", "فُتحت فترة إدخال نتائج المؤشرات", "KPI results are open for update",
        "فُتحت فترة إدخال النتائج: لديك {{count}} نتيجة بانتظار الإدخال حتى {{date}}.",
        "The submission window is open: you have {{count}} result(s) to submit by {{date}}.");

    public static readonly NotificationTemplateDefinition Reminder = T(
        "kpi.reminder", "تذكير بإدخال النتيجة", "Result reminder", "تذكير: نتيجة «{{kpiAr}}» مستحقة", "Reminder: “{{kpiEn}}” result due",
        "نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}) مستحقة في {{date}}.",
        "The {{period}} {{year}} result of “{{kpiEn}}” ({{code}}) is due on {{date}}.");

    public static readonly NotificationTemplateDefinition Overdue = T(
        "kpi.overdue", "تأخر إدخال النتيجة", "Result overdue", "تأخرت نتيجة «{{kpiAr}}»", "“{{kpiEn}}” result is overdue",
        "تجاوزت نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}) تاريخ الاستحقاق {{date}}. ما زال الإدخال متاحاً وسيُسجل متأخراً.",
        "The {{period}} {{year}} result of “{{kpiEn}}” ({{code}}) passed its due date {{date}}. You can still submit; it will be flagged late.");

    public static readonly NotificationTemplateDefinition Escalation = T(
        "kpi.escalation", "تصعيد تأخر النتائج", "Overdue result escalation", "تصعيد: نتيجة «{{kpiAr}}» متأخرة", "Escalation: “{{kpiEn}}” result overdue",
        "نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}) في وحدتك متأخرة منذ {{date}}.",
        "The {{period}} {{year}} result of “{{kpiEn}}” ({{code}}) in your unit has been overdue since {{date}}.");

    public static readonly NotificationTemplateDefinition Approved = T(
        "kpi.resultApproved", "اعتماد النتيجة", "Result approved", "اعتُمدت نتيجة «{{kpiAr}}»", "“{{kpiEn}}” result approved",
        "اعتمد {{actor}} نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}).", "{{actor}} approved the {{period}} {{year}} result of “{{kpiEn}}” ({{code}}).");

    public static readonly NotificationTemplateDefinition Returned = T(
        "kpi.resultReturned", "إعادة النتيجة للتصحيح", "Result returned", "أُعيدت نتيجة «{{kpiAr}}» للتصحيح", "“{{kpiEn}}” result returned for correction",
        "أعاد {{actor}} نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}): {{text}}",
        "{{actor}} returned the {{period}} {{year}} result of “{{kpiEn}}” ({{code}}): {{text}}");

    public static readonly NotificationTemplateDefinition Reopened = T(
        "kpi.resultReopened", "إعادة فتح النتيجة", "Result reopened", "أُعيد فتح نتيجة «{{kpiAr}}»", "“{{kpiEn}}” result reopened",
        "أعاد {{actor}} فتح نتيجة {{period}} {{year}} للمؤشر «{{kpiAr}}» ({{code}}): {{text}}",
        "{{actor}} reopened the {{period}} {{year}} result of “{{kpiEn}}” ({{code}}): {{text}}");

    public static readonly NotificationTemplateDefinition ChangeDecided = T(
        "kpi.changeDecided", "قرار تعديل تعريف المؤشر", "Definition change decided", "قرار على تعديل «{{kpiAr}}»", "Decision on the “{{kpiEn}}” change",
        "قرار {{actor}} على طلب تعديل تعريف المؤشر «{{kpiAr}}» ({{code}}): {{text}}",
        "{{actor}}'s decision on the definition change of “{{kpiEn}}” ({{code}}): {{text}}");

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [WindowOpened, Reminder, Overdue, Escalation, Approved, Returned, Reopened, ChangeDecided];
}

/// <summary>Sends KPI notifications to employees (their active user accounts) or users.</summary>
internal sealed class KpiNotifier(INotificationService notifications, IUserDirectory users)
{
    public async Task SendAsync(
        NotificationTemplateDefinition template, IEnumerable<Guid?> employeeIds, IReadOnlyDictionary<string, string> values, string link, object? transactionContext,
        CancellationToken ct, IEnumerable<Guid>? userIds = null)
    {
        var ids = employeeIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var recipients = ids.Count == 0
            ? []
            : (await users.GetByEmployeesAsync(ids, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id).ToList();
        recipients.AddRange(userIds ?? []);
        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(template.Key, recipients.Distinct().Select(NotificationRecipient.User).ToList(), values, link),
            transactionContext, ct).ConfigureAwait(false);
    }

    public static Dictionary<string, string> Values(KpiCard card, KpiResult? result, string? date = null, string? actor = null, string? text = null, int? count = null) => new()
    {
        ["kpiAr"] = card.NameAr ?? card.NameEn ?? card.Code,
        ["kpiEn"] = card.NameEn ?? card.NameAr ?? card.Code,
        ["code"] = card.Code,
        ["period"] = result is null ? string.Empty : result.PeriodCode.ToUpperInvariant(),
        ["year"] = result is null ? string.Empty : result.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["date"] = date ?? string.Empty,
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["count"] = count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
