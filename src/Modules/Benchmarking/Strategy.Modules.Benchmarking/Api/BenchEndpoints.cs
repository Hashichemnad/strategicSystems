using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Benchmarking.Ai;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Benchmarking.Api;

public sealed record BenchCommentInput(string? Comment);

public sealed record BenchReasonInput(string? Reason);

public sealed record BenchTeamInput(Guid? TeamId);

public sealed record BenchCandidatesInput(IReadOnlyList<BenchCandidateInput> Candidates);

public sealed record BenchVisitsInput(IReadOnlyList<BenchVisitInput> Visits);

public sealed record BenchSettingsInput(BenchSettings Settings, uint Version);

public sealed record BenchQuestionnaireInput(Guid? ComparatorId);

public sealed record BenchNotesInput(string? Notes);

public sealed record BenchQuestionInput(string? Question);

/// <summary>
/// <c>/api/v1/bench</c> (docs/design/benchmarking.md). New studies start from the Request catalogue (<c>bench.request</c>). Handlers check the record
/// itself (404 outside the read scope, 403 for a visible record and a forbidden action).
/// </summary>
internal static class BenchEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetBenchOptions(), ct)).ToOk())
            .WithName("GetBenchOptions").RequirePermission(BenchPermissions.Read);
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetBenchSettings(), ct)).ToOk())
            .WithName("GetBenchSettings").RequirePermission(BenchPermissions.SettingsManage);
        group.MapPut("/settings", async (BenchSettingsInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveBenchSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SaveBenchSettings").RequirePermission(BenchPermissions.SettingsManage);
        group.MapGet("/dashboard", async (int? year, Guid? unitId, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetBenchDashboard(year, unitId, from, to), ct)).ToOk())
            .WithName("GetBenchDashboard").RequirePermission(BenchPermissions.DashboardRead);
        group.MapGet("/plan", async (int year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetBenchPlan(year), ct)).ToOk())
            .WithName("GetBenchPlan").RequirePermission(BenchPermissions.PlanRead);
        group.MapPost("/plan/{year:int}/circulate", async (int year, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CirculateBenchPlan(year), ct)).ToOk())
            .WithName("CirculateBenchPlan").RequirePermission(BenchPermissions.Office);

        // Studies
        group.MapGet("/studies", async (int? page, int? pageSize, string? view, string? search, int? year, int? quarter, string? stage, string? status, Guid? unitId,
                    IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListStudies(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), view, search, year, quarter, stage, status, unitId), ct))
                .ToOk())
            .WithName("ListBenchStudies").RequirePermission(BenchPermissions.Read);
        group.MapGet("/studies/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudy(id), ct)).ToOk())
            .WithName("GetBenchStudy").RequirePermission(BenchPermissions.Read);
        group.MapGet("/studies/{id:guid}/search", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudySearch(id), ct)).ToOk())
            .WithName("GetBenchStudySearch").RequirePermission(BenchPermissions.Read);
        group.MapPut("/studies/{id:guid}/search", async (Guid id, BenchCandidatesInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveStudySearch(id, b.Candidates), ct)).ToOk())
            .WithName("SaveBenchStudySearch").RequirePermission(BenchPermissions.Edit);
        group.MapPost("/studies/{id:guid}/search/submit", async (Guid id, BenchCommentInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitStudySearch(id, b.Comment), ct)).ToOk())
            .WithName("SubmitBenchStudySearch").RequirePermission(BenchPermissions.Edit).WithIdempotency();
        group.MapPost("/studies/{id:guid}/outreach", async (Guid id, BenchOutreachInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AddStudyOutreach(id, b), ct)).ToOk())
            .WithName("AddBenchStudyOutreach").RequirePermission(BenchPermissions.Edit);
        group.MapPost("/studies/{id:guid}/ratings", async (Guid id, BenchRatingInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RateStudyComparator(id, b), ct)).ToOk())
            .WithName("RateBenchStudyComparator").RequirePermission(BenchPermissions.Edit);
        group.MapGet("/studies/{id:guid}/visits", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudyVisits(id), ct)).ToOk())
            .WithName("GetBenchStudyVisits").RequirePermission(BenchPermissions.Read);
        group.MapPut("/studies/{id:guid}/visits", async (Guid id, BenchVisitsInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveStudyVisits(id, b.Visits), ct)).ToOk())
            .WithName("SaveBenchStudyVisits").RequirePermission(BenchPermissions.Edit);
        group.MapPost("/studies/{id:guid}/send-to-analysis", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SendStudyToAnalysis(id), ct)).ToOk())
            .WithName("SendBenchStudyToAnalysis").RequirePermission(BenchPermissions.Edit);
        group.MapGet("/studies/{id:guid}/analysis", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudyAnalysis(id), ct)).ToOk())
            .WithName("GetBenchStudyAnalysis").RequirePermission(BenchPermissions.Read);
        group.MapPut("/studies/{id:guid}/analysis", async (Guid id, BenchAnalysisInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveStudyAnalysis(id, b), ct)).ToOk())
            .WithName("SaveBenchStudyAnalysis").RequirePermission(BenchPermissions.Edit);
        group.MapPost("/studies/{id:guid}/analysis/submit", async (Guid id, BenchCommentInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitStudyAnalysis(id, b.Comment), ct)).ToOk())
            .WithName("SubmitBenchStudyAnalysis").RequirePermission(BenchPermissions.Edit).WithIdempotency();
        group.MapGet("/studies/{id:guid}/actions", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudyActions(id), ct)).ToOk())
            .WithName("GetBenchStudyActions").RequirePermission(BenchPermissions.Read);
        group.MapPost("/studies/{id:guid}/request-closure", async (Guid id, BenchCommentInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RequestStudyClosure(id, b.Comment), ct)).ToOk())
            .WithName("RequestBenchStudyClosure").RequirePermission(BenchPermissions.Edit).WithIdempotency();
        group.MapPost("/studies/{id:guid}/cancel", async (Guid id, BenchReasonInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new CancelStudy(id, b.Reason), ct)).ToOk())
            .WithName("CancelBenchStudy").RequirePermission(BenchPermissions.Office);
        group.MapPost("/studies/{id:guid}/team", async (Guid id, BenchTeamInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new LinkStudyTeam(id, b.TeamId), ct)).ToOk())
            .WithName("LinkBenchStudyTeam").RequirePermission(BenchPermissions.Edit);
        group.MapGet("/studies/{id:guid}/impact", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetStudyImpact(id), ct)).ToOk())
            .WithName("GetBenchStudyImpact").RequirePermission(BenchPermissions.Read);
        group.MapPost("/studies/{id:guid}/impact", async (Guid id, BenchImpactInput b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new RecordStudyImpact(id, b), ct)).ToOk())
            .WithName("RecordBenchStudyImpact").RequirePermission(BenchPermissions.Read);
        group.MapGroup("/studies").MapRecordHistory(Study.EntityType, BenchPermissions.Read, "GetBenchStudy", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetStudy(id), context.RequestAborted)).IsSuccess);

        // Comparator register and best-practice library
        group.MapGet("/comparators", async (int? page, int? pageSize, string? search, string? scope, string? sector, bool? includeInactive, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListComparators(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), search, scope, sector, includeInactive ?? false), ct))
                .ToOk())
            .WithName("ListBenchComparators").RequirePermission(BenchPermissions.ComparatorRead);
        group.MapGet("/comparators/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetComparator(id), ct)).ToOk())
            .WithName("GetBenchComparator").RequirePermission(BenchPermissions.ComparatorRead);
        group.MapPost("/comparators", async (BenchComparatorBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveComparator(null, b.Values, null), ct)).ToOk())
            .WithName("CreateBenchComparator").RequirePermission(BenchPermissions.ComparatorManage);
        group.MapPut("/comparators/{id:guid}", async (Guid id, BenchComparatorBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveComparator(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateBenchComparator").RequirePermission(BenchPermissions.ComparatorManage);
        group.MapGet("/practices", async (int? page, int? pageSize, string? search, string? topic, string? sector, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SearchBestPractices(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), search, topic, sector), ct)).ToOk())
            .WithName("SearchBenchPractices").RequirePermission(BenchPermissions.PracticeRead);
        group.MapPost("/practices", async (BenchPracticeBody b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new SaveBestPractice(null, b.Values, null), ct)).ToOk())
            .WithName("CreateBenchPractice").RequirePermission(BenchPermissions.PracticeManage);
        group.MapPut("/practices/{id:guid}", async (Guid id, BenchPracticeBody b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SaveBestPractice(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateBenchPractice").RequirePermission(BenchPermissions.PracticeManage);

        // AI (drafts and advice only)
        var ai = group.MapGroup("/ai");
        ai.MapPost("/studies/{id:guid}/comparators", async (Guid id, BenchAiService service, CancellationToken ct) => (await service.ComparatorsAsync(id, ct)).ToOk())
            .WithName("SuggestBenchComparatorsWithAi").RequirePermission(BenchPermissions.AiComparators);
        ai.MapPost("/studies/{id:guid}/questionnaire", async (Guid id, BenchQuestionnaireInput b, BenchAiService service, CancellationToken ct) =>
                (await service.QuestionnaireAsync(id, b.ComparatorId, ct)).ToOk())
            .WithName("DraftBenchQuestionnaireWithAi").RequirePermission(BenchPermissions.AiQuestionnaire);
        ai.MapPost("/studies/{id:guid}/analysis", async (Guid id, BenchNotesInput b, BenchAiService service, CancellationToken ct) => (await service.AnalysisAsync(id, b.Notes, ct)).ToOk())
            .WithName("AnalyseBenchVisitNotesWithAi").RequirePermission(BenchPermissions.AiAnalysis);
        ai.MapPost("/studies/{id:guid}/summary", async (Guid id, BenchAiService service, CancellationToken ct) => (await service.SummaryAsync(id, ct)).ToOk())
            .WithName("SummariseBenchStudyWithAi").RequirePermission(BenchPermissions.AiSummary);
        ai.MapPost("/practices/search", async (BenchQuestionInput b, BenchAiService service, CancellationToken ct) => (await service.SearchAsync(b.Question, ct)).ToOk())
            .WithName("SearchBenchPracticesWithAi").RequirePermission(BenchPermissions.AiSearch);
    }
}
