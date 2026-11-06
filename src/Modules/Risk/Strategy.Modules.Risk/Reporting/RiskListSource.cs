using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>
/// Dashboard lists: Top-10 (the dashboard's ranking), risks outside appetite and risks in the framework's highest band — active risks the viewer
/// may read (<see cref="RiskReader"/>), linking to the risk page.
/// </summary>
internal sealed class RiskListSource(RiskReader reader, FrameworkStore frameworks) : IReportingListSource
{
    public const string Top10List = "risk.top10";
    public const string OutsideAppetiteList = "risk.outside-appetite";
    public const string CriticalList = "risk.critical";

    public string SystemCode => RiskPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(Top10List, RiskPermissions.SystemCode, "أهم المخاطر المؤسسية", "Top institutional risks", RiskPermissions.Read),
        new(OutsideAppetiteList, RiskPermissions.SystemCode, "مخاطر خارج الرغبة في المخاطر", "Risks outside appetite", RiskPermissions.Read),
        new(CriticalList, RiskPermissions.SystemCode, "المخاطر في أعلى نطاق تصنيف", "Risks in the highest rating band", RiskPermissions.Read),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Lists.All(l => l.Code != code))
        {
            return [];
        }

        var active = (await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => r.Status == RiskStatuses.Active);
        if (query.UnitIds is { Count: > 0 } units)
        {
            var ids = units.ToList();
            active = active.Where(r => ids.Contains(r.UnitId));
        }

        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        var bands = scorer.Content.Bands.OrderByDescending(b => b.Min).ToList();
        var take = Math.Clamp(query.Take, 1, 100);
        List<RiskRecord> risks;
        switch (code)
        {
            case Top10List:
                take = Math.Min(take, (await reader.SettingsAsync(cancellationToken).ConfigureAwait(false)).TopCount);
                risks = await active.OrderByDescending(r => r.Top10).ThenBy(r => r.Top10Rank ?? int.MaxValue).ThenByDescending(r => r.ResidualScore)
                    .ThenByDescending(r => r.InherentScore).ThenBy(r => r.Number).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
                break;
            case OutsideAppetiteList:
                risks = await active.Where(r => r.WithinAppetite == false).OrderByDescending(r => r.ResidualScore).ThenBy(r => r.Number).Take(take)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                var highest = bands.Count > 0 ? bands[0].Code : string.Empty;
                risks = await active.Where(r => r.ResidualBand == highest).OrderByDescending(r => r.ResidualScore).ThenBy(r => r.Number).Take(take)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                break;
        }

        return risks.Select(r =>
        {
            var index = bands.FindIndex(b => b.Code == r.ResidualBand);
            var band = index >= 0 ? bands[index] : null;
            var tone = index switch { 0 => "danger", 1 => "warning", _ => "info" };
            return new ReportingListItem(RiskRecord.EntityType, r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status, tone, r.ResidualScore, r.NextReviewOn, r.UnitId,
                $"/risk/risks/{r.Id:D}", band?.LabelAr ?? band?.LabelEn ?? r.ResidualBand, band?.LabelEn ?? band?.LabelAr ?? r.ResidualBand);
        }).ToList();
    }
}
