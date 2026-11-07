using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Integration.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration.Api;

/// <summary>One reference of a record as the record page shows it (source names resolved).</summary>
public sealed record RecordReferenceDto(
    string SourceSystem,
    string? SourceNameAr,
    string? SourceNameEn,
    string? SourceTypeCode,
    bool SourceActive,
    string ExternalId,
    bool IsOrigin,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? SourceModifiedAt,
    string SyncStatus);

/// <summary>References of one record; <c>Origin</c> is the reference the record was created from (origin badge), if any.</summary>
public sealed record RecordReferencesDto(string EntityType, Guid RecordId, IReadOnlyList<RecordReferenceDto> References, RecordReferenceDto? Origin);

public sealed record ReferenceableTypeDto(string ResourceType, string SystemCode, string NameAr, string NameEn);

/// <summary><c>/api/v1/integration</c> (ADR 0164): source systems and the references of a record.</summary>
internal static class IntegrationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var sources = group.MapGroup("/source-systems");
        sources.MapGet("/", async (bool? includeInactive, SourceSystemAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(includeInactive ?? false, ct)))
            .WithName("ListSourceSystems").RequirePermission(IntegrationPermissions.SourceRead);
        sources.MapPost("/", async (SourceSystemInput body, SourceSystemAdministration a, CancellationToken ct) => (await a.CreateAsync(body, ct)).ToOk())
            .WithName("CreateSourceSystem").RequirePermission(IntegrationPermissions.SourceManage);
        sources.MapPut("/{id:guid}", async (Guid id, SourceSystemInput body, SourceSystemAdministration a, CancellationToken ct) => (await a.UpdateAsync(id, body, ct)).ToOk())
            .WithName("UpdateSourceSystem").RequirePermission(IntegrationPermissions.SourceManage);
        sources.MapPost("/{id:guid}/activate", async (Guid id, SourceSystemVersionInput body, SourceSystemAdministration a, CancellationToken ct) =>
                (await a.SetActiveAsync(id, true, body.Version, ct)).ToOk())
            .WithName("ActivateSourceSystem").RequirePermission(IntegrationPermissions.SourceManage);
        sources.MapPost("/{id:guid}/deactivate", async (Guid id, SourceSystemVersionInput body, SourceSystemAdministration a, CancellationToken ct) =>
                (await a.SetActiveAsync(id, false, body.Version, ct)).ToOk())
            .WithName("DeactivateSourceSystem").RequirePermission(IntegrationPermissions.SourceManage);

        group.MapGet("/types", (ReferencedRecordResolver resolver, Links.ILinkRegistry links, CancellationToken ct) => TypesAsync(resolver, links, ct))
            .WithName("ListReferenceableTypes").RequirePermission(IntegrationPermissions.SourceRead);

        group.MapGet("/references/{type}/{id:guid}", ReferencesAsync).WithName("ListRecordExternalReferences").RequireSignedIn();
    }

    private static async Task<Ok<List<ReferenceableTypeDto>>> TypesAsync(ReferencedRecordResolver resolver, Links.ILinkRegistry links, CancellationToken ct)
    {
        var linkable = await links.TypesAsync(null, ct).ConfigureAwait(false);
        var types = linkable.Select(t => new ReferenceableTypeDto(t.ResourceType, t.SystemCode, t.NameAr, t.NameEn))
            .Concat(resolver.RegisteredTypes.Select(t => new ReferenceableTypeDto(t.ResourceType, t.SystemCode, t.NameAr, t.NameEn)))
            .DistinctBy(t => t.ResourceType).OrderBy(t => t.ResourceType, StringComparer.Ordinal).ToList();
        return TypedResults.Ok(types);
    }

    /// <summary>The references of a record: 404 unless the caller may read the record (Links registry or the registered source).</summary>
    private static async Task<Results<Ok<RecordReferencesDto>, ProblemHttpResult>> ReferencesAsync(
        string type, Guid id, ClaimsPrincipal user, IReferencedRecordResolver resolver, IExternalReferenceStore store, Infrastructure.IntegrationDbContext db,
        CancellationToken ct)
    {
        if (!user.IsInternal())
        {
            return Error.Forbidden().ToProblem();
        }

        var visible = await resolver.ResolveAsync(type, [id], user, ct).ConfigureAwait(false);
        if (!visible.ContainsKey(id))
        {
            return Error.NotFound().ToProblem();
        }

        var references = await store.ListForRecordAsync(type, id, ct).ConfigureAwait(false);
        var codes = references.Select(r => r.SourceSystem).Distinct().ToList();
        var sources = codes.Count == 0
            ? []
            : await db.SourceSystems.AsNoTracking().Where(s => codes.Contains(s.Code)).ToListAsync(ct).ConfigureAwait(false);
        var byCode = sources.ToDictionary(s => s.Code, StringComparer.Ordinal);
        var dtos = references.Select(r =>
        {
            var source = byCode.GetValueOrDefault(r.SourceSystem);
            return new RecordReferenceDto(r.SourceSystem, source?.NameAr, source?.NameEn, source?.TypeCode, source?.Active ?? false, r.ExternalId, r.IsOrigin,
                r.FirstSeenAt, r.LastSyncedAt, r.SourceModifiedAt, r.SyncStatus);
        }).ToList();
        return TypedResults.Ok(new RecordReferencesDto(type, id, dtos, dtos.FirstOrDefault(d => d.IsOrigin)));
    }
}
