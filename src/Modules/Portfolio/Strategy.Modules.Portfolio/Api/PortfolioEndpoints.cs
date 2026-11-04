using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Portfolio.Api;

public sealed record RequestInput(RequestValues Values, string? Source = null);

public sealed record RequestUpdateInput(RequestValues Values, uint Version);

public sealed record RequestVersionInput(uint Version, string? Comment = null);

public sealed record CancelRequestInput(string? Reason, uint Version);

public sealed record ClassificationInput(string? Value, string? Reason, uint Version);

public sealed record ApprovedBudgetInput(decimal Amount, uint Version);

public sealed record PortfolioSettingsInput(PortfolioSettings Settings, uint Version);

/// <summary>
/// <c>/api/v1/portfolio</c> (docs/design/portfolio.md §11). Endpoints declare the permission needed to use them at all; handlers check the
/// record itself (404 outside the read scope, 403 for a visible record and a forbidden action).
/// </summary>
internal static class PortfolioEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/options", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetPortfolioOptions(), ct)).ToOk())
            .WithName("GetPortfolioOptions").RequirePermission(PortfolioPermissions.RequestRead);

        var requests = group.MapGroup("/requests");
        requests.MapGet("/", async (int? page, int? pageSize, string? sort, string? view, string? status, string? classification, string? priorityBand, Guid? unitId,
                    string? search, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListRequests(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, status, classification, priorityBand,
                    unitId, search), ct)).ToOk())
            .WithName("ListProgramRequests").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/", async Task<Results<Created<RequestDto>, ProblemHttpResult>> (RequestInput body, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(new CreateRequest(body.Values, body.Source), ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/portfolio/requests/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateProgramRequest").RequirePermission(PortfolioPermissions.RequestCreate);
        requests.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetRequest(id), ct)).ToOk())
            .WithName("GetProgramRequest").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPut("/{id:guid}", async (Guid id, RequestUpdateInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateRequest(id, b.Values, b.Version), ct)).ToOk())
            .WithName("UpdateProgramRequest").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/submit", async (Guid id, RequestVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SubmitRequest(id, b.Version, b.Comment), ct)).ToOk())
            .WithName("SubmitProgramRequest").RequirePermission(PortfolioPermissions.RequestRead).WithIdempotency();
        requests.MapPost("/{id:guid}/cancel", async (Guid id, CancelRequestInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelRequest(id, b.Reason, b.Version), ct)).ToOk())
            .WithName("CancelProgramRequest").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/classification", async (Guid id, ClassificationInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new OverrideClassification(id, b.Value, b.Reason, b.Version), ct)).ToOk())
            .WithName("OverrideRequestClassification").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/approved-budget", async (Guid id, ApprovedBudgetInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SetApprovedBudget(id, b.Amount, b.Version), ct)).ToOk())
            .WithName("SetRequestApprovedBudget").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/hand-off", async (Guid id, RequestVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new HandOffRequest(id, b.Version), ct)).ToOk())
            .WithName("HandOffProgramRequest").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/quotations-uploaded", async (Guid id, RequestVersionInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ConfirmQuotations(id, b.Version), ct)).ToOk())
            .WithName("ConfirmRequestQuotations").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapGet("/{id:guid}/files", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListRequestFiles(id), ct)).ToOk())
            .WithName("ListProgramRequestFiles").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/files/{kind}", async (Guid id, string kind, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? comment, IDispatcher d,
                CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await d.SendAsync(new UploadRequestFile(id, kind, file.FileName, stream, comment), ct)).ToOk();
            })
            .WithName("UploadProgramRequestFile").RequirePermission(PortfolioPermissions.RequestRead).DisableAntiforgery();
        requests.MapGet("/{id:guid}/assessments", async (Guid id, AssessmentService ai, CancellationToken ct) => (await ai.ListAsync(id, ct)).ToOk())
            .WithName("ListProposalAssessments").RequirePermission(PortfolioPermissions.RequestRead);
        requests.MapPost("/{id:guid}/assessments", async (Guid id, AssessmentService ai, CancellationToken ct) => (await ai.GenerateAsync(id, ct)).ToOk())
            .WithName("GenerateProposalAssessment").RequirePermission(PortfolioPermissions.RequestRead);

        requests.MapRecordHistory(ProgramRequest.EntityType, PortfolioPermissions.RequestRead, "GetProgramRequest", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetRequest(id), context.RequestAborted)).IsSuccess);

        var projects = group.MapGroup("/projects");
        projects.MapGet("/", async (int? page, int? pageSize, string? sort, string? classification, string? stage, Guid? unitId, string? search, string? status,
                    string? view, string? progressStatus, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ListProjects(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), classification, stage, unitId, search, status,
                    view, progressStatus), ct)).ToOk())
            .WithName("ListPortfolioProjects").RequirePermission(PortfolioPermissions.ProjectRead);

        ProjectEndpoints.Map(group);
        Phase10Endpoints.Map(group);
        Phase11Endpoints.Map(group);

        var settings = group.MapGroup("/settings");
        settings.MapGet("/", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetPortfolioSettings(), ct)).ToOk())
            .WithName("GetPortfolioSettings").RequirePermission(PortfolioPermissions.SettingsManage);
        settings.MapPut("/", async (PortfolioSettingsInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new SavePortfolioSettings(b.Settings, b.Version), ct)).ToOk())
            .WithName("SavePortfolioSettings").RequirePermission(PortfolioPermissions.SettingsManage);
        settings.MapPost("/template", async (IFormFile file, IDispatcher d, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await d.SendAsync(new UploadProjectScopeTemplate(file.FileName, stream), ct)).ToOk();
            })
            .WithName("UploadProjectScopeTemplate").RequirePermission(PortfolioPermissions.SettingsManage).DisableAntiforgery();
    }
}
