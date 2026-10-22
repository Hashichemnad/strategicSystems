using Strategy.Platform.Http.Modules;
using Strategy.Platform.Identity;
using Strategy.Platform.Organisation;

namespace Strategy.Host;

/// <summary>Platform services with their own endpoints, always on (ADR 0010). Registered before the modules.</summary>
internal static class PlatformCatalog
{
    public static readonly IReadOnlyList<IPlatformComponent> All = [new OrganisationComponent(), new IdentityComponent()];
}
