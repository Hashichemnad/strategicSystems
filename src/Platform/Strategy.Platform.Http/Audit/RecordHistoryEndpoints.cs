using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Audit;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Audit;

/// <summary>
/// The History tab of a record (CLAUDE.md §8): <c>GET {route}/{id}/audit</c> and <c>{route}/{id}/audit/as-of?at=</c> (<c>/history</c> is the organisation's effective-dated history).
/// The owner supplies the record check, so a record outside the caller's scope answers 404 like the record itself.
/// </summary>
public static class RecordHistoryEndpoints
{
    public delegate Task<bool> CanReadRecord(Guid id, HttpContext context);

    public static void MapRecordHistory(
        this RouteGroupBuilder group, string entityType, string permission, string operationPrefix, CanReadRecord canRead)
    {
        group.MapGet("/{id:guid}/audit", async Task<Results<Ok<PagedResult<AuditEntryDto>>, ProblemHttpResult>> (
                Guid id, int? page, int? pageSize, string? field, Guid? userId, HttpContext context, IAuditQuery query) =>
                await canRead(id, context).ConfigureAwait(false)
                    ? TypedResults.Ok(await query.HistoryAsync(entityType, id.ToString("D"), new PageRequest(page ?? 1, pageSize ?? 50), field, userId,
                        context.RequestAborted).ConfigureAwait(false))
                    : Error.NotFound().ToProblem())
            .WithName(operationPrefix + "AuditTrail").RequirePermission(permission);

        group.MapGet("/{id:guid}/audit/as-of", async Task<Results<Ok<RecordSnapshotDto>, ProblemHttpResult>> (
                Guid id, DateTimeOffset at, HttpContext context, IAuditQuery query) =>
                await canRead(id, context).ConfigureAwait(false)
                    ? TypedResults.Ok(await query.AsOfAsync(entityType, id.ToString("D"), at, context.RequestAborted).ConfigureAwait(false))
                    : Error.NotFound().ToProblem())
            .WithName(operationPrefix + "AsOf").RequirePermission(permission);
    }
}
