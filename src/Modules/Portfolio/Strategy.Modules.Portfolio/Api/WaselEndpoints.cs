using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Api;

/// <summary>
/// Wasel vendor API (<c>/api/wasel/v1</c>, docs/design/portfolio.md §61): vendor scheme only, vendor DTOs only (ADR 0110, 0113). Every endpoint declares a
/// <c>wasel.*</c> permission (record roles); the services resolve the vendor scope (404 for anything not assigned). 404 <c>module.disabled</c> while Wasel is off.
/// </summary>
internal static class WaselEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/wasel/v1").WithTags("wasel");
        group.AddEndpointFilter(async (context, next) =>
        {
            var systems = context.HttpContext.RequestServices.GetRequiredService<ISystemState>();
            var ct = context.HttpContext.RequestAborted;
            return await systems.IsEnabledAsync(WaselPermissions.SystemCode, ct).ConfigureAwait(false)
                   && await systems.IsEnabledAsync(PortfolioPermissions.SystemCode, ct).ConfigureAwait(false)
                ? await next(context).ConfigureAwait(false)
                : new Error("module.disabled", "errors.moduleDisabled", ErrorKind.Disabled).ToProblem();
        });

        const string Read = WaselPermissions.ProjectRead;
        group.MapGet("/portal", async (VendorPortalService s, CancellationToken ct) => (await s.MeAsync(ct)).ToOk())
            .WithName("GetWaselPortal").RequireVendorPermission(Read);

        var projects = group.MapGroup("/projects");
        projects.MapGet("/{id:guid}", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.ProjectAsync(id, ct)).ToOk())
            .WithName("GetWaselProject").RequireVendorPermission(Read);
        projects.MapGet("/{id:guid}/milestones", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.MilestonesAsync(id, ct)).ToOk())
            .WithName("ListWaselMilestones").RequireVendorPermission(Read);

        projects.MapGet("/{id:guid}/proposals", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.ProposalsAsync(id, ct)).ToOk())
            .WithName("ListWaselProposals").RequireVendorPermission(Read);
        projects.MapPost("/{id:guid}/proposals", async (Guid id, VendorProposalInput b, VendorPortalService s, CancellationToken ct) =>
                (await s.SaveProposalAsync(id, null, b, ct)).ToOk())
            .WithName("CreateWaselProposal").RequireVendorPermission(WaselPermissions.PlanPrepare);
        projects.MapPut("/{id:guid}/proposals/{pid:guid}", async (Guid id, Guid pid, VendorProposalInput b, VendorPortalService s, CancellationToken ct) =>
                (await s.SaveProposalAsync(id, pid, b, ct)).ToOk())
            .WithName("UpdateWaselProposal").RequireVendorPermission(WaselPermissions.PlanPrepare);
        projects.MapPost("/{id:guid}/proposals/{pid:guid}/submit", async (Guid id, Guid pid, VendorVersionInput b, VendorPortalService s, CancellationToken ct) =>
                (await s.MoveProposalAsync(id, pid, ProposalActions.Submit, b.Version, ct)).ToOk())
            .WithName("SubmitWaselProposal").RequireVendorPermission(WaselPermissions.PlanSubmit).WithIdempotency();
        projects.MapPost("/{id:guid}/proposals/{pid:guid}/withdraw", async (Guid id, Guid pid, VendorVersionInput b, VendorPortalService s, CancellationToken ct) =>
                (await s.MoveProposalAsync(id, pid, ProposalActions.Withdraw, b.Version, ct)).ToOk())
            .WithName("WithdrawWaselProposal").RequireVendorPermission(WaselPermissions.PlanPrepare);

        projects.MapGet("/{id:guid}/submission", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.SubmissionAsync(id, ct)).ToOk())
            .WithName("GetWaselSubmission").RequireVendorPermission(Read);
        projects.MapGet("/{id:guid}/submissions", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.HistoryAsync(id, ct)).ToOk())
            .WithName("ListWaselSubmissions").RequireVendorPermission(Read);
        projects.MapPut("/{id:guid}/submission/items/{milestoneId:guid}", async (Guid id, Guid milestoneId, VendorProgressInput b, VendorPortalService s,
                CancellationToken ct) => (await s.SaveItemAsync(id, milestoneId, b, ct)).ToOk())
            .WithName("SaveWaselSubmissionItem").RequireVendorPermission(WaselPermissions.UpdatePrepare);
        projects.MapPost("/{id:guid}/submission/items/{milestoneId:guid}/evidence", async (Guid id, Guid milestoneId, IFormFile file, VendorPortalService s,
                CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.UploadEvidenceAsync(id, milestoneId, file.FileName, stream, ct)).ToOk();
            })
            .WithName("UploadWaselEvidence").RequireVendorPermission(WaselPermissions.UpdatePrepare).DisableAntiforgery();
        projects.MapPost("/{id:guid}/submission/submit", async (Guid id, VendorSubmitInput b, VendorPortalService s, CancellationToken ct) =>
                (await s.SubmitAsync(id, b, ct)).ToOk())
            .WithName("SubmitWaselSubmission").RequireVendorPermission(WaselPermissions.UpdateSubmit).WithIdempotency();

        projects.MapGet("/{id:guid}/deliverables", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.DeliverablesAsync(id, ct)).ToOk())
            .WithName("ListWaselDeliverables").RequireVendorPermission(Read);
        projects.MapPost("/{id:guid}/deliverables", async (Guid id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? title,
                [Microsoft.AspNetCore.Mvc.FromForm] string? description, [Microsoft.AspNetCore.Mvc.FromForm] Guid? milestoneId, VendorPortalService s, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.UploadDeliverableAsync(id, milestoneId, title, description, file.FileName, stream, ct)).ToOk();
            })
            .WithName("UploadWaselDeliverable").RequireVendorPermission(WaselPermissions.DeliverableUpload).DisableAntiforgery();

        projects.MapGet("/{id:guid}/items", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.ItemsAsync(id, ct)).ToOk())
            .WithName("ListWaselItems").RequireVendorPermission(Read);
        projects.MapPost("/{id:guid}/items", async (Guid id, VendorItemInput b, VendorPortalService s, CancellationToken ct) => (await s.ProposeItemAsync(id, b, ct)).ToOk())
            .WithName("ProposeWaselItem").RequireVendorPermission(WaselPermissions.ItemPropose);

        projects.MapGet("/{id:guid}/meetings", async (Guid id, VendorPortalService s, CancellationToken ct) => (await s.MeetingsAsync(id, ct)).ToOk())
            .WithName("ListWaselMeetings").RequireVendorPermission(Read);
        projects.MapPost("/{id:guid}/meetings/{mid:guid}/confirm", async (Guid id, Guid mid, VendorPortalService s, CancellationToken ct) =>
                (await s.ConfirmMeetingAsync(id, mid, ct)).ToOk())
            .WithName("ConfirmWaselMeeting").RequireVendorPermission(WaselPermissions.MeetingConfirm);

        projects.MapGet("/{id:guid}/comments", async (Guid id, string? subjectType, Guid? subjectId, VendorPortalService s, CancellationToken ct) =>
                (await s.CommentsAsync(id, subjectType, subjectId, ct)).ToOk())
            .WithName("ListWaselComments").RequireVendorPermission(Read);
        projects.MapPost("/{id:guid}/comments", async (Guid id, CommentInput b, VendorPortalService s, CancellationToken ct) => (await s.AddCommentAsync(id, b, ct)).ToOk())
            .WithName("AddWaselComment").RequireVendorPermission(WaselPermissions.CommentWrite);

        projects.MapGet("/{id:guid}/files/{fileId:guid}", async Task<Results<FileStreamHttpResult, ProblemHttpResult>> (Guid id, Guid fileId, VendorPortalService s,
                CancellationToken ct) =>
            {
                var content = await s.FileAsync(id, fileId, ct);
                return content.IsFailure
                    ? content.Error.ToProblem()
                    : TypedResults.Stream(content.Value.Stream, content.Value.ContentType, content.Value.FileName, content.Value.LastModified);
            })
            .WithName("DownloadWaselFile").RequireVendorPermission(Read);
    }
}
