using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Links.Api;

public sealed record LinkableTypeDto(string ResourceType, string SystemCode, string NameAr, string NameEn, IReadOnlyList<string> Capabilities);

/// <summary>Links pointing at a record: the visible ones resolved, the others only counted.</summary>
public sealed record LinksToDto(IReadOnlyList<LinkToDto> Links, int Restricted);

public sealed record LinkToDto(string Kind, LinkView From);

/// <summary><c>/api/v1/links</c>: signed-in internal users; every record is checked by its owning module (ADR 0034).</summary>
internal static class LinksEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/types", async (string? capability, ClaimsPrincipal user, ILinkRegistry registry, CancellationToken ct) =>
            {
                if (!user.IsInternal())
                {
                    return Results.Problem(Error.Forbidden().ToProblemDetails());
                }

                var types = await registry.TypesAsync(capability, ct).ConfigureAwait(false);
                return Results.Ok(types.Select(t => new LinkableTypeDto(t.ResourceType, t.SystemCode, t.NameAr, t.NameEn, t.Capabilities)).ToList());
            })
            .WithName("ListLinkableTypes").RequireSignedIn().Produces<List<LinkableTypeDto>>();

        group.MapGet("/resolve", async Task<Results<Ok<LinkView>, ProblemHttpResult>> (string type, Guid id, ClaimsPrincipal user, ILinkRegistry registry, CancellationToken ct) =>
            {
                if (!user.IsInternal())
                {
                    return Error.Forbidden().ToProblem();
                }

                var view = (await registry.ResolveAsync([new LinkRef(type, id)], user, ct).ConfigureAwait(false))[0];
                return view.Visibility == LinkVisibility.Visible ? TypedResults.Ok(view) : Error.NotFound().ToProblem();
            })
            .WithName("ResolveLink").RequireSignedIn();

        group.MapGet("/to/{type}/{id:guid}", async Task<Results<Ok<LinksToDto>, ProblemHttpResult>> (
                string type, Guid id, string? kind, ClaimsPrincipal user, ILinkRegistry registry, ILinkStore store, CancellationToken ct) =>
            {
                if (!user.IsInternal())
                {
                    return Error.Forbidden().ToProblem();
                }

                var target = (await registry.ResolveAsync([new LinkRef(type, id)], user, ct).ConfigureAwait(false))[0];
                if (target.Visibility != LinkVisibility.Visible)
                {
                    return Error.NotFound().ToProblem();
                }

                var links = await store.ListToAsync(new LinkRef(type, id), kind, ct).ConfigureAwait(false);
                var froms = await registry.ResolveAsync(links.Select(l => new LinkRef(l.FromType, l.FromId)).ToList(), user, ct).ConfigureAwait(false);
                var visible = links.Zip(froms).Where(p => p.Second.Visibility == LinkVisibility.Visible).Select(p => new LinkToDto(p.First.Kind, p.Second)).ToList();
                return TypedResults.Ok(new LinksToDto(visible, links.Count - visible.Count));
            })
            .WithName("ListLinksTo").RequireSignedIn();
    }
}
