using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Features;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.AI.Api;

/// <summary><c>/api/v1/ai</c>: capabilities, the shared features (drafts only, nothing saved) and Settings → AI (docs/design/ai.md).</summary>
internal static class AiEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/capabilities", async (AiAdministration a, CancellationToken ct) => TypedResults.Ok(await a.CapabilitiesAsync(ct)))
            .WithName("GetAiCapabilities").RequireSignedIn();

        // Features: every result is a draft for the user to accept (CLAUDE.md §2 rule 6).
        group.MapGet("/forms/{formKey}", async (string formKey, ExtractionService s, CancellationToken ct) => (await s.GetFormAsync(formKey, ct)).ToOk())
            .WithName("GetAiForm").RequirePermission(AiPermissions.Extract);
        group.MapPost("/forms/{formKey}/extract", ExtractAsync)
            .WithName("ExtractAiForm").RequirePermission(AiPermissions.Extract).DisableAntiforgery();
        group.MapPost("/brief/{resourceType}/{resourceId:guid}", async (string resourceType, Guid resourceId, BriefService s, CancellationToken ct) =>
                (await s.BriefAsync(resourceType, resourceId, ct)).ToOk())
            .WithName("GetApproverBrief").RequirePermission(AiPermissions.Brief);
        group.MapPost("/translate", async (TranslateInput body, TranslationService s, CancellationToken ct) => (await s.TranslateAsync(body, ct)).ToOk())
            .WithName("TranslateText").RequirePermission(AiPermissions.Translate);
        group.MapPost("/similar/{resourceType}", async (string resourceType, SimilarInput body, SimilarService s, CancellationToken ct) =>
                (await s.FindAsync(resourceType, body, ct)).ToOk())
            .WithName("FindSimilarRecords").RequirePermission(AiPermissions.Similar);
        group.MapPost("/ask", async (AskInput body, AskService s, CancellationToken ct) => (await s.AskAsync(body, ct)).ToOk())
            .WithName("AskTheSystem").RequirePermission(AiPermissions.Ask);

        // Settings → AI.
        group.MapGet("/settings", async (AiAdministration a, CancellationToken ct) => TypedResults.Ok(await a.SettingsAsync(ct)))
            .WithName("GetAiSettings").RequirePermission(AiPermissions.Manage);
        group.MapPut("/settings", async (AiSettingsInput body, AiAdministration a, CancellationToken ct) => (await a.SaveSettingsAsync(body, ct)).ToOk())
            .WithName("SaveAiSettings").RequirePermission(AiPermissions.Manage);
        group.MapGet("/providers", async (AiAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ProvidersAsync(ct)))
            .WithName("ListAiProviders").RequirePermission(AiPermissions.Manage);
        group.MapPost("/providers", async (AiProviderInput body, AiAdministration a, CancellationToken ct) => (await a.SaveProviderAsync(null, body, ct)).ToOk())
            .WithName("CreateAiProvider").RequirePermission(AiPermissions.Manage);
        group.MapPut("/providers/{id:guid}", async (Guid id, AiProviderInput body, AiAdministration a, CancellationToken ct) =>
                (await a.SaveProviderAsync(id, body, ct)).ToOk())
            .WithName("UpdateAiProvider").RequirePermission(AiPermissions.Manage);
        group.MapPost("/providers/{id:guid}/test", async (Guid id, AiProviderProbeInput body, AiAdministration a, CancellationToken ct) =>
                (await a.TestAsync(id, body, ct)).ToOk())
            .WithName("TestAiProvider").RequirePermission(AiPermissions.Manage);
        group.MapPost("/providers/{id:guid}/models", async (Guid id, AiProviderProbeInput body, AiAdministration a, CancellationToken ct) =>
                (await a.ModelsAsync(id, body, ct)).ToOk())
            .WithName("ListAiProviderModels").RequirePermission(AiPermissions.Manage);
        group.MapGet("/usage", async (int? page, int? pageSize, string? feature, Guid? userId, bool? success, AiAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.UsageAsync(new PageRequest(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, PageRequest.MaxPageSize)), feature, userId, success, ct)))
            .WithName("ListAiUsage").RequirePermission(AiPermissions.UsageRead);
        group.MapGet("/usage/summary", async (AiAdministration a, CancellationToken ct) => TypedResults.Ok(await a.UsageSummaryAsync(ct)))
            .WithName("GetAiUsageSummary").RequirePermission(AiPermissions.UsageRead);
    }

    private static async Task<Results<Ok<ExtractionResultDto>, ProblemHttpResult>> ExtractAsync(
        string formKey, IFormFile file, ExtractionService service, CancellationToken ct)
    {
        if (file.Length is 0 or > DocumentTextExtractor.MaxBytes)
        {
            return Error.Rule("ai.extract.size", "ai.errors.extractSize",
                new Dictionary<string, object?> { ["maxMb"] = DocumentTextExtractor.MaxBytes / 1024 / 1024 }).ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (await service.ExtractAsync(formKey, buffer.ToArray(), file.FileName, ct)).ToOk();
    }
}
