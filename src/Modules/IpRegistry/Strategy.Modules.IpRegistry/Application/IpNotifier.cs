using System.Globalization;
using Strategy.Modules.IpRegistry.Contracts;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.IpRegistry.Application;

/// <summary>Bilingual templates of the IP Registry, editable in Settings → Notifications.</summary>
public static class IpNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "registrationNo", "expiry", "months"];

    public static readonly NotificationTemplateDefinition Certificate = new(
        "ip.certificate", IpPermissions.SystemCode, "شهادة تسجيل ملكية فكرية", "IP registration certificate",
        "تم تسجيل {{number}} — الشهادة متاحة", "Registered: {{number}} — certificate available",
        "مرحباً {{name}}،\nتم تسجيل العمل «{{titleAr}}» ({{number}}) الذي ساهمت فيه برقم التسجيل {{registrationNo}}. يمكنك تنزيل الشهادة من السجل.\n{{button}}",
        "Hello {{name}},\nThe work “{{titleEn}}” ({{number}}) you contributed to is registered under {{registrationNo}}. You can download the certificate from the record.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition RenewalReminder = new(
        "ip.renewalReminder", IpPermissions.SystemCode, "تذكير بتجديد ملكية فكرية", "IP renewal reminder",
        "تنتهي حماية {{number}} خلال {{months}} أشهر", "{{number}} protection ends in {{months}} months",
        "مرحباً {{name}}،\nتنتهي حماية «{{titleAr}}» ({{number}}) في {{expiry}}. يرجى البدء بإجراءات التجديد.\n{{button}}",
        "Hello {{name}},\nThe protection of “{{titleEn}}” ({{number}}) ends on {{expiry}}. Please start the renewal.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition Expired = new(
        "ip.expired", IpPermissions.SystemCode, "انتهاء حماية ملكية فكرية", "IP protection expired",
        "انتهت حماية {{number}}", "Protection expired: {{number}}",
        "مرحباً {{name}}،\nانتهت حماية «{{titleAr}}» ({{number}}) في {{expiry}}.\n{{button}}",
        "Hello {{name}},\nThe protection of “{{titleEn}}” ({{number}}) ended on {{expiry}}.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly NotificationTemplateDefinition CreatedFromDocument = new(
        "ip.fromDocument", IpPermissions.SystemCode, "سجل ملكية فكرية من وثيقة منشورة", "IP record from a published document",
        "سجل ملكية فكرية جديد: {{number}}", "New IP record: {{number}}",
        "مرحباً {{name}}،\nأنشئ سجل الملكية الفكرية {{number}} تلقائياً من الوثيقة المنشورة «{{titleAr}}». يرجى استكمال بيانات المساهمين قبل التقديم للوزارة.\n{{button}}",
        "Hello {{name}},\nThe IP record {{number}} was created automatically from the published document “{{titleEn}}”. Complete the contributors before filing with MOET.\n{{button}}",
        "فتح السجل", "Open the record", Common);

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [Certificate, RenewalReminder, Expired, CreatedFromDocument];
}

internal sealed class IpNotifier(INotificationService notifications, IUserDirectory users, IUserRoleQuery roles, IOrgDirectory org)
{
    public Task CertificateAsync(IpRecord record, IReadOnlyCollection<Guid> employeeIds, object? transactionContext, CancellationToken ct) =>
        SendAsync(IpNotifications.Certificate, record, employeeIds, [], transactionContext, ct);

    public async Task RenewalAsync(IpRecord record, int months, CancellationToken ct) =>
        await SendAsync(IpNotifications.RenewalReminder, record, await HeadAsync(record, ct).ConfigureAwait(false), await OfficeAsync(ct).ConfigureAwait(false), null, ct, months)
            .ConfigureAwait(false);

    public async Task ExpiredAsync(IpRecord record, CancellationToken ct) =>
        await SendAsync(IpNotifications.Expired, record, await HeadAsync(record, ct).ConfigureAwait(false), await OfficeAsync(ct).ConfigureAwait(false), null, ct)
            .ConfigureAwait(false);

    public async Task FromDocumentAsync(IpRecord record, object transactionContext, CancellationToken ct) =>
        await SendAsync(IpNotifications.CreatedFromDocument, record, [], await OfficeAsync(ct).ConfigureAwait(false), transactionContext, ct).ConfigureAwait(false);

    private async Task<IReadOnlyCollection<Guid>> HeadAsync(IpRecord record, CancellationToken ct) =>
        record.OwnerUnitId is { } unit && await org.GetHeadOfUnitAsync(unit, cancellationToken: ct).ConfigureAwait(false) is { } head ? [head] : [];

    private Task<IReadOnlyList<Guid>> OfficeAsync(CancellationToken ct) => roles.UsersWithPermissionAsync(IpPermissions.Process, ct);

    private async Task SendAsync(NotificationTemplateDefinition template, IpRecord record, IReadOnlyCollection<Guid> employeeIds, IEnumerable<Guid> userIds,
        object? transactionContext, CancellationToken ct, int? months = null)
    {
        var recipients = employeeIds.Count == 0 ? [] : (await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id).ToList();
        recipients.AddRange(userIds);
        if (recipients.Count == 0)
        {
            return;
        }

        var values = new Dictionary<string, string>
        {
            ["number"] = record.Number,
            ["titleAr"] = record.NameAr ?? record.NameEn ?? record.Number,
            ["titleEn"] = record.NameEn ?? record.NameAr ?? record.Number,
            ["registrationNo"] = record.RegistrationNo ?? string.Empty,
            ["expiry"] = record.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            ["months"] = months?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        };
        await notifications.SendAsync(new NotificationRequest(template.Key, recipients.Distinct().Select(NotificationRecipient.User).ToList(), values,
            IpResource.Link(record.Id)), transactionContext, ct).ConfigureAwait(false);
    }
}
