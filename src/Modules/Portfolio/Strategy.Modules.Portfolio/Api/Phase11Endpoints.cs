using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Strategy.Modules.Portfolio.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;

namespace Strategy.Modules.Portfolio.Api;

/// <summary>Phase 11 internal endpoints (docs/design/portfolio.md §61): vendor register, project Wasel panel, capacity view, quarterly reviews.</summary>
internal static class Phase11Endpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        const string Read = PortfolioPermissions.ProjectRead;
        const string Vendors = PortfolioPermissions.ProjectVendors;

        // Vendor register (ADR 0112)
        group.MapGet("/vendors", async (string? search, string? status, VendorRegisterService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(search, status, ct)))
            .WithName("ListVendors").RequirePermission(PortfolioPermissions.VendorRead);
        group.MapGet("/vendors/{id:guid}", async (Guid id, VendorRegisterService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToOk())
            .WithName("GetVendor").RequirePermission(PortfolioPermissions.VendorRead);
        group.MapPost("/vendors", async (VendorInput b, VendorRegisterService s, CancellationToken ct) => (await s.SaveAsync(null, b, ct)).ToOk())
            .WithName("CreateVendor").RequirePermission(PortfolioPermissions.VendorManage);
        group.MapPut("/vendors/{id:guid}", async (Guid id, VendorInput b, VendorRegisterService s, CancellationToken ct) => (await s.SaveAsync(id, b, ct)).ToOk())
            .WithName("UpdateVendor").RequirePermission(PortfolioPermissions.VendorManage);
        group.MapPost("/vendors/{id:guid}/status", async (Guid id, VendorStatusInput b, VendorRegisterService s, CancellationToken ct) => (await s.SetStatusAsync(id, b, ct)).ToOk())
            .WithName("SetVendorStatus").RequirePermission(PortfolioPermissions.VendorManage);
        group.MapPost("/vendor-users/{id:guid}/deactivate", async (Guid id, ReasonOnlyInput b, VendorRegisterService s, CancellationToken ct) =>
                (await s.SetUserActiveAsync(id, false, b.Reason, ct)).ToOk())
            .WithName("DeactivateVendorUser").RequirePermission(PortfolioPermissions.VendorManage);
        group.MapPost("/vendor-users/{id:guid}/reactivate", async (Guid id, VendorRegisterService s, CancellationToken ct) =>
                (await s.SetUserActiveAsync(id, true, null, ct)).ToOk())
            .WithName("ReactivateVendorUser").RequirePermission(PortfolioPermissions.VendorManage);

        // Project Wasel panel (PM / PM Office; services check the project record)
        var projects = group.MapGroup("/projects");
        projects.MapGet("/{id:guid}/wasel", async (Guid id, WaselManagementService s, CancellationToken ct) => (await s.PanelAsync(id, ct)).ToOk())
            .WithName("GetProjectWasel").RequirePermission(Read);
        projects.MapPut("/{id:guid}/wasel/mode", async (Guid id, UpdateModeInput b, WaselManagementService s, CancellationToken ct) => (await s.SetModeAsync(id, b, ct)).ToOk())
            .WithName("SetProjectUpdateMode").RequirePermission(Vendors);
        projects.MapPost("/{id:guid}/wasel/assignments", async (Guid id, AssignmentInput b, WaselManagementService s, CancellationToken ct) => (await s.AssignAsync(id, b, ct)).ToOk())
            .WithName("AssignProjectVendor").RequirePermission(Vendors);
        projects.MapPost("/{id:guid}/wasel/invitations", async (Guid id, InvitationInput b, WaselManagementService s, CancellationToken ct) => (await s.InviteAsync(id, b, ct)).ToOk())
            .WithName("InviteVendorUser").RequirePermission(Vendors);
        projects.MapGet("/{id:guid}/wasel/comments", async (Guid id, string? subjectType, Guid? subjectId, WaselManagementService s, CancellationToken ct) =>
                (await s.CommentsAsync(id, subjectType, subjectId, ct)).ToOk())
            .WithName("ListProjectWaselComments").RequirePermission(Read);
        projects.MapPost("/{id:guid}/wasel/comments", async (Guid id, CommentInput b, WaselManagementService s, CancellationToken ct) => (await s.AddCommentAsync(id, b, ct)).ToOk())
            .WithName("AddProjectWaselComment").RequirePermission(Vendors);
        projects.MapPost("/{id:guid}/wasel/ratings", async (Guid id, VendorRatingInput b, WaselManagementService s, CancellationToken ct) => (await s.RateAsync(id, b, ct)).ToOk())
            .WithName("RateProjectVendor").RequirePermission(Vendors);
        group.MapPost("/wasel-assignments/{aid:guid}/revoke", async (Guid aid, ReasonOnlyInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.RevokeAssignmentAsync(aid, b, ct)).ToOk())
            .WithName("RevokeVendorAssignment").RequirePermission(Vendors);
        group.MapPost("/wasel-members/{mid:guid}/revoke", async (Guid mid, ReasonOnlyInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.RevokeMemberAsync(mid, b, ct)).ToOk())
            .WithName("RevokeVendorMember").RequirePermission(Vendors);
        group.MapPut("/milestones/{mid:guid}/vendor", async (Guid mid, MilestoneVendorInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.AssignMilestoneAsync(mid, b, ct)).ToOk())
            .WithName("AssignMilestoneVendor").RequirePermission(Vendors);
        group.MapPut("/meetings/{mid:guid}/vendor", async (Guid mid, MeetingVendorInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.ShareMeetingAsync(mid, b, ct)).ToOk())
            .WithName("ShareMeetingWithVendor").RequirePermission(Vendors);
        group.MapPost("/proposals/{pid:guid}/accept", async (Guid pid, ProposalDecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideProposalAsync(pid, true, b, ct)).ToOk())
            .WithName("AcceptPlanProposal").RequirePermission(Vendors).WithIdempotency();
        group.MapPost("/proposals/{pid:guid}/return", async (Guid pid, ProposalDecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideProposalAsync(pid, false, b, ct)).ToOk())
            .WithName("ReturnPlanProposal").RequirePermission(Vendors).WithIdempotency();
        group.MapPost("/vendor-submissions/{sid:guid}/items/{iid:guid}/accept", async (Guid sid, Guid iid, ItemReviewInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.ReviewItemAsync(sid, iid, true, b, ct)).ToOk())
            .WithName("AcceptVendorSubmissionItem").RequirePermission(Vendors).WithIdempotency();
        group.MapPost("/vendor-submissions/{sid:guid}/items/{iid:guid}/return", async (Guid sid, Guid iid, ItemReviewInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.ReviewItemAsync(sid, iid, false, b, ct)).ToOk())
            .WithName("ReturnVendorSubmissionItem").RequirePermission(Vendors).WithIdempotency();
        group.MapPost("/vendor-submissions/{sid:guid}/share", async (Guid sid, ShareOutcomeInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.ShareOutcomeAsync(sid, b, ct)).ToOk())
            .WithName("ShareVendorOutcome").RequirePermission(Vendors);
        group.MapPost("/vendor-submissions/{sid:guid}/ai-review", async (Guid sid, VendorReviewAiService s, CancellationToken ct) => (await s.ReviewAsync(sid, ct)).ToOk())
            .WithName("ReviewVendorSubmissionWithAi").RequirePermission(Vendors);
        group.MapPost("/vendor-deliverables/{did:guid}/accept", async (Guid did, DecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideDeliverableAsync(did, true, b, ct)).ToOk())
            .WithName("AcceptVendorDeliverable").RequirePermission(Vendors);
        group.MapPost("/vendor-deliverables/{did:guid}/return", async (Guid did, DecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideDeliverableAsync(did, false, b, ct)).ToOk())
            .WithName("ReturnVendorDeliverable").RequirePermission(Vendors);
        group.MapPost("/vendor-items/{vid:guid}/accept", async (Guid vid, DecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideItemAsync(vid, true, b, ct)).ToOk())
            .WithName("AcceptVendorItem").RequirePermission(Vendors);
        group.MapPost("/vendor-items/{vid:guid}/reject", async (Guid vid, DecisionInput b, WaselManagementService s, CancellationToken ct) =>
                (await s.DecideItemAsync(vid, false, b, ct)).ToOk())
            .WithName("RejectVendorItem").RequirePermission(Vendors);

        // Capacity view and quarterly reviews (ADR 0116)
        group.MapGet("/capacity", async (Guid? unitId, bool? below, bool? overOnly, CapacityService s, CancellationToken ct) =>
                (await s.GetAsync(unitId, below ?? true, overOnly ?? false, ct)).ToOk())
            .WithName("GetPortfolioCapacity").RequirePermission(PortfolioPermissions.CapacityRead);
        group.MapGet("/reviews", async (PortfolioReviewService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(ct)))
            .WithName("ListPortfolioReviews").RequirePermission(PortfolioPermissions.ReviewRead);
        group.MapPost("/reviews", async (ReviewInput b, PortfolioReviewService s, CancellationToken ct) => (await s.CreateAsync(b, ct)).ToOk())
            .WithName("CreatePortfolioReview").RequirePermission(PortfolioPermissions.ReviewManage);
        group.MapGet("/reviews/{rid:guid}", async (Guid rid, PortfolioReviewService s, CancellationToken ct) => (await s.GetAsync(rid, ct)).ToOk())
            .WithName("GetPortfolioReview").RequirePermission(PortfolioPermissions.ReviewRead);
        group.MapPut("/reviews/{rid:guid}/decisions/{projectId:guid}", async (Guid rid, Guid projectId, ReviewDecisionInput b, PortfolioReviewService s, CancellationToken ct) =>
                (await s.DecideAsync(rid, projectId, b, ct)).ToOk())
            .WithName("SetPortfolioReviewDecision").RequirePermission(PortfolioPermissions.ReviewManage);
        group.MapPost("/reviews/{rid:guid}/complete", async (Guid rid, PortfolioReviewService s, CancellationToken ct) => (await s.CompleteAsync(rid, ct)).ToOk())
            .WithName("CompletePortfolioReview").RequirePermission(PortfolioPermissions.ReviewManage).WithIdempotency();
    }
}
