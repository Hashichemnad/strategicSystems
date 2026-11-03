using Strategy.Platform.AI;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Calendar;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Identity;
using Strategy.Platform.Mcp;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Host;

/// <summary>
/// Platform services with their own endpoints, always on (ADR 0010). Registered before the modules. The order is the migration
/// order: <c>audit</c> first (later migrations stage older records into it, ADR 0018), <c>access</c> before <c>identity</c>, whose Phase 2 migration hands the old administrator flag to Access (ADR 0016).
/// </summary>
internal static class PlatformCatalog
{
    public static readonly IReadOnlyList<IPlatformComponent> All =
        [new AuditComponent(), new SchedulingComponent(), new FilesComponent(), new SettingsComponent(), new NotificationsComponent(), new AccessComponent(), new OrganisationComponent(), new IdentityComponent(), new CalendarComponent(), new WorkflowComponent(), new AiComponent(), new McpComponent()];
}
