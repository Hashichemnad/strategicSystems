using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Demo;

/// <summary>
/// One part of the optional demo pack (ADR 0153): the platform (structure, people, roles) or one system. Registered by the component or module
/// that owns the records and run by the host's demo pack service — loaded in ascending <see cref="Order"/>, removed in descending order.
/// <para>
/// Contributors create records only through their own commands and handlers (validated, access-checked and audited with channel
/// <c>import</c>), never with SQL on business tables, and record every created id with <see cref="DemoPackContext.TrackAsync"/>.
/// Removal follows CLAUDE.md §6: cancel / withdraw / deactivate with a reason, hard delete only for never-submitted drafts through the
/// module's own audited delete. Both operations must be safe to repeat.
/// </para>
/// </summary>
public interface IDemoPackContributor
{
    /// <summary>Stable code, e.g. <c>platform</c>, <c>risk</c>; also the system code checked against "system enabled".</summary>
    string Code { get; }

    /// <summary>Load order: the platform (people) first, then systems that others link to (KPI before Portfolio and Risk…).</summary>
    int Order { get; }

    Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken);

    Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken);
}

/// <summary>A demo person created by the platform contributor (user, employee, unit).</summary>
public sealed record DemoPerson(string Key, Guid UserId, Guid EmployeeId, Guid UnitId, string Email, string NameAr, string NameEn);

/// <summary>A record created by the demo pack, as tracked in <c>core.demo_record</c>.</summary>
/// <param name="Contributor">The contributor that created it.</param>
/// <param name="ResourceType">Module record type, e.g. <c>tasks.task</c>.</param>
/// <param name="ResourceId">The record's id.</param>
/// <param name="Key">Optional lookup key for other contributors.</param>
/// <param name="Actor">Key of the demo person who created it and removes it again (e.g. the creator who may cancel a task).</param>
public sealed record DemoRecordRef(string Contributor, string ResourceType, Guid ResourceId, string? Key, string? Actor);

/// <summary>Stores what the demo pack created, so that it can be listed and removed (implemented by Platform.Organisation).</summary>
public interface IDemoRecordStore
{
    Task TrackAsync(string contributor, string resourceType, Guid resourceId, string? key, string? actor, string packVersion, CancellationToken cancellationToken);

    /// <summary>Records not yet removed; <paramref name="contributor"/> null means all.</summary>
    Task<IReadOnlyList<DemoRecordRef>> ActiveAsync(string? contributor, CancellationToken cancellationToken);

    Task MarkRemovedAsync(string contributor, CancellationToken cancellationToken);
}

/// <summary>
/// What a contributor gets: the demo people, a way to act as one of them (so records have realistic creators and owners and go through the
/// same record-level checks as in the UI) and the tracker. Every <see cref="SendAsync{TResult}"/> / <see cref="RunAsync{T}"/> call runs in its own
/// service scope, like one HTTP request: channel <c>import</c>, the current person (or the system), the audit reason "demo pack".
/// </summary>
public sealed class DemoPackContext(
    string contributor,
    string packVersion,
    DateOnly today,
    IDemoRecordStore store,
    IReadOnlyDictionary<string, DemoPerson> people,
    IReadOnlyDictionary<string, Guid> units,
    Func<DemoPerson?, Func<IServiceProvider, Task<object?>>, Task<object?>> run,
    string? password = null)
{
    public const string PackVersion = "1.0";

    public string Contributor { get; } = contributor;

    public string Version { get; } = packVersion;

    /// <summary>Today in the installation's time zone.</summary>
    public DateOnly Today { get; } = today;

    /// <summary>Demo people by key (e.g. <c>director</c>, <c>pm</c>); empty for the platform contributor itself.</summary>
    public IReadOnlyDictionary<string, DemoPerson> People { get; } = people;

    /// <summary>Demo units by key (e.g. <c>org</c>, <c>strategy</c>).</summary>
    public IReadOnlyDictionary<string, Guid> Units { get; } = units;

    /// <summary>Password of the demo users (platform contributor only).</summary>
    public string? Password { get; } = password;

    public DemoPerson Person(string key) =>
        People.TryGetValue(key, out var person) ? person : throw new InvalidOperationException($"Demo person '{key}' is not loaded.");

    /// <summary>Runs the following commands as this demo person (channel stays <c>import</c>).</summary>
    public Task ActAsAsync(string personKey)
    {
        _person = Person(personKey);
        CurrentActor = personKey;
        return Task.CompletedTask;
    }

    /// <summary>Runs the following commands as the system principal (seeding without a person).</summary>
    public Task ActAsSystemAsync()
    {
        _person = null;
        CurrentActor = null;
        return Task.CompletedTask;
    }

    private DemoPerson? _person;

    /// <summary>Sends a command or query of the contributor's module through the dispatcher, in a new scope, as the current person.</summary>
    public async Task<Result<TResult>> SendAsync<TResult>(Messaging.IRequest<TResult> request, CancellationToken cancellationToken = default) =>
        (Result<TResult>)(await run(_person, async services =>
            (object?)await services.GetRequiredService<Messaging.IDispatcher>().SendAsync(request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false))!;

    /// <summary>Runs other work (e.g. a file upload through <c>IFileService</c>) in a new scope, as the current person.</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> work) =>
        (T)(await run(_person, async services => (object?)await work(services).ConfigureAwait(false)).ConfigureAwait(false))!;

    /// <summary>Records a created record; <paramref name="actor"/> is the demo person who removes it again (default: the person acted as).</summary>
    public Task TrackAsync(string resourceType, Guid resourceId, string? key = null, string? actor = null, CancellationToken cancellationToken = default) =>
        store.TrackAsync(Contributor, resourceType, resourceId, key, actor ?? CurrentActor, Version, cancellationToken);

    /// <summary>Key of the demo person the contributor currently acts as (null: system).</summary>
    public string? CurrentActor { get; private set; }

    /// <summary>Acts as the person who created <paramref name="record"/> (or as the system when none was recorded).</summary>
    public Task ActAsAsync(DemoRecordRef record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Actor is { } key && People.ContainsKey(key) ? ActAsAsync(key) : ActAsSystemAsync();
    }

    /// <summary>This contributor's records not yet removed, newest last.</summary>
    public async Task<IReadOnlyList<DemoRecordRef>> TrackedAsync(string? resourceType = null, CancellationToken cancellationToken = default)
    {
        var all = await store.ActiveAsync(Contributor, cancellationToken).ConfigureAwait(false);
        return resourceType is null ? all : all.Where(r => r.ResourceType == resourceType).ToList();
    }

    /// <summary>Id of a tracked record by key (any contributor), e.g. a KPI card the Risk demo links to.</summary>
    public async Task<Guid?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        (await store.ActiveAsync(null, cancellationToken).ConfigureAwait(false)).FirstOrDefault(r => r.Key == key)?.ResourceId;
}

/// <summary>A demo pack step failed; the run reports <see cref="ErrorCode"/> for the contributor and continues with the next one.</summary>
public sealed class DemoPackException : Exception
{
    public DemoPackException()
        : this("demo.failed", "The demo pack step failed.")
    {
    }

    public DemoPackException(string message)
        : this("demo.failed", message)
    {
    }

    public DemoPackException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = "demo.failed";
    }

    public DemoPackException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; } = "demo.failed";
}

/// <summary>Helpers for contributors: a failed command stops the contributor with the command's error code.</summary>
public static class DemoResults
{
    public static T Require<T>(this Result<T> result, string step)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? result.Value
            : throw new DemoPackException(result.Error.Code, $"Demo step '{step}' failed: {result.Error.Code} {Describe(result.Error)}");
    }

    public static void Require(this Result result, string step)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsFailure)
        {
            throw new DemoPackException(result.Error.Code, $"Demo step '{step}' failed: {result.Error.Code} {Describe(result.Error)}");
        }
    }

    private static string Describe(Error error) =>
        error.Fields is { Count: > 0 } fields ? string.Join(", ", fields.Select(f => f.ToString())) : error.MessageKey;
}
