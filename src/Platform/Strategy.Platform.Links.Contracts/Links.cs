using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Links;

/// <summary>What a record type can be linked as (SPEC §0.0): tasks, teams and remark sources point at records of these types.</summary>
public static class LinkCapabilities
{
    public const string TaskAble = "task-able";
    public const string TeamAble = "team-able";
    public const string RemarkSource = "remark-source";

    public static readonly IReadOnlyList<string> All = [TaskAble, TeamAble, RemarkSource];
}

/// <summary>
/// A record type other systems may link to, declared in code by its owning module (ADR 0034).
/// </summary>
/// <remarks><c>ReadPermission</c>: Permission needed to see the record's badge at all (the source still checks the record).</remarks>
/// <remarks><c>LinkTemplate</c>: Deep link in the SPA, <c>{id}</c> replaced by the record id.</remarks>
/// <remarks><c>FollowLinkedPermission</c>: 
/// Permission on the record that lets a person see every item linked to it (SPEC §11.9 "linked-record roles", e.g. the PM sees every task
/// of the project). Null: only the linked items' own rules apply.
/// </remarks>
public sealed record LinkableTypeDefinition(
    string ResourceType,
    string SystemCode,
    string NameAr,
    string NameEn,
    IReadOnlyList<string> Capabilities,
    string ReadPermission,
    string LinkTemplate,
    string? FollowLinkedPermission = null)
{
    public string LinkOf(Guid id) => LinkTemplate.Replace("{id}", id.ToString("D"), StringComparison.Ordinal);
}

/// <summary>A linked record's badge: number, title and live status, as the owning module reports it.</summary>
/// <remarks><c>Tone</c>: neutral, info, success, warning or danger.</remarks>
/// <remarks><c>IsClosed</c>: The record is finished (closing it can prompt to close the items linked to it).</remarks>
public sealed record LinkTarget(
    Guid Id, string? Number, string? TitleAr, string? TitleEn, string? StatusCode, string? StatusLabelAr, string? StatusLabelEn, string Tone, bool IsClosed);

/// <summary>Implemented by the owning module per linkable type. Returns only the records <c>user</c> may read (others are left out).</summary>
public interface ILinkTargetSource
{
    string ResourceType { get; }

    Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>Does <c>user</c> hold <c>permission</c> on the record (with the record's data-scope attributes)? False when it does not exist.</summary>
    Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>A reference to a record of a linkable type.</summary>
public sealed record LinkRef(string Type, Guid Id);

public static class LinkVisibility
{
    /// <summary>The user may read the record: title, status and link are given.</summary>
    public const string Visible = "visible";

    /// <summary>The record is outside the user's access (or does not exist): nothing about it is given.</summary>
    public const string Restricted = "restricted";

    /// <summary>The type is unknown or its system is switched off.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>A resolved link as the UI shows it. Only <see cref="LinkVisibility.Visible"/> views carry content.</summary>
public sealed record LinkView(
    string Type,
    Guid Id,
    string Visibility,
    string? SystemCode,
    string? TypeNameAr,
    string? TypeNameEn,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string? StatusCode,
    string? StatusLabelAr,
    string? StatusLabelEn,
    string? Tone,
    bool IsClosed,
    string? Link);

/// <summary>The one entry point for linkable types and link badges (permission-aware, server-side).</summary>
public interface ILinkRegistry
{
    /// <summary>Declared types (optionally with a capability) whose system is switched on.</summary>
    Task<IReadOnlyList<LinkableTypeDefinition>> TypesAsync(string? capability = null, CancellationToken cancellationToken = default);

    LinkableTypeDefinition? Find(string resourceType);

    Task<IReadOnlyList<LinkView>> ResolveAsync(IReadOnlyCollection<LinkRef> references, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>May <paramref name="user"/> link something of <paramref name="capability"/> to this record (type declares it, system on, record readable)?</summary>
    Task<bool> CanLinkAsync(ClaimsPrincipal user, string type, Guid id, string capability, CancellationToken cancellationToken = default);

    /// <summary>Does <paramref name="user"/> hold the type's <see cref="LinkableTypeDefinition.FollowLinkedPermission"/> on this record?</summary>
    Task<bool> CanFollowLinkedAsync(ClaimsPrincipal user, string type, Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A stored link: <c>From</c> (e.g. a task) points at <c>To</c> (e.g. a project), created for <c>Kind</c> (a capability).</summary>
public sealed record StoredLink(Guid Id, string Kind, string FromType, Guid FromId, string ToType, Guid ToId, DateTimeOffset CreatedAt);

/// <summary>
/// Link storage (<c>links.record_link</c>). The owner of the linking record writes its links; pass its DbContext with an open transaction
/// as <c>transactionContext</c> so the link commits with the record (ADR 0023).
/// </summary>
public interface ILinkStore
{
    Task AddAsync(string kind, LinkRef from, LinkRef to, object? transactionContext = null, CancellationToken cancellationToken = default);

    /// <summary>Deactivates the active link (no hard delete).</summary>
    Task RemoveAsync(string kind, LinkRef from, LinkRef to, string? reason, object? transactionContext = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredLink>> ListToAsync(LinkRef to, string? kind = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredLink>> ListFromAsync(LinkRef from, string? kind = null, CancellationToken cancellationToken = default);
}

public static class LinksServiceCollectionExtensions
{
    public static IServiceCollection AddLinkableType(this IServiceCollection services, LinkableTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        return services;
    }

    public static IServiceCollection AddLinkTargetSource<TSource>(this IServiceCollection services)
        where TSource : class, ILinkTargetSource =>
        services.AddScoped<ILinkTargetSource, TSource>();
}
