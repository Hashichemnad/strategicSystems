using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Ai;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Integration;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Kpi.Api;

public sealed record KpiCardBody(KpiCardInput Card, uint Version);

public sealed record KpiCardStatusInput(uint Version);

public sealed record KpiTargetsInput(IReadOnlyList<KpiCardTargetDto> Targets);

public sealed record KpiYearStartInput(IReadOnlyList<KpiYearItemInput> Items);

public sealed record KpiWindowsInput(IReadOnlyList<KpiWindowInput> Windows);

public sealed record KpiLockInput(string? Reason);

public sealed record KpiConfirmInput(IReadOnlyList<Guid> PlanIds);

public sealed record KpiDraftInput(KpiEntryInput Entry, uint Version);

public sealed record KpiSubmitInput(KpiEntryInput Entry, string? Comment, uint Version);

public sealed record KpiVersionInput(uint Version);

public sealed record KpiDecisionInput(string Action, string? Comment, IReadOnlyDictionary<string, bool>? Checklist);

public sealed record KpiBulkInput(IReadOnlyList<Guid> ResultIds, string? Comment, bool ChecklistConfirmed);

public sealed record KpiReasonInput(string Reason);

public sealed record KpiWithdrawInput(string? Reason);

public sealed record KpiSettingsInput(KpiSettings Settings, uint Version);

public sealed record KpiAnomaliesInput(IReadOnlyList<Guid> ResultIds);

public sealed record KpiCausalityInput(string? Notes);

public sealed record KpiBuilderInput(string? Description);

public sealed record KpiNarrativeInput(int? Year, Guid? UnitId);

/// <summary>
/// <c>/api/v1/kpi</c> (docs/design/kpi.md §14). Endpoints declare the permission needed to use them at all; handlers check the record itself
/// (404 outside the read scope, 403 for a visible record and a forbidden action).
/// </summary>
internal static class KpiEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // Strategy hierarchy
        group.MapGet("/strategy/elements", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListKpiElements(), ct)).ToOk())
            .WithName("ListKpiStrategyElements").RequirePermission(KpiPermissions.CardRead);
        group.MapPost("/strategy/elements", async (KpiElementInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveKpiElement(b), ct)).ToOk())
            .WithName("SaveKpiStrategyElement").RequirePermission(KpiPermissions.StrategyManage);
        group.MapGet("/strategy/map", async (int? year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiStrategyMap(year), ct)).ToOk())
            .WithName("GetKpiStrategyMap").RequirePermission(KpiPermissions.ScorecardRead);

        // Cards
        group.MapGet("/cards", async (int? page, int? pageSize, string? sort, string? search, Guid? unitId, string? typeCode, Guid? objectiveId, string? status, string? gem,
                    bool? globalStar, int? year, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListKpiCards(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), search, unitId, typeCode, objectiveId, status, gem,
                    globalStar, year), ct)).ToOk())
            .WithName("ListKpiCards").RequirePermission(KpiPermissions.CardRead);
        group.MapGet("/cards/lookup", async (string? search, Guid[]? ids, string? status, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LookupKpiCards(search, ids, status, page ?? 1, pageSize ?? 20), ct)).ToOk())
            .WithName("LookupKpiCards").RequirePermission(KpiPermissions.CardRead);
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiOptions(), ct)).ToOk())
            .WithName("GetKpiOptions").RequirePermission(KpiPermissions.CardRead);
        group.MapPost("/cards", async Task<Results<Created<KpiCardDto>, ProblemHttpResult>> (KpiCardInput b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateKpiCard(b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/kpi/cards/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateKpiCard").RequirePermission(KpiPermissions.CardManage);
        group.MapGet("/cards/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiCard(id), ct)).ToOk())
            .WithName("GetKpiCard").RequirePermission(KpiPermissions.CardRead);
        group.MapPut("/cards/{id:guid}", async (Guid id, KpiCardBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateKpiCard(id, b.Card, b.Version), ct)).ToOk())
            .WithName("UpdateKpiCard").RequirePermission(KpiPermissions.CardManage);
        group.MapByExternal("/cards", CardLinkTargets.Definition.ResourceType, KpiPermissions.CardRead, "KpiCard", c => c.Id, (id, http) => http.Dispatch(new GetKpiCard(id)))
            .WithUpsert<KpiCardInput>(KpiPermissions.CardManage, KpiPermissions.CardManage, (b, http) => http.Dispatch(new CreateKpiCard(b)),
                (id, b, current, http) => http.Dispatch(new UpdateKpiCard(id, b, current.Version)));
        group.MapPost("/cards/{id:guid}/activate", async (Guid id, KpiCardStatusInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetKpiCardStatus(id, true, b.Version), ct)).ToOk())
            .WithName("ActivateKpiCard").RequirePermission(KpiPermissions.CardManage);
        group.MapPost("/cards/{id:guid}/deactivate", async (Guid id, KpiCardStatusInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetKpiCardStatus(id, false, b.Version), ct)).ToOk())
            .WithName("DeactivateKpiCard").RequirePermission(KpiPermissions.CardManage);
        group.MapPut("/cards/{id:guid}/targets", async (Guid id, KpiTargetsInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetKpiCardTargets(id, b.Targets), ct)).ToOk())
            .WithName("SetKpiCardTargets").RequirePermission(KpiPermissions.CardManage);
        group.MapGet("/cards/{id:guid}/versions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListKpiCardVersions(id), ct)).ToOk())
            .WithName("ListKpiCardVersions").RequirePermission(KpiPermissions.CardRead);
        group.MapGet("/cards/{id:guid}/performance", async (Guid id, int? year, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetKpiPerformance(id, year), ct)).ToOk())
            .WithName("GetKpiPerformance").RequirePermission(KpiPermissions.CardRead);
        group.MapGet("/cards/{id:guid}/linked", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListKpiLinkedRecords(id), ct)).ToOk())
            .WithName("ListKpiLinkedRecords").RequirePermission(KpiPermissions.CardRead);
        group.MapPost("/cards/{id:guid}/changes", async (Guid id, KpiChangeInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RequestKpiDefinitionChange(id, b), ct)).ToOk())
            .WithName("RequestKpiDefinitionChange").RequirePermission(KpiPermissions.CardManage).WithIdempotency();
        group.MapGet("/changes", async (Guid? cardId, string? status, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListKpiDefinitionChanges(cardId, status), ct)).ToOk())
            .WithName("ListKpiDefinitionChanges").RequirePermission(KpiPermissions.CardRead);
        group.MapPost("/changes/{id:guid}/withdraw", async (Guid id, KpiWithdrawInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new WithdrawKpiDefinitionChange(id, b.Reason), ct)).ToOk())
            .WithName("WithdrawKpiDefinitionChange").RequirePermission(KpiPermissions.CardRead);
        group.MapGroup("/cards").MapRecordHistory(KpiCard.EntityType, KpiPermissions.CardRead, "GetKpiCard", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetKpiCard(id), context.RequestAborted)).IsSuccess);

        // Measuring year, windows, plans
        group.MapGet("/years/{year:int}/preview", async (int year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new PreviewKpiYear(year), ct)).ToOk())
            .WithName("PreviewKpiYear").RequirePermission(KpiPermissions.PlanManage);
        group.MapPost("/years/{year:int}/start", async (int year, KpiYearStartInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new StartKpiYear(year, b.Items), ct)).ToOk())
            .WithName("StartKpiYear").RequirePermission(KpiPermissions.PlanManage).WithIdempotency();
        group.MapGet("/years/{year:int}/windows", async (int year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListKpiWindows(year), ct)).ToOk())
            .WithName("ListKpiWindows").RequirePermission(KpiPermissions.ResultRead);
        group.MapPut("/years/{year:int}/windows", async (int year, KpiWindowsInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateKpiWindows(year, b.Windows), ct)).ToOk())
            .WithName("UpdateKpiWindows").RequirePermission(KpiPermissions.PlanManage);
        group.MapPost("/years/{year:int}/periods/{period}/lock", async (int year, string period, KpiLockInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LockKpiPeriod(year, period, true, b.Reason), ct)).ToOk())
            .WithName("LockKpiPeriod").RequirePermission(KpiPermissions.ResultReopen);
        group.MapPost("/years/{year:int}/periods/{period}/reopen", async (int year, string period, KpiLockInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LockKpiPeriod(year, period, false, b.Reason), ct)).ToOk())
            .WithName("ReopenKpiPeriod").RequirePermission(KpiPermissions.ResultReopen);
        group.MapGet("/plans", async (int year, Guid? unitId, string? status, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListKpiPlans(year, unitId, status), ct)).ToOk())
            .WithName("ListKpiPlans").RequirePermission(KpiPermissions.CardRead);
        group.MapGet("/plans/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiPlan(id), ct)).ToOk())
            .WithName("GetKpiPlan").RequirePermission(KpiPermissions.CardRead);
        group.MapPut("/plans/{id:guid}", async (Guid id, KpiPlanInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateKpiPlan(id, b), ct)).ToOk())
            .WithName("UpdateKpiPlan").RequirePermission(KpiPermissions.PlanManage);
        group.MapPost("/plans/confirm", async (KpiConfirmInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ConfirmKpiPlans(b.PlanIds), ct)).ToOk())
            .WithName("ConfirmKpiPlans").RequirePermission(KpiPermissions.PlanManage);

        // Results
        group.MapGet("/results/pending", async (string? view, int? year, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListMyPendingKpiResults(view, year), ct)).ToOk())
            .WithName("ListMyPendingKpiResults").RequirePermission(KpiPermissions.ResultRead);
        group.MapGet("/results/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiResult(id), ct)).ToOk())
            .WithName("GetKpiResult").RequirePermission(KpiPermissions.ResultRead);
        group.MapByExternal("/results", ResultLinkTargets.Definition.ResourceType, KpiPermissions.ResultRead, "KpiResult", r => r.Id,
            (id, http) => http.Dispatch(new GetKpiResult(id)));
        group.MapPut("/results/{id:guid}/draft", async (Guid id, KpiDraftInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveKpiResultDraft(id, b.Entry, b.Version), ct)).ToOk())
            .WithName("SaveKpiResultDraft").RequirePermission(KpiPermissions.ResultSubmit);
        group.MapPost("/results/{id:guid}/submit", async (Guid id, KpiSubmitInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitKpiResult(id, b.Entry, b.Comment, b.Version), ct)).ToOk())
            .WithName("SubmitKpiResult").RequirePermission(KpiPermissions.ResultSubmit).WithIdempotency();
        group.MapPost("/results/{id:guid}/recall", async (Guid id, KpiVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RecallKpiResult(id, b.Version), ct)).ToOk())
            .WithName("RecallKpiResult").RequirePermission(KpiPermissions.ResultSubmit);
        group.MapPost("/results/{id:guid}/decide", async (Guid id, KpiDecisionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new DecideKpiResult(id, b.Action, b.Comment, b.Checklist), ct)).ToOk())
            .WithName("DecideKpiResult").RequirePermission(KpiPermissions.ResultRead).WithIdempotency();
        group.MapPost("/results/{id:guid}/reopen", async (Guid id, KpiReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ReopenKpiResult(id, b.Reason), ct)).ToOk())
            .WithName("ReopenKpiResult").RequirePermission(KpiPermissions.ResultReopen);
        group.MapPost("/results/{id:guid}/open-now", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new OpenKpiResultNow(id), ct)).ToOk())
            .WithName("OpenKpiResultNow").RequirePermission(KpiPermissions.PlanManage);
        group.MapGroup("/results").MapRecordHistory(KpiResult.EntityType, KpiPermissions.ResultRead, "GetKpiResult", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetKpiResult(id), context.RequestAborted)).IsSuccess);

        // Approval queue
        group.MapGet("/approvals", async (int? year, string? period, Guid? unitId, bool? mine, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListKpiApprovalQueue(year, period, unitId, mine ?? false), ct)).ToOk())
            .WithName("ListKpiApprovalQueue").RequirePermission(KpiPermissions.ResultRead);
        group.MapPost("/approvals/bulk", async (KpiBulkInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new BulkApproveKpiResults(b.ResultIds, b.Comment, b.ChecklistConfirmed), ct)).ToOk())
            .WithName("BulkApproveKpiResults").RequirePermission(KpiPermissions.ResultReview).WithIdempotency();

        // Reports
        group.MapGet("/scorecard", async (int? year, Guid? unitId, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiScorecard(year, unitId), ct)).ToOk())
            .WithName("GetKpiScorecard").RequirePermission(KpiPermissions.ScorecardRead);
        group.MapGet("/dashboard", async (int? year, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetKpiDashboard(year, from, to), ct)).ToOk())
            .WithName("GetKpiDashboard").RequirePermission(KpiPermissions.DashboardRead);
        group.MapGet("/data-quality", async (int? year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiDataQuality(year), ct)).ToOk())
            .WithName("GetKpiDataQuality").RequirePermission(KpiPermissions.DashboardRead);
        group.MapGet("/export", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string kind, string format, int? year, string? language, string? gem,
                bool? globalStar, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportKpi(kind, format, year, language, gem, globalStar), ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("ExportKpi").RequirePermission(KpiPermissions.Export);

        // AI (drafts and advice only)
        var ai = group.MapGroup("/ai");
        ai.MapPost("/results/{id:guid}/evidence-check", async (Guid id, KpiAiService service, CancellationToken ct) => (await service.CheckEvidenceAsync(id, ct)).ToOk())
            .WithName("CheckKpiEvidenceWithAi").RequirePermission(KpiPermissions.AiEvidence);
        ai.MapPost("/anomalies", async (KpiAnomaliesInput b, KpiAiService service, CancellationToken ct) => (await service.AnomaliesAsync(b.ResultIds, ct)).ToOk())
            .WithName("GetKpiAnomalies").RequirePermission(KpiPermissions.AiAnomalies);
        ai.MapPost("/results/{id:guid}/causality", async (Guid id, KpiCausalityInput b, KpiAiService service, CancellationToken ct) =>
                (await service.DraftCausalityAsync(id, b.Notes, ct)).ToOk())
            .WithName("DraftKpiCausalityWithAi").RequirePermission(KpiPermissions.AiCausality);
        ai.MapPost("/card-builder", async (KpiBuilderInput b, KpiAiService service, CancellationToken ct) => (await service.DraftCardAsync(b.Description, null, null, ct)).ToOk())
            .WithName("DraftKpiCardWithAi").RequirePermission(KpiPermissions.AiBuilder);
        ai.MapPost("/card-builder/document", BuildFromDocumentAsync).WithName("DraftKpiCardFromDocumentWithAi").RequirePermission(KpiPermissions.AiBuilder).DisableAntiforgery();
        ai.MapGet("/cards/{id:guid}/smart", async (Guid id, KpiAiService service, CancellationToken ct) => (await service.SmartCheckAsync(id, ct)).ToOk())
            .WithName("CheckKpiCardSmart").RequirePermission(KpiPermissions.AiBuilder);
        ai.MapGet("/cards/{id:guid}/target-suggestion", async (Guid id, int year, KpiAiService service, CancellationToken ct) =>
                (await service.SuggestTargetAsync(id, year, ct)).ToOk())
            .WithName("SuggestKpiTarget").RequirePermission(KpiPermissions.AiTargets);
        ai.MapPost("/narrative", async (KpiNarrativeInput b, KpiAiService service, CancellationToken ct) => (await service.NarrativeAsync(b.Year, b.UnitId, ct)).ToOk())
            .WithName("DraftKpiNarrativeWithAi").RequirePermission(KpiPermissions.AiNarrative);

        // Settings
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetKpiSettings(), ct)).ToOk())
            .WithName("GetKpiSettings").RequireSignedIn();
        group.MapPut("/settings", async (KpiSettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveKpiSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveKpiSettings").RequirePermission(KpiPermissions.SettingsManage);
    }

    private static async Task<Results<Ok<KpiCardDraftDto>, ProblemHttpResult>> BuildFromDocumentAsync(
        IFormFile file, string? description, KpiAiService service, CancellationToken ct)
    {
        if (file.Length is 0 or > DocumentTextExtractor.MaxBytes)
        {
            return Error.Rule("ai.extract.size", "ai.errors.extractSize",
                new Dictionary<string, object?> { ["maxMb"] = DocumentTextExtractor.MaxBytes / 1024 / 1024 }).ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (await service.DraftCardAsync(description, buffer.ToArray(), file.FileName, ct)).ToOk();
    }
}
