using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.AI;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>
/// Strategic objectives read from the KPI strategy hierarchy (ADR 0075). The KPI contract is optional at run time: with the KPI module switched
/// off there are no objectives (the field stays optional).
/// </summary>
internal sealed class StrategicObjectives(IServiceProvider services)
{
    private IKpiStrategyCatalog? Catalog => services.GetService<IKpiStrategyCatalog>();

    public async Task<IReadOnlyList<StrategicObjective>> ListAsync(bool includeInactive, CancellationToken ct) =>
        Catalog is { } catalog ? await catalog.ListObjectivesAsync(includeInactive, ct).ConfigureAwait(false) : [];

    public async Task<bool> IsActiveAsync(string code, CancellationToken ct) =>
        (await ListAsync(false, ct).ConfigureAwait(false)).Any(o => string.Equals(o.Code, code, StringComparison.Ordinal));

    public async Task<IReadOnlyList<PortfolioOptionDto>> OptionsAsync(CancellationToken ct) =>
        (await ListAsync(false, ct).ConfigureAwait(false)).Select(o => new PortfolioOptionDto(o.Code, o.NameAr ?? o.NameEn, o.NameEn ?? o.NameAr)).ToList();

    /// <summary>The same shape the request prompts used with the old lookup.</summary>
    public async Task<IReadOnlyList<LookupValueReference>> AsLookupValuesAsync(CancellationToken ct) =>
        (await ListAsync(false, ct).ConfigureAwait(false)).Select(o => new LookupValueReference(Guid.Empty, o.Code, o.NameAr, o.NameEn, o.Sort, o.Active, null)).ToList();

    public async Task<AiFieldChoices> ChoicesAsync(CancellationToken ct)
    {
        var values = await ListAsync(false, ct).ConfigureAwait(false);
        return new AiFieldChoices(values.Select(v => v.Code).ToList(),
            values.Count == 0 ? null : "Codes: " + string.Join("; ", values.Select(v => $"{v.Code} = {v.NameEn} / {v.NameAr}")));
    }
}

/// <summary>
/// ADR 0075 lookup migration: copies every Phase 8 <c>portfolio.strategic-objective</c> lookup value missing from the KPI hierarchy into it (same
/// code, labels, sort, active flag). Idempotent; stored request / project codes are unchanged.
/// </summary>
internal sealed class ObjectivesToKpiSeeder : IDatabaseSeeder
{
    public const string LegacyLookupType = "portfolio.strategic-objective";

    public int Order => 900;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<IKpiStrategyCatalog>() is not { } catalog)
        {
            return;
        }

        var legacy = await services.GetRequiredService<ILookupService>().GetValuesAsync(LegacyLookupType, includeInactive: true, cancellationToken).ConfigureAwait(false);
        if (legacy.Count > 0)
        {
            await catalog.EnsureObjectivesAsync(legacy.Select(v => new StrategicObjective(v.Code, v.LabelAr, v.LabelEn, v.Active, v.Sort)).ToList(), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
