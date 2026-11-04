using System.Globalization;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>Bilingual templates of the Document Registry (docs/design/requests.md §3.7), editable in Settings → Notifications.</summary>
public static class DocRegNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "version", "expiry", "days", "text"];

    public static readonly NotificationTemplateDefinition Published = new(
        "docreg.published", DocRegPermissions.SystemCode, "نشر وثيقة", "Document published",
        "تم نشر الوثيقة {{number}} الإصدار {{version}}", "Published: {{number}} version {{version}}",
        "مرحباً {{name}}،\nتم نشر الوثيقة «{{titleAr}}» ({{number}}) الإصدار {{version}}.\n{{button}}",
        "Hello {{name}},\nThe document “{{titleEn}}” ({{number}}) version {{version}} has been published.\n{{button}}",
        "فتح الوثيقة", "Open the document", Common);

    public static readonly NotificationTemplateDefinition ExpiryReminder = new(
        "docreg.expiryReminder", DocRegPermissions.SystemCode, "تذكير بانتهاء صلاحية وثيقة", "Document expiry reminder",
        "تنتهي صلاحية {{number}} خلال {{days}} يوماً", "{{number}} expires in {{days}} days",
        "مرحباً {{name}}،\nتنتهي صلاحية الوثيقة «{{titleAr}}» ({{number}}) في {{expiry}}.\nيمكنك تأكيد استمرار صلاحيتها أو تقديم إصدار جديد أو طلب سحبها.\n{{button}}",
        "Hello {{name}},\nThe document “{{titleEn}}” ({{number}}) expires on {{expiry}}.\nConfirm it is still valid, submit a new version or request its withdrawal.\n{{button}}",
        "مراجعة الوثيقة", "Review the document", Common);

    public static readonly NotificationTemplateDefinition Expired = new(
        "docreg.expired", DocRegPermissions.SystemCode, "انتهاء صلاحية وثيقة", "Document expired",
        "انتهت صلاحية {{number}}", "Expired: {{number}}",
        "مرحباً {{name}}،\nانتهت صلاحية الوثيقة «{{titleAr}}» ({{number}}) في {{expiry}} دون إجراء. تظهر في المكتبة بحالة «منتهية».\n{{button}}",
        "Hello {{name}},\nThe document “{{titleEn}}” ({{number}}) expired on {{expiry}} without action. The library shows it as expired.\n{{button}}",
        "فتح الوثيقة", "Open the document", Common);

    public static readonly NotificationTemplateDefinition Withdrawn = new(
        "docreg.withdrawn", DocRegPermissions.SystemCode, "سحب وثيقة", "Document withdrawn",
        "تم سحب {{number}}", "Withdrawn: {{number}}",
        "مرحباً {{name}}،\nتم سحب الوثيقة «{{titleAr}}» ({{number}}) من المكتبة النشطة.\nالسبب: {{text}}\n{{button}}",
        "Hello {{name}},\nThe document “{{titleEn}}” ({{number}}) was withdrawn from the active library.\nReason: {{text}}\n{{button}}",
        "فتح الوثيقة", "Open the document", Common);

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [Published, ExpiryReminder, Expired, Withdrawn];
}

/// <summary>Today in the installation's time zone (Settings → Regional): validity dates are calendar dates there (ADR 0084).</summary>
internal sealed class DocRegCalendar(ISettingsStore settings, IClock clock)
{
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var zoneId = (await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone;
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return clock.Today;
        }
    }
}

internal sealed class DocRegNotifier(INotificationService notifications, IUserDirectory users, IUserRoleQuery userRoles, IOrgDirectory org)
{
    public async Task PublishedAsync(Document document, DocumentVersion version, object transactionContext, CancellationToken ct) =>
        await SendAsync(DocRegNotifications.Published, document, [version.RequestedByEmployeeId, document.OwnerEmployeeId], [], transactionContext, ct, version.Label)
            .ConfigureAwait(false);

    public async Task WithdrawnAsync(Document document, string reason, object transactionContext, CancellationToken ct) =>
        await SendAsync(DocRegNotifications.Withdrawn, document, [document.OwnerEmployeeId, document.RequesterEmployeeId], [], transactionContext, ct, text: reason)
            .ConfigureAwait(false);

    /// <summary>The owner, or the owner unit's head when the document has no owner.</summary>
    public async Task ReminderAsync(Document document, int days, CancellationToken ct)
    {
        var owner = document.OwnerEmployeeId ?? (document.OwnerUnitId is { } unit ? await org.GetHeadOfUnitAsync(unit, cancellationToken: ct).ConfigureAwait(false) : null);
        await SendAsync(DocRegNotifications.ExpiryReminder, document, [owner], [], null, ct, days: days).ConfigureAwait(false);
    }

    public async Task ExpiredAsync(Document document, CancellationToken ct)
    {
        var office = await userRoles.UsersWithPermissionAsync(DocRegPermissions.Process, ct).ConfigureAwait(false);
        await SendAsync(DocRegNotifications.Expired, document, [document.OwnerEmployeeId], office, null, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(NotificationTemplateDefinition template, Document document, IEnumerable<Guid?> employeeIds, IEnumerable<Guid> userIds,
        object? transactionContext, CancellationToken ct, string? version = null, int? days = null, string? text = null)
    {
        var ids = employeeIds.Where(e => e is not null).Select(e => e!.Value).Distinct().ToList();
        var recipients = ids.Count == 0 ? [] : (await users.GetByEmployeesAsync(ids, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id).ToList();
        recipients.AddRange(userIds);
        if (recipients.Count == 0)
        {
            return;
        }

        var values = new Dictionary<string, string>
        {
            ["number"] = document.DocumentNo ?? string.Empty,
            ["titleAr"] = document.NameAr ?? document.NameEn ?? string.Empty,
            ["titleEn"] = document.NameEn ?? document.NameAr ?? string.Empty,
            ["version"] = version ?? document.CurrentVersionLabel ?? string.Empty,
            ["expiry"] = document.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            ["days"] = days?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["text"] = text ?? string.Empty,
        };
        await notifications.SendAsync(new NotificationRequest(template.Key, recipients.Distinct().Select(NotificationRecipient.User).ToList(), values,
            DocumentResource.Link(document.Id)), transactionContext, ct).ConfigureAwait(false);
    }
}
