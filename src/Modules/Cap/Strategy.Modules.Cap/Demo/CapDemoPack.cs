using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Cap.Demo;

/// <summary>
/// Demo pack (ADR 0153): an excellence-workshop source with four remarks for the demo departments (owners: the department heads), two of
/// them accepted by their owners. Removal: open remarks cancelled by the PM Office with a reason, the source closed.
/// </summary>
internal sealed class CapDemoPack : IDemoPackContributor
{
    private const string SourceType = "cap.source";
    private const string RemarkType = "cap.remark";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    private sealed record Seed(string TextAr, string TextEn, string Type, string Priority, string Unit, string Head, bool Accept);

    private static readonly Seed[] Seeds =
    [
        new("توحيد إجراءات تقديم الخدمات الرقمية (عرض)", "Standardise the digital service procedures (demo)", CapRemarkTypes.Improvement, CapPriorities.Medium, "strategy",
            "strategyHead", true),
        new("تأخر إقفال الحسابات الشهرية (عرض)", "Monthly closing of accounts is late (demo)", CapRemarkTypes.Nonconformity, CapPriorities.Medium, "finance", "financeHead",
            true),
        new("غياب سجل موحد للمخاطر التشغيلية (عرض)", "No single register of operational risks (demo)", CapRemarkTypes.Observation, CapPriorities.Low, "strategy",
            "strategyHead", false),
        new("متابعة توصيات التدقيق السابقة (عرض)", "Follow up earlier audit recommendations (demo)", CapRemarkTypes.Improvement, CapPriorities.Low, "audit", "auditHead",
            false),
    ];

    public string Code => "cap";

    public int Order => 80;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("director").ConfigureAwait(false);
        var source = (await context.SendAsync(new CreateSource(new CapSourceValues("ورشة التميز المؤسسي (عرض)", "Institutional excellence workshop (demo)",
            CapSourceTypes.Workshop, "Demo: findings of the annual excellence workshop", context.Today.AddDays(-30), context.Person("director").EmployeeId, null, null)),
            cancellationToken).ConfigureAwait(false)).Require("source");
        await context.TrackAsync(SourceType, source.Id, "cap.source.workshop", cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var seed in Seeds)
        {
            await context.ActAsAsync("director").ConfigureAwait(false);
            var remark = (await context.SendAsync(new CreateRemark(source.Id, new CapRemarkValues(seed.TextAr, seed.TextEn, null, seed.Type, seed.Priority,
                context.Units[seed.Unit], null, context.Person(seed.Head).EmployeeId)), cancellationToken).ConfigureAwait(false)).Require("remark " + seed.TextEn);
            await context.TrackAsync(RemarkType, remark.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (seed.Accept)
            {
                await context.ActAsAsync(seed.Head).ConfigureAwait(false);
                (await context.SendAsync(new AcceptRemark(remark.Id), cancellationToken).ConfigureAwait(false)).Require("accept remark");
            }
        }
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("director").ConfigureAwait(false);
        foreach (var record in await context.TrackedAsync(RemarkType, cancellationToken).ConfigureAwait(false))
        {
            var remark = await context.SendAsync(new GetRemark(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (remark.IsSuccess && !CapRemarkStatuses.IsFinal(remark.Value.Status))
            {
                (await context.SendAsync(new CancelRemark(record.ResourceId, Reason), cancellationToken).ConfigureAwait(false)).Require("cancel remark");
            }
        }

        foreach (var record in await context.TrackedAsync(SourceType, cancellationToken).ConfigureAwait(false))
        {
            var source = await context.SendAsync(new GetSource(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (source.IsSuccess && source.Value.Status != "closed")
            {
                (await context.SendAsync(new CloseSource(record.ResourceId, true), cancellationToken).ConfigureAwait(false)).Require("close source");
            }
        }
    }
}
