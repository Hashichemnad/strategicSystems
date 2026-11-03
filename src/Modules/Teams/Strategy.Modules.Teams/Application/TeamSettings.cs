using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Application;

/// <summary>Settings → Teams, general area (docs/design/teams.md §2). Types, roles, templates and matrices are tables (ADR 0041).</summary>
public sealed record TeamSettings(
    int OverAssignedThreshold = 4,
    int ReportReminderDays = 3,
    int EvaluationReminderDays = 5,
    int RecommendationDefaultDueDays = 30,
    int MemberSuggestionLimit = 10,
    bool AllowMemberCertificateDownload = false) : ISettingsArea
{
    public static string Key => "teams";

    public static List<FieldError> Validate(TeamSettings value)
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

        Range("overAssignedThreshold", value.OverAssignedThreshold, 1, 50);
        Range("reportReminderDays", value.ReportReminderDays, 0, 30);
        Range("evaluationReminderDays", value.EvaluationReminderDays, 1, 60);
        Range("recommendationDefaultDueDays", value.RecommendationDefaultDueDays, 1, 365);
        Range("memberSuggestionLimit", value.MemberSuggestionLimit, 1, 50);
        return errors;
    }
}

/// <summary>Bilingual templates of Teams Management (editable in Settings → Notifications).</summary>
public static class TeamNotifications
{
    private static readonly string[] Common = ["name", "teamAr", "teamEn", "number", "actor", "text", "date"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, TeamPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition Activated = T(
        "teams.activated", "تشكيل فريق", "Team formed", "تم تشكيل «{{teamAr}}»", "“{{teamEn}}” has been formed",
        "تم اعتماد تشكيل «{{teamAr}}» ({{number}}) وأنت من أعضائه.", "The formation of “{{teamEn}}” ({{number}}) was approved and you are a member.");

    public static readonly NotificationTemplateDefinition MemberAdded = T(
        "teams.memberAdded", "إضافة عضو", "Added to a team", "أضفت إلى «{{teamAr}}»", "You were added to “{{teamEn}}”",
        "أضافك {{actor}} إلى «{{teamAr}}» ({{number}}) بدور {{text}}.", "{{actor}} added you to “{{teamEn}}” ({{number}}) as {{text}}.");

    public static readonly NotificationTemplateDefinition MemberLeft = T(
        "teams.memberLeft", "انتهاء العضوية", "Membership ended", "انتهت عضويتك في «{{teamAr}}»", "Your membership of “{{teamEn}}” ended",
        "انتهت عضويتك في «{{teamAr}}» ({{number}}) بتاريخ {{date}}. السبب: {{text}}", "Your membership of “{{teamEn}}” ({{number}}) ended on {{date}}. Reason: {{text}}");

    public static readonly NotificationTemplateDefinition FormationReturned = T(
        "teams.formationReturned", "إعادة طلب التشكيل", "Formation request returned", "أُعيد طلب تشكيل «{{teamAr}}»", "Formation request “{{teamEn}}” returned",
        "أعاد {{actor}} طلب التشكيل للتصحيح: {{text}}", "{{actor}} returned the formation request for correction: {{text}}");

    public static readonly NotificationTemplateDefinition FormationRejected = T(
        "teams.formationRejected", "رفض طلب التشكيل", "Formation request rejected", "رُفض طلب تشكيل «{{teamAr}}»", "Formation request “{{teamEn}}” rejected",
        "رفض {{actor}} طلب التشكيل: {{text}}", "{{actor}} rejected the formation request: {{text}}");

    public static readonly NotificationTemplateDefinition EvaluationOpened = T(
        "teams.evaluationOpened", "فتح تقييم فريق", "Team evaluation opened", "تقييم «{{teamAr}}» بانتظارك", "Evaluation of “{{teamEn}}” is waiting for you",
        "فُتحت دورة تقييم «{{teamAr}}» ({{text}}). أكمل تقييم الفريق وأعضائه.", "The evaluation cycle {{text}} of “{{teamEn}}” is open. Please evaluate the team and its members.");

    public static readonly NotificationTemplateDefinition EvaluationReminder = T(
        "teams.evaluationReminder", "تذكير بتقييم فريق", "Team evaluation reminder", "تذكير: تقييم «{{teamAr}}»", "Reminder: evaluation of “{{teamEn}}”",
        "ما زال تقييم «{{teamAr}}» ({{text}}) بانتظارك منذ {{date}}.", "The evaluation {{text}} of “{{teamEn}}” has been waiting for you since {{date}}.");

    public static readonly NotificationTemplateDefinition EvaluationReturned = T(
        "teams.evaluationReturned", "إعادة تقييم فريق", "Team evaluation returned", "أُعيد تقييم «{{teamAr}}»", "Evaluation of “{{teamEn}}” returned",
        "أعاد {{actor}} تقييم «{{teamAr}}» لإعادة النظر: {{text}}", "{{actor}} returned the evaluation of “{{teamEn}}” for re-evaluation: {{text}}");

    public static readonly NotificationTemplateDefinition EvaluationApproved = T(
        "teams.evaluationApproved", "اعتماد تقييم فريق", "Team evaluation approved", "اعتُمد تقييم «{{teamAr}}»", "Evaluation of “{{teamEn}}” approved",
        "اعتُمد تقييم «{{teamAr}}» ({{text}}). حُسبت المكافآت المقترحة.", "The evaluation {{text}} of “{{teamEn}}” was approved. Suggested rewards were calculated.");

    public static readonly NotificationTemplateDefinition RewardsApproved = T(
        "teams.rewardsApproved", "اعتماد المكافآت", "Rewards approved", "اعتُمدت مكافآت «{{teamAr}}»", "Rewards of “{{teamEn}}” approved",
        "اعتُمدت مكافآت تقييم «{{teamAr}}» ({{text}}) وأُصدرت الشهادات.", "The rewards of evaluation {{text}} of “{{teamEn}}” were approved and the certificates issued.");

    public static readonly NotificationTemplateDefinition ReportDue = T(
        "teams.reportDue", "تقرير دوري مستحق", "Periodic report due", "تقرير «{{teamAr}}» يستحق في {{date}}", "Report of “{{teamEn}}” due {{date}}",
        "التقرير الدوري لـ «{{teamAr}}» للفترة {{text}} يستحق في {{date}}.", "The periodic report of “{{teamEn}}” for {{text}} is due {{date}}.");

    public static readonly NotificationTemplateDefinition ReportOverdue = T(
        "teams.reportOverdue", "تقرير دوري متأخر", "Periodic report overdue", "متأخر: تقرير «{{teamAr}}»", "Overdue: report of “{{teamEn}}”",
        "تجاوز التقرير الدوري لـ «{{teamAr}}» للفترة {{text}} موعده {{date}}.", "The periodic report of “{{teamEn}}” for {{text}} passed its due date {{date}}.");

    public static readonly NotificationTemplateDefinition RecommendationAssigned = T(
        "teams.recommendationAssigned", "توصية مسندة", "Recommendation assigned", "توصية من «{{teamAr}}»", "Recommendation from “{{teamEn}}”",
        "أسند إليك {{actor}} التوصية {{number}}: {{text}} (الاستحقاق {{date}}).", "{{actor}} assigned you recommendation {{number}}: {{text}} (due {{date}}).");

    public static readonly NotificationTemplateDefinition MeetingScheduled = T(
        "teams.meetingScheduled", "اجتماع فريق", "Team meeting", "اجتماع «{{teamAr}}» في {{date}}", "“{{teamEn}}” meeting on {{date}}",
        "دعاك {{actor}} إلى اجتماع «{{text}}» بتاريخ {{date}}.", "{{actor}} invited you to the meeting “{{text}}” on {{date}}.");

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All =
    [
        Activated, MemberAdded, MemberLeft, FormationReturned, FormationRejected, EvaluationOpened, EvaluationReminder, EvaluationReturned,
        EvaluationApproved, RewardsApproved, ReportDue, ReportOverdue, RecommendationAssigned, MeetingScheduled,
    ];
}

/// <summary>Sends team notifications to employees (their active users), in the caller's transaction when given.</summary>
internal sealed class TeamNotifier(INotificationService notifications, IUserDirectory users)
{
    public async Task SendAsync(
        NotificationTemplateDefinition template, Team team, IEnumerable<Guid?> employeeIds, string? actor, string? text, string? date, object? transactionContext,
        CancellationToken ct, string? link = null, string? number = null)
    {
        var ids = employeeIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var recipients = (await users.GetByEmployeesAsync(ids, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => NotificationRecipient.User(u.Id)).ToList();
        await SendToUsersAsync(template, team, recipients, actor, text, date, transactionContext, ct, link, number).ConfigureAwait(false);
    }

    public async Task SendToUsersAsync(
        NotificationTemplateDefinition template, Team team, IReadOnlyList<NotificationRecipient> recipients, string? actor, string? text, string? date,
        object? transactionContext, CancellationToken ct, string? link = null, string? number = null)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(
            template.Key,
            recipients,
            new Dictionary<string, string>
            {
                ["teamAr"] = team.NameAr ?? team.NameEn ?? string.Empty,
                ["teamEn"] = team.NameEn ?? team.NameAr ?? string.Empty,
                ["number"] = number ?? team.Number ?? string.Empty,
                ["actor"] = actor ?? string.Empty,
                ["text"] = text ?? string.Empty,
                ["date"] = date ?? string.Empty,
            },
            link ?? $"/teams/{team.Id:D}"), transactionContext, ct).ConfigureAwait(false);
    }
}
