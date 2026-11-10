using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Integration;

/// <summary>
/// <c>GET / PUT {path}/by-external/{source}/{externalId}</c> for one referenceable record type (ADR 0165). The module supplies its own GET-by-id,
/// create and update (its commands: validation, scope, audit and notifications stay the module's). Wire a type in a few lines:
/// <code>
/// group.MapByExternal("/risks", "risk.risk", RiskPermissions.Read, "Risk", r => r.Id, (id, http) => Dispatcher(http).SendAsync(new GetRisk(id)))
///      .WithUpsert&lt;RiskValues&gt;(RiskPermissions.Create, RiskPermissions.Read, (body, http) => …CreateRisk(body), (id, body, current, http) => …UpdateRisk(id, body, current.Version));
/// </code>
/// </summary>
public static class ByExternalEndpoints
{
    /// <summary>Sends a module command / query through the request's dispatcher (for the GET / create / update delegates).</summary>
    public static Task<Result<T>> Dispatch<T>(this HttpContext http, IRequest<T> request)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.RequestServices.GetRequiredService<IDispatcher>().SendAsync(request, http.RequestAborted);
    }

    public static ByExternalBuilder<TDto> MapByExternal<TDto>(
        this RouteGroupBuilder group, string path, string resourceType, string readPermission, string operationName, Func<TDto, Guid> idOf,
        Func<Guid, HttpContext, Task<Result<TDto>>> get)
    {
        ArgumentNullException.ThrowIfNull(group);
        var route = $"{path.TrimEnd('/')}/by-external/{{source}}/{{externalId}}";
        group.MapGet(route, async Task<Results<Ok<TDto>, ProblemHttpResult>> (string source, string externalId, HttpContext context) =>
            {
                var store = context.RequestServices.GetRequiredService<IExternalReferenceStore>();
                var reference = await store.FindAsync(source, resourceType, externalId, context.RequestAborted).ConfigureAwait(false);
                if (reference is null)
                {
                    return Error.NotFound().ToProblem();
                }

                // The module's GET decides visibility: a record outside the caller's scope is 404 like the record itself.
                var record = await get(reference.RecordId, context).ConfigureAwait(false);
                return record.IsSuccess ? TypedResults.Ok(record.Value) : record.Error.ToProblem();
            })
            .WithName($"Get{operationName}ByExternalId").RequirePermission(readPermission);
        return new ByExternalBuilder<TDto>(group, route, resourceType, readPermission, operationName, idOf, get);
    }
}

public sealed class ByExternalBuilder<TDto>
{
    private readonly RouteGroupBuilder _group;
    private readonly string _route;
    private readonly string _resourceType;
    private readonly string _readPermission;
    private readonly string _operationName;
    private readonly Func<TDto, Guid> _idOf;
    private readonly Func<Guid, HttpContext, Task<Result<TDto>>> _get;

    internal ByExternalBuilder(
        RouteGroupBuilder group, string route, string resourceType, string readPermission, string operationName, Func<TDto, Guid> idOf,
        Func<Guid, HttpContext, Task<Result<TDto>>> get)
    {
        _group = group;
        _route = route;
        _resourceType = resourceType;
        _readPermission = readPermission;
        _operationName = operationName;
        _idOf = idOf;
        _get = get;
    }

    /// <summary>
    /// Adds the <c>PUT</c>: update the referenced record through <paramref name="update"/> (given its current DTO, e.g. for the version), or — with
    /// <paramref name="createPermission"/> — create it through <paramref name="create"/> and link the reference as its origin. 200 / 201.
    /// <paramref name="updatePermission"/> is the permission of the module's own update endpoint (checked here, 403; the update command still checks
    /// the record itself).
    /// </summary>
    public ByExternalBuilder<TDto> WithUpsert<TBody>(
        string createPermission, string updatePermission, Func<TBody, HttpContext, Task<Result<TDto>>> create,
        Func<Guid, TBody, TDto, HttpContext, Task<Result<TDto>>> update)
    {
        _group.MapPut(_route, async Task<Results<Ok<TDto>, Created<TDto>, ProblemHttpResult>> (string source, string externalId, TBody body, HttpContext context) =>
            {
                var ct = context.RequestAborted;
                var store = context.RequestServices.GetRequiredService<IExternalReferenceStore>();
                externalId = externalId.Trim();
                if (externalId.Length is 0 or > ExternalReferenceErrors.ExternalIdMaxLength)
                {
                    return ExternalReferenceErrors.Of("externalId", externalId.Length == 0 ? ExternalReferenceErrors.ExternalIdRequired : ExternalReferenceErrors.ExternalIdTooLong)
                        .ToProblem();
                }

                var sourceCheck = await store.ValidateSourceAsync(source, "source", ct).ConfigureAwait(false);
                if (sourceCheck.IsFailure)
                {
                    return sourceCheck.Error.ToProblem();
                }

                source = sourceCheck.Value.Code;
                await using var exclusive = await store.LockAsync(source, _resourceType, externalId, ct).ConfigureAwait(false);
                var access = context.RequestServices.GetRequiredService<IAccessService>();
                var reference = await store.FindAsync(source, _resourceType, externalId, ct).ConfigureAwait(false);
                if (reference is not null)
                {
                    if (!await access.CanAsync(context.User, updatePermission, cancellationToken: ct).ConfigureAwait(false))
                    {
                        return Error.Forbidden().ToProblem();
                    }

                    var current = await _get(reference.RecordId, context).ConfigureAwait(false);
                    if (current.IsFailure)
                    {
                        return current.Error.ToProblem();
                    }

                    var updated = await update(reference.RecordId, body, current.Value, context).ConfigureAwait(false);
                    if (updated.IsFailure)
                    {
                        return updated.Error.ToProblem();
                    }

                    var refreshed = await store.LinkAsync(new ExternalReferenceInput(source, _resourceType, externalId, reference.RecordId, SyncStatus: SyncStatuses.Synced),
                        cancellationToken: ct).ConfigureAwait(false);
                    return refreshed.IsSuccess ? TypedResults.Ok(updated.Value) : refreshed.Error.ToProblem();
                }

                if (!await access.CanAsync(context.User, createPermission, cancellationToken: ct).ConfigureAwait(false))
                {
                    return Error.Forbidden().ToProblem();
                }

                var created = await create(body, context).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    return created.Error.ToProblem();
                }

                var id = _idOf(created.Value);
                var linked = await store.LinkAsync(new ExternalReferenceInput(source, _resourceType, externalId, id, IsOrigin: true, SyncStatus: SyncStatuses.Synced),
                    cancellationToken: ct).ConfigureAwait(false);
                return linked.IsSuccess ? TypedResults.Created(context.Request.Path.Value, created.Value) : linked.Error.ToProblem();
            })
            .WithName($"Upsert{_operationName}ByExternalId").RequirePermission(_readPermission);
        return this;
    }
}
