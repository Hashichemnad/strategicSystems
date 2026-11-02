using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Workflow;

public static class WorkflowPermissions
{
    public const string SystemCode = "workflow";
    public const string ProcessRead = "workflow.process.read";
    public const string ProcessManage = "workflow.process.manage";
}

/// <summary>Approval Settings centre permissions and roles (design note §10). Acting on tasks needs no permission: being the approver is the check.</summary>
internal sealed class WorkflowCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(WorkflowPermissions.SystemCode, SystemKind.Platform, "الموافقات", "Approvals", 45);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(WorkflowPermissions.ProcessRead, "عرض إعدادات الموافقات", "View approval settings", Scopable: false),
        new(WorkflowPermissions.ProcessManage, "تعديل مسارات الموافقات", "Edit approval processes", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول الموافقات", "Approvals administrator",
            [new(WorkflowPermissions.ProcessRead), new(WorkflowPermissions.ProcessManage)], RequiresMfa: true),
        new("viewer", "مستعرض إعدادات الموافقات", "Approval settings viewer", [new(WorkflowPermissions.ProcessRead)]),
    ];
}

/// <summary>Settings → Approvals: which organisation levels the "section manager" and "department director" rules look for (ADR 0025).</summary>
public sealed record ApprovalSettings(string SectionLevelCode = "section", string DepartmentLevelCode = "department") : ISettingsArea
{
    public static string Key => "workflow.approvals";

    public static List<FieldError> Validate(ApprovalSettings value, IReadOnlyCollection<string> levelCodes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        if (!levelCodes.Contains(value.SectionLevelCode))
        {
            errors.Add(new FieldError("sectionLevelCode", "validation.unknownReference", "validation.unknownReference"));
        }

        if (!levelCodes.Contains(value.DepartmentLevelCode))
        {
            errors.Add(new FieldError("departmentLevelCode", "validation.unknownReference", "validation.unknownReference"));
        }

        return errors;
    }
}

/// <summary>Bilingual notification templates of the approval engine (design note §8), editable in Settings → Notifications.</summary>
public static class WorkflowNotifications
{
    private static readonly string[] Common = ["name", "process", "processAr", "processEn", "number", "titleAr", "titleEn", "step", "stepAr", "stepEn"];

    public static readonly NotificationTemplateDefinition TaskAssigned = new(
        "workflow.taskAssigned", WorkflowPermissions.SystemCode, "طلب موافقة جديد", "New approval request",
        "بانتظار موافقتك: {{processAr}} {{number}}", "Awaiting your approval: {{processEn}} {{number}}",
        "مرحباً {{name}}،\n«{{titleAr}}» ({{number}}) بانتظار موافقتك في خطوة «{{stepAr}}».\nمقدم الطلب: {{submitter}}\n{{due}}\n{{button}}",
        "Hello {{name}},\n“{{titleEn}}” ({{number}}) is waiting for your approval at the step “{{stepEn}}”.\nSubmitted by: {{submitter}}\n{{due}}\n{{button}}",
        "فتح السجل", "Open the record", [.. Common, "submitter", "due"]);

    public static readonly NotificationTemplateDefinition TaskOverdue = new(
        "workflow.taskOverdue", WorkflowPermissions.SystemCode, "تجاوز مهلة الموافقة", "Approval overdue",
        "تأخرت الموافقة: {{processAr}} {{number}}", "Approval overdue: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nتجاوزت خطوة «{{stepAr}}» لـ «{{titleAr}}» ({{number}}) المهلة المحددة.\n{{button}}",
        "Hello {{name}},\nThe step “{{stepEn}}” of “{{titleEn}}” ({{number}}) is past its deadline.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition TaskEscalated = new(
        "workflow.taskEscalated", WorkflowPermissions.SystemCode, "تصعيد موافقة متأخرة", "Overdue approval escalated",
        "تم تصعيد موافقة متأخرة إليك: {{processAr}} {{number}}", "Overdue approval escalated to you: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nتم تصعيد خطوة «{{stepAr}}» لـ «{{titleAr}}» ({{number}}) إليك لتجاوزها المهلة. يمكنك اتخاذ القرار الآن.\n{{button}}",
        "Hello {{name}},\nThe step “{{stepEn}}” of “{{titleEn}}” ({{number}}) is overdue and has been escalated to you. You can decide now.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition Approved = new(
        "workflow.approved", WorkflowPermissions.SystemCode, "اعتماد الطلب", "Request approved",
        "تم اعتماد {{processAr}} {{number}}", "Approved: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nتم اعتماد «{{titleAr}}» ({{number}}).\n{{button}}",
        "Hello {{name}},\n“{{titleEn}}” ({{number}}) has been approved.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition Rejected = new(
        "workflow.rejected", WorkflowPermissions.SystemCode, "رفض الطلب", "Request rejected",
        "تم رفض {{processAr}} {{number}}", "Rejected: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nرفض {{actor}} «{{titleAr}}» ({{number}}) في خطوة «{{stepAr}}».\nالملاحظات: {{comment}}\n{{button}}",
        "Hello {{name}},\n{{actor}} rejected “{{titleEn}}” ({{number}}) at the step “{{stepEn}}”.\nComments: {{comment}}\n{{button}}",
        "فتح السجل", "Open the record", [.. Common, "actor", "comment"]);

    public static readonly NotificationTemplateDefinition Returned = new(
        "workflow.returned", WorkflowPermissions.SystemCode, "إعادة الطلب للتصحيح", "Request returned for correction",
        "مطلوب تصحيح: {{processAr}} {{number}}", "Correction needed: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nأعاد {{actor}} «{{titleAr}}» ({{number}}) للتصحيح في خطوة «{{stepAr}}».\nالملاحظات: {{comment}}\nبعد التصحيح أعد تقديمه للموافقة.\n{{button}}",
        "Hello {{name}},\n{{actor}} returned “{{titleEn}}” ({{number}}) for correction at the step “{{stepEn}}”.\nComments: {{comment}}\nResubmit it for approval once corrected.\n{{button}}",
        "فتح السجل", "Open the record", [.. Common, "actor", "comment"]);

    public static readonly NotificationTemplateDefinition ActedOnBehalf = new(
        "workflow.actedOnBehalf", WorkflowPermissions.SystemCode, "إجراء نيابة عنك", "Action taken on your behalf",
        "اتخذ {{actor}} قراراً نيابة عنك: {{processAr}} {{number}}", "{{actor}} acted on your behalf: {{processEn}} {{number}}",
        "مرحباً {{name}}،\nاتخذ {{actor}} القرار «{{actionAr}}» نيابة عنك على «{{titleAr}}» ({{number}}) في خطوة «{{stepAr}}».\n{{button}}",
        "Hello {{name}},\n{{actor}} took the decision “{{actionEn}}” on your behalf on “{{titleEn}}” ({{number}}) at the step “{{stepEn}}”.\n{{button}}",
        "فتح السجل", "Open the record", [.. Common, "actor", "actionAr", "actionEn"]);

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [TaskAssigned, TaskOverdue, TaskEscalated, Approved, Rejected, Returned, ActedOnBehalf];
}
