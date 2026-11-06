using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Portfolio.Demo;

/// <summary>
/// Demo pack (ADR 0153): two program requests linked to the demo strategic objective (from the KPI demo) — one submitted by a member of staff
/// and in approval (the demo PM Office sees it in its queue), one draft. Removal: both cancelled by their requester with a reason.
/// </summary>
internal sealed class PortfolioDemoPack : IDemoPackContributor
{
    private const string RequestType = "portfolio.request";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    public string Code => "portfolio";

    public int Order => 50;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        await context.ActAsAsync("analyst").ConfigureAwait(false);
        var submitted = await CreateAsync(context, "منصة الخدمات الرقمية الموحدة (عرض)", "Unified digital services platform (demo)", cancellationToken).ConfigureAwait(false);
        (await context.SendAsync(new SubmitRequest(submitted.Id, submitted.Version, "Demo submission"), cancellationToken).ConfigureAwait(false)).Require("submit request");

        await context.ActAsAsync("pm").ConfigureAwait(false);
        await CreateAsync(context, "برنامج تطوير القيادات (مسودة عرض)", "Leadership development programme (demo draft)", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RequestDto> CreateAsync(DemoPackContext context, string nameAr, string nameEn, CancellationToken ct)
    {
        var year = context.Today.Year;
        var values = new RequestValues
        {
            NameAr = nameAr,
            NameEn = nameEn,
            Description = "Demo: " + nameEn,
            Justifications = "Demo: customers and staff need a better service.",
            StrategicObjectiveCode = "demo-excellence",
            SuggestionSourceCode = "idea",
            ProgramTypeCode = "digital",
            RequiresBudget = false,
            ExpectedStartDate = new DateOnly(year + 1, 1, 1),
            ExpectedEndDate = new DateOnly(year + 2, 6, 30),
            ProgramNature = ProgramNatures.New,
            ScopeOfWork = "Demo scope of work.",
            ScreenCoreWork = true,
            ScreenOneTime = true,
            ScreenTimeBound = true,
            ScreenUniqueOutput = true,
            ScreenMinCost = true,
            ScreenOperationalCosts = true,
            PriorityScores = new Dictionary<string, int> { ["vision-alignment"] = 5, ["impact"] = 4, ["complexity"] = 3, ["radical-change"] = 4 },
            MainOptionCode = "contracting",
            PracticeService = "Demo service",
        };
        var created = (await context.SendAsync(new CreateRequest(values, "demo"), ct).ConfigureAwait(false)).Require("request " + nameEn);
        await context.TrackAsync(RequestType, created.Id, cancellationToken: ct).ConfigureAwait(false);
        return created;
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(RequestType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync(record).ConfigureAwait(false);
            var request = await context.SendAsync(new GetRequest(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (request.IsFailure || request.Value.Status is RequestStatuses.Cancelled or RequestStatuses.Rejected or RequestStatuses.Approved)
            {
                continue;
            }

            // The requester cancels a draft; a request in approval is cancelled by the PM Office (the demo Director General holds it).
            if (request.Value.Status != RequestStatuses.Draft)
            {
                await context.ActAsAsync("director").ConfigureAwait(false);
            }

            (await context.SendAsync(new CancelRequest(record.ResourceId, Reason, request.Value.Version), cancellationToken).ConfigureAwait(false))
                .Require("cancel request");
        }
    }
}
