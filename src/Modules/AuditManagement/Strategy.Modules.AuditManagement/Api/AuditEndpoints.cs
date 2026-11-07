using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.AuditManagement.Ai;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Integration;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Api;

public sealed record AuditSettingsInput(AuditMgmtSettings Settings, uint Version);

public sealed record AuditExtractInput(string? Text);

public sealed record AuditSummaryInput(Guid? UnitId, int? Year);

public sealed record AuditFollowUpInput(FollowUpValues Values);

public sealed record AuditRiskOptionDto(Guid Id, string Number, string? TitleAr, string? TitleEn, string Status, string? ResidualBand);

public sealed record AuditControlOptionDto(Guid Id, string? NameAr, string? NameEn, string? EffectivenessCode);

public sealed record AuditDocumentOptionDto(Guid Id, string? DocumentNo, string? NameAr, string? NameEn, string Status);

public sealed record AuditWorkPaperActionInput(string Action);

/// <summary>
/// <c>/api/v1/auditmgmt</c> (docs/design/audit.md §11). Reads declare <c>auditmgmt.finding.read</c> and filter by scope (404 outside it); every write
/// declares <c>auditmgmt.office.manage</c>, so department users get 403 (ADR 0132). Layer 2 routes answer 404 while the switch is off (ADR 0136).
/// </summary>
internal static class AuditEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(RouteGroupBuilder group)
    {
        const string Read = AuditPermissions.Read;
        const string Office = AuditPermissions.Office;
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditOptions(), ct)).ToOk())
            .WithName("GetAuditOptions").RequirePermission(Read);
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditSettings(), ct)).ToOk())
            .WithName("GetAuditSettings").RequirePermission(AuditPermissions.SettingsManage);
        group.MapPut("/settings", async (AuditSettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveAuditSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveAuditSettings").RequirePermission(AuditPermissions.SettingsManage);
        group.MapGet("/dashboard", async (Guid? unitId, int? year, string? priority, string? reportType, Guid? reportId, DateOnly? from, DateOnly? to, IDispatcher d,
                    CancellationToken ct) =>
                (await d.SendAsync(new GetAuditDashboard(new AuditDashboardFilter(unitId, year, priority, reportType, reportId, from, to)), ct)).ToOk())
            .WithName("GetAuditDashboard").RequirePermission(AuditPermissions.DashboardRead);
        group.MapGet("/department-view", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditDepartmentView(), ct)).ToOk())
            .WithName("GetAuditDepartmentView").RequirePermission(Read);
        group.MapGet("/calendar", async (DateOnly? from, DateOnly? to, Guid? auditorEmployeeId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetAuditCalendar(from, to, auditorEmployeeId), ct)).ToOk())
            .WithName("GetAuditCalendar").RequirePermission(Office);
        group.MapPost("/exports/{kind}", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (string kind, AuditExportInput b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new ExportAuditReport(kind, b), ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("ExportAuditReport").RequirePermission(AuditPermissions.Export);

        // Reports
        group.MapGet("/reports", async (int? page, int? pageSize, string? search, string? type, string? status, int? year, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListAuditReports(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), search, type, status, year), ct)).ToOk())
            .WithName("ListAuditReports").RequirePermission(Read);
        group.MapPost("/reports", async Task<Results<Created<AuditReportDto>, ProblemHttpResult>> (AuditReportValues b, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateAuditReport(b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/auditmgmt/reports/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateAuditReport").RequirePermission(Office);
        group.MapGet("/reports/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditReport(id), ct)).ToOk())
            .WithName("GetAuditReport").RequirePermission(Read);
        group.MapPut("/reports/{id:guid}", async (Guid id, AuditReportBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateAuditReport(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateAuditReport").RequirePermission(Office);
        group.MapByExternal("/reports", AuditReport.EntityType, Read, "AuditReport", r => r.Id, (id, http) => http.Dispatch(new GetAuditReport(id)))
            .WithUpsert<AuditReportValues>(Office, Office, (b, http) => http.Dispatch(new CreateAuditReport(b)),
                (id, b, current, http) => http.Dispatch(new UpdateAuditReport(id, b, current.Version)));
        group.MapPost("/reports/{id:guid}/issue", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new IssueAuditReport(id), ct)).ToOk())
            .WithName("IssueAuditReport").RequirePermission(Office).WithIdempotency();
        group.MapPost("/reports/{id:guid}/cancel", async (Guid id, AuditReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelAuditReport(id, b.Reason), ct)).ToOk())
            .WithName("CancelAuditReport").RequirePermission(Office);
        group.MapPost("/reports/{id:guid}/findings", async Task<Results<Created<AuditFindingDto>, ProblemHttpResult>> (Guid id, NewFindingInput b, IDispatcher d,
                CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateAuditFinding(id, b), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/auditmgmt/findings/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateAuditFinding").RequirePermission(Office);
        group.MapPost("/reports/{id:guid}/findings/batch", async (Guid id, AuditBatchInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CreateAuditFindingsBatch(id, b.Findings), ct)).ToOk())
            .WithName("CreateAuditFindingsBatch").RequirePermission(Office).WithIdempotency();
        group.MapGet("/import/template", () => TypedResults.File(AuditImporter.Template(), XlsxContentType, "audit-findings-template.xlsx"))
            .WithName("GetAuditImportTemplate").RequirePermission(Office);
        group.MapPost("/reports/{id:guid}/import", ImportAsync).WithName("ImportAuditFindings").RequirePermission(Office).DisableAntiforgery();
        group.MapPost("/reports/{id:guid}/import/report", ImportReportAsync).WithName("GetAuditImportReport").RequirePermission(Office).DisableAntiforgery();
        group.MapGroup("/reports").MapRecordHistory(AuditReport.EntityType, Read, "GetAuditReport", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetAuditReport(id), context.RequestAborted)).IsSuccess);

        // Findings
        group.MapGet("/findings", async (int? page, int? pageSize, string? sort, string? view, string? search, Guid? reportId, string? status, string? priority, Guid? unitId,
                    int? year, string? reportType, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListAuditFindings(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, search, reportId, status, priority,
                    unitId, year, reportType), ct)).ToOk())
            .WithName("ListAuditFindings").RequirePermission(Read);
        group.MapGet("/findings/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditFinding(id), ct)).ToOk())
            .WithName("GetAuditFinding").RequirePermission(Read);
        group.MapPut("/findings/{id:guid}", async (Guid id, AuditFindingBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateAuditFinding(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateAuditFinding").RequirePermission(Office);
        group.MapByExternal("/findings", Finding.EntityType, Read, "AuditFinding", f => f.Id, (id, http) => http.Dispatch(new GetAuditFinding(id)))
            .WithUpsert<AuditFindingUpsertInput>(Office, Office,
                (b, http) => http.Dispatch(new CreateAuditFinding(b.ReportId, new NewFindingInput(b.Values, b.Recommendations))),
                (id, b, current, http) => http.Dispatch(new UpdateAuditFinding(id, b.Values, current.Version)));
        group.MapPost("/findings/{id:guid}/cancel", async (Guid id, AuditReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelAuditFinding(id, b.Reason), ct)).ToOk())
            .WithName("CancelAuditFinding").RequirePermission(Office);
        group.MapGet("/findings/{id:guid}/recommendations", async (Guid id, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListAuditRecommendations(id), ct)).ToOk())
            .WithName("ListAuditRecommendations").RequirePermission(Read);
        group.MapPost("/findings/{id:guid}/recommendations", async (Guid id, RecommendationValues b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CreateAuditRecommendation(id, b), ct)).ToOk())
            .WithName("CreateAuditRecommendation").RequirePermission(Office);
        group.MapGet("/findings/{id:guid}/similar", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListSimilarFindings(id), ct)).ToOk())
            .WithName("ListSimilarAuditFindings").RequirePermission(Office);
        group.MapPost("/findings/{id:guid}/risks", async (Guid id, AuditRiskLinkInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new LinkAuditFindingRisk(id, b), ct)).ToOk())
            .WithName("LinkAuditFindingRisk").RequirePermission(Office);
        group.MapPost("/findings/{id:guid}/risks/{linkId:guid}/remove", async (Guid id, Guid linkId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UnlinkAuditFindingRisk(id, linkId), ct)).ToOk())
            .WithName("UnlinkAuditFindingRisk").RequirePermission(Office);
        group.MapPost("/findings/{id:guid}/send-to-cap", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SendAuditFindingToCap(id), ct)).ToOk())
            .WithName("SendAuditFindingToCap").RequirePermission(Office).WithIdempotency();
        group.MapGroup("/findings").MapRecordHistory(Finding.EntityType, Read, "GetAuditFinding", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetAuditFinding(id), context.RequestAborted)).IsSuccess);

        // Recommendations and follow-ups
        group.MapGet("/recommendations/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAuditRecommendation(id), ct)).ToOk())
            .WithName("GetAuditRecommendation").RequirePermission(Read);
        group.MapPut("/recommendations/{id:guid}", async (Guid id, AuditRecommendationBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateAuditRecommendation(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateAuditRecommendation").RequirePermission(Office);
        group.MapPost("/recommendations/{id:guid}/revise", async (Guid id, AuditReviseInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ReviseAuditRecommendation(id, b.NewDueOn, b.Reason), ct)).ToOk())
            .WithName("ReviseAuditRecommendation").RequirePermission(Office);
        group.MapPost("/recommendations/{id:guid}/accept-risk", async (Guid id, AuditRiskAcceptInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AcceptAuditRecommendationRisk(id, b.Justification, b.Reference), ct)).ToOk())
            .WithName("AcceptAuditRecommendationRisk").RequirePermission(Office);
        group.MapPost("/recommendations/{id:guid}/reopen", async (Guid id, AuditReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ReopenAuditRecommendation(id, b.Reason), ct)).ToOk())
            .WithName("ReopenAuditRecommendation").RequirePermission(Office);
        group.MapPost("/recommendations/{id:guid}/cancel", async (Guid id, AuditReasonInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelAuditRecommendation(id, b.Reason), ct)).ToOk())
            .WithName("CancelAuditRecommendation").RequirePermission(Office);
        group.MapPost("/recommendations/{id:guid}/follow-ups", async (Guid id, FollowUpValues b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RecordAuditFollowUp(id, b), ct)).ToOk())
            .WithName("RecordAuditFollowUp").RequirePermission(Office).WithIdempotency();

        // Pickers for the integrations (results filtered by the caller's own access in the other system)
        group.MapGet("/lookups/risks", async (string? search, IServiceProvider services, IRequestContext context, CancellationToken ct) =>
            {
                if (services.GetService(typeof(Risk.Contracts.IRiskDirectory)) is not Risk.Contracts.IRiskDirectory risks)
                {
                    return TypedResults.Ok<IReadOnlyList<AuditRiskOptionDto>>([]);
                }

                var rows = await risks.SearchAsync(search, context.User, 20, ct);
                return TypedResults.Ok<IReadOnlyList<AuditRiskOptionDto>>(rows.Select(r => new AuditRiskOptionDto(r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status, r.ResidualBand))
                    .ToList());
            })
            .WithName("SearchAuditRiskOptions").RequirePermission(Office);
        group.MapGet("/lookups/risks/{riskId:guid}/controls", async (Guid riskId, IServiceProvider services, IRequestContext context, CancellationToken ct) =>
            {
                if (services.GetService(typeof(Risk.Contracts.IRiskDirectory)) is not Risk.Contracts.IRiskDirectory risks)
                {
                    return TypedResults.Ok<IReadOnlyList<AuditControlOptionDto>>([]);
                }

                var rows = await risks.ListControlsAsync(riskId, context.User, ct);
                return TypedResults.Ok<IReadOnlyList<AuditControlOptionDto>>(rows.Select(c => new AuditControlOptionDto(c.Id, c.NameAr, c.NameEn, c.EffectivenessCode)).ToList());
            })
            .WithName("ListAuditRiskControlOptions").RequirePermission(Office);
        group.MapGet("/lookups/documents", async (string? search, IServiceProvider services, IRequestContext context, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(search) || services.GetService(typeof(DocumentRegistry.Contracts.IDocumentDirectory)) is not
                    DocumentRegistry.Contracts.IDocumentDirectory documents)
                {
                    return TypedResults.Ok<IReadOnlyList<AuditDocumentOptionDto>>([]);
                }

                var rows = await documents.SearchAsync(search, context.User, 20, ct);
                return TypedResults.Ok<IReadOnlyList<AuditDocumentOptionDto>>(rows.Select(r => new AuditDocumentOptionDto(r.Id, r.DocumentNo, r.NameAr, r.NameEn, r.Status))
                    .ToList());
            })
            .WithName("SearchAuditDocumentOptions").RequirePermission(Office);

        // AI (drafts and advice only — nothing is saved)
        var ai = group.MapGroup("/ai");
        ai.MapPost("/reports/{id:guid}/extract", async (Guid id, AuditExtractInput b, AuditAiService service, CancellationToken ct) =>
                (await service.ExtractAsync(id, b.Text, null, null, ct)).ToOk())
            .WithName("ExtractAuditFindingsWithAi").RequirePermission(AuditPermissions.AiExtract);
        ai.MapPost("/reports/{id:guid}/extract/document", ExtractFromDocumentAsync).WithName("ExtractAuditFindingsFromDocumentWithAi")
            .RequirePermission(AuditPermissions.AiExtract).DisableAntiforgery();
        ai.MapPost("/recommendations/{id:guid}/evidence-review", EvidenceReviewAsync).WithName("ReviewAuditEvidenceWithAi")
            .RequirePermission(AuditPermissions.AiEvidence).DisableAntiforgery();
        ai.MapPost("/findings/{id:guid}/repeat", async (Guid id, AuditAiService service, CancellationToken ct) => (await service.RepeatAsync(id, ct)).ToOk())
            .WithName("CheckAuditRepeatFindingWithAi").RequirePermission(AuditPermissions.AiRepeat);
        ai.MapPost("/summary", async Task<Results<Ok<AuditSummaryDraftDto>, ProblemHttpResult>> (AuditSummaryInput b, IDispatcher d, AuditAiService service,
                CancellationToken ct) =>
            {
                var dashboard = await d.SendAsync(new GetAuditDashboard(new AuditDashboardFilter(b.UnitId, b.Year, null, null, null)), ct);
                if (dashboard.IsFailure)
                {
                    return dashboard.Error.ToProblem();
                }

                return (await service.SummaryAsync(dashboard.Value, ct)).ToOk();
            })
            .WithName("DraftAuditCommitteeSummaryWithAi").RequirePermission(AuditPermissions.AiSummary);

        MapLifecycle(group.MapGroup("/lifecycle"));
    }

    /// <summary>Layer 2 (ADR 0136): 404 while the switch is off.</summary>
    private static void MapLifecycle(RouteGroupBuilder lifecycle)
    {
        const string Manage = AuditPermissions.Lifecycle;
        lifecycle.MapGet("/universe", async (AuditLifecycle l, CancellationToken ct) => (await l.UniverseAsync(ct)).ToOk())
            .WithName("ListAuditUniverse").RequireLifecycle(Manage);
        lifecycle.MapPost("/universe", async (AuditableEntityValues b, AuditLifecycle l, CancellationToken ct) => (await l.SaveEntityAsync(null, b, 0, ct)).ToOk())
            .WithName("CreateAuditableEntity").RequireLifecycle(Manage);
        lifecycle.MapPut("/universe/{id:guid}", async (Guid id, AuditEntityBody b, AuditLifecycle l, CancellationToken ct) => (await l.SaveEntityAsync(id, b.Values, b.Version, ct)).ToOk())
            .WithName("UpdateAuditableEntity").RequireLifecycle(Manage);
        lifecycle.MapPost("/universe/sync", async (AuditLifecycle l, CancellationToken ct) => (await l.SyncUniverseAsync(ct)).ToOk())
            .WithName("SyncAuditUniverse").RequireLifecycle(Manage);
        lifecycle.MapGet("/plans", async (AuditLifecycle l, CancellationToken ct) => (await l.PlansAsync(ct)).ToOk())
            .WithName("ListAuditPlans").RequireLifecycle(AuditPermissions.PlanRead);
        lifecycle.MapPost("/plans", async (AuditPlanInput b, AuditLifecycle l, CancellationToken ct) => (await l.CreatePlanAsync(b, ct)).ToOk())
            .WithName("CreateAuditPlan").RequireLifecycle(Manage);
        lifecycle.MapGet("/plans/{id:guid}", async (Guid id, AuditLifecycle l, CancellationToken ct) => (await l.PlanAsync(id, ct)).ToOk())
            .WithName("GetAuditPlan").RequireLifecycle(AuditPermissions.PlanRead);
        lifecycle.MapPut("/plans/{id:guid}", async (Guid id, AuditPlanBody b, AuditLifecycle l, CancellationToken ct) => (await l.UpdatePlanAsync(id, b, ct)).ToOk())
            .WithName("UpdateAuditPlan").RequireLifecycle(Manage);
        lifecycle.MapPost("/plans/{id:guid}/submit", async (Guid id, AuditReasonInput b, AuditLifecycle l, CancellationToken ct) =>
                (await l.SubmitPlanAsync(id, b.Reason, ct)).ToOk())
            .WithName("SubmitAuditPlan").RequireLifecycle(Manage).WithIdempotency();
        lifecycle.MapGet("/teams", async (string? search, AuditLifecycle l, CancellationToken ct) => (await l.TeamsAsync(search, ct)).ToOk())
            .WithName("SearchAuditEngagementTeams").RequireLifecycle(Manage);
        lifecycle.MapGet("/engagements", async (Guid? planId, AuditLifecycle l, CancellationToken ct) => (await l.EngagementsAsync(planId, ct)).ToOk())
            .WithName("ListAuditEngagements").RequireLifecycle(Manage);
        lifecycle.MapPost("/engagements", async (AuditEngagementInput b, AuditLifecycle l, CancellationToken ct) => (await l.CreateEngagementAsync(b, ct)).ToOk())
            .WithName("CreateAuditEngagement").RequireLifecycle(Manage);
        lifecycle.MapGet("/engagements/{id:guid}", async (Guid id, AuditLifecycle l, CancellationToken ct) => (await l.EngagementAsync(id, ct)).ToOk())
            .WithName("GetAuditEngagement").RequireLifecycle(Manage);
        lifecycle.MapPut("/engagements/{id:guid}", async (Guid id, AuditEngagementBody b, AuditLifecycle l, CancellationToken ct) =>
                (await l.UpdateEngagementAsync(id, b, ct)).ToOk())
            .WithName("UpdateAuditEngagement").RequireLifecycle(Manage);
        lifecycle.MapPost("/engagements/{id:guid}/transition", async (Guid id, AuditTransitionInput b, AuditLifecycle l, CancellationToken ct) =>
                (await l.TransitionAsync(id, b, ct)).ToOk())
            .WithName("TransitionAuditEngagement").RequireLifecycle(Manage);
        lifecycle.MapPost("/engagements/{id:guid}/survey", async (Guid id, AuditSurveyInput b, AuditLifecycle l, CancellationToken ct) => (await l.SurveyAsync(id, b, ct)).ToOk())
            .WithName("RecordAuditEngagementSurvey").RequireLifecycle(Manage);
        lifecycle.MapGet("/engagements/{id:guid}/work-papers", async (Guid id, AuditLifecycle l, CancellationToken ct) => (await l.WorkPapersAsync(id, ct)).ToOk())
            .WithName("ListAuditWorkPapers").RequireLifecycle(AuditPermissions.WorkPapers);
        lifecycle.MapPost("/engagements/{id:guid}/work-papers", async (Guid id, WorkPaperValues b, AuditLifecycle l, CancellationToken ct) =>
                (await l.SaveWorkPaperAsync(id, null, b, 0, ct)).ToOk())
            .WithName("CreateAuditWorkPaper").RequireLifecycle(AuditPermissions.WorkPapers);
        lifecycle.MapPut("/engagements/{id:guid}/work-papers/{paperId:guid}", async (Guid id, Guid paperId, AuditWorkPaperBody b, AuditLifecycle l, CancellationToken ct) =>
                (await l.SaveWorkPaperAsync(id, paperId, b.Values, b.Version, ct)).ToOk())
            .WithName("UpdateAuditWorkPaper").RequireLifecycle(AuditPermissions.WorkPapers);
        lifecycle.MapPost("/engagements/{id:guid}/work-papers/{paperId:guid}/action", async (Guid id, Guid paperId, AuditWorkPaperActionInput b, AuditLifecycle l,
                CancellationToken ct) => (await l.WorkPaperActionAsync(id, paperId, b.Action, ct)).ToOk())
            .WithName("ActOnAuditWorkPaper").RequireLifecycle(AuditPermissions.WorkPapers);
    }

    private static async Task<Results<Ok<AuditImportReportDto>, ProblemHttpResult>> ImportAsync(
        Guid id, string? mode, IFormFile file, AuditImporter importer, AuditReader reader, IRequestContext context, CancellationToken ct)
    {
        if (!await reader.IsOfficeAsync(context.User, ct))
        {
            return Error.Forbidden().ToProblem();
        }

        if (context is RequestContext mutable)
        {
            mutable.SetChannel(Channels.Import);
        }

        await using var stream = file.OpenReadStream();
        var result = await importer.RunAsync(id, stream, file.FileName, mode == "commit", ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value.Report) : result.Error.ToProblem();
    }

    private static async Task<IResult> ImportReportAsync(Guid id, IFormFile file, AuditImporter importer, AuditReader reader, IRequestContext context, CancellationToken ct)
    {
        if (!await reader.IsOfficeAsync(context.User, ct))
        {
            return Error.Forbidden().ToProblem();
        }

        await using var stream = file.OpenReadStream();
        var result = await importer.RunAsync(id, stream, file.FileName, false, ct);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var errors = result.Value.Report.Issues.ToLookup(i => i.Row, i => i.Column is null ? i.Code : $"{i.Column}: {i.Code}");
        return TypedResults.File(Platform.Organisation.Import.ImportTable.Report(AuditImporter.SheetName, AuditImporter.Columns, result.Value.Rows, errors), XlsxContentType,
            "audit-findings-validation-report.xlsx");
    }

    private static async Task<Results<Ok<FindingExtractionDto>, ProblemHttpResult>> ExtractFromDocumentAsync(
        Guid id, IFormFile file, string? text, AuditAiService service, CancellationToken ct)
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

    private static async Task<Results<Ok<EvidenceReviewDto>, ProblemHttpResult>> EvidenceReviewAsync(
        Guid id, [Microsoft.AspNetCore.Mvc.FromForm] decimal pct, [Microsoft.AspNetCore.Mvc.FromForm] string? actionsImplemented,
        [Microsoft.AspNetCore.Mvc.FromForm] string? evidenceText, IFormFile? file, AuditAiService service, CancellationToken ct)
    {
        byte[]? bytes = null;
        if (file is { Length: > 0 })
        {
            if (file.Length > DocumentTextExtractor.MaxBytes)
            {
                return Error.Rule("ai.extract.size", "ai.errors.extractSize",
                    new Dictionary<string, object?> { ["maxMb"] = DocumentTextExtractor.MaxBytes / 1024 / 1024 }).ToProblem();
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        return (await service.EvidenceAsync(id, new EvidenceReviewInput(pct, actionsImplemented, evidenceText), bytes, file?.FileName, ct)).ToOk();
    }
}

internal static class LifecycleEndpointExtensions
{
    /// <summary>The permission first (403 for users without it), then the Layer 2 switch (404 problem <c>auditmgmt.lifecycleOff</c> while off).</summary>
    public static RouteHandlerBuilder RequireLifecycle(this RouteHandlerBuilder builder, string permission)
    {
        builder.RequirePermission(permission);
        builder.AddEndpointFilter(async (context, next) =>
        {
            var features = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>();
            return await features.IsEnabledAsync(AuditPermissions.LifecycleFeature, context.HttpContext.RequestAborted).ConfigureAwait(false)
                ? await next(context).ConfigureAwait(false)
                : Error.NotFound("auditmgmt.lifecycleOff").ToProblem();
        });
        return builder;
    }
}
