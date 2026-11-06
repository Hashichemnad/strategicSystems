using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.AuditManagement.Demo;

/// <summary>
/// Demo pack (ADR 0153): an issued internal-audit report of the strategy and finance departments with four findings (one already past its
/// expected closing date) and a recommendation each, entered by the demo Audit Office. Removal: the report is cancelled with a reason
/// (its findings and recommendations follow: each cancelled with the same reason).
/// </summary>
internal sealed class AuditDemoPack : IDemoPackContributor
{
    private const string ReportType = "auditmgmt.report";
    private const string FindingType = "auditmgmt.finding";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    private sealed record Seed(string TitleAr, string TitleEn, string Unit, string Contact, string Priority, int DueInDays);

    private static readonly Seed[] Seeds =
    [
        new("ضعف الرقابة على صلاحيات الأنظمة المالية (عرض)", "Weak control of access rights to finance systems (demo)", "finance", "financeHead", AuditPriorities.High, 30),
        new("عدم توثيق مراجعة الموازنة الشهرية (عرض)", "Monthly budget review is not documented (demo)", "finance", "accountant", AuditPriorities.Medium, -10),
        new("تأخر تحديث خطة استمرارية الأعمال (عرض)", "Business continuity plan not updated (demo)", "strategy", "strategyHead", AuditPriorities.Critical, 45),
        new("نقص أدلة قياس مؤشرات الأداء (عرض)", "Missing evidence for KPI measurement (demo)", "strategy", "analyst", AuditPriorities.Low, 60),
    ];

    public string Code => "auditmgmt";

    public int Order => 90;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("auditHead").ConfigureAwait(false);
        var report = (await context.SendAsync(new CreateAuditReport(new AuditReportValues("تقرير تدقيق العمليات المالية والاستراتيجية (عرض)",
            "Audit of financial and strategy operations (demo)", ReportTypes.InternalAudit, "Internal Audit Office (demo)", context.Person("auditor").EmployeeId,
            [context.Units["strategy"], context.Units["finance"]], context.Today.AddMonths(-6), context.Today.AddMonths(-1), context.Today.AddDays(-20),
            [context.Person("director").EmployeeId], null, "Demo report")), cancellationToken).ConfigureAwait(false)).Require("report");
        await context.TrackAsync(ReportType, report.Id, "auditmgmt.report.1", cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var seed in Seeds)
        {
            var finding = new FindingValues(seed.TitleAr, seed.TitleEn, FindingTypes.Finding, "Demo condition: " + seed.TitleEn, "Demo criteria", null,
                "Demo cause", "Demo effect", context.Today.AddDays(-25), context.Units[seed.Unit], seed.Priority, false, null, context.Today.AddDays(seed.DueInDays),
                context.Person("auditor").EmployeeId);
            var recommendation = new RecommendationValues("معالجة الملاحظة وتوثيق الإجراء (عرض)", "Treat the finding and document the procedure (demo)", "agree",
                "Management agrees (demo)", "Demo plan", context.Person(seed.Contact).EmployeeId, context.Today.AddDays(seed.DueInDays), null);
            var created = (await context.SendAsync(new CreateAuditFinding(report.Id, new NewFindingInput(finding, [recommendation])), cancellationToken)
                .ConfigureAwait(false)).Require("finding " + seed.TitleEn);
            await context.TrackAsync(FindingType, created.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        (await context.SendAsync(new IssueAuditReport(report.Id), cancellationToken).ConfigureAwait(false)).Require("issue report");
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("auditHead").ConfigureAwait(false);
        foreach (var record in await context.TrackedAsync(FindingType, cancellationToken).ConfigureAwait(false))
        {
            var finding = await context.SendAsync(new GetAuditFinding(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (finding.IsSuccess && finding.Value.Status != AuditFindingStatuses.Cancelled)
            {
                (await context.SendAsync(new CancelAuditFinding(record.ResourceId, Reason), cancellationToken).ConfigureAwait(false)).Require("cancel finding");
            }
        }

        foreach (var record in await context.TrackedAsync(ReportType, cancellationToken).ConfigureAwait(false))
        {
            var report = await context.SendAsync(new GetAuditReport(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (report.IsSuccess && report.Value.Status is not (ReportStatuses.Cancelled or ReportStatuses.Closed))
            {
                (await context.SendAsync(new CancelAuditReport(record.ResourceId, Reason), cancellationToken).ConfigureAwait(false)).Require("cancel report");
            }
        }
    }
}
