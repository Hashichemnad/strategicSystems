using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Cap.Ai;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Cap.Api;

public sealed record CapReasonInput(string? Reason);

public sealed record CapCommentInput(string? Comment);

public sealed record CapConfirmInput(bool Confirm, string? Note);

public sealed record CapReassignInput(Guid OwnerEmployeeId);

public sealed record CapRootCauseBody(RootCauseValues Values, uint Version);

public sealed record CapVerifyInput(bool Effective, string? Comment);

public sealed record CapLinkActionInput(Guid ActionId);

public sealed record CapLinkProjectInput(Guid? ProjectId);

public sealed record CapNoteInput(string? Note);

public sealed record CapRaiseInput(Guid? UnitId);

public sealed record CapSettingsInput(CapSettings Settings, uint Version);

public sealed record CapExtractInput(string? Text);

public sealed record CapSummaryInput(Guid? UnitId, int? Year);

public sealed record CapProjectOptionDto(Guid Id, string? Number, string? NameAr, string? NameEn, string Status);

/// <summary>
/// <c>/api/v1/cap</c> (docs/design/cap.md §14). Endpoints declare the permission needed to use them at all; handlers check the record itself (404
/// outside the read scope, 403 for a visible record and a forbidden action).
/// </summary>
internal static class CapEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetCapOptions(), ct)).ToOk())
            .WithName("GetCapOptions").RequirePermission(CapPermissions.Read);
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetCapSettings(), ct)).ToOk())
            .WithName("GetCapSettings").RequirePermission(CapPermissions.SettingsManage);
        group.MapPut("/settings", async (CapSettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveCapSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveCapSettings").RequirePermission(CapPermissions.SettingsManage);
        group.MapGet("/dashboard", async (Guid? unitId, int? year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetCapDashboard(unitId, year), ct)).ToOk())
            .WithName("GetCapDashboard").RequirePermission(CapPermissions.DashboardRead);
        group.MapGet("/reports/{kind}", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string kind, string format, Guid? unitId, Guid? sourceId,
                string? sourceType, int? year, string? language, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportCapReport(kind, format, unitId, sourceId, sourceType, year, language), ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("ExportCapReport").RequirePermission(CapPermissions.Export);

        // Sources
        group.MapGet("/sources", async (int? page, int? pageSize, string? search, string? sourceType, string? status, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListSources(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), search, sourceType, status), ct)).ToOk())
            .WithName("ListCapSources").RequirePermission(CapPermissions.Read);
        group.MapPost("/sources", async Task<Results<Created<SourceDto>, ProblemHttpResult>> (SourceValues b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateSource(b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/cap/sources/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateCapSource").RequirePermission(CapPermissions.SourceManage);
        group.MapGet("/sources/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetSource(id), ct)).ToOk())
            .WithName("GetCapSource").RequirePermission(CapPermissions.Read);
        group.MapPut("/sources/{id:guid}", async (Guid id, SourceBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateSource(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateCapSource").RequirePermission(CapPermissions.SourceManage);
        group.MapPost("/sources/{id:guid}/close", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CloseSource(id, true), ct)).ToOk())
            .WithName("CloseCapSource").RequirePermission(CapPermissions.SourceManage);
        group.MapPost("/sources/{id:guid}/reopen", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CloseSource(id, false), ct)).ToOk())
            .WithName("ReopenCapSource").RequirePermission(CapPermissions.SourceManage);
        group.MapPost("/sources/{id:guid}/remarks", async Task<Results<Created<RemarkDto>, ProblemHttpResult>> (Guid id, RemarkValues b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateRemark(id, b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/cap/remarks/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateCapRemark").RequirePermission(CapPermissions.SourceManage);
        group.MapGroup("/sources").MapRecordHistory(CapSource.EntityType, CapPermissions.Read, "GetCapSource", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetSource(id), context.RequestAborted)).IsSuccess);

        // Remarks
        group.MapGet("/remarks", async (int? page, int? pageSize, string? sort, string? view, string? search, Guid? sourceId, string? status, string? priority, Guid? unitId,
                    string? sourceType, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListRemarks(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, search, sourceId, status, priority, unitId,
                    sourceType), ct)).ToOk())
            .WithName("ListCapRemarks").RequirePermission(CapPermissions.Read);
        group.MapGet("/remarks/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRemark(id), ct)).ToOk())
            .WithName("GetCapRemark").RequirePermission(CapPermissions.Read);
        group.MapPut("/remarks/{id:guid}", async (Guid id, RemarkBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new UpdateRemark(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateCapRemark").RequirePermission(CapPermissions.SourceManage);
        group.MapPost("/remarks/{id:guid}/accept", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new AcceptRemark(id), ct)).ToOk())
            .WithName("AcceptCapRemark").RequirePermission(CapPermissions.Respond).WithIdempotency();
        group.MapPost("/remarks/{id:guid}/reject", async (Guid id, CapReasonInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RejectRemark(id, b.Reason), ct)).ToOk())
            .WithName("RejectCapRemark").RequirePermission(CapPermissions.Respond).WithIdempotency();
        group.MapPost("/remarks/{id:guid}/confirm-rejection", async (Guid id, CapConfirmInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ConfirmRemarkRejection(id, b.Confirm, b.Note), ct)).ToOk())
            .WithName("ConfirmCapRemarkRejection").RequirePermission(CapPermissions.Office);
        group.MapPost("/remarks/{id:guid}/reassign", async (Guid id, CapReassignInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ReassignRemark(id, b.OwnerEmployeeId), ct)).ToOk())
            .WithName("ReassignCapRemark").RequirePermission(CapPermissions.Read);
        group.MapPut("/remarks/{id:guid}/root-cause", async (Guid id, CapRootCauseBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveRootCause(id, b.Values, b.Version), ct)).ToOk())
            .WithName("SaveCapRootCause").RequirePermission(CapPermissions.Respond);
        group.MapPost("/remarks/{id:guid}/submit", async (Guid id, CapCommentInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitRemarkActions(id, b.Comment), ct)).ToOk())
            .WithName("SubmitCapRemarkActions").RequirePermission(CapPermissions.Respond).WithIdempotency();
        group.MapPost("/remarks/{id:guid}/cancel", async (Guid id, CapReasonInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CancelRemark(id, b.Reason), ct)).ToOk())
            .WithName("CancelCapRemark").RequirePermission(CapPermissions.Read);
        group.MapPost("/remarks/{id:guid}/verify", async (Guid id, CapVerifyInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new VerifyRemark(id, b.Effective, b.Comment), ct)).ToOk())
            .WithName("VerifyCapRemark").RequirePermission(CapPermissions.Office);
        group.MapGet("/remarks/{id:guid}/kpi-trend", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRemarkKpiTrend(id), ct)).ToOk())
            .WithName("GetCapRemarkKpiTrend").RequirePermission(CapPermissions.Read);
        group.MapGet("/remarks/{id:guid}/actions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRemarkActions(id), ct)).ToOk())
            .WithName("ListCapRemarkActions").RequirePermission(CapPermissions.Read);
        group.MapPost("/remarks/{id:guid}/actions", async (Guid id, ActionValues b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CreateAction(id, b), ct)).ToOk())
            .WithName("CreateCapAction").RequirePermission(CapPermissions.Respond);
        group.MapPost("/remarks/{id:guid}/link-action", async (Guid id, CapLinkActionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LinkAction(id, b.ActionId), ct)).ToOk())
            .WithName("LinkCapAction").RequirePermission(CapPermissions.Respond);
        group.MapPost("/remarks/{id:guid}/unlink-action", async (Guid id, CapLinkActionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UnlinkAction(id, b.ActionId), ct)).ToOk())
            .WithName("UnlinkCapAction").RequirePermission(CapPermissions.Respond);
        group.MapGet("/remarks/{id:guid}/linkable-actions", async (Guid id, string? search, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SearchLinkableActions(id, search), ct)).ToOk())
            .WithName("SearchCapLinkableActions").RequirePermission(CapPermissions.Read);
        group.MapGet("/remarks/{id:guid}/validation", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetValidationView(id), ct)).ToOk())
            .WithName("GetCapValidationView").RequirePermission(CapPermissions.Office);
        group.MapGroup("/remarks").MapRecordHistory(Remark.EntityType, CapPermissions.Read, "GetCapRemark", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetRemark(id), context.RequestAborted)).IsSuccess);

        // Actions
        group.MapGet("/actions", async (int? page, int? pageSize, string? view, string? search, string? status, Guid? unitId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListActions(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), view, search, status, unitId), ct)).ToOk())
            .WithName("ListCapActions").RequirePermission(CapPermissions.Read);
        group.MapGet("/actions/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAction(id), ct)).ToOk())
            .WithName("GetCapAction").RequirePermission(CapPermissions.Read);
        group.MapPut("/actions/{id:guid}", async (Guid id, ActionBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateAction(id, b.Values, b.Version ?? 0), ct)).ToOk())
            .WithName("UpdateCapAction").RequirePermission(CapPermissions.Respond);
        group.MapPost("/actions/{id:guid}/validate", async (Guid id, ValidateInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ValidateAction(id, b), ct)).ToOk())
            .WithName("ValidateCapAction").RequirePermission(CapPermissions.Office).WithIdempotency();
        group.MapPost("/actions/{id:guid}/link-project", async (Guid id, CapLinkProjectInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LinkActionProject(id, b.ProjectId), ct)).ToOk())
            .WithName("LinkCapActionProject").RequirePermission(CapPermissions.Office);
        group.MapPost("/actions/{id:guid}/progress", async (Guid id, ProgressInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateAchievement(id, b with { FromMcp = false }), ct)).ToOk())
            .WithName("UpdateCapActionAchievement").RequirePermission(CapPermissions.ActionUpdate);
        group.MapGet("/actions/{id:guid}/updates", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListAchievementUpdates(id), ct)).ToOk())
            .WithName("ListCapActionUpdates").RequirePermission(CapPermissions.Read);
        group.MapPost("/actions/{id:guid}/updates/{updateId:guid}/confirm", async (Guid id, Guid updateId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ConfirmAchievementDraft(id, updateId), ct)).ToOk())
            .WithName("ConfirmCapActionUpdate").RequirePermission(CapPermissions.ActionUpdate);
        group.MapPost("/actions/{id:guid}/request-completion", async (Guid id, CapNoteInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RequestActionCompletion(id, b.Note), ct)).ToOk())
            .WithName("RequestCapActionCompletion").RequirePermission(CapPermissions.ActionUpdate).WithIdempotency();
        group.MapGet("/actions/{id:guid}/extensions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListExtensions(id), ct)).ToOk())
            .WithName("ListCapActionExtensions").RequirePermission(CapPermissions.Read);
        group.MapPost("/actions/{id:guid}/extensions", async (Guid id, ExtensionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RequestActionExtension(id, b), ct)).ToOk())
            .WithName("RequestCapActionExtension").RequirePermission(CapPermissions.Read).WithIdempotency();
        group.MapPost("/actions/{id:guid}/cancel", async (Guid id, CapReasonInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CancelAction(id, b.Reason), ct)).ToOk())
            .WithName("CancelCapAction").RequirePermission(CapPermissions.Read);
        group.MapGroup("/actions").MapRecordHistory(CapAction.EntityType, CapPermissions.Read, "GetCapAction", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetAction(id), context.RequestAborted)).IsSuccess);
        group.MapGet("/projects", async (string? search, IServiceProvider services, IRequestContext context, CancellationToken ct) =>
            {
                if (services.GetService(typeof(IPortfolioDirectory)) is not IPortfolioDirectory portfolio)
                {
                    return TypedResults.Ok<IReadOnlyList<CapProjectOptionDto>>([]);
                }

                var rows = await portfolio.SearchProjectsAsync(search, context.User, 20, ct);
                return TypedResults.Ok<IReadOnlyList<CapProjectOptionDto>>(rows.Select(r => new CapProjectOptionDto(r.Id, r.Number, r.NameAr, r.NameEn, r.Status)).ToList());
            })
            .WithName("SearchCapProjects").RequirePermission(CapPermissions.Office);

        // Suggestions from other systems
        group.MapGet("/suggestions", async (int? page, int? pageSize, string? status, string? kind, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListSuggestions(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), status, kind), ct)).ToOk())
            .WithName("ListCapSuggestions").RequirePermission(CapPermissions.Office);
        group.MapPost("/suggestions/{id:guid}/raise", async (Guid id, CapRaiseInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RaiseSuggestion(id, b.UnitId), ct)).ToOk())
            .WithName("RaiseCapSuggestion").RequirePermission(CapPermissions.Office).WithIdempotency();
        group.MapPost("/suggestions/{id:guid}/dismiss", async (Guid id, CapReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new DismissSuggestion(id, b.Reason), ct)).ToOk())
            .WithName("DismissCapSuggestion").RequirePermission(CapPermissions.Office);

        // AI (drafts and advice only)
        var ai = group.MapGroup("/ai");
        ai.MapPost("/sources/{id:guid}/extract", async (Guid id, CapExtractInput b, CapAiService service, CancellationToken ct) =>
                (await service.ExtractAsync(id, b.Text, null, null, ct)).ToOk())
            .WithName("ExtractCapRemarksWithAi").RequirePermission(CapPermissions.AiExtract);
        ai.MapPost("/sources/{id:guid}/extract/document", ExtractFromDocumentAsync).WithName("ExtractCapRemarksFromDocumentWithAi").RequirePermission(CapPermissions.AiExtract)
            .DisableAntiforgery();
        ai.MapPost("/remarks/{id:guid}/root-cause", async (Guid id, CapAiService service, CancellationToken ct) => (await service.RootCauseAsync(id, ct)).ToOk())
            .WithName("DraftCapRootCauseWithAi").RequirePermission(CapPermissions.AiRootCause);
        ai.MapPost("/remarks/{id:guid}/suggestions", async (Guid id, CapAiService service, CancellationToken ct) => (await service.SuggestAsync(id, ct)).ToOk())
            .WithName("SuggestCapActionsWithAi").RequirePermission(CapPermissions.AiSuggest);
        ai.MapPost("/actions/{id:guid}/quality", async (Guid id, CapAiService service, CancellationToken ct) => (await service.QualityAsync(id, ct)).ToOk())
            .WithName("CheckCapActionQuality").RequirePermission(CapPermissions.AiQuality);
        ai.MapPost("/summary", async Task<Results<Ok<CapSummaryDraftDto>, ProblemHttpResult>> (CapSummaryInput b, IDispatcher d, CapAiService service, CancellationToken ct) =>
            {
                var dashboard = await d.SendAsync(new GetCapDashboard(b.UnitId, b.Year), ct);
                if (dashboard.IsFailure)
                {
                    return dashboard.Error.ToProblem();
                }

                return (await service.SummaryAsync(dashboard.Value, DateTime.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), ct)).ToOk();
            })
            .WithName("DraftCapSummaryWithAi").RequirePermission(CapPermissions.AiSummary);
    }

    private static async Task<Results<Ok<RemarkExtractionDto>, ProblemHttpResult>> ExtractFromDocumentAsync(
        Guid id, IFormFile file, string? text, CapAiService service, CancellationToken ct)
    {
        if (file.Length is 0 or > DocumentTextExtractor.MaxBytes)
        {
            return Error.Rule("ai.extract.size", "ai.errors.extractSize",
                new Dictionary<string, object?> { ["maxMb"] = DocumentTextExtractor.MaxBytes / 1024 / 1024 }).ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (await service.ExtractAsync(id, text, buffer.ToArray(), file.FileName, ct)).ToOk();
    }
}
