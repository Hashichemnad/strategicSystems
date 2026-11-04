using Strategy.Modules.Kpi;
using Strategy.Modules.Portfolio;
using Strategy.Modules.Sample;
using Strategy.Modules.Tasks;
using Strategy.Modules.Teams;
using Strategy.Platform.Http.Modules;

namespace Strategy.Host;

/// <summary>Every system compiled into the product. Whether a system is enabled is configuration, not this list.</summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new SampleModule(),
        new TasksModule(),
        new TeamsModule(),
        new PortfolioModule(),
        new KpiModule(),
    ];
}
