using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Requests.Ai;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Requests.Api;

/// <summary>
/// <c>/api/v1/requests</c> (docs/design/requests.md §6). Endpoints declare the permission needed to use them at all; the services check the request
/// itself (404 outside visibility, 403 for a visible request and a forbidden action).
/// </summary>
internal static class RequestEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/catalogue", async (RequestQueries q, CancellationToken ct) => TypedResults.Ok(await q.CatalogueAsync(ct)))
            .WithName("GetRequestCatalogue").RequirePermission(RequestPermissions.Submit);
        group.MapGet("/types/{code}/form", async (string code, RequestQueries q, CancellationToken ct) => (await q.TypeFormAsync(code, ct)).ToOk())
            .WithName("GetRequestTypeForm").RequirePermission(RequestPermissions.Submit);
        group.MapPost("/types/{code}/ai-fill", async (string code, IFormFile file, RequestAiService ai, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                return (await ai.FillAsync(code, buffer.ToArray(), file.FileName, ct)).ToOk();
            })
            .WithName("FillRequestFormWithAi").RequirePermission(RequestPermissions.AiExtract).DisableAntiforgery();

        group.MapGet("/mine", async (int? page, int? pageSize, string? status, string? type, string? search, RequestQueries q, CancellationToken ct) =>
                TypedResults.Ok(await q.MineAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalised(), status, type, search, ct)))
            .WithName("ListMyRequests").RequirePermission(RequestPermissions.Read);
        group.MapGet("/queue", async (int? page, int? pageSize, string? status, string? type, string? stage, string? search, bool? overdue, RequestQueries q,
                    CancellationToken ct) =>
                TypedResults.Ok(await q.QueueAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalised(), status, type, stage, search,
                    overdue == true, ct)))
            .WithName("ListRequestQueue").RequirePermission(RequestPermissions.Read);
        group.MapPost("/queue/bulk-complete", async (BulkCompleteInput body, RequestCommands c, CancellationToken ct) => (await c.BulkCompleteAsync(body, ct)).ToOk())
            .WithName("BulkCompleteRequests").RequirePermission(RequestPermissions.Read);

        group.MapPost("/", async Task<Results<Created<RequestDto>, ProblemHttpResult>> (DraftInput body, RequestCommands c, CancellationToken ct) =>
            {
                var result = await c.CreateAsync(body, ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/requests/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateRequestDraft").RequirePermission(RequestPermissions.Submit);
        group.MapGet("/{id:guid}", async (Guid id, RequestQueries q, CancellationToken ct) => (await q.GetAsync(id, ct)).ToOk())
            .WithName("GetRequest").RequirePermission(RequestPermissions.Read);
        group.MapPut("/{id:guid}", async (Guid id, SaveInput body, RequestCommands c, CancellationToken ct) => (await c.SaveAsync(id, body, ct)).ToOk())
            .WithName("SaveRequestDraft").RequirePermission(RequestPermissions.Read);
        group.MapPost("/{id:guid}/submit", async (Guid id, SubmitInput body, RequestCommands c, CancellationToken ct) => (await c.SubmitAsync(id, body, ct)).ToOk())
            .WithName("SubmitRequest").RequirePermission(RequestPermissions.Read).WithIdempotency();
        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelInput body, RequestCommands c, CancellationToken ct) => (await c.CancelAsync(id, body, ct)).ToOk())
            .WithName("CancelRequest").RequirePermission(RequestPermissions.Read);
        group.MapPost("/{id:guid}/decide", async (Guid id, DecideInput body, RequestCommands c, CancellationToken ct) => (await c.DecideAsync(id, body, ct)).ToOk())
            .WithName("DecideRequest").RequirePermission(RequestPermissions.Read).WithIdempotency();
        group.MapPost("/{id:guid}/actions/{action}", async (Guid id, string action, ActionInput body, RequestCommands c, CancellationToken ct) =>
                (await c.ActAsync(id, action, body, ct)).ToOk())
            .WithName("ActOnRequest").RequirePermission(RequestPermissions.Read).WithIdempotency();
        group.MapGet("/{id:guid}/pdf/{template}", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid id, string template, string? language, RequestPdf pdf,
                CancellationToken ct) =>
            {
                var result = await pdf.RenderAsync(id, template, language, ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, "application/pdf", result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("GetRequestPdf").RequirePermission(RequestPermissions.Read);
        group.MapRecordHistory(Request.EntityType, RequestPermissions.Read, "GetRequest", async (id, context) =>
            (await context.RequestServices.GetRequiredService<RequestQueries>().GetAsync(id, context.RequestAborted)).IsSuccess);

        var admin = group.MapGroup("/admin/types");
        admin.MapGet("/", async (TypeAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListRequestTypes").RequirePermission(RequestPermissions.TypeManage);
        admin.MapGet("/options", async (TypeAdministration a, CancellationToken ct) => TypedResults.Ok(await a.OptionsAsync(ct)))
            .WithName("GetRequestTypeOptions").RequirePermission(RequestPermissions.TypeManage);
        admin.MapPost("/", async (TypeInput body, TypeAdministration a, CancellationToken ct) => (await a.CreateAsync(body, ct)).ToOk())
            .WithName("CreateRequestType").RequirePermission(RequestPermissions.TypeManage);
        admin.MapGet("/{code}", async (string code, TypeAdministration a, CancellationToken ct) => (await a.GetAsync(code, ct)).ToOk())
            .WithName("GetRequestType").RequirePermission(RequestPermissions.TypeManage);
        admin.MapPut("/{code}", async (string code, TypeInput body, TypeAdministration a, CancellationToken ct) => (await a.UpdateAsync(code, body, ct)).ToOk())
            .WithName("UpdateRequestType").RequirePermission(RequestPermissions.TypeManage);
        admin.MapPost("/{code}/preview-pdf", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string code, TemplatePreviewInput body, RequestPdf pdf,
                CancellationToken ct) =>
            {
                var result = await pdf.PreviewAsync(code, body, ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, "application/pdf", result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("PreviewRequestTypePdf").RequirePermission(RequestPermissions.TypeManage);
    }
}
