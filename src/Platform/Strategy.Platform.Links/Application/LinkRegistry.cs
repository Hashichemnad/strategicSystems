using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links.Domain;
using Strategy.Platform.Links.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Links.Application;

/// <summary>Declared types, permission-aware badges and link checks (docs/design/links.md, ADR 0034).</summary>
/// <remarks>Sources are resolved on first use: a module's source may itself depend on the registry (tasks read their linked records).</remarks>
internal sealed class LinkRegistry(
    IEnumerable<LinkableTypeDefinition> definitions, IServiceProvider services, IAccessService access, ISystemState systems) : ILinkRegistry
{
    private readonly Dictionary<string, LinkableTypeDefinition> _types =
        definitions.GroupBy(d => d.ResourceType, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    private Dictionary<string, ILinkTargetSource>? _sources;

    private Dictionary<string, ILinkTargetSource> Sources => _sources ??= services.GetServices<ILinkTargetSource>()
        .GroupBy(s => s.ResourceType, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    public LinkableTypeDefinition? Find(string resourceType) => _types.GetValueOrDefault(resourceType);

    public async Task<IReadOnlyList<LinkableTypeDefinition>> TypesAsync(string? capability = null, CancellationToken cancellationToken = default)
    {
        var result = new List<LinkableTypeDefinition>();
        foreach (var type in _types.Values.Where(t => capability is null || t.Capabilities.Contains(capability, StringComparer.Ordinal)))
        {
            if (await systems.IsEnabledAsync(type.SystemCode, cancellationToken).ConfigureAwait(false))
            {
                result.Add(type);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<LinkView>> ResolveAsync(IReadOnlyCollection<LinkRef> references, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        var found = new Dictionary<LinkRef, LinkView>();
        foreach (var group in references.Distinct().GroupBy(r => r.Type, StringComparer.Ordinal))
        {
            var type = Find(group.Key);
            if (type is null || !Sources.TryGetValue(group.Key, out var source) || !await systems.IsEnabledAsync(type.SystemCode, cancellationToken).ConfigureAwait(false))
            {
                foreach (var reference in group)
                {
                    found[reference] = Unavailable(reference);
                }

                continue;
            }

            var targets = await access.CanAsync(user, type.ReadPermission, null, cancellationToken).ConfigureAwait(false)
                ? await source.GetAsync(group.Select(r => r.Id).ToList(), user, cancellationToken).ConfigureAwait(false)
                : new Dictionary<Guid, LinkTarget>();
            foreach (var reference in group)
            {
                found[reference] = targets.TryGetValue(reference.Id, out var target)
                    ? new LinkView(reference.Type, reference.Id, LinkVisibility.Visible, type.SystemCode, type.NameAr, type.NameEn, target.Number, target.TitleAr,
                        target.TitleEn, target.StatusCode, target.StatusLabelAr, target.StatusLabelEn, target.Tone, target.IsClosed, type.LinkOf(reference.Id))
                    : new LinkView(reference.Type, reference.Id, LinkVisibility.Restricted, type.SystemCode, type.NameAr, type.NameEn, null, null, null, null, null, null,
                        null, false, null);
            }
        }

        return references.Select(r => found[r]).ToList();
    }

    public async Task<bool> CanLinkAsync(ClaimsPrincipal user, string type, Guid id, string capability, CancellationToken cancellationToken = default)
    {
        var definition = Find(type);
        if (definition is null || !definition.Capabilities.Contains(capability, StringComparer.Ordinal))
        {
            return false;
        }

        var view = (await ResolveAsync([new LinkRef(type, id)], user, cancellationToken).ConfigureAwait(false))[0];
        return view.Visibility == LinkVisibility.Visible;
    }

    public async Task<bool> CanFollowLinkedAsync(ClaimsPrincipal user, string type, Guid id, CancellationToken cancellationToken = default)
    {
        var definition = Find(type);
        if (definition?.FollowLinkedPermission is not { } permission || !Sources.TryGetValue(type, out var source)
            || !await systems.IsEnabledAsync(definition.SystemCode, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await source.HasPermissionAsync(id, permission, user, cancellationToken).ConfigureAwait(false);
    }

    private LinkView Unavailable(LinkRef reference)
    {
        var type = Find(reference.Type);
        return new LinkView(reference.Type, reference.Id, LinkVisibility.Unavailable, type?.SystemCode, type?.NameAr, type?.NameEn, null, null, null, null, null, null,
            null, false, null);
    }
}

/// <summary>Link rows in <c>links.record_link</c>, joined to the caller's transaction when one is given (ADR 0023).</summary>
internal sealed class LinkStore(LinksDbContext db, IClock clock) : ILinkStore
{
    public async Task AddAsync(string kind, LinkRef from, LinkRef to, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var exists = await db.Links.AnyAsync(l => l.Active && l.Kind == kind && l.FromType == from.Type && l.FromId == from.Id
            && l.ToType == to.Type && l.ToId == to.Id, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        db.Links.Add(RecordLink.Create(kind, from, to));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string kind, LinkRef from, LinkRef to, string? reason, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var links = await db.Links.Where(l => l.Active && l.Kind == kind && l.FromType == from.Type && l.FromId == from.Id
            && l.ToType == to.Type && l.ToId == to.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var link in links)
        {
            link.Remove(clock.UtcNow, reason);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredLink>> ListToAsync(LinkRef to, string? kind = null, CancellationToken cancellationToken = default) =>
        await db.Links.AsNoTracking()
            .Where(l => l.Active && l.ToType == to.Type && l.ToId == to.Id && (kind == null || l.Kind == kind))
            .OrderBy(l => l.CreatedAt)
            .Select(l => new StoredLink(l.Id, l.Kind, l.FromType, l.FromId, l.ToType, l.ToId, l.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StoredLink>> ListFromAsync(LinkRef from, string? kind = null, CancellationToken cancellationToken = default) =>
        await db.Links.AsNoTracking()
            .Where(l => l.Active && l.FromType == from.Type && l.FromId == from.Id && (kind == null || l.Kind == kind))
            .OrderBy(l => l.CreatedAt)
            .Select(l => new StoredLink(l.Id, l.Kind, l.FromType, l.FromId, l.ToType, l.ToId, l.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
