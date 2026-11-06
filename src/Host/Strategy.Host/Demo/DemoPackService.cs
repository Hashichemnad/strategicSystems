using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Settings;

namespace Strategy.Host.Demo;

/// <summary>Outcome of one contributor in a demo pack run.</summary>
public sealed record DemoPackStepDto(string Code, string Status, int Records, string? ErrorCode);

/// <summary>State of the demo pack: per contributor, the number of records it created that are not removed.</summary>
public sealed record DemoPackStatusDto(bool Loaded, string Version, IReadOnlyList<DemoPackStepDto> Contributors);

/// <summary>
/// Runs the demo pack contributors (ADR 0153): load in ascending order, remove in descending order, each in its own scope with channel
/// <c>import</c>, the reason "demo pack" on every audit entry and an explicit audit event per run. A contributor that already has
/// records is not loaded again (no duplicates); removing twice changes nothing.
/// </summary>
public sealed partial class DemoPackService(IServiceScopeFactory scopes, ILogger<DemoPackService> logger)
{
    public const string Loaded = "loaded";
    public const string Skipped = "skipped";
    public const string Removed = "removed";
    public const string Failed = "failed";

    private const string AuditSystem = "platform";
    private const string Reason = "demo pack";

    public async Task<DemoPackStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDemoRecordStore>();
        var active = await store.ActiveAsync(null, cancellationToken).ConfigureAwait(false);
        var steps = Contributors(scope.ServiceProvider)
            .Select(c => new DemoPackStepDto(c.Code, active.Any(r => r.Contributor == c.Code) ? Loaded : "notLoaded", active.Count(r => r.Contributor == c.Code), null))
            .ToList();
        return new DemoPackStatusDto(active.Count > 0, DemoPackContext.PackVersion, steps);
    }

    public async Task<DemoPackStatusDto> LoadAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var steps = new List<DemoPackStepDto>();
        IReadOnlyList<string> codes;
        await using (var scope = CreateScope())
        {
            codes = Contributors(scope.ServiceProvider).Select(c => c.Code).ToList();
        }

        foreach (var code in codes)
        {
            await using var scope = CreateScope();
            var contributor = Contributors(scope.ServiceProvider).Single(c => c.Code == code);
            var store = scope.ServiceProvider.GetRequiredService<IDemoRecordStore>();
            if ((await store.ActiveAsync(code, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                steps.Add(new DemoPackStepDto(code, Skipped, (await store.ActiveAsync(code, cancellationToken).ConfigureAwait(false)).Count, null));
                continue;
            }

            try
            {
                var context = await CreateContextAsync(scope.ServiceProvider, code, password, cancellationToken).ConfigureAwait(false);
                await contributor.LoadAsync(context, cancellationToken).ConfigureAwait(false);
                await context.ActAsSystemAsync().ConfigureAwait(false);
                steps.Add(new DemoPackStepDto(code, Loaded, (await store.ActiveAsync(code, cancellationToken).ConfigureAwait(false)).Count, null));
            }
            catch (DemoPackException ex)
            {
                LogFailed(logger, code, ex.ErrorCode, ex);
                steps.Add(new DemoPackStepDto(code, Failed, (await store.ActiveAsync(code, cancellationToken).ConfigureAwait(false)).Count, ex.ErrorCode));
                if (code == PlatformDemoPack.ContributorCode)
                {
                    break; // nothing else can be loaded without the demo people
                }
            }
        }

        await AuditAsync("demo.pack.load", steps, cancellationToken).ConfigureAwait(false);
        return new DemoPackStatusDto(steps.Any(s => s.Records > 0), DemoPackContext.PackVersion, steps);
    }

    public async Task<DemoPackStatusDto> RemoveAsync(CancellationToken cancellationToken)
    {
        var steps = new List<DemoPackStepDto>();
        IReadOnlyList<string> codes;
        await using (var scope = CreateScope())
        {
            codes = Contributors(scope.ServiceProvider).Select(c => c.Code).Reverse().ToList();
        }

        foreach (var code in codes)
        {
            await using var scope = CreateScope();
            var contributor = Contributors(scope.ServiceProvider).Single(c => c.Code == code);
            var store = scope.ServiceProvider.GetRequiredService<IDemoRecordStore>();
            var count = (await store.ActiveAsync(code, cancellationToken).ConfigureAwait(false)).Count;
            if (count == 0)
            {
                steps.Add(new DemoPackStepDto(code, Skipped, 0, null));
                continue;
            }

            try
            {
                var context = await CreateContextAsync(scope.ServiceProvider, code, null, cancellationToken).ConfigureAwait(false);
                await contributor.RemoveAsync(context, cancellationToken).ConfigureAwait(false);
                await context.ActAsSystemAsync().ConfigureAwait(false);
                await store.MarkRemovedAsync(code, cancellationToken).ConfigureAwait(false);
                steps.Add(new DemoPackStepDto(code, Removed, count, null));
            }
            catch (DemoPackException ex)
            {
                LogFailed(logger, code, ex.ErrorCode, ex);
                steps.Add(new DemoPackStepDto(code, Failed, count, ex.ErrorCode));
            }
        }

        await AuditAsync("demo.pack.remove", steps, cancellationToken).ConfigureAwait(false);
        return new DemoPackStatusDto(false, DemoPackContext.PackVersion, steps);
    }

    private static List<IDemoPackContributor> Contributors(IServiceProvider services) =>
        services.GetServices<IDemoPackContributor>().OrderBy(c => c.Order).ThenBy(c => c.Code, StringComparer.Ordinal).ToList();

    private AsyncServiceScope CreateScope()
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Import, StrategyClaims.CreateSystemPrincipal());
        scope.ServiceProvider.GetRequiredService<IAuditReason>().Reason = Reason;
        return scope;
    }

    private static async Task<DemoPackContext> CreateContextAsync(IServiceProvider services, string code, string? password, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IDemoRecordStore>();
        var records = await store.ActiveAsync(PlatformDemoPack.ContributorCode, cancellationToken).ConfigureAwait(false);
        var people = PlatformDemoPack.PeopleFrom(records);
        var units = PlatformDemoPack.UnitsFrom(records);
        var requestContext = services.GetRequiredService<RequestContext>();
        var users = services.GetRequiredService<StrategyUserManager>();
        var claims = services.GetRequiredService<IUserClaimsPrincipalFactory<StrategyUser>>();
        var principals = new Dictionary<Guid, ClaimsPrincipal>();

        async Task ActAs(DemoPerson? person)
        {
            if (person is null)
            {
                requestContext.SetUser(StrategyClaims.CreateSystemPrincipal());
                return;
            }

            if (!principals.TryGetValue(person.UserId, out var principal))
            {
                var user = await users.FindByIdAsync(person.UserId.ToString()).ConfigureAwait(false)
                    ?? throw new DemoPackException("demo.personMissing", $"Demo user {person.Key} does not exist.");
                var created = await claims.CreateAsync(user).ConfigureAwait(false);
                var identity = new ClaimsIdentity(created.Claims, "strategy-demo", ClaimTypes.Name, ClaimTypes.Role);
                identity.AddClaim(new Claim(StrategyClaims.AuthMethod, "mfa"));
                principal = new ClaimsPrincipal(identity);
                principals[person.UserId] = principal;
            }

            requestContext.SetUser(principal);
        }

        var today = await TodayAsync(services, cancellationToken).ConfigureAwait(false);
        return new DemoPackContext(code, DemoPackContext.PackVersion, today, store, people, units, ActAs, password);
    }

    private static async Task<DateOnly> TodayAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var regional = await services.GetRequiredService<ISettingsStore>().GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false);
        var utc = services.GetRequiredService<Strategy.Platform.Kernel.Time.IClock>().UtcNow;
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(regional.TimeZone)).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(utc.UtcDateTime);
        }
    }

    private async Task AuditAsync(string action, List<DemoPackStepDto> steps, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().WriteAsync(
            new AuditEvent(action, steps.TrueForAll(s => s.Status != Failed), AuditSystem, "demo-pack", null,
                new { version = DemoPackContext.PackVersion, steps = steps.Select(s => new { s.Code, s.Status, s.Records, s.ErrorCode }) }, Reason),
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo pack contributor {Contributor} failed: {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string contributor, string errorCode, Exception exception);
}
