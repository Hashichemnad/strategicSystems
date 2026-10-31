using Strategy.Platform.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Identity;
using Strategy.Platform.Organisation;
using Strategy.Platform.Settings;

namespace Strategy.Host;

/// <summary>
/// Platform services with their own endpoints, always on (ADR 0010). Registered before the modules. The order is the migration
/// order: <c>access</c> before <c>identity</c>, whose Phase 2 migration hands the old administrator flag to Access (ADR 0016).
/// </summary>
internal static class PlatformCatalog
{
    public static readonly IReadOnlyList<IPlatformComponent> All =
        [new SettingsComponent(), new AccessComponent(), new OrganisationComponent(), new IdentityComponent()];
}
