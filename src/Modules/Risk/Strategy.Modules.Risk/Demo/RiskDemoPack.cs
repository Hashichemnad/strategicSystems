using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Demo;

/// <summary>
/// Demo pack (ADR 0153): six institutional risks across the demo departments with different ratings — four registered (submitted by their
/// owners and approved by the Risk Office), two awaiting the Risk Office — so the register, heat-map and Top-10 are filled.
/// Removal: active risks closed through the closure approval ("no longer relevant"), pending ones rejected by the Risk Office (a draft
/// would be discarded: the module's audited hard delete of a never-submitted draft).
/// </summary>
internal sealed class RiskDemoPack : IDemoPackContributor
{
    private const string RiskType = "risk.risk";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    private sealed record Seed(string Owner, string Unit, string TitleAr, string TitleEn, string Category, int L, int I, int RL, int RI, string State);

    private static readonly Seed[] Seeds =
    [
        new("analyst", "strategy", "تأخر التحول الرقمي للخدمات (عرض)", "Delay in the digital transformation of services (demo)", "strategic", 4, 5, 3, 4, "active"),
        new("accountant", "finance", "تجاوز الميزانية التشغيلية (عرض)", "Operating budget overrun (demo)", "financial", 3, 4, 3, 3, "active"),
        new("pm", "pmo", "نقص الكفاءات في إدارة المشاريع (عرض)", "Shortage of project management skills (demo)", "operational", 3, 3, 2, 3, "active"),
        new("auditor", "audit", "عدم معالجة ملاحظات التدقيق في موعدها (عرض)", "Audit findings not treated on time (demo)", "operational", 4, 3, 3, 3, "active"),
        new("financeHead", "finance", "تقلب أسعار الموردين (عرض)", "Supplier price volatility (demo)", "financial", 2, 4, 2, 3, "submitted"),
        new("strategyHead", "strategy", "انقطاع الأنظمة الحرجة (عرض)", "Outage of critical systems (demo)", "operational", 2, 5, 2, 4, "submitted"),
    ];

    public string Code => "risk";

    public int Order => 40;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var seed in Seeds)
        {
            // Registered by the department's risk coordinator (the department head), owned by the member of staff.
            await context.ActAsAsync(seed.Unit switch { "finance" => "financeHead", "audit" => "auditHead", _ => "strategyHead" }).ConfigureAwait(false);
            var values = new RiskValues(seed.TitleAr, seed.TitleEn, "Demo: cause of " + seed.TitleEn, "Demo: " + seed.TitleEn + " occurs",
                "Demo: services and objectives are affected", null, context.Today.AddMonths(-2), null, RiskTypes.Internal, [seed.Category], false, false,
                context.Units[seed.Unit], context.Person(seed.Owner).EmployeeId, null, null, null, null, seed.L, seed.I, null, seed.RL, seed.RI, 1, 2,
                context.Today.AddMonths(9), "reduce", true);
            var created = await context.SendAsync(new CreateRisk(values), cancellationToken).ConfigureAwait(false);
            if (created.IsFailure && created.Error.Fields?.Any(f => f.Field.StartsWith("categoryCodes", StringComparison.Ordinal)) == true)
            {
                created = await context.SendAsync(new CreateRisk(values with { CategoryCodes = ["operational"] }), cancellationToken).ConfigureAwait(false);
            }

            var risk = created.Require("risk " + seed.TitleEn);
            await context.TrackAsync(RiskType, risk.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (seed.State == "draft")
            {
                continue;
            }

            (await context.SendAsync(new SubmitRisk(risk.Id, "Demo submission"), cancellationToken).ConfigureAwait(false)).Require("submit risk");
            if (seed.State == "active")
            {
                await context.ActAsAsync("director").ConfigureAwait(false);
                await DecideUntilDoneAsync(context, RiskProcesses.Review, risk.Id, ApprovalActions.Approve, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(RiskType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync("director").ConfigureAwait(false);
            var risk = await context.SendAsync(new GetRisk(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (risk.IsFailure || risk.Value.Status is RiskStatuses.Closed or RiskStatuses.Rejected)
            {
                continue;
            }

            switch (risk.Value.Status)
            {
                case RiskStatuses.Draft when risk.Value.PendingProcess is null:
                    await context.ActAsAsync(record).ConfigureAwait(false);
                    var discarded = await context.SendAsync(new DiscardRisk(record.ResourceId), cancellationToken).ConfigureAwait(false);
                    if (discarded.IsFailure)
                    {
                        // Submitted once and returned / recalled: no longer discardable — it stays a withdrawn draft of a departed demo owner.
                        continue;
                    }

                    break;
                case RiskStatuses.UnderReview:
                    await DecideUntilDoneAsync(context, risk.Value.PendingProcess ?? RiskProcesses.Review, record.ResourceId, ApprovalActions.Reject, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case RiskStatuses.Active:
                    if (risk.Value.PendingProcess is { } pending)
                    {
                        await DecideUntilDoneAsync(context, pending, record.ResourceId, ApprovalActions.Reject, cancellationToken).ConfigureAwait(false);
                    }

                    // Requested by the risk owner, approved by the Risk Office (nobody approves their own request).
                    var owner = context.People.Values.FirstOrDefault(p => p.EmployeeId == risk.Value.Values.OwnerEmployeeId);
                    await context.ActAsAsync(owner?.Key ?? "strategyHead").ConfigureAwait(false);
                    (await context.SendAsync(new RequestRiskClosure(record.ResourceId, "no-longer-relevant", Reason), cancellationToken).ConfigureAwait(false))
                        .Require("request closure");
                    await context.ActAsAsync("director").ConfigureAwait(false);
                    await DecideUntilDoneAsync(context, RiskProcesses.Closure, record.ResourceId, ApprovalActions.Approve, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>Decides every pending step of the process as the current person (the demo office holds every step's role).</summary>
    private static async Task DecideUntilDoneAsync(DemoPackContext context, string processKey, Guid id, string action, CancellationToken cancellationToken)
    {
        for (var step = 0; step < 5; step++)
        {
            var decided = await context.RunAsync(services => services.GetRequiredService<IApprovalEngine>()
                .DecideAsync(processKey, id, action, action == ApprovalActions.Approve ? "Approved (demo)" : Reason, cancellationToken)).ConfigureAwait(false);
            if (decided.IsFailure)
            {
                if (step == 0)
                {
                    decided.Require($"{action} {processKey}");
                }

                return;
            }

            if (decided.Value.InstanceStatus != ApprovalInstanceStatuses.InProgress)
            {
                return;
            }
        }
    }
}
