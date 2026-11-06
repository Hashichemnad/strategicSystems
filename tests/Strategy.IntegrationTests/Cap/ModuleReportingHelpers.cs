using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Cap;

/// <summary>Calls a module's reporting sources directly (Phase 17), as the reporting engine does: system request context, explicit principal.</summary>
internal static class ModuleReportingHelpers
{
    /// <summary>The claims the sign-in issues for an internal user.</summary>
    public static ClaimsPrincipal Principal(TestUser user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.UserName),
        new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
        new Claim(StrategyClaims.EmployeeId, user.EmployeeId!.Value.ToString()),
    ], "test"));

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Facts of <paramref name="metrics"/> (all when empty) for the current month; <paramref name="user"/> null = the cache.</summary>
    public static Task<IReadOnlyList<MetricFact>> FactsAsync(ApiFactory factory, string system, DateOnly today, TestUser? user, params string[] metrics) =>
        FactsAsync(factory, system, today, user, MonthOf(today), MonthOf(today), metrics);

    /// <summary>Facts for the months <paramref name="from"/> – <paramref name="to"/>, today being <paramref name="today"/>.</summary>
    public static async Task<IReadOnlyList<MetricFact>> FactsAsync(
        ApiFactory factory, string system, DateOnly today, TestUser? user, DateOnly from, DateOnly to, params string[] metrics)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == system);
        var month = MonthOf(today);
        var wanted = metrics.Length == 0 ? source.Metrics.Select(m => m.Code).ToArray() : metrics;
        var computation = new MetricComputation(wanted, MonthOf(from), MonthOf(to), month, today, user is null ? null : Principal(user));
        var facts = await source.ComputeAsync(computation, CancellationToken.None);
        foreach (var fact in facts)
        {
            wanted.ShouldContain(fact.Metric);
            var definition = source.Metrics.Single(m => m.Code == fact.Metric);
            fact.Dimensions.Keys.ShouldBeSubsetOf(definition.Dimensions, $"{fact.Metric} declares every dimension it emits");
            computation.InRange(fact.Month).ShouldBeTrue();
            if (definition.Kind == MetricKind.Stock)
            {
                fact.Month.ShouldBe(month, "stock metrics are stamped on the current month");
            }
        }

        return facts;
    }

    public static async Task<IReadOnlyCollection<string>> PartitionsAsync(ApiFactory factory, string system, TestUser user)
    {
        await using var scope = TestAccess.SystemScope(factory);
        return await scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == system)
            .VisiblePartitionsAsync(Principal(user), CancellationToken.None);
    }

    public static async Task<IReadOnlyList<ReportingListItem>> ListAsync(ApiFactory factory, string system, string code, TestUser user, DateOnly today, int take = 50)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var source = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == system);
        source.Lists.ShouldContain(l => l.Code == code);
        return await source.GetAsync(code, new ReportingListQuery(today.AddMonths(-12), today, today, null, take), Principal(user), CancellationToken.None);
    }

    public static async Task<ReportResult> RunAsync(
        ApiFactory factory, string system, TestUser user, string dataset, IReadOnlyList<string> columns, IReadOnlyList<ReportFilter>? filters = null,
        IReadOnlyList<string>? groupBy = null, IReadOnlyList<ReportSort>? sort = null)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var source = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == system);
        var definition = source.Datasets.Single(d => d.Code == dataset);
        columns.Concat(groupBy ?? []).ShouldAllBe(c => definition.Fields.Any(f => f.Code == c));
        return await source.RunAsync(new ReportQuery(dataset, columns, filters ?? [], groupBy ?? [], sort ?? [], 0, 200), Principal(user), CancellationToken.None);
    }

    public static async Task<IReadOnlyList<MyWorkCard>> CardsAsync(ApiFactory factory, string system, TestUser user)
    {
        await using var scope = TestAccess.SystemScope(factory);
        return await scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == system).GetCardsAsync(Principal(user), CancellationToken.None);
    }

    public static decimal Sum(this IEnumerable<MetricFact> facts, string metric, Func<MetricFact, bool>? where = null) =>
        facts.Where(f => f.Metric == metric && (where?.Invoke(f) ?? true)).Sum(f => f.Value);

    public static decimal Weight(this IEnumerable<MetricFact> facts, string metric) => facts.Where(f => f.Metric == metric).Sum(f => f.Weight);

    public static bool Has(this MetricFact fact, string dimension, string value) => fact.Dimensions.TryGetValue(dimension, out var v) && v == value;

    public static int Count(this MyWorkCard card, string key) => card.Counts.SingleOrDefault(c => c.Key == key)?.Value ?? 0;
}
