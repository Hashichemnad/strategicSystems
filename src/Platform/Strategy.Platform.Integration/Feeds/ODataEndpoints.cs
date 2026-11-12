using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>
/// Read-only OData v4 feeds per system at <c>/odata/v1/{system}</c> (ADR 0192) and the incremental extracts at
/// <c>/api/v1/integration/extracts</c> (ADR 0193). Integration clients with scope <c>feeds</c> only (token-scope rule, ADR 0191).
/// </summary>
internal static class ODataEndpoints
{
    public const string Prefix = "/odata/v1";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Prefix + "/{system}", ServiceDocumentAsync).ExcludeFromDescription();
        endpoints.MapGet(Prefix + "/{system}/{**rest}", ResourceAsync).ExcludeFromDescription();
    }

    public static void MapExtracts(RouteGroupBuilder group)
    {
        var extracts = group.MapGroup("/extracts");
        extracts.MapGet("/", CatalogueAsync).WithName("ListExtractTypes").ExcludeFromDescription().RequireIntegrationClient();
        extracts.MapGet("/{system}/{entitySet}", (HttpContext c, string system, string entitySet, FeedService f, CancellationToken ct) =>
            ExtractAsync(c, system, entitySet, RecordQueryMode.Current, f, ct)).ExcludeFromDescription().RequireIntegrationClient();
        extracts.MapGet("/{system}/{entitySet}/deleted", (HttpContext c, string system, string entitySet, FeedService f, CancellationToken ct) =>
            ExtractAsync(c, system, entitySet, RecordQueryMode.Deleted, f, ct)).ExcludeFromDescription().RequireIntegrationClient();
    }

    public static string Lang(HttpRequest request) => request.Query["lang"].ToString() is "ar" ? "ar" : "en";

    private static string ServiceRoot(HttpRequest request, string system) => $"{request.Scheme}://{request.Host}{request.PathBase}{Prefix}/{system}/";

    /// <summary>401 (with the Basic and Bearer challenges BI tools understand) or 403; null when the caller is an integration client.</summary>
    private static async Task<bool> RefusedAsync(HttpContext context, bool odata)
    {
        if (!context.User.IsAuthenticated())
        {
            context.Response.Headers.Append("WWW-Authenticate", ApiKeyAuthenticationHandler.BasicRealm);
            context.Response.Headers.Append("WWW-Authenticate", "Bearer");
            if (odata)
            {
                await ErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "Sign in with an integration client.").ConfigureAwait(false);
            }
            else
            {
                await new Error("auth.required", "errors.unauthorized", ErrorKind.Unauthorized).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            }

            return true;
        }

        if (!context.User.IsIntegrationClient())
        {
            if (odata)
            {
                await ErrorAsync(context, StatusCodes.Status403Forbidden, "forbidden", "Only integration clients read the feeds.").ConfigureAwait(false);
            }
            else
            {
                await new Error("integration.clientOnly", "integration.errors.clientOnly", ErrorKind.Forbidden).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    private static async Task ServiceDocumentAsync(HttpContext context, string system, FeedService feeds, CancellationToken ct)
    {
        if (await RefusedAsync(context, odata: true).ConfigureAwait(false))
        {
            return;
        }

        var model = await feeds.ModelAsync(context.User, system, ct).ConfigureAwait(false);
        if (model is null)
        {
            await ErrorAsync(context, StatusCodes.Status404NotFound, "notFound", "Unknown system.").ConfigureAwait(false);
            return;
        }

        var root = ServiceRoot(context.Request, system);
        context.Response.Headers["OData-Version"] = FeedService.ODataVersion;
        context.Response.ContentType = "application/json; odata.metadata=minimal; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("@odata.context", root + "$metadata");
        writer.WriteStartArray("value");
        foreach (var type in model.Types.OrderBy(t => t.EntitySet, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", type.EntitySet);
            writer.WriteString("kind", "EntitySet");
            writer.WriteString("url", type.EntitySet);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task ResourceAsync(HttpContext context, string system, string rest, FeedService feeds, CancellationToken ct)
    {
        if (await RefusedAsync(context, odata: true).ConfigureAwait(false))
        {
            return;
        }

        var model = await feeds.ModelAsync(context.User, system, ct).ConfigureAwait(false);
        if (model is null)
        {
            await ErrorAsync(context, StatusCodes.Status404NotFound, "notFound", "Unknown system.").ConfigureAwait(false);
            return;
        }

        if (rest == "$metadata")
        {
            context.Response.Headers["OData-Version"] = FeedService.ODataVersion;
            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(Csdl(model.Model), ct).ConfigureAwait(false);
            return;
        }

        var root = ServiceRoot(context.Request, system);
        ODataRequest request;
        try
        {
            request = ODataQuery.Parse(model, new Uri(root), new Uri(root + rest + context.Request.QueryString.Value));
        }
        catch (FeedQueryException ex)
        {
            await ErrorAsync(context, ex.Code == "notFound" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, ex.Code, ex.Message).ConfigureAwait(false);
            return;
        }

        try
        {
            await feeds.WriteODataAsync(context, model, request, root, Lang(context.Request), ct).ConfigureAwait(false);
        }
        catch (FeedQueryException ex) when (!context.Response.HasStarted)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, ex.Code, ex.Message).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (!context.Response.HasStarted)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, "badRequest", ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>The CSDL (XML) of a model.</summary>
    public static string Csdl(IEdmModel model)
    {
        var builder = new StringBuilder();
        using (var xml = XmlWriter.Create(builder, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            if (!CsdlWriter.TryWriteCsdl(model, xml, CsdlTarget.OData, out var errors))
            {
                throw new InvalidOperationException("Invalid model: " + string.Join("; ", errors.Select(e => e.ErrorMessage)));
            }
        }

        // StringBuilder writers declare UTF-16; the response is UTF-8.
        return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal);
    }

    public static async Task ErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.Headers["OData-Version"] = FeedService.ODataVersion;
        await context.Response.WriteAsJsonAsync(new { error = new { code, message } }).ConfigureAwait(false);
    }

    private static async Task CatalogueAsync(HttpContext context, FeedService feeds, CancellationToken ct)
    {
        if (await RefusedAsync(context, odata: false).ConfigureAwait(false))
        {
            return;
        }

        var types = await feeds.VisibleAsync(context.User, null, ct).ConfigureAwait(false);
        await context.Response.WriteAsJsonAsync(types.GroupBy(t => t.SystemCode).Select(g => new
        {
            system = g.Key,
            recordTypes = g.Select(t => new { code = t.Code, entitySet = t.EntitySet, labelAr = t.Definition.LabelAr, labelEn = t.Definition.LabelEn }),
        }), ct).ConfigureAwait(false);
    }

    private static async Task ExtractAsync(HttpContext context, string system, string entitySet, RecordQueryMode mode, FeedService feeds, CancellationToken ct)
    {
        if (await RefusedAsync(context, odata: false).ConfigureAwait(false))
        {
            return;
        }

        var type = (await feeds.VisibleAsync(context.User, system, ct).ConfigureAwait(false)).FirstOrDefault(t => t.EntitySet == entitySet);
        if (type is null)
        {
            await Error.NotFound().ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var query = context.Request.Query;
        DateTimeOffset? since = null;
        if (query["changedSince"].ToString() is { Length: > 0 } text)
        {
            if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            {
                await Error.Validation([new FieldError("changedSince", "validation.invalid", "validation.invalid")]).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            since = parsed.ToUniversalTime();
        }

        if (!Cursor.TryDecode(query["cursor"].ToString(), out var cursor))
        {
            await Error.Validation([new FieldError("cursor", "integration.errors.cursor", "integration.errors.cursor")]).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        int? pageSize = int.TryParse(query["pageSize"].ToString(), out var size) ? size : null;
        var page = await feeds.ExtractAsync(context.User, type, mode, since, cursor, pageSize, Lang(context.Request), ct).ConfigureAwait(false);
        if (query["format"].ToString() == "csv")
        {
            context.Response.ContentType = "text/csv; charset=utf-8";
            context.Response.Headers["X-Has-More"] = page.HasMore ? "true" : "false";
            if (page.NextCursor is not null)
            {
                context.Response.Headers["X-Next-Cursor"] = page.NextCursor;
            }

            if (page.Watermark is { } mark)
            {
                context.Response.Headers["X-Watermark"] = (string)FeedService.JsonValue(mark)!;
            }

            await context.Response.WriteAsync(FeedService.Csv(page), ct).ConfigureAwait(false);
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            system = page.System,
            entitySet = page.EntitySet,
            deleted = page.Deleted,
            items = page.Items,
            hasMore = page.HasMore,
            nextCursor = page.NextCursor,
            watermark = page.Watermark is { } w ? FeedService.JsonValue(w) : null,
        }, ct).ConfigureAwait(false);
    }
}
