using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Domain;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.Application;

public sealed record MyWorkCardDto(
    string Code, string SystemCode, string TitleKey, IReadOnlyList<MyWorkCount> Counts, IReadOnlyList<MyWorkCardItem> Items, string Link, bool Hidden, string State);

public sealed record MyWorkLayoutItem(string Code, bool Hidden);

public sealed record MyWorkCardsDto(IReadOnlyList<MyWorkCardDto> Cards);

/// <summary>
/// My Work cards (SPEC §0.4 level 1, ADR 0149): sources of systems the user may open, in parallel scopes with a time-out; the user's order and
/// hidden cards applied. A failing source gives an "unavailable" card, never a broken page.
/// </summary>
public sealed partial class MyWorkCardsService(
    IEnumerable<IMyWorkCardSource> sources, IUserRoleQuery roles, ReportingDbContext db, IServiceProvider services, IRequestContext context,
    ILogger<MyWorkCardsService> logger)
{
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(8);

    public async Task<MyWorkCardsDto> GetAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return new MyWorkCardsDto([]);
        }

        var open = await roles.OpenSystemsAsync(userId, user.EmployeeIdOf(), ct).ConfigureAwait(false);
        // Notifications are not a system in the access catalogue: every signed-in user has them.
        var types = sources.Where(s => open.Contains(s.SystemCode) || s.SystemCode == "notifications").Select(s => s.GetType()).Distinct().ToList();
        var tasks = types.OrderBy(t => SourceOrder(sources.First(s => s.GetType() == t).SystemCode)).Select(type => RunAsync(type, user, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var layout = await LayoutAsync(userId, ct).ConfigureAwait(false);
        var order = layout.Select((item, index) => (item.Code, index)).ToDictionary(p => p.Code, p => p.index, StringComparer.Ordinal);
        var hidden = layout.Where(l => l.Hidden).Select(l => l.Code).ToHashSet(StringComparer.Ordinal);
        var cards = results.SelectMany(r => r)
            .Select((c, index) => (Card: c, Index: index))
            .OrderBy(p => order.TryGetValue(p.Card.Code, out var i) ? i : 1000 + p.Index)
            .Select(p => p.Card)
            .Select(c => c with { Hidden = hidden.Contains(c.Code) })
            .ToList();
        return new MyWorkCardsDto(cards);
    }

    private async Task<IReadOnlyList<MyWorkCardDto>> RunAsync(Type type, ClaimsPrincipal user, CancellationToken ct)
    {
        // Each source in its own scope (DbContexts are not thread-safe).
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(services);
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(context.CorrelationId, context.Channel, user);
        var source = scope.ServiceProvider.GetServices<IMyWorkCardSource>().First(s => s.GetType() == type);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceTimeout);
        try
        {
            var cards = await source.GetCardsAsync(user, timeout.Token).ConfigureAwait(false);
            return cards.OrderBy(c => c.Sort).Select(c => new MyWorkCardDto(c.Code, c.SystemCode, c.TitleKey, c.Counts, c.Items.Take(5).ToList(), c.Link, false, "ok"))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailed(logger, ex, source.SystemCode);
            return [new MyWorkCardDto(source.SystemCode + ".unavailable", source.SystemCode, "myWork.cards.unavailable", [], [], "/", false, "unavailable")];
        }
    }

    /// <summary>Default card order (SPEC §0.4 level 1): approvals and tasks first, notifications and pins last.</summary>
    private static int SourceOrder(string system) => Array.IndexOf(DefaultOrder, system) is var i and >= 0 ? i : DefaultOrder.Length;

    private static readonly string[] DefaultOrder =
        ["workflow", "tasks", "kpi", "risk", "cap", "portfolio", "teams", "requests", "auditmgmt", "notifications", ReportingPermissions.SystemCode];

    public async Task<IReadOnlyList<MyWorkLayoutItem>> LayoutAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.MyWorkLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct).ConfigureAwait(false);
        return row is null ? [] : JsonSerializer.Deserialize<List<MyWorkLayoutItem>>(row.CardsJson, DashboardService.Json) ?? [];
    }

    public async Task<Result> SaveLayoutAsync(ClaimsPrincipal user, IReadOnlyList<MyWorkLayoutItem> items, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        if (items.Count > 100 || items.Any(i => string.IsNullOrWhiteSpace(i.Code) || i.Code.Length > 100))
        {
            return Error.Validation([new FieldError("cards", "reporting.errors.layout", "reporting.errors.layout")]);
        }

        var json = JsonSerializer.Serialize(items.DistinctBy(i => i.Code).ToList(), DashboardService.Json);
        var row = await db.MyWorkLayouts.FirstOrDefaultAsync(l => l.UserId == userId, ct).ConfigureAwait(false);
        if (row is null)
        {
            db.MyWorkLayouts.Add(new MyWorkLayout(userId, json));
        }
        else
        {
            row.Update(json);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "My Work card source of {System} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string system);
}

/// <summary>Reporting's own card: the user's pinned dashboards and reports.</summary>
internal sealed class PinsMyWorkCards(DashboardService dashboards) : IMyWorkCardSource
{
    public string SystemCode => ReportingPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var pins = await dashboards.PinsAsync(user, cancellationToken).ConfigureAwait(false);
        if (pins.Count == 0)
        {
            return [];
        }

        var items = pins.Take(5).Select(p => new MyWorkCardItem(
            p.Kind == "report" ? "reporting.report" : "reporting.dashboard", p.RefId, null, p.TitleAr, p.TitleEn, "reporting.myWork.items.pinned",
            p.Kind == "report" ? $"/reports/{p.RefId:D}" : $"/dashboards/{p.Code}")).ToList();
        return [new MyWorkCard("reporting.pins", SystemCode, "myWork.cards.pins", [new MyWorkCount("pinned", pins.Count)], items, "/dashboards", 900)];
    }
}
