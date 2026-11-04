using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Requests.Application;

/// <summary>Bilingual templates of the framework (docs/design/requests.md §2.10); approval notices come from the approval engine.</summary>
public static class RequestNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "typeAr", "typeEn", "stageAr", "stageEn", "comment"];

    public static readonly NotificationTemplateDefinition StageEntered = new(
        "requests.stageEntered", RequestPermissions.SystemCode, "طلب بانتظار المعالجة", "Request waiting for processing",
        "طلب بانتظار المعالجة: {{typeAr}} {{number}}", "Waiting for processing: {{typeEn}} {{number}}",
        "مرحباً {{name}}،\nالطلب «{{titleAr}}» ({{number}}) وصل إلى مرحلة «{{stageAr}}» وينتظر إجراءك.\n{{button}}",
        "Hello {{name}},\nThe request “{{titleEn}}” ({{number}}) reached the stage “{{stageEn}}” and is waiting for your action.\n{{button}}",
        "فتح الطلب", "Open the request", Common);

    public static readonly NotificationTemplateDefinition Returned = new(
        "requests.returned", RequestPermissions.SystemCode, "إعادة الطلب للتصحيح", "Request returned for correction",
        "مطلوب تصحيح: {{typeAr}} {{number}}", "Correction needed: {{typeEn}} {{number}}",
        "مرحباً {{name}}،\nأعيد طلبك «{{titleAr}}» ({{number}}) للتصحيح في مرحلة «{{stageAr}}».\nالملاحظات: {{comment}}\n{{button}}",
        "Hello {{name}},\nYour request “{{titleEn}}” ({{number}}) was returned for correction at the stage “{{stageEn}}”.\nComments: {{comment}}\n{{button}}",
        "فتح الطلب", "Open the request", Common);

    public static readonly NotificationTemplateDefinition Rejected = new(
        "requests.rejected", RequestPermissions.SystemCode, "رفض الطلب", "Request rejected",
        "تم رفض الطلب: {{typeAr}} {{number}}", "Request rejected: {{typeEn}} {{number}}",
        "مرحباً {{name}}،\nتم رفض طلبك «{{titleAr}}» ({{number}}) في مرحلة «{{stageAr}}».\nالملاحظات: {{comment}}\n{{button}}",
        "Hello {{name}},\nYour request “{{titleEn}}” ({{number}}) was rejected at the stage “{{stageEn}}”.\nComments: {{comment}}\n{{button}}",
        "فتح الطلب", "Open the request", Common);

    public static readonly NotificationTemplateDefinition Completed = new(
        "requests.completed", RequestPermissions.SystemCode, "اكتمال الطلب", "Request completed",
        "اكتمل الطلب: {{typeAr}} {{number}}", "Request completed: {{typeEn}} {{number}}",
        "مرحباً {{name}}،\nاكتمل طلبك «{{titleAr}}» ({{number}}).\n{{button}}",
        "Hello {{name}},\nYour request “{{titleEn}}” ({{number}}) is complete.\n{{button}}",
        "فتح الطلب", "Open the request", Common);

    public static readonly NotificationTemplateDefinition StageOverdue = new(
        "requests.stageOverdue", RequestPermissions.SystemCode, "تأخر معالجة طلب", "Request processing overdue",
        "تأخرت المعالجة: {{typeAr}} {{number}}", "Processing overdue: {{typeEn}} {{number}}",
        "مرحباً {{name}}،\nتجاوز الطلب «{{titleAr}}» ({{number}}) مهلة مرحلة «{{stageAr}}».\n{{button}}",
        "Hello {{name}},\nThe request “{{titleEn}}” ({{number}}) is past the deadline of the stage “{{stageEn}}”.\n{{button}}",
        "فتح الطلب", "Open the request", Common);

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [StageEntered, Returned, Rejected, Completed, StageOverdue];
}

internal sealed class RequestNotifier(
    INotificationService notifications, IRoleHolderQuery roles, IUserRoleQuery userRoles, IOrgDirectory org, IEmployeeDirectory employees, IUserDirectory users)
{
    public async Task ToRequesterAsync(
        NotificationTemplateDefinition template, Request request, TypeModel type, StageDefinition? stage, string? comment, object? transactionContext, CancellationToken ct)
    {
        if (request.RequesterUserId is not { } requester)
        {
            return;
        }

        await SendAsync(template, [requester], request, type, stage ?? type.Stage(request.Stage), comment, transactionContext, ct).ConfigureAwait(false);
    }

    public async Task ToProcessorsAsync(
        NotificationTemplateDefinition template, Request request, TypeModel type, StageDefinition stage, object? transactionContext, CancellationToken ct)
    {
        var recipients = await ProcessorUsersAsync(type, stage, ct).ConfigureAwait(false);
        await SendAsync(template, recipients, request, type, stage, null, transactionContext, ct).ConfigureAwait(false);
    }

    /// <summary>Users who process a stage: holders of its roles (units include the units below) and, for built-in types, the system's processing permission.</summary>
    public async Task<IReadOnlyList<Guid>> ProcessorUsersAsync(TypeModel type, StageDefinition stage, CancellationToken ct)
    {
        var result = new HashSet<Guid>();
        foreach (var key in stage.ProcessorRoleKeys.Count > 0 ? stage.ProcessorRoleKeys : [RequestPermissions.OfficeRoleOf(type.SystemCode)])
        {
            var holders = await roles.GetHoldersAsync(key, ct).ConfigureAwait(false);
            if (!holders.Exists || holders.Everyone)
            {
                continue;
            }

            result.UnionWith(holders.UserIds);
            var units = new HashSet<Guid>();
            foreach (var unit in holders.UnitIds)
            {
                units.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, ct).ConfigureAwait(false));
            }

            if (units.Count > 0)
            {
                var employeeIds = await employees.GetEmployeeIdsInUnitsAsync(units, ct).ConfigureAwait(false);
                result.UnionWith((await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id));
            }
        }

        if (type.IsBuiltIn)
        {
            result.UnionWith(await userRoles.UsersWithPermissionAsync(RequestPermissions.ProcessPermissionOf(type.SystemCode), ct).ConfigureAwait(false));
        }

        return [.. result];
    }

    private Task SendAsync(NotificationTemplateDefinition template, IReadOnlyCollection<Guid> userIds, Request request, TypeModel type, StageDefinition? stage,
        string? comment, object? transactionContext, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var values = new Dictionary<string, string>
        {
            ["number"] = request.Number,
            ["titleAr"] = request.TitleAr ?? type.NameAr ?? type.NameEn ?? request.Number,
            ["titleEn"] = request.TitleEn ?? type.NameEn ?? type.NameAr ?? request.Number,
            ["typeAr"] = type.NameAr ?? type.NameEn ?? type.Code,
            ["typeEn"] = type.NameEn ?? type.NameAr ?? type.Code,
            ["stageAr"] = stage?.NameAr ?? stage?.NameEn ?? stage?.Code ?? string.Empty,
            ["stageEn"] = stage?.NameEn ?? stage?.NameAr ?? stage?.Code ?? string.Empty,
            ["comment"] = comment ?? string.Empty,
        };
        return notifications.SendAsync(new NotificationRequest(template.Key, userIds.Distinct().Select(NotificationRecipient.User).ToList(), values,
            RequestResource.Link(request.Id)), transactionContext, ct);
    }
}
