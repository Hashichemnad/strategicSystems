using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Tasks.Application;

/// <summary>Bilingual templates of Task Management (editable in Settings → Notifications). Placeholders: task title AR / EN, number, actor, text.</summary>
public static class TaskNotifications
{
    private static readonly string[] Common = ["name", "taskAr", "taskEn", "number", "due", "actor", "text"];

    private static NotificationTemplateDefinition Template(
        string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn, bool mandatory = false) =>
        new(key, TaskPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح المهمة", "Open the task", Common, mandatory);

    public static readonly NotificationTemplateDefinition Assigned = Template(
        "tasks.assigned", "إسناد مهمة", "Task assigned", "مهمة جديدة: «{{taskAr}}»", "New task: “{{taskEn}}”",
        "أسند إليك {{actor}} المهمة «{{taskAr}}» ({{number}})، تاريخ الاستحقاق {{due}}.", "{{actor}} assigned you “{{taskEn}}” ({{number}}), due {{due}}.");

    public static readonly NotificationTemplateDefinition Rejected = Template(
        "tasks.rejected", "رفض مهمة", "Task rejected", "رُفضت المهمة «{{taskAr}}»", "Task rejected: “{{taskEn}}”",
        "رفض {{actor}} المهمة «{{taskAr}}» ({{number}}). السبب: {{text}}", "{{actor}} rejected “{{taskEn}}” ({{number}}). Reason: {{text}}");

    public static readonly NotificationTemplateDefinition Submitted = Template(
        "tasks.submitted", "تسليم مهمة", "Task submitted", "سُلّمت المهمة «{{taskAr}}» للاعتماد", "Task submitted: “{{taskEn}}”",
        "سلّم {{actor}} المهمة «{{taskAr}}» ({{number}}) وهي بانتظار اعتمادك.", "{{actor}} submitted “{{taskEn}}” ({{number}}); it is waiting for your approval.");

    public static readonly NotificationTemplateDefinition Completed = Template(
        "tasks.completed", "إنجاز مهمة", "Task done", "أُنجزت المهمة «{{taskAr}}»", "Task done: “{{taskEn}}”",
        "أنجز {{actor}} المهمة «{{taskAr}}» ({{number}}).", "{{actor}} completed “{{taskEn}}” ({{number}}).");

    public static readonly NotificationTemplateDefinition Approved = Template(
        "tasks.approved", "اعتماد مهمة", "Task approved", "اعتُمدت المهمة «{{taskAr}}»", "Task approved: “{{taskEn}}”",
        "اعتمد {{actor}} المهمة «{{taskAr}}» ({{number}}). التقييم: {{text}}", "{{actor}} approved “{{taskEn}}” ({{number}}). Feedback: {{text}}");

    public static readonly NotificationTemplateDefinition Returned = Template(
        "tasks.returned", "إعادة مهمة", "Task returned", "أُعيدت المهمة «{{taskAr}}»", "Task returned: “{{taskEn}}”",
        "أعاد {{actor}} المهمة «{{taskAr}}» ({{number}}) لاستكمالها: {{text}}", "{{actor}} returned “{{taskEn}}” ({{number}}) for more work: {{text}}");

    public static readonly NotificationTemplateDefinition Cancelled = Template(
        "tasks.cancelled", "إلغاء مهمة", "Task cancelled", "أُلغيت المهمة «{{taskAr}}»", "Task cancelled: “{{taskEn}}”",
        "ألغى {{actor}} المهمة «{{taskAr}}» ({{number}}). السبب: {{text}}", "{{actor}} cancelled “{{taskEn}}” ({{number}}). Reason: {{text}}");

    public static readonly NotificationTemplateDefinition FollowUpAdded = Template(
        "tasks.followUpAdded", "إضافة موظف متابعة", "Added as follow-up", "أصبحت متابعاً للمهمة «{{taskAr}}»", "You follow “{{taskEn}}”",
        "أضافك {{actor}} موظف متابعة على المهمة «{{taskAr}}» ({{number}}).", "{{actor}} added you as a follow-up employee on “{{taskEn}}” ({{number}}).");

    public static readonly NotificationTemplateDefinition Mentioned = Template(
        "tasks.mentioned", "إشارة في تعليق", "Mentioned in a comment", "أشار إليك {{actor}} في «{{taskAr}}»", "{{actor}} mentioned you in “{{taskEn}}”",
        "{{actor}}: {{text}}", "{{actor}}: {{text}}");

    public static readonly NotificationTemplateDefinition GroupMessage = Template(
        "tasks.groupMessage", "رسالة لمكلفي المهمة", "Message to all assignees", "رسالة بخصوص «{{taskAr}}»", "Message about “{{taskEn}}”",
        "{{actor}}: {{text}}", "{{actor}}: {{text}}");

    public static readonly NotificationTemplateDefinition DueChanged = Template(
        "tasks.dueChanged", "تغيير تاريخ الاستحقاق", "Due date changed", "تاريخ استحقاق جديد لـ «{{taskAr}}»", "New due date for “{{taskEn}}”",
        "غيّر {{actor}} تاريخ استحقاق «{{taskAr}}» ({{number}}) إلى {{due}}.", "{{actor}} moved the due date of “{{taskEn}}” ({{number}}) to {{due}}.");

    public static readonly NotificationTemplateDefinition DueSoon = Template(
        "tasks.dueSoon", "تذكير بقرب الاستحقاق", "Due soon reminder", "تذكير: «{{taskAr}}» تستحق في {{due}}", "Reminder: “{{taskEn}}” is due {{due}}",
        "المهمة «{{taskAr}}» ({{number}}) تستحق في {{due}}.", "“{{taskEn}}” ({{number}}) is due {{due}}.");

    public static readonly NotificationTemplateDefinition Overdue = Template(
        "tasks.overdue", "مهمة متأخرة", "Task overdue", "متأخرة: «{{taskAr}}»", "Overdue: “{{taskEn}}”",
        "تجاوزت المهمة «{{taskAr}}» ({{number}}) تاريخ استحقاقها {{due}}.", "“{{taskEn}}” ({{number}}) passed its due date {{due}}.");

    public static readonly NotificationTemplateDefinition Escalated = Template(
        "tasks.escalated", "تصعيد مهمة متأخرة", "Overdue task escalated", "تصعيد: «{{taskAr}}» متأخرة", "Escalation: “{{taskEn}}” is overdue",
        "المهمة «{{taskAr}}» ({{number}}) المسندة إلى {{actor}} متأخرة منذ {{due}}.", "“{{taskEn}}” ({{number}}), assigned to {{actor}}, has been overdue since {{due}}.");

    public static readonly NotificationTemplateDefinition AcceptReminder = Template(
        "tasks.acceptReminder", "تذكير بقبول مهمة", "Reminder to accept a task", "بانتظار قبولك: «{{taskAr}}»", "Waiting for you to accept “{{taskEn}}”",
        "المهمة «{{taskAr}}» ({{number}}) ما زالت بانتظار قبولك.", "“{{taskEn}}” ({{number}}) is still waiting for you to accept it.");

    public static readonly NotificationTemplateDefinition Digest = new(
        "tasks.digest", TaskPermissions.SystemCode, "ملخص المهام", "Task digest", "ملخص مهامك", "Your task digest",
        "مرحباً {{name}}،\nلديك {{open}} مهمة مفتوحة، منها {{overdue}} متأخرة و{{dueSoon}} تستحق قريباً، و{{approvals}} بانتظار اعتمادك.\n{{button}}",
        "Hello {{name}},\nYou have {{open}} open tasks: {{overdue}} overdue, {{dueSoon}} due soon, and {{approvals}} waiting for your approval.\n{{button}}",
        "فتح مهامي", "Open my tasks", ["name", "open", "overdue", "dueSoon", "approvals"]);

    private static readonly string[] Bulk = ["name", "count", "fromAr", "fromEn", "toAr", "toEn", "actor", "text"];

    private static NotificationTemplateDefinition BulkTemplate(
        string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn, string buttonAr, string buttonEn) =>
        new(key, TaskPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", buttonAr, buttonEn, Bulk);

    public static readonly NotificationTemplateDefinition ReassignedToYou = BulkTemplate(
        "tasks.reassignedToYou", "مهام أُعيد إسنادها إليك", "Tasks reassigned to you", "{{count}} مهمة من {{fromAr}} أصبحت لك",
        "{{count}} tasks of {{fromEn}} are now yours",
        "نقل {{actor}} إليك {{count}} مهمة مفتوحة كانت مسندة إلى {{fromAr}}. السبب: {{text}}",
        "{{actor}} moved {{count}} open tasks of {{fromEn}} to you. Reason: {{text}}", "فتح مهامي", "Open my tasks");

    public static readonly NotificationTemplateDefinition AssigneeChanged = BulkTemplate(
        "tasks.assigneeChanged", "تغيير المكلف بمهامك", "New assignee for your tasks", "{{count}} من مهامك انتقلت إلى {{toAr}}",
        "{{count}} of your tasks moved to {{toEn}}",
        "{{count}} من المهام التي أنشأتها وكانت مسندة إلى {{fromAr}} أصبحت مسندة إلى {{toAr}}. السبب: {{text}}",
        "{{count}} tasks you created that were assigned to {{fromEn}} are now assigned to {{toEn}}. Reason: {{text}}", "فتح المهام", "Open the tasks");

    public static readonly NotificationTemplateDefinition DecisionNeeded = BulkTemplate(
        "tasks.decisionNeeded", "مهام بانتظار قرارك", "Tasks waiting for your decision", "{{count}} من مهامك تحتاج قرارك",
        "{{count}} of your tasks need your decision",
        "{{count}} من المهام التي أنشأتها كانت مسندة إلى {{fromAr}} وأُعيدت إليك. أعد إسنادها أو ألغها. السبب: {{text}}",
        "{{count}} tasks you created were assigned to {{fromEn}} and came back to you: reassign or cancel them. Reason: {{text}}", "فتح المهام", "Open the tasks");

    public static readonly NotificationTemplateDefinition YourTasksReassigned = BulkTemplate(
        "tasks.yourTasksReassigned", "إعادة إسناد مهامك", "Your tasks were reassigned", "أُعيد إسناد {{count}} من مهامك",
        "{{count}} of your tasks were reassigned",
        "أعاد {{actor}} إسناد {{count}} من مهامك المفتوحة. السبب: {{text}}",
        "{{actor}} reassigned {{count}} of your open tasks. Reason: {{text}}", "فتح", "Open");

    public static readonly NotificationTemplateDefinition EmployeeLeftWithTasks = BulkTemplate(
        "tasks.employeeLeftWithTasks", "موظف غادر ولديه مهام مفتوحة", "Employee left with open tasks", "{{fromAr}} غادر ولديه {{count}} مهمة مفتوحة",
        "{{fromEn}} left with {{count}} open tasks",
        "تغيرت حالة {{fromAr}} إلى «غادر» ولديه {{count}} مهمة مفتوحة. أعد إسنادها إلى موظف آخر أو أعدها إلى منشئيها.",
        "{{fromEn}} is now marked as left and has {{count}} open tasks. Reassign them to another employee or hand them back to their creators.",
        "إعادة إسناد المهام", "Reassign the tasks");

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All =
        [Assigned, Rejected, Submitted, Completed, Approved, Returned, Cancelled, FollowUpAdded, Mentioned, GroupMessage, DueChanged, DueSoon, Overdue, Escalated, AcceptReminder, Digest, ReassignedToYou,
            AssigneeChanged, DecisionNeeded, YourTasksReassigned, EmployeeLeftWithTasks];
}

/// <summary>Sends task notifications to employees (their active users), in the caller's transaction when given.</summary>
internal sealed class TaskNotifier(INotificationService notifications, IUserDirectory users, IEmployeeDirectory employees)
{
    public async Task SendAsync(
        NotificationTemplateDefinition template, TaskItem task, IEnumerable<Guid> employeeIds, string? actorName, string? text, object? transactionContext,
        CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var recipients = (await users.GetByEmployeesAsync(ids, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => NotificationRecipient.User(u.Id)).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(
            template.Key,
            recipients,
            new Dictionary<string, string>
            {
                ["taskAr"] = task.TitleAr ?? task.TitleEn ?? string.Empty,
                ["taskEn"] = task.TitleEn ?? task.TitleAr ?? string.Empty,
                ["number"] = task.Number ?? string.Empty,
                ["due"] = task.DueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "—",
                ["actor"] = actorName ?? string.Empty,
                ["text"] = text ?? string.Empty,
            },
            $"/tasks/{task.Id:D}"), transactionContext, ct).ConfigureAwait(false);
    }

    public async Task<string?> NameOfAsync(Guid? employeeId, CancellationToken ct) =>
        employeeId is { } id && await employees.GetAsync(id, ct).ConfigureAwait(false) is { } e ? e.NameEn ?? e.NameAr : null;
}
