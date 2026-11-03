using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow.Application;

namespace Strategy.Platform.Workflow.Api;

/// <summary><c>/api/v1/workflow</c> (design note §11).</summary>
internal static class WorkflowEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var processes = group.MapGroup("/processes");
        processes.MapGet("/", async (ProcessAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListApprovalProcesses").RequirePermission(WorkflowPermissions.ProcessRead);
        processes.MapGet("/{key}", async (string key, ProcessAdministration a, CancellationToken ct) => (await a.GetAsync(key, null, ct)).ToOk())
            .WithName("GetApprovalProcess").RequirePermission(WorkflowPermissions.ProcessRead);
        processes.MapGet("/{key}/versions/{versionNo:int}", async (string key, int versionNo, ProcessAdministration a, CancellationToken ct) =>
                (await a.GetAsync(key, versionNo, ct)).ToOk())
            .WithName("GetApprovalProcessVersion").RequirePermission(WorkflowPermissions.ProcessRead);
        processes.MapPut("/{key}", async (string key, SaveProcessInput body, ProcessAdministration a, CancellationToken ct) => (await a.SaveAsync(key, body, ct)).ToOk())
            .WithName("SaveApprovalProcess").RequirePermission(WorkflowPermissions.ProcessManage);
        processes.MapPost("/{key}/validate-condition", (string key, ConditionInput body, ProcessAdministration a) => a.CheckCondition(key, body).ToOk())
            .WithName("ValidateApprovalCondition").RequirePermission(WorkflowPermissions.ProcessRead);
        processes.MapPost("/{key}/preview", async (string key, PreviewInput body, ProcessAdministration a, CancellationToken ct) =>
                (await a.PreviewAsync(key, body, ct)).ToOk())
            .WithName("PreviewApprovalProcess").RequirePermission(WorkflowPermissions.ProcessRead);

        group.MapGet("/settings", async (ProcessAdministration a, IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await a.GetSettingsAsync(await LevelsAsync(dispatcher, ct), ct)))
            .WithName("GetApprovalSettings").RequirePermission(WorkflowPermissions.ProcessRead);
        group.MapPut("/settings", async (ApprovalSettingsInput body, ProcessAdministration a, IDispatcher dispatcher, CancellationToken ct) =>
                (await a.SaveSettingsAsync(body, await LevelsAsync(dispatcher, ct), ct)).ToOk())
            .WithName("SaveApprovalSettings").RequirePermission(WorkflowPermissions.ProcessManage);

        // Self-service: what the caller may see and do is checked record by record (404 / 403, design note §6).
        group.MapGet("/my-work", async (ApprovalQueries q, CancellationToken ct) => TypedResults.Ok(await q.MyWorkAsync(ct)))
            .WithName("GetMyWork").RequireSignedIn();
        group.MapGet("/subjects/{resourceType}/{resourceId:guid}", async (string resourceType, Guid resourceId, ApprovalQueries q, CancellationToken ct) =>
                (await q.SubjectAsync(resourceType, resourceId, ct)).ToOk())
            .WithName("GetSubjectApprovals").RequireSignedIn();
        group.MapGet("/status/{resourceType}/{resourceId:guid}", async (string resourceType, Guid resourceId, ApprovalQueries q, CancellationToken ct) =>
                (await q.StatusAsync(resourceType, resourceId, ct)).ToOk())
            .WithName("GetCurrentStatus").RequireSignedIn();
        group.MapPost("/tasks/{taskId:guid}/act", async (Guid taskId, ActInput body, ApprovalEngine engine, CancellationToken ct) =>
                (await engine.ActAsync(taskId, body, ct)).ToOk())
            .WithName("ActOnApprovalTask").RequireSignedIn().WithIdempotency();
    }

    private static async Task<IReadOnlyList<OptionDto>> LevelsAsync(IDispatcher dispatcher, CancellationToken ct)
    {
        var levels = await dispatcher.SendAsync(new ListLevels(), ct);
        return levels.IsSuccess ? levels.Value.Where(l => l.Active).Select(l => new OptionDto(l.Code, l.NameAr, l.NameEn)).ToList() : [];
    }
}
