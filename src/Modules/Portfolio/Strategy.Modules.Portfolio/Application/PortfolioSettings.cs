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

    /// <summary>Q-P2 / Q-P3, ADR 0070: the stage template of projects (Impact switch included).</summary>
    public StageTemplate ProjectTemplate { get; init; } = StageTemplate.ProjectDefault;

    public StageTemplate InitiativeTemplate { get; init; } = StageTemplate.InitiativeDefault;

    /// <summary>ADR 0071: working days after the window opens until the monthly update is due.</summary>
    public int MonthlyDueWorkingDays { get; init; } = 10;

    public int MonthlyReminderWorkingDays { get; init; } = 3;

    /// <summary>Q-P6: Delayed when approved progress is more than this below planned-to-date (ratio, 0.05 = 5 points).</summary>
    public decimal ProgressTolerance { get; init; } = ProgressCalculator.DefaultTolerance;

    /// <summary>Q-P4: change requests with a cost change above this amount go to the GM Office.</summary>
    public decimal GmThreshold { get; init; } = 100_000m;

    /// <summary>Standards enhancements switches (SPEC §1.12, ADR 0092).</summary>
    public PortfolioFeatures Features { get; init; } = new();

    /// <summary>SPEC §1.13 decision, ADR 0091: <c>monthly</c> (default) or <c>sprint</c>; a project may override.</summary>
    public string AgileApprovalMode { get; init; } = AgileApprovalModes.Monthly;

    public int DefaultSprintLengthDays { get; init; } = 14;

    /// <summary>Program type code → suggested methodology (ADR 0090).</summary>
    public IReadOnlyDictionary<string, string> MethodologySuggestions { get; init; } = new Dictionary<string, string>();

    /// <summary>SPEC §1.9 "View costs / budget — team member: configurable" (ADR 0098). Default off.</summary>
    public bool TeamMembersSeeCosts { get; init; }

    /// <summary>SPEC §1.9 "Create tasks / user stories — team member: configurable". Default off.</summary>
    public bool TeamMembersCreateStories { get; init; }

    public HealthThresholds Health { get; init; } = HealthThresholds.Default;

    public GateChecklists GateChecklists { get; init; } = GateChecklists.Default;

    public string SuggestedMethodology(string? programTypeCode) =>
        programTypeCode is { } code && MethodologySuggestions.TryGetValue(code, out var m) && Methodologies.All.Contains(m) ? m : Methodologies.Waterfall;

    public StageTemplate TemplateFor(string classification) => classification == Classifications.Project ? ProjectTemplate : InitiativeTemplate;

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

        Range("monthlyDueWorkingDays", value.MonthlyDueWorkingDays, 1, 60);
        Range("monthlyReminderWorkingDays", value.MonthlyReminderWorkingDays, 0, 30);
        if (value.ProgressTolerance is < 0 or > 1)
        {
            errors.Add(new FieldError("progressTolerance", "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0, ["to"] = 1 }));
        }

        if (value.GmThreshold < 0 || decimal.Round(value.GmThreshold, 2) != value.GmThreshold)
        {
            errors.Add(new FieldError("gmThreshold", "validation.precisionScale", "validation.precisionScale"));
        }

        foreach (var (field, template) in new[] { ("projectTemplate", value.ProjectTemplate), ("initiativeTemplate", value.InitiativeTemplate) })
        {
            foreach (var problem in template?.Problems() ?? ["validation.notEmpty"])
            {
                errors.Add(new FieldError(field, problem, problem));
            }
        }

        if (!AgileApprovalModes.All.Contains(value.AgileApprovalMode ?? string.Empty))
        {
            errors.Add(new FieldError("agileApprovalMode", "validation.unknownCode", "validation.unknownCode"));
        }

        Range("defaultSprintLengthDays", value.DefaultSprintLengthDays, 1, 28);
        foreach (var (code, methodology) in value.MethodologySuggestions ?? new Dictionary<string, string>())
        {
            if (!Methodologies.All.Contains(methodology) || string.IsNullOrWhiteSpace(code))
            {
                errors.Add(new FieldError($"methodologySuggestions.{code}", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        var health = value.Health ?? HealthThresholds.Default;
        if (health.ScheduleAmberGap is < 0 or > 1 || health.ScheduleRedGap < health.ScheduleAmberGap || health.ScheduleRedGap > 1)
        {
            errors.Add(new FieldError("health.scheduleRedGap", "portfolio.errors.thresholdOrder", "portfolio.errors.thresholdOrder"));
        }

        if (health.CostAmberVariance is < 0 or > 1 || health.CostRedVariance < health.CostAmberVariance || health.CostRedVariance > 1)
        {
            errors.Add(new FieldError("health.costRedVariance", "portfolio.errors.thresholdOrder", "portfolio.errors.thresholdOrder"));
        }

        Range("health.pastDueRedDays", health.PastDueRedDays, 0, 365);
        Range("health.scopeWindowDays", health.ScopeWindowDays, 1, 3650);
        Range("health.scopeRedChanges", health.ScopeRedChanges, 1, 50);
        foreach (var gate in Gates.All)
        {
            var items = (value.GateChecklists ?? GateChecklists.Default).For(gate);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 60 || !item.Code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                {
                    errors.Add(new FieldError($"gateChecklists.{gate}[{i}].code", "validation.code", "validation.code"));
                }

                if (string.IsNullOrWhiteSpace(item.LabelAr) && string.IsNullOrWhiteSpace(item.LabelEn))
                {
                    errors.Add(new FieldError($"gateChecklists.{gate}[{i}].labelEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
                }
            }

            if (items.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != items.Count)
            {
                errors.Add(new FieldError($"gateChecklists.{gate}", "validation.duplicate", "validation.duplicate"));
            }
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

public static class FeatureCodes
{
    public const string Portfolios = "portfolios";
    public const string Prioritisation = "prioritisation";
    public const string Health = "health";
    public const string Baselines = "baselines";
    public const string EarnedValue = "earnedValue";
    public const string Raid = "raid";
    public const string RiskMatrix = "riskMatrix";
    public const string Raci = "raci";
    public const string CommunicationPlan = "communicationPlan";
    public const string Benefits = "benefits";
    public const string Quality = "quality";
    public const string Procurement = "procurement";
    public const string StatusReport = "statusReport";
    public const string GateChecklists = "gateChecklists";
    public const string Templates = "templates";

    public static readonly IReadOnlyList<string> AllCodes =
    [
        Portfolios, Prioritisation, Health, Baselines, EarnedValue, Raid, RiskMatrix, Raci, CommunicationPlan, Benefits, Quality, Procurement, StatusReport,
        GateChecklists, Templates,
    ];
}

/// <summary>Switches of the standards enhancements (ADR 0092). All on except earned value ("optional").</summary>
public sealed record PortfolioFeatures
{
    public bool Portfolios { get; init; } = true;

    public bool Prioritisation { get; init; } = true;

    public bool Health { get; init; } = true;

    public bool Baselines { get; init; } = true;

    public bool EarnedValue { get; init; }

    public bool Raid { get; init; } = true;

    public bool RiskMatrix { get; init; } = true;

    public bool Raci { get; init; } = true;

    public bool CommunicationPlan { get; init; } = true;

    public bool Benefits { get; init; } = true;

    public bool Quality { get; init; } = true;

    public bool Procurement { get; init; } = true;

    public bool StatusReport { get; init; } = true;

    public bool GateChecklists { get; init; } = true;

    public bool Templates { get; init; } = true;

    public bool IsOn(string code) => code switch
    {
        FeatureCodes.Portfolios => Portfolios,
        FeatureCodes.Prioritisation => Prioritisation,
        FeatureCodes.Health => Health,
        FeatureCodes.Baselines => Baselines,
        FeatureCodes.EarnedValue => EarnedValue,
        FeatureCodes.Raid => Raid,
        FeatureCodes.RiskMatrix => RiskMatrix,
        FeatureCodes.Raci => Raci,
        FeatureCodes.CommunicationPlan => CommunicationPlan,
        FeatureCodes.Benefits => Benefits,
        FeatureCodes.Quality => Quality,
        FeatureCodes.Procurement => Procurement,
        FeatureCodes.StatusReport => StatusReport,
        FeatureCodes.GateChecklists => GateChecklists,
        FeatureCodes.Templates => Templates,
        _ => false,
    };

    public IReadOnlyDictionary<string, bool> ToMap() => FeatureCodes.AllCodes.ToDictionary(c => c, IsOn);
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

    private static readonly string[] ProjectCommon = ["name", "projectAr", "projectEn", "number", "actor", "text", "stageAr", "stageEn", "due"];

    private static NotificationTemplateDefinition P(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, PortfolioPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح المشروع", "Open the project", ProjectCommon);

    public static readonly NotificationTemplateDefinition OwnershipTaken = P(
        "portfolio.ownershipTaken", "تولي إدارة مشروع", "Project ownership taken", "أصبحت مدير المشروع {{number}}", "You manage project {{number}}",
        "أنت الآن مدير المشروع «{{projectAr}}» ({{number}}). الخطوة التالية: {{stageAr}}.",
        "You are now the project manager of “{{projectEn}}” ({{number}}). Next: {{stageEn}}.");

    public static readonly NotificationTemplateDefinition CharterApproved = P(
        "portfolio.charterApproved", "اعتماد ميثاق المشروع — رفع أمر الشراء", "Charter approved — upload the LPO", "مطلوب أمر الشراء للمشروع {{number}}",
        "LPO needed for project {{number}}", "اعتُمد ميثاق المشروع «{{projectAr}}» ({{number}}). يرجى رفع أمر الشراء وتفاصيل العقد والمورّد.",
        "The charter of “{{projectEn}}” ({{number}}) was approved. Please upload the LPO with the contract and supplier details.");

    public static readonly NotificationTemplateDefinition LpoUploaded = P(
        "portfolio.lpoUploaded", "تم رفع أمر الشراء — تشكيل الفريق", "LPO uploaded — form the team", "تشكيل فريق المشروع {{number}}", "Form the team of project {{number}}",
        "رُفع أمر الشراء للمشروع «{{projectAr}}» ({{number}}). يرجى تشكيل فريق المشروع من إدارة الفرق.",
        "The LPO of “{{projectEn}}” ({{number}}) was uploaded. Please form the project team in Teams Management.");

    public static readonly NotificationTemplateDefinition StageChanged = P(
        "portfolio.stageChanged", "انتقال مرحلة المشروع", "Project stage changed", "المشروع {{number}}: {{stageAr}}", "Project {{number}}: {{stageEn}}",
        "انتقل المشروع «{{projectAr}}» ({{number}}) إلى مرحلة: {{stageAr}}.", "“{{projectEn}}” ({{number}}) moved to the stage: {{stageEn}}.");

    public static readonly NotificationTemplateDefinition MonthlyOpened = P(
        "portfolio.monthlyOpened", "فتح التحديث الشهري", "Monthly update open", "التحديث الشهري للمشروع {{number}} مفتوح", "Monthly update open for {{number}}",
        "فُتحت نافذة التحديث الشهري ({{text}}) للمشروع «{{projectAr}}». الموعد النهائي: {{due}}.",
        "The monthly update window ({{text}}) of “{{projectEn}}” is open. Due: {{due}}.");

    public static readonly NotificationTemplateDefinition MonthlyReminder = P(
        "portfolio.monthlyReminder", "تذكير بالتحديث الشهري", "Monthly update reminder", "تذكير: تحديث المشروع {{number}}", "Reminder: update project {{number}}",
        "لم يُقدَّم التحديث الشهري ({{text}}) للمشروع «{{projectAr}}» بعد. الموعد النهائي: {{due}}.",
        "The monthly update ({{text}}) of “{{projectEn}}” is not submitted yet. Due: {{due}}.");

    public static readonly NotificationTemplateDefinition MonthlyOverdue = P(
        "portfolio.monthlyOverdue", "تأخر التحديث الشهري", "Monthly update overdue", "متأخر: تحديث المشروع {{number}}", "Overdue: update of project {{number}}",
        "تجاوز التحديث الشهري ({{text}}) للمشروع «{{projectAr}}» موعده ({{due}}).", "The monthly update ({{text}}) of “{{projectEn}}” passed its due date ({{due}}).");

    public static readonly NotificationTemplateDefinition ChangeRequestSubmitted = P(
        "portfolio.changeRequestSubmitted", "تقديم طلب تغيير", "Change request submitted", "طلب تغيير على المشروع {{number}}", "Change request on project {{number}}",
        "قدّم {{actor}} طلب التغيير {{text}} على المشروع «{{projectAr}}».", "{{actor}} submitted change request {{text}} on “{{projectEn}}”.");

    public static readonly NotificationTemplateDefinition ChangeRequestDecided = P(
        "portfolio.changeRequestDecided", "قرار طلب التغيير", "Change request decided", "طلب التغيير {{text}}: {{stageAr}}", "Change request {{text}}: {{stageEn}}",
        "صدر القرار على طلب التغيير {{text}} للمشروع «{{projectAr}}»: {{stageAr}}.", "Change request {{text}} of “{{projectEn}}” was decided: {{stageEn}}.");

    public static readonly NotificationTemplateDefinition ProjectOnHold = P(
        "portfolio.projectOnHold", "تعليق مشروع", "Project on hold", "عُلِّق المشروع {{number}}", "Project {{number}} on hold",
        "علّق {{actor}} المشروع «{{projectAr}}». السبب: {{text}}", "{{actor}} put “{{projectEn}}” on hold. Reason: {{text}}");

    public static readonly NotificationTemplateDefinition ProjectCancelled = P(
        "portfolio.projectCancelled", "إلغاء مشروع", "Project cancelled", "أُلغي المشروع {{number}}", "Project {{number}} cancelled",
        "أُلغي المشروع «{{projectAr}}». السبب: {{text}}", "“{{projectEn}}” was cancelled. Reason: {{text}}");

    public static readonly NotificationTemplateDefinition CommunicationDue = P(
        "portfolio.communicationDue", "موعد بند في خطة التواصل", "Communication plan item due", "تواصل مستحق — المشروع {{number}}", "Communication due — project {{number}}",
        "حان موعد التواصل مع «{{text}}» في خطة تواصل المشروع «{{projectAr}}» (الموعد: {{due}}).",
        "Communication with “{{text}}” is due in the communication plan of “{{projectEn}}” (due: {{due}}).");

    public static readonly NotificationTemplateDefinition SprintSubmitted = P(
        "portfolio.sprintReviewed", "اعتماد مراجعة السبرنت", "Sprint review decided", "مراجعة السبرنت — المشروع {{number}}", "Sprint review — project {{number}}",
        "صدر القرار على مراجعة السبرنت {{text}} للمشروع «{{projectAr}}»: {{stageAr}}.", "The review of sprint {{text}} of “{{projectEn}}” was decided: {{stageEn}}.");

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All =
    [
        CommunicationDue, SprintSubmitted,
        ScopeSubmitted, QuotationsUploaded, RequestApproved, RequestCancelled, OwnershipTaken, CharterApproved, LpoUploaded, StageChanged, MonthlyOpened, MonthlyReminder,
        MonthlyOverdue, ChangeRequestSubmitted, ChangeRequestDecided, ProjectOnHold, ProjectCancelled,
    ];
}
