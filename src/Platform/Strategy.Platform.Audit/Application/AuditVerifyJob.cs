using System.Globalization;
using Strategy.Platform.Access;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Notifications;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Audit.Application;

/// <summary>Daily incremental check of the hash chain; a broken chain alerts the users who may verify it (in-app and e-mail).</summary>
internal sealed class AuditVerifyJob(RequestContext requestContext, AuditVerifier verifier, ChainAlert alert) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "audit.verify-chain", "التحقق من سلامة سجل التدقيق", "Verify the audit chain", "0 10 2 * * ?", typeof(AuditVerifyJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var run = await verifier.VerifyAsync(VerificationModes.Incremental, cancellationToken).ConfigureAwait(false);
        await alert.RaiseIfBrokenAsync(run, cancellationToken).ConfigureAwait(false);
        return run.Ok ? $"ok entries={run.EntriesChecked} last={run.LastSeq}" : $"BROKEN at {run.BrokenSeq}: {run.MessageKey}";
    }
}

/// <summary>Notifies the holders of <c>audit.chain.verify</c> when a verification fails.</summary>
internal sealed class ChainAlert(IUserRoleQuery roles, INotificationService notifications)
{
    public static readonly NotificationTemplateDefinition Template = new(
        "audit.chainBroken", AuditComponent.SystemCode, "تنبيه سلامة سجل التدقيق", "Audit chain integrity alert",
        "تنبيه: فشل التحقق من سلامة سجل التدقيق", "Alert: audit chain verification failed",
        "مرحباً {{name}}،\nفشل التحقق من سلسلة سجل التدقيق عند القيد رقم {{seq}} ({{reason}}).\nقد يكون أحد القيود قد عُدّل أو حُذف خارج النظام. افتح شاشة التحقق للمراجعة.\n{{button}}",
        "Hello {{name}},\nThe audit chain verification failed at entry {{seq}} ({{reason}}).\nAn entry may have been changed or removed outside the application. Open the verification screen to review.\n{{button}}",
        "فتح شاشة التحقق", "Open verification", ["name", "seq", "reason"], Mandatory: true);

    public async Task RaiseIfBrokenAsync(VerificationRun run, CancellationToken ct)
    {
        if (run.Ok)
        {
            return;
        }

        var recipients = await roles.UsersWithPermissionAsync(AuditPermissions.ChainVerify, ct).ConfigureAwait(false);
        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(
            Template.Key,
            recipients.Select(NotificationRecipient.User).ToList(),
            new Dictionary<string, string>
            {
                ["seq"] = (run.BrokenSeq ?? 0).ToString(CultureInfo.InvariantCulture),
                ["reason"] = run.MessageKey ?? string.Empty,
            },
            "/audit/verification"), cancellationToken: ct).ConfigureAwait(false);
    }
}
