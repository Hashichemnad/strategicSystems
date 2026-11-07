using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.DocumentRegistry.Ai;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Integration;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.DocumentRegistry.Api;

/// <summary><c>/api/v1/docreg</c> (docs/design/requests.md §6). Every read goes through the visibility rule (404 outside it).</summary>
internal static class DocRegEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/library", async (int? page, int? pageSize, string? level, string? type, Guid? unitId, string? sensitivity, string? status, int? expiringWithin,
                    string? search, DocumentService s, CancellationToken ct) =>
                TypedResults.Ok(await s.LibraryAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalised(), level, type, unitId, sensitivity, status,
                    expiringWithin, search, ct)))
            .WithName("GetDocumentLibrary").RequirePermission(DocRegPermissions.Read);
        group.MapGet("/dashboard", async (DateOnly? from, DateOnly? to, DocumentService s, CancellationToken ct) => TypedResults.Ok(await s.DashboardAsync(ct, from, to)))
            .WithName("GetDocumentDashboard").RequirePermission(DocRegPermissions.DashboardRead);

        var documents = group.MapGroup("/documents");
        documents.MapGet("/{id:guid}", async (Guid id, DocumentService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToOk())
            .WithName("GetDocument").RequirePermission(DocRegPermissions.Read);
        documents.MapByExternal("", DocumentLinks.Definition.ResourceType, DocRegPermissions.Read, "Document", d => d.Id,
            (id, http) => http.RequestServices.GetRequiredService<DocumentService>().GetAsync(id, http.RequestAborted));
        documents.MapGet("/{id:guid}/compare", async (Guid id, Guid from, Guid to, DocumentService s, CancellationToken ct) => (await s.CompareAsync(id, from, to, ct)).ToOk())
            .WithName("CompareDocumentVersions").RequirePermission(DocRegPermissions.Read);
        documents.MapGet("/{id:guid}/download", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid id, Guid? versionId, DocumentService s,
                HttpContext http, CancellationToken ct) =>
            {
                var result = await s.DownloadAsync(id, versionId, ct);
                if (result.IsFailure)
                {
                    return result.Error.ToProblem();
                }

                http.Response.Headers["X-Watermark"] = result.Value.Stamped ? "applied" : "none";
                http.Response.Headers.CacheControl = "no-store";
                return TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .WithName("DownloadDocument").RequirePermission(DocRegPermissions.Read);
        documents.MapPost("/{id:guid}/confirm-valid", async (Guid id, ConfirmInput body, DocumentService s, CancellationToken ct) =>
                (await s.ConfirmValidAsync(id, body, ct)).ToOk())
            .WithName("ConfirmDocumentValid").RequirePermission(DocRegPermissions.Read);
        documents.MapPut("/{id:guid}/expiry", async (Guid id, ExpiryInput body, DocumentService s, CancellationToken ct) => (await s.SetExpiryAsync(id, body, ct)).ToOk())
            .WithName("SetDocumentExpiry").RequirePermission(DocRegPermissions.Manage);
        documents.MapPost("/{id:guid}/acknowledge", async (Guid id, DocumentService s, CancellationToken ct) => (await s.AcknowledgeAsync(id, ct)).ToOk())
            .WithName("AcknowledgeDocument").RequirePermission(DocRegPermissions.Read);
        documents.MapRecordHistory(Document.EntityType, DocRegPermissions.Read, "GetDocument", async (id, context) =>
            (await context.RequestServices.GetRequiredService<DocumentService>().GetAsync(id, context.RequestAborted)).IsSuccess);

        var ai = group.MapGroup("/ai");
        ai.MapPost("/requests/{requestId:guid}/classify", async (Guid requestId, DocRegAiService s, CancellationToken ct) => (await s.ClassifyAsync(requestId, ct)).ToOk())
            .WithName("SuggestDocumentClassification").RequirePermission(DocRegPermissions.AiClassify);
        ai.MapPost("/requests/{requestId:guid}/duplicates", async (Guid requestId, DocRegAiService s, CancellationToken ct) =>
                (await s.DuplicatesAsync(requestId, ct)).ToOk())
            .WithName("CheckDocumentDuplicates").RequirePermission(DocRegPermissions.AiDuplicates);
        ai.MapPost("/documents/{id:guid}/summary", async (Guid id, DocRegAiService s, CancellationToken ct) => (await s.SummaryAsync(id, ct)).ToOk())
            .WithName("SummariseDocument").RequirePermission(DocRegPermissions.AiSummary);
        ai.MapPost("/documents/{id:guid}/what-changed", async (Guid id, Guid from, Guid to, DocRegAiService s, CancellationToken ct) =>
                (await s.WhatChangedAsync(id, from, to, ct)).ToOk())
            .WithName("ExplainDocumentChanges").RequirePermission(DocRegPermissions.AiWhatChanged);
        ai.MapPost("/ask", async (QuestionInput body, DocRegAiService s, CancellationToken ct) => (await s.AskAsync(body, ct)).ToOk())
            .WithName("AskDocuments").RequirePermission(DocRegPermissions.AiQa);

        var settings = group.MapGroup("/settings");
        settings.MapGet("/", async (DocumentService s, CancellationToken ct) => TypedResults.Ok(await s.SettingsAsync(ct)))
            .WithName("GetDocRegSettings").RequirePermission(DocRegPermissions.SettingsManage);
        settings.MapPut("/", async (DocRegSettingsInput body, DocumentService s, CancellationToken ct) => (await s.SaveSettingsAsync(body, ct)).ToOk())
            .WithName("SaveDocRegSettings").RequirePermission(DocRegPermissions.SettingsManage);
    }
}
