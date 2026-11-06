using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Kpi.Demo;

/// <summary>
/// Demo pack (ADR 0153): two demo strategic objectives, four quarterly KPI cards (one per demo department), their annual plans for the current
/// year, and results for the periods that have started: submitted by the KPI owners with an evidence file and approved by the Strategy Office —
/// the latest one left pending, so the approval queue and My Work are not empty. Removal: pending results recalled, cards and objectives
/// deactivated (approved results stay as history of inactive cards).
/// </summary>
internal sealed class KpiDemoPack : IDemoPackContributor
{
    public const string CardType = "kpi.card";
    private const string ObjectiveType = "kpi.element";
    private const string ResultType = "kpi.result";

    private sealed record CardSeed(string Key, string NameAr, string NameEn, string Owner, string Unit, int Objective, decimal Target, decimal[] Achieved);

    private static readonly CardSeed[] Seeds =
    [
        new("kpi.card.1", "نسبة الخدمات الرقمية المتاحة (عرض)", "Share of services available digitally (demo)", "analyst", "strategy", 0, 100, [22, 25, 27, 30]),
        new("kpi.card.2", "عدد المبادرات المنجزة في موعدها (عرض)", "Initiatives delivered on time (demo)", "pm", "pmo", 0, 40, [8, 11, 9, 12]),
        new("kpi.card.3", "نسبة الالتزام بالميزانية المعتمدة (عرض)", "Budget adherence (demo)", "financeHead", "finance", 1, 100, [24, 26, 23, 25]),
        new("kpi.card.4", "عدد ملاحظات التدقيق المغلقة (عرض)", "Audit findings closed (demo)", "auditHead", "audit", 1, 60, [10, 18, 14, 16]),
    ];

    public string Code => "kpi";

    public int Order => 30;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("director").ConfigureAwait(false);
        // Top-level demo objectives through the strategy catalogue (as the Development seed does); the cards link to them.
        StrategicObjective[] demoObjectives =
        [
            new("demo-excellence", "تميز مؤسسي مستدام (عرض)", "Sustainable institutional excellence (demo)", true, 900),
            new("demo-efficiency", "كفاءة مالية وتشغيلية (عرض)", "Financial and operational efficiency (demo)", true, 910),
        ];
        await context.RunAsync(services => services.GetRequiredService<IKpiStrategyCatalog>().EnsureObjectivesAsync(demoObjectives, cancellationToken))
            .ConfigureAwait(false);
        var elements = (await context.SendAsync(new ListKpiElements(), cancellationToken).ConfigureAwait(false)).Require("elements");
        var objectives = new List<Guid>();
        foreach (var objective in demoObjectives)
        {
            var id = elements.Single(e => e.Code == objective.Code && e.Kind == ElementKinds.Objective).Id;
            objectives.Add(id);
            await context.TrackAsync(ObjectiveType, id, "kpi.objective." + objective.Code, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var cards = new List<(CardSeed Seed, KpiCardDto Card)>();
        foreach (var seed in Seeds)
        {
            var owner = context.Person(seed.Owner);
            var values = new CardValues(seed.NameAr, seed.NameEn, null, null, "Demo: " + seed.NameEn, null, null, "strategic", null, null, "quantitative", null,
                "cumulative", false, false, "number", MeasuringPeriods.Quarterly, Directions.Increase, TargetTypes.SingleValue, null, false, objectives[seed.Objective],
                null, null, null, null, null, null, context.Units[seed.Unit], context.Units[seed.Unit], owner.EmployeeId, null, null, null, "department", null,
                null, null, null, null, null, null, null, null);
            var card = (await context.SendAsync(new CreateKpiCard(new KpiCardInput(null, values)), cancellationToken).ConfigureAwait(false)).Require("card " + seed.NameEn);
            await context.TrackAsync(CardType, card.Id, seed.Key, cancellationToken: cancellationToken).ConfigureAwait(false);
            cards.Add((seed, card));
        }

        var year = context.Today.Year;
        (await context.SendAsync(new StartKpiYear(year, cards.Select(c => new KpiYearItemInput(c.Card.Id, true, MeasuringPeriods.Quarterly, "number",
            Directions.Increase, TargetTypes.SingleValue, c.Seed.Target, null, null, false)).ToList()), cancellationToken).ConfigureAwait(false)).Require("start year");
        var cardIds = cards.Select(c => c.Card.Id).ToHashSet();
        var plans = (await context.SendAsync(new ListKpiPlans(year, null, null), cancellationToken).ConfigureAwait(false)).Require("plans")
            .Where(p => cardIds.Contains(p.CardId)).ToList();
        (await context.SendAsync(new ConfirmKpiPlans(plans.Select(p => p.Id).ToList()), cancellationToken).ConfigureAwait(false)).Require("confirm plans");

        foreach (var plan in plans)
        {
            var seed = cards.Single(c => c.Card.Id == plan.CardId).Seed;
            await context.ActAsAsync("director").ConfigureAwait(false);
            var periods = (await context.SendAsync(new GetKpiPlan(plan.Id), cancellationToken).ConfigureAwait(false)).Require("plan").Periods
                .Where(p => p.PeriodStart <= context.Today).OrderBy(p => p.PeriodStart).ToList();
            for (var i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                await context.ActAsAsync("director").ConfigureAwait(false);
                var result = (await context.SendAsync(new GetKpiResult(period.ResultId), cancellationToken).ConfigureAwait(false)).Require("result");
                if (!ResultStatuses.IsOpenForUpdate(result.Status))
                {
                    result = (await context.SendAsync(new OpenKpiResultNow(period.ResultId), cancellationToken).ConfigureAwait(false)).Require("open result");
                }

                await context.ActAsAsync(seed.Owner).ConfigureAwait(false);
                using (var evidence = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n")))
                {
                    var resultId = period.ResultId;
                    (await context.RunAsync(services => services.GetRequiredService<IFileService>().UploadAsync(new FileUpload(KpiFilePurposes.Evidence.Key,
                        resultId.ToString("D"), "demo-evidence.pdf", evidence, "Demo evidence"), cancellationToken)).ConfigureAwait(false)).Require("evidence");
                }

                var achieved = seed.Achieved[i % seed.Achieved.Length];
                var submitted = (await context.SendAsync(new SubmitKpiResult(period.ResultId, new KpiEntryInput(achieved, null, null,
                    "تقدم وفق الخطة (عرض) / On plan (demo)", "متابعة شهرية (عرض) / Monthly follow-up (demo)", "Demo result", false, null), null, result.Version),
                    cancellationToken).ConfigureAwait(false)).Require("submit result");
                await context.TrackAsync(ResultType, submitted.Id, actor: seed.Owner, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (i < periods.Count - 1)
                {
                    await context.ActAsAsync("director").ConfigureAwait(false);
                    (await context.SendAsync(new DecideKpiResult(period.ResultId, "approve", "Approved (demo)", Checklist), cancellationToken).ConfigureAwait(false))
                        .Require("approve result");
                }
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, bool> Checklist = new Dictionary<string, bool>
    {
        ["evidence-readable"] = true,
        ["evidence-supports"] = true,
        ["equation-followed"] = true,
        ["correct-period"] = true,
        ["justification-adequate"] = true,
    };

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(ResultType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync(record).ConfigureAwait(false);
            var result = await context.SendAsync(new GetKpiResult(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value.Status == ResultStatuses.PendingApproval)
            {
                (await context.SendAsync(new RecallKpiResult(record.ResourceId, result.Value.Version), cancellationToken).ConfigureAwait(false)).Require("recall result");
            }
        }

        await context.ActAsAsync("director").ConfigureAwait(false);
        foreach (var record in await context.TrackedAsync(CardType, cancellationToken).ConfigureAwait(false))
        {
            var card = await context.SendAsync(new GetKpiCard(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (card.IsSuccess && card.Value.Status == CardStatuses.Active)
            {
                (await context.SendAsync(new SetKpiCardStatus(record.ResourceId, false, card.Value.Version), cancellationToken).ConfigureAwait(false))
                    .Require("deactivate card");
            }
        }

        // Top-level objectives (created through the strategy catalogue) are deactivated through the domain, tracked and audited like any edit.
        var objectiveIds = (await context.TrackedAsync(ObjectiveType, cancellationToken).ConfigureAwait(false)).Select(r => r.ResourceId).ToList();
        await context.RunAsync(async services =>
        {
            var db = services.GetRequiredService<KpiDbContext>();
            foreach (var element in await db.Elements.Where(e => objectiveIds.Contains(e.Id) && e.Active).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                element.Edit(element.Code, element.NameAr, element.NameEn, element.DescriptionAr, element.DescriptionEn, element.ParentId, element.Weight, element.Sort,
                    false, element.CycleStartYear, element.CycleEndYear);
            }

            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
