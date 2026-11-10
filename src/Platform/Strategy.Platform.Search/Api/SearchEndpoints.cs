using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Search.Application;

namespace Strategy.Platform.Search.Api;

/// <summary>
/// <c>/api/v1/search</c>: signed-in internal users. What each user finds is decided by the owning modules (scoped queries) and the Links
/// registry (visible records only, ADR 0160). Reads: not audited; rate-limited by the global API limiter.
/// </summary>
internal static class SearchEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async Task<Results<Ok<SearchResultsDto>, ProblemHttpResult>> (
                string? q, string? system, int? limit, ClaimsPrincipal user, SearchService search, CancellationToken ct) =>
            {
                var result = await search.SearchAsync(user, q, system, limit, ct).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
            })
            .WithName("GlobalSearch").RequireSignedIn();

        group.MapGet("/recent", async Task<Results<Ok<IReadOnlyList<RecentItemDto>>, ProblemHttpResult>> (
                ClaimsPrincipal user, SearchService search, CancellationToken ct) =>
            {
                var result = await search.RecentAsync(user, ct).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
            })
            .WithName("ListRecentItems").RequireSignedIn();

        group.MapPost("/recent", async Task<Results<NoContent, ProblemHttpResult>> (
                RecentVisitInput input, ClaimsPrincipal user, SearchService search, CancellationToken ct) =>
            {
                var result = await search.VisitAsync(user, input, ct).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
            })
            .WithName("RecordRecentItem").RequireSignedIn();

        group.MapGet("/types", async Task<Results<Ok<IReadOnlyList<SearchTypeDto>>, ProblemHttpResult>> (
                ClaimsPrincipal user, SearchService search, CancellationToken ct) =>
            {
                if (!user.IsInternal())
                {
                    return Error.Forbidden().ToProblem();
                }

                return TypedResults.Ok(await search.TypesAsync(ct).ConfigureAwait(false));
            })
            .WithName("ListSearchTypes").RequireSignedIn();
    }
}
