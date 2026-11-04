using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Settings → Portfolio (docs/design/portfolio.md §2.1). Defaults are SPEC Appendix A (Q-P5, Q-P6).</summary>
public sealed record PortfolioSettings : ISettingsArea
{
    public static string Key => "portfolio";

    public decimal ThresholdAmount { get; init; } = 50_000m;

    public IReadOnlyList<PriorityCriterion> PriorityCriteria { get; init; } = DefaultCriteria;

    public IReadOnlyList<PriorityBand> PriorityBands { get; init; } = DefaultBands;

    public int StartYearsAhead { get; init; } = 3;

    public int EndYearsAhead { get; init; } = 3;

    public string BudgetStepCode { get; init; } = RequestApprovalProcess.BudgetStep;

    public Guid? ProjectScopeTemplateFileId { get; init; }

    public bool AssessmentOnCreate { get; init; } = true;

    public static readonly IReadOnlyList<PriorityCriterion> DefaultCriteria =
    [
        new("vision-alignment", "الارتباط برؤية الإمارة والإدارة", "Link to the vision and the department", 1),
        new("impact", "الأثر المتوقع والقيمة المضافة", "Expected impact and added value", 1),
        new("complexity", "مستوى التعقيد", "Complexity level", 1),
        new("radical-change", "إحداث تغيير جذري", "Driving radical change", 1),
    ];

    public static readonly IReadOnlyList<PriorityBand> DefaultBands =
    [
        new("low", "منخفضة", "Low", 0m),
        new("medium", "متوسطة", "Medium", 0.60m),
        new("high", "عالية", "High", 0.80m),
    ];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<PriorityCriterion> ActiveCriteria => PriorityCriteria.Where(c => c.Active).ToList();

    public static List<FieldError> Validate(PortfolioSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        if (value.ThresholdAmount < 0 || decimal.Round(value.ThresholdAmount, 2) != value.ThresholdAmount)
        {
            errors.Add(new FieldError("thresholdAmount", "validation.precisionScale", "validation.precisionScale"));
        }

        void Range(string field, int actual, int min, int max)
        {
            if (actual < min || actual > max)
            {
                errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = min, ["to"] = max }));
            }
        }

        Range("startYearsAhead", value.StartYearsAhead, 0, 10);
        Range("endYearsAhead", value.EndYearsAhead, 0, 15);
        if (string.IsNullOrWhiteSpace(value.BudgetStepCode) || value.BudgetStepCode.Length > 100)
        {
            errors.Add(new FieldError("budgetStepCode", "validation.notEmpty", "validation.notEmpty"));
        }

        var criteria = value.PriorityCriteria ?? [];
        if (!criteria.Any(c => c.Active))
        {
            errors.Add(new FieldError("priorityCriteria", "portfolio.errors.criteriaRequired", "portfolio.errors.criteriaRequired"));
        }

        if (criteria.Count > 12)
        {
            errors.Add(new FieldError("priorityCriteria", "validation.invalid", "validation.invalid"));
        }

        for (var i = 0; i < criteria.Count; i++)
        {
            var c = criteria[i];
            if (string.IsNullOrWhiteSpace(c.Code) || c.Code.Length > 60 || !c.Code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            {
                errors.Add(new FieldError($"priorityCriteria[{i}].code", "validation.code", "validation.code"));
            }

            if (string.IsNullOrWhiteSpace(c.LabelAr) && string.IsNullOrWhiteSpace(c.LabelEn))
            {
                errors.Add(new FieldError($"priorityCriteria[{i}].labelEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
            }

            if (c.Weight <= 0 || c.Weight > 100)
            {
                errors.Add(new FieldError($"priorityCriteria[{i}].weight", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = 0.01m, ["to"] = 100 }));
            }
        }

        if (criteria.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() != criteria.Count)
        {
            errors.Add(new FieldError("priorityCriteria", "validation.duplicate", "validation.duplicate"));
        }

        var bands = value.PriorityBands ?? [];
        if (bands.Count == 0 || bands.Min(b => b.Minimum) != 0)
        {
            errors.Add(new FieldError("priorityBands", "portfolio.errors.bandsStartAtZero", "portfolio.errors.bandsStartAtZero"));
        }

        for (var i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            if (string.IsNullOrWhiteSpace(b.Code) || b.Code.Length > 60)
            {
                errors.Add(new FieldError($"priorityBands[{i}].code", "validation.code", "validation.code"));
            }

            if (b.Minimum is < 0 or > 1)
            {
                errors.Add(new FieldError($"priorityBands[{i}].minimum", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = 0, ["to"] = 1 }));
            }
        }

        if (bands.Select(b => b.Code).Distinct(StringComparer.Ordinal).Count() != bands.Count
            || bands.Select(b => b.Minimum).Distinct().Count() != bands.Count)
        {
            errors.Add(new FieldError("priorityBands", "validation.duplicate", "validation.duplicate"));
        }

        return errors;
    }
}

/// <summary>Bilingual templates of Portfolio Management (SPEC §1.7; editable in Settings → Notifications).</summary>
public static class PortfolioNotifications
{
    private static readonly string[] Common = ["name", "requestAr", "requestEn", "number", "actor", "text"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, PortfolioPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition ScopeSubmitted = T(
        "portfolio.scopeSubmitted", "نطاق مشروع بانتظار عروض الأسعار", "Project scope waiting for quotations",
        "مطلوب عروض أسعار للطلب {{number}}", "Quotations needed for request {{number}}",
        "أرسل {{actor}} نطاق المشروع للطلب «{{requestAr}}» ({{number}}). يرجى رفع عروض الأسعار.",
        "{{actor}} sent the project scope of “{{requestEn}}” ({{number}}). Please upload the quotations.");

    public static readonly NotificationTemplateDefinition QuotationsUploaded = T(
        "portfolio.quotationsUploaded", "تم رفع عروض الأسعار", "Quotations uploaded",
        "عروض الأسعار جاهزة للطلب {{number}}", "Quotations ready for request {{number}}",
        "رفعت الإدارة عروض الأسعار للطلب «{{requestAr}}» ({{number}}). أكمل تفاصيل الميزانية وقدّم الطلب للاعتماد.",
        "Administration uploaded the quotations for “{{requestEn}}” ({{number}}). Complete the budget details and submit the request.");

    public static readonly NotificationTemplateDefinition RequestApproved = T(
        "portfolio.requestApproved", "اعتماد طلب برنامج", "Program request approved",
        "اعتُمد الطلب {{number}}", "Request {{number}} approved",
        "اعتُمد طلبك «{{requestAr}}» نهائياً وأُنشئ {{text}}.",
        "Your request “{{requestEn}}” received its final approval and {{text}} was created.");

    public static readonly NotificationTemplateDefinition RequestCancelled = T(
        "portfolio.requestCancelled", "إلغاء طلب برنامج", "Program request cancelled",
        "أُلغي الطلب {{number}}", "Request {{number}} cancelled",
        "ألغى {{actor}} الطلب «{{requestAr}}» ({{number}}). السبب: {{text}}",
        "{{actor}} cancelled the request “{{requestEn}}” ({{number}}). Reason: {{text}}");

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [ScopeSubmitted, QuotationsUploaded, RequestApproved, RequestCancelled];
}
