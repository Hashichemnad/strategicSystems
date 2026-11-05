using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Risk.Ai;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Risk.Api;

public sealed record RiskBody(RiskValues Values, uint Version);

public sealed record RiskCommentInput(string? Comment);

public sealed record RiskClosureInput(string ReasonCode, string? Note);

public sealed record RiskReasonInput(string Reason);

public sealed record RiskEscalationInput(bool Escalated, string? Note);

public sealed record RiskDecisionInput(string DecisionCode, string? Note, DateOnly? DecidedOn);

public sealed record RiskControlBody(ControlValues Values, uint? Version);

public sealed record RiskRateInput(string? EffectivenessCode, uint Version);

public sealed record RiskActionBody(ActionValues Values, uint? Version);

public sealed record RiskKriBody(KriValues Values, uint? Version);

public sealed record RiskEventBody(EventValues Values, uint? Version);

public sealed record RiskReviewBody(ReviewValues Values, uint Version);

public sealed record RiskSettingsInput(RiskSettings Settings, uint Version);

public sealed record RiskPreviewInput(FrameworkContent Content);

public sealed record RiskIdentifyInput(string? Description);

public sealed record RiskDuplicateInput(string? Text, Guid? ExcludeId);

public sealed record RiskReportInput(Guid? UnitId, int? Year);

/// <summary>
/// <c>/api/v1/risk</c> (docs/design/risk.md §16). Endpoints declare the permission needed to use them at all; handlers check the record itself (404
/// outside the read scope, 403 for a visible record and a forbidden action).
/// </summary>
internal static class RiskEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // Framework, categories, settings, mechanism
        group.MapGet("/framework", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRiskFramework(), ct)).ToOk())
            .WithName("GetRiskFramework").RequirePermission(RiskPermissions.Read);
        group.MapGet("/framework/versions", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRiskFrameworkVersions(), ct)).ToOk())
            .WithName("ListRiskFrameworkVersions").RequirePermission(RiskPermissions.Read);
        group.MapGet("/framework/versions/{versionNo:int}", async (int versionNo, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetRiskFrameworkVersion(versionNo), ct)).ToOk())
            .WithName("GetRiskFrameworkVersion").RequirePermission(RiskPermissions.Read);
        group.MapPost("/framework/preview", async (RiskPreviewInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new PreviewRiskFramework(b.Content), ct)).ToOk())
            .WithName("PreviewRiskFramework").RequirePermission(RiskPermissions.FrameworkManage);
        group.MapPost("/framework", async (FrameworkInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveRiskFramework(b), ct)).ToOk())
            .WithName("SaveRiskFramework").RequirePermission(RiskPermissions.FrameworkManage).WithIdempotency();
        group.MapGet("/categories", async (bool? includeInactive, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListRiskCategories(includeInactive ?? false), ct)).ToOk())
            .WithName("ListRiskCategories").RequirePermission(RiskPermissions.Read);
        group.MapPost("/categories", async (CategoryInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveRiskCategory(null, b), ct)).ToOk())
            .WithName("CreateRiskCategory").RequirePermission(RiskPermissions.FrameworkManage);
        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveRiskCategory(id, b), ct)).ToOk())
            .WithName("UpdateRiskCategory").RequirePermission(RiskPermissions.FrameworkManage);
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRiskSettings(), ct)).ToOk())
            .WithName("GetRiskSettings").RequirePermission(RiskPermissions.FrameworkManage);
        group.MapPut("/settings", async (RiskSettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveRiskSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveRiskSettings").RequirePermission(RiskPermissions.FrameworkManage);
        group.MapGet("/mechanism", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRiskMechanism(), ct)).ToOk())
            .WithName("GetRiskMechanism").RequirePermission(RiskPermissions.Read);
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRiskOptions(), ct)).ToOk())
            .WithName("GetRiskOptions").RequirePermission(RiskPermissions.Read);

        // Risks
        group.MapGet("/risks", async (int? page, int? pageSize, string? sort, string? view, string? search, Guid? unitId, string? category, string? status, string? residualBand,
                    string? residualStatus, int? year, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListRisks(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, search, unitId, category, status, residualBand,
                    residualStatus, year), ct)).ToOk())
            .WithName("ListRisks").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks", async Task<Results<Created<RiskDto>, ProblemHttpResult>> (RiskValues b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateRisk(b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/risk/risks/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateRisk").RequirePermission(RiskPermissions.Create);
        group.MapGet("/risks/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRisk(id), ct)).ToOk())
            .WithName("GetRisk").RequirePermission(RiskPermissions.Read);
        group.MapPut("/risks/{id:guid}", async (Guid id, RiskBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateRisk(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateRisk").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/submit", async (Guid id, RiskCommentInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SubmitRisk(id, b.Comment), ct)).ToOk())
            .WithName("SubmitRisk").RequirePermission(RiskPermissions.Submit).WithIdempotency();
        group.MapPost("/risks/{id:guid}/recall", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RecallRisk(id), ct)).ToOk())
            .WithName("RecallRisk").RequirePermission(RiskPermissions.Submit);
        group.MapPost("/risks/{id:guid}/office-update", async (Guid id, OfficeInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new OfficeUpdateRisk(id, b), ct)).ToOk())
            .WithName("OfficeUpdateRisk").RequirePermission(RiskPermissions.Office);
        group.MapPost("/risks/{id:guid}/request-closure", async (Guid id, RiskClosureInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RequestRiskClosure(id, b.ReasonCode, b.Note), ct)).ToOk())
            .WithName("RequestRiskClosure").RequirePermission(RiskPermissions.CloseRequest).WithIdempotency();
        group.MapPost("/risks/{id:guid}/reopen", async (Guid id, RiskReasonInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ReopenRisk(id, b.Reason), ct)).ToOk())
            .WithName("ReopenRisk").RequirePermission(RiskPermissions.Office);
        group.MapPost("/risks/{id:guid}/escalation", async (Guid id, RiskEscalationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new EscalateRisk(id, b.Escalated, b.Note), ct)).ToOk())
            .WithName("EscalateRisk").RequirePermission(RiskPermissions.Office);
        group.MapGet("/risks/{id:guid}/committee-decisions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListCommitteeDecisions(id), ct)).ToOk())
            .WithName("ListRiskCommitteeDecisions").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/committee-decisions", async (Guid id, RiskDecisionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RecordCommitteeDecision(id, b.DecisionCode, b.Note, b.DecidedOn), ct)).ToOk())
            .WithName("RecordRiskCommitteeDecision").RequirePermission(RiskPermissions.CommitteeDecide);
        group.MapPost("/risks/{id:guid}/discard", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new DiscardRisk(id), ct)).ToOk())
            .WithName("DiscardRisk").RequirePermission(RiskPermissions.Read);
        group.MapGet("/risks/{id:guid}/assessments", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRiskAssessments(id), ct)).ToOk())
            .WithName("ListRiskAssessments").RequirePermission(RiskPermissions.Read);
        group.MapGroup("/risks").MapRecordHistory(RiskRecord.EntityType, RiskPermissions.Read, "GetRisk", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetRisk(id), context.RequestAborted)).IsSuccess);

        // Controls
        group.MapGet("/risks/{id:guid}/controls", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListControls(id), ct)).ToOk())
            .WithName("ListRiskControls").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/controls", async (Guid id, RiskControlBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveControl(id, null, b.Values, null), ct)).ToOk())
            .WithName("CreateRiskControl").RequirePermission(RiskPermissions.Read);
        group.MapPut("/risks/{id:guid}/controls/{controlId:guid}", async (Guid id, Guid controlId, RiskControlBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveControl(id, controlId, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateRiskControl").RequirePermission(RiskPermissions.Read);
        group.MapPost("/controls/{id:guid}/rate", async (Guid id, RiskRateInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RateControl(id, b.EffectivenessCode, b.Version), ct)).ToOk())
            .WithName("RateRiskControl").RequirePermission(RiskPermissions.ControlUpdate);
        group.MapPost("/controls/{id:guid}/retire", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RetireControl(id), ct)).ToOk())
            .WithName("RetireRiskControl").RequirePermission(RiskPermissions.Read);

        // Treatment actions
        group.MapGet("/risks/{id:guid}/actions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListActions(id), ct)).ToOk())
            .WithName("ListRiskActions").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/actions", async (Guid id, RiskActionBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveAction(id, null, b.Values, null), ct)).ToOk())
            .WithName("CreateRiskAction").RequirePermission(RiskPermissions.Read);
        group.MapPut("/risks/{id:guid}/actions/{actionId:guid}", async (Guid id, Guid actionId, RiskActionBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveAction(id, actionId, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateRiskAction").RequirePermission(RiskPermissions.Read);
        group.MapPost("/actions/{id:guid}/cancel", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CancelAction(id), ct)).ToOk())
            .WithName("CancelRiskAction").RequirePermission(RiskPermissions.Read);
        group.MapPost("/actions/{id:guid}/progress", async (Guid id, ProgressInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateActionProgress(id, b with { FromMcp = false }), ct)).ToOk())
            .WithName("UpdateRiskActionProgress").RequirePermission(RiskPermissions.ActionUpdate);
        group.MapPost("/actions/{id:guid}/convert-to-control", async (Guid id, ConvertInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ConvertActionToControl(id, b), ct)).ToOk())
            .WithName("ConvertRiskActionToControl").RequirePermission(RiskPermissions.ActionUpdate);
        group.MapGet("/actions/{id:guid}/updates", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListActionUpdates(id), ct)).ToOk())
            .WithName("ListRiskActionUpdates").RequirePermission(RiskPermissions.Read);
        group.MapGet("/my-actions", async (bool? includeCompleted, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListMyActions(includeCompleted ?? false), ct)).ToOk())
            .WithName("ListMyRiskActions").RequirePermission(RiskPermissions.Read);

        // KRIs
        group.MapGet("/risks/{id:guid}/kris", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListKris(id), ct)).ToOk())
            .WithName("ListRiskKris").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/kris", async (Guid id, RiskKriBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveKri(id, null, b.Values, null), ct)).ToOk())
            .WithName("CreateRiskKri").RequirePermission(RiskPermissions.Read);
        group.MapPut("/risks/{id:guid}/kris/{kriId:guid}", async (Guid id, Guid kriId, RiskKriBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveKri(id, kriId, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateRiskKri").RequirePermission(RiskPermissions.Read);
        group.MapPost("/kris/{id:guid}/remove", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RemoveKri(id), ct)).ToOk())
            .WithName("RemoveRiskKri").RequirePermission(RiskPermissions.Read);

        // Events
        group.MapGet("/events", async (Guid? riskId, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRiskEvents(riskId), ct)).ToOk())
            .WithName("ListRiskEvents").RequirePermission(RiskPermissions.Read);
        group.MapPost("/events", async (RiskEventBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new LogRiskEvent(null, b.Values, null), ct)).ToOk())
            .WithName("LogRiskEvent").RequirePermission(RiskPermissions.EventManage);
        group.MapPut("/events/{id:guid}", async (Guid id, RiskEventBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new LogRiskEvent(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateRiskEvent").RequirePermission(RiskPermissions.EventManage);

        // Periodic reviews
        group.MapGet("/risks/{id:guid}/reviews", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListReviews(id), ct)).ToOk())
            .WithName("ListRiskReviews").RequirePermission(RiskPermissions.Read);
        group.MapPost("/risks/{id:guid}/reviews", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new StartReview(id), ct)).ToOk())
            .WithName("StartRiskReview").RequirePermission(RiskPermissions.Read);
        group.MapPut("/reviews/{id:guid}", async (Guid id, RiskReviewBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveReview(id, b.Values, b.Version), ct)).ToOk())
            .WithName("SaveRiskReview").RequirePermission(RiskPermissions.Read);
        group.MapPost("/reviews/{id:guid}/submit", async (Guid id, RiskCommentInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SubmitReview(id, b.Comment), ct)).ToOk())
            .WithName("SubmitRiskReview").RequirePermission(RiskPermissions.Read).WithIdempotency();

        // Dashboard, heat-maps, Project Risks, reports
        group.MapGet("/dashboard", async (Guid? unitId, string? category, string? residualStatus, int? year, bool? includeProjectRisks, DateOnly? from, DateOnly? to,
                    IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetRiskDashboard(new RiskFilter(unitId, category, residualStatus, year, includeProjectRisks, from, to)), ct)).ToOk())
            .WithName("GetRiskDashboard").RequirePermission(RiskPermissions.DashboardRead);
        group.MapGet("/heatmap", async (string kind, int likelihood, int impact, Guid? unitId, string? category, string? residualStatus,
                    int? year, bool? includeProjectRisks, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetHeatCellRisks(kind, likelihood, impact, new RiskFilter(unitId, category, residualStatus, year, includeProjectRisks, from, to)), ct))
                    .ToOk())
            .WithName("GetRiskHeatCell").RequirePermission(RiskPermissions.DashboardRead);
        group.MapGet("/project-risks", async (Guid? projectId, string? band, string? search, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListProjectRisks(projectId, band, search), ct)).ToOk())
            .WithName("ListRiskProjectRisks").RequirePermission(RiskPermissions.ProjectRisksRead);
        group.MapGet("/reports/{kind}", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string kind, string format, Guid? unitId, string? language, IDispatcher d,
                CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportRiskReport(kind, format, unitId, language), ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("ExportRiskReport").RequirePermission(RiskPermissions.Export);
        group.MapGet("/mechanism/pdf", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string? language, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportRiskReport("mechanism", "pdf", null, language), ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("ExportRiskMechanism").RequirePermission(RiskPermissions.Read);

        // AI (drafts and advice only)
        var ai = group.MapGroup("/ai");
        ai.MapPost("/identify", async (RiskIdentifyInput b, RiskAiService service, CancellationToken ct) => (await service.IdentifyAsync(b.Description, null, null, ct)).ToOk())
            .WithName("IdentifyRisksWithAi").RequirePermission(RiskPermissions.AiIdentify);
        ai.MapPost("/identify/document", IdentifyFromDocumentAsync).WithName("IdentifyRisksFromDocumentWithAi").RequirePermission(RiskPermissions.AiIdentify)
            .DisableAntiforgery();
        ai.MapPost("/statement-check", async (StatementInput b, RiskAiService service, CancellationToken ct) => (await service.CheckStatementAsync(b, ct)).ToOk())
            .WithName("CheckRiskStatement").RequirePermission(RiskPermissions.AiQuality);
        ai.MapPost("/risks/{id:guid}/suggestions", async (Guid id, RiskAiService service, CancellationToken ct) => (await service.SuggestAsync(id, ct)).ToOk())
            .WithName("SuggestRiskTreatmentWithAi").RequirePermission(RiskPermissions.AiSuggest);
        ai.MapPost("/duplicates", async (RiskDuplicateInput b, RiskAiService service, CancellationToken ct) => (await service.DuplicatesAsync(b.Text, b.ExcludeId, ct)).ToOk())
            .WithName("FindDuplicateRisks").RequirePermission(RiskPermissions.AiDuplicates);
        ai.MapPost("/risks/{id:guid}/review-assistant", async (Guid id, RiskAiService service, CancellationToken ct) => (await service.ReviewAssistAsync(id, ct)).ToOk())
            .WithName("AssistRiskReviewWithAi").RequirePermission(RiskPermissions.AiReview);
        ai.MapPost("/report", async Task<Results<Ok<RiskReportDraftDto>, ProblemHttpResult>> (RiskReportInput b, IDispatcher d, RiskAiService service, CancellationToken ct) =>
            {
                var dashboard = await d.SendAsync(new GetRiskDashboard(new RiskFilter(b.UnitId, null, null, b.Year, null)), ct);
                if (dashboard.IsFailure)
                {
                    return dashboard.Error.ToProblem();
                }

                var period = $"Q{((DateTime.UtcNow.Month - 1) / 3) + 1} {b.Year ?? DateTime.UtcNow.Year}";
                return (await service.ReportAsync(dashboard.Value, period, ct)).ToOk();
            })
            .WithName("DraftRiskReportWithAi").RequirePermission(RiskPermissions.AiReport);
    }

    private static async Task<Results<Ok<RiskIdentificationDto>, ProblemHttpResult>> IdentifyFromDocumentAsync(
        IFormFile file, string? description, RiskAiService service, CancellationToken ct)
    {
        if (file.Length is 0 or > DocumentTextExtractor.MaxBytes)
        {
            return Error.Rule("ai.extract.size", "ai.errors.extractSize",
                new Dictionary<string, object?> { ["maxMb"] = DocumentTextExtractor.MaxBytes / 1024 / 1024 }).ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (await service.IdentifyAsync(description, buffer.ToArray(), file.FileName, ct)).ToOk();
    }
}
