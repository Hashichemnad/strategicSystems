using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration.Feeds;

public sealed record PortalRecordTypeDto(string Code, string EntitySet, string LabelAr, string LabelEn, IReadOnlyList<PortalFieldDto> Fields, IReadOnlyList<string> Relationships);

public sealed record PortalFieldDto(string Code, string LabelAr, string LabelEn, string Type, bool HasLabel);

public sealed record PortalSystemDto(string Code, string ODataUrl, IReadOnlyList<PortalRecordTypeDto> RecordTypes);

public sealed record PortalDto(string BaseUrl, string OpenApiUrl, string TokenUrl, IReadOnlyList<PortalSystemDto> Systems);

/// <summary>Developer portal (ADR 0195): the feeds and metadata as the viewer would read them, the URLs to use.</summary>
internal static class PortalEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var portal = group.MapGroup("/portal");
        portal.MapGet("/", PortalAsync).WithName("GetDeveloperPortal").RequirePermission(IntegrationPermissions.PortalView);
        portal.MapGet("/metadata/{system}", MetadataAsync).WithName("GetDeveloperPortalMetadata").RequirePermission(IntegrationPermissions.PortalView);
    }

    private static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}";

    private static async Task<Microsoft.AspNetCore.Http.HttpResults.Ok<PortalDto>> PortalAsync(HttpContext context, FeedService feeds, CancellationToken ct)
    {
        var types = await feeds.VisibleAsync(context.User, null, ct);
        var root = BaseUrl(context.Request);
        var systems = types.GroupBy(t => t.SystemCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new PortalSystemDto(
            g.Key,
            $"{root}{ODataEndpoints.Prefix}/{g.Key}",
            g.Select(t => new PortalRecordTypeDto(t.Code, t.EntitySet, t.Definition.LabelAr, t.Definition.LabelEn,
                t.AllFields.Select(f => new PortalFieldDto(f.Code, f.LabelAr, f.LabelEn, f.Type.ToString(), FeedLabels.HasLabel(f))).ToList(),
                t.Relationships.Select(r => r.Name).ToList())).ToList())).ToList();
        return TypedResults.Ok(new PortalDto(root, root + "/openapi/v1.json", root + "/connect/token", systems));
    }

    private static async Task<IResult> MetadataAsync(string system, HttpContext context, FeedService feeds, CancellationToken ct)
    {
        var model = await feeds.ModelAsync(context.User, system, ct);
        return model is null ? Error.NotFound().ToProblem() : TypedResults.Text(ODataEndpoints.Csdl(model.Model), "application/xml");
    }
}
