using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Search.Infrastructure;

namespace Strategy.Platform.Search.Application;

/// <summary>A found record as the UI shows it: resolved by the Links registry (visible only).</summary>
public sealed record SearchItemDto(
    string Type,
    Guid Id,
    string SystemCode,
    string? TypeNameAr,
    string? TypeNameEn,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string? StatusCode,
    string? StatusLabelAr,
    string? StatusLabelEn,
    string? Tone,
    string Link,
    string? MatchedOn,
    string? MatchedText,
    double Score);

/// <summary>The results of one system, best first.</summary>
public sealed record SearchGroupDto(string SystemCode, string NameAr, string NameEn, IReadOnlyList<SearchItemDto> Items);

public sealed record SearchResultsDto(string Query, IReadOnlyList<SearchGroupDto> Groups);

/// <summary>A recent item (re-resolved on every read).</summary>
public sealed record RecentItemDto(SearchItemDto Item, string SystemNameAr, string SystemNameEn, DateTimeOffset VisitedAt);

public sealed record RecentVisitInput(string Type, Guid Id);

/// <summary>A linkable type with its deep-link template (the SPA records record-page visits centrally).</summary>
public sealed record SearchTypeDto(string Type, string SystemCode, string NameAr, string NameEn, string LinkTemplate);

/// <summary>
/// Global search (ADR 0160): contributors of the systems the user may open, each in its own scope; every hit resolved through the Links
/// registry and kept only when visible (404 semantics: an invisible record is neither shown nor counted).
/// </summary>
public sealed partial class SearchService(
    IEnumerable<SearchContributorRegistration> registrations,
    IEnumerable<IAccessCatalogueSource> catalogues,
    IUserRoleQuery roles,
    ILinkRegistry links,
    IServiceProvider services,
    IRequestContext context,
    SearchDbContext db,
    IClock clock,
    ILogger<SearchService> logger)
{
    public const int MinLength = 2;
    public const int MaxLength = 200;
    public const int DefaultLimit = 5;
    public const int MaxLimit = 50;

    private static readonly TimeSpan ContributorTimeout = TimeSpan.FromSeconds(5);
    private const int Parallelism = 4;

    public async Task<Result<SearchResultsDto>> SearchAsync(ClaimsPrincipal user, string? q, string? system, int? limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInternal() || user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        var text = (q ?? string.Empty).Trim();
        var normalised = SearchText.Normalise(text);
        var errors = new List<FieldError>();
        if (normalised.Length < MinLength)
        {
            errors.Add(new FieldError("q", "validation.minimumLength", "validation.minimumLength", new Dictionary<string, object?> { ["minLength"] = MinLength }));
        }
        else if (text.Length > MaxLength)
        {
            errors.Add(new FieldError("q", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = MaxLength }));
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors.Add(new FieldError("limit", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 1, ["to"] = MaxLimit }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var take = limit ?? DefaultLimit;
        var open = await roles.OpenSystemsAsync(userId, user.EmployeeIdOf(), ct).ConfigureAwait(false);
        var query = new SearchQuery(text, normalised, take);
        var selected = registrations
            .Where(r => (r.SystemCode is null || open.Contains(r.SystemCode)) && (string.IsNullOrEmpty(system) || r.SystemCode is null || r.SystemCode == system))
            .Select(r => r.ContributorType).Distinct().ToList();
        var hits = await RunAsync(selected, query, user, ct).ConfigureAwait(false);

        var best = hits.GroupBy(h => (h.ResourceType, h.Id)).Select(g => g.OrderByDescending(h => h.Score).First()).ToList();
        var views = await links.ResolveAsync(best.Select(h => new LinkRef(h.ResourceType, h.Id)).ToList(), user, ct).ConfigureAwait(false);
        var items = best.Zip(views)
            .Where(p => IsShown(p.Second, open, system))
            .Select(p => ItemOf(p.Second, p.First.MatchedOn, p.First.MatchedText, p.First.Score))
            .ToList();

        var systems = Systems();
        var groups = items.GroupBy(i => i.SystemCode, StringComparer.Ordinal)
            .Select(g => (Code: g.Key, Items: g.OrderByDescending(i => i.Score).ThenBy(i => i.Number, StringComparer.Ordinal).Take(take).ToList()))
            .OrderByDescending(g => g.Items[0].Score)
            .ThenBy(g => systems.TryGetValue(g.Code, out var s) ? s.Sort : int.MaxValue)
            .Select(g => systems.TryGetValue(g.Code, out var s)
                ? new SearchGroupDto(g.Code, s.NameAr, s.NameEn, g.Items)
                : new SearchGroupDto(g.Code, g.Code, g.Code, g.Items))
            .ToList();
        return new SearchResultsDto(text, groups);
    }

    /// <summary>The user's recent items, newest first, re-resolved now: records no longer visible (or of a closed system) are left out.</summary>
    public async Task<Result<IReadOnlyList<RecentItemDto>>> RecentAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInternal() || user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        var rows = await db.RecentItems.AsNoTracking().Where(r => r.UserId == userId).OrderByDescending(r => r.VisitedAt).Take(RecentItem.MaxPerUser)
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return Result<IReadOnlyList<RecentItemDto>>.Success([]);
        }

        var open = await roles.OpenSystemsAsync(userId, user.EmployeeIdOf(), ct).ConfigureAwait(false);
        var views = await links.ResolveAsync(rows.Select(r => new LinkRef(r.ResourceType, r.ResourceId)).ToList(), user, ct).ConfigureAwait(false);
        var systems = Systems();
        IReadOnlyList<RecentItemDto> items = rows.Zip(views)
            .Where(p => IsShown(p.Second, open, null))
            .Select(p => new RecentItemDto(ItemOf(p.Second, null, null, 0), NameOf(systems, p.Second.SystemCode!, ar: true), NameOf(systems, p.Second.SystemCode!, ar: false),
                p.First.VisitedAt))
            .ToList();
        return Result<IReadOnlyList<RecentItemDto>>.Success(items);
    }

    /// <summary>Records a visit of a record the user may see (404 otherwise, nothing stored); keeps the newest <see cref="RecentItem.MaxPerUser"/>.</summary>
    public async Task<Result> VisitAsync(ClaimsPrincipal user, RecentVisitInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(input);
        if (!user.IsInternal() || user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(input.Type) || input.Type.Length > 100)
        {
            return Error.Validation([new FieldError("type", "validation.required", "validation.required")]);
        }

        var view = (await links.ResolveAsync([new LinkRef(input.Type, input.Id)], user, ct).ConfigureAwait(false))[0];
        if (view.Visibility != LinkVisibility.Visible)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        var rows = await db.RecentItems.Where(r => r.UserId == userId).OrderByDescending(r => r.VisitedAt).ToListAsync(ct).ConfigureAwait(false);
        var existing = rows.FirstOrDefault(r => r.ResourceType == input.Type && r.ResourceId == input.Id);
        if (existing is null)
        {
            existing = RecentItem.Create(userId, input.Type, input.Id, now);
            db.RecentItems.Add(existing);
        }
        else
        {
            existing.Visit(now);
        }

        db.RecentItems.RemoveRange(rows.Where(r => r != existing).Skip(RecentItem.MaxPerUser - 1));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A parallel visit of the same record stored it first: the visit is recorded either way.
        }

        return Result.Success();
    }

    /// <summary>Linkable types of switched-on systems with their link templates.</summary>
    public async Task<IReadOnlyList<SearchTypeDto>> TypesAsync(CancellationToken ct) =>
        (await links.TypesAsync(cancellationToken: ct).ConfigureAwait(false))
            .Select(t => new SearchTypeDto(t.ResourceType, t.SystemCode, t.NameAr, t.NameEn, t.LinkTemplate)).ToList();

    private static bool IsShown(LinkView view, IReadOnlySet<string> open, string? system) =>
        view.Visibility == LinkVisibility.Visible && view.SystemCode is { } code && open.Contains(code) && view.Link is not null
        && (string.IsNullOrEmpty(system) || code == system);

    private static SearchItemDto ItemOf(LinkView view, string? matchedOn, string? matchedText, double score) =>
        new(view.Type, view.Id, view.SystemCode!, view.TypeNameAr, view.TypeNameEn, view.Number, view.TitleAr, view.TitleEn, view.StatusCode, view.StatusLabelAr,
            view.StatusLabelEn, view.Tone, view.Link!, matchedOn, matchedText, Math.Round(score, 4));

    private Dictionary<string, SystemDefinition> Systems() =>
        catalogues.Select(c => c.System).GroupBy(s => s.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static string NameOf(Dictionary<string, SystemDefinition> systems, string code, bool ar) =>
        systems.TryGetValue(code, out var s) ? (ar ? s.NameAr : s.NameEn) : code;

    private async Task<List<SearchHit>> RunAsync(IReadOnlyList<Type> types, SearchQuery query, ClaimsPrincipal user, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Parallelism);
        var tasks = types.Select(async type =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await RunOneAsync(type, query, user, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.SelectMany(r => r).ToList();
    }

    private async Task<IReadOnlyList<SearchHit>> RunOneAsync(Type type, SearchQuery query, ClaimsPrincipal user, CancellationToken ct)
    {
        // Each contributor in its own scope: DbContexts are not thread-safe.
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(context.CorrelationId, context.Channel, user, context.IpAddress, context.UserAgent);
        var contributor = (ISearchContributor)scope.ServiceProvider.GetRequiredService(type);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ContributorTimeout);
        try
        {
            return await contributor.SearchAsync(query, user, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailed(logger, ex, type.Name);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search contributor {Contributor} failed; its results are left out.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string contributor);
}
