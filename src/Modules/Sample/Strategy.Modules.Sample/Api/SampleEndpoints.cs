using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Sample.Api;

internal static class SampleEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var items = group.MapGroup("/items");

        items.MapGet("/", ListAsync).WithName("ListSampleItems").RequirePermission(SamplePermissions.Read);
        items.MapGet("/{id:guid}", GetAsync).WithName("GetSampleItem").RequirePermission(SamplePermissions.Read);
        items.MapPost("/", CreateAsync).WithName("CreateSampleItem").RequirePermission(SamplePermissions.Create);
        items.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateSampleItem").RequirePermission(SamplePermissions.Update);
        items.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivateSampleItem").RequirePermission(SamplePermissions.Update);
        items.MapPost("/{id:guid}/deactivate", DeactivateAsync).WithName("DeactivateSampleItem").RequirePermission(SamplePermissions.Update);

        // Phase 4: submit for approval (the approval engine runs the chain configured in Settings → Approvals).
        items.MapPost("/{id:guid}/submit", async (Guid id, SubmitInput body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new SubmitSampleItem(id, body.Version, body.Comment), ct)).ToOk())
            .WithName("SubmitSampleItem").RequirePermission(SamplePermissions.Submit).WithIdempotency();

        // History tab: the item's audit trail, for anyone who may read the item (404 otherwise).
        items.MapRecordHistory(SampleItem.EntityType, SamplePermissions.Read, "GetSampleItem", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetSampleItem(id), context.RequestAborted)).IsSuccess);

        // Record roles: editors of one item (granted by someone who may edit it).
        items.MapGet("/{id:guid}/editors", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new ListSampleEditors(id), ct)).ToOk())
            .WithName("ListSampleItemEditors").RequirePermission(SamplePermissions.Read);
        items.MapPost("/{id:guid}/editors", async (Guid id, EditorInput body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new AddSampleEditor(id, body.EmployeeId), ct)).ToOk())
            .WithName("AddSampleItemEditor").RequirePermission(SamplePermissions.Update);
        items.MapPost("/{id:guid}/editors/{employeeId:guid}/remove", async (Guid id, Guid employeeId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RemoveSampleEditor(id, employeeId), ct)).ToOk())
            .WithName("RemoveSampleItemEditor").RequirePermission(SamplePermissions.Update);
    }

    private static async Task<Results<Ok<PagedResult<SampleItemDto>>, ProblemHttpResult>> ListAsync(
        int? page, int? pageSize, string? sort, string? search, string? status, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort);
        var result = await dispatcher.SendAsync(new ListSampleItems(paging, search, status), cancellationToken);
        return result.ToOk();
    }

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> GetAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new GetSampleItem(id), cancellationToken)).ToOk();

    private static async Task<Results<Created<SampleItemDto>, ProblemHttpResult>> CreateAsync(
        SampleItemInput body, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new CreateSampleItem(body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.Amount), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/sample/items/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateSampleItemInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(
            new UpdateSampleItem(id, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.Version, body.Amount),
            cancellationToken)).ToOk();

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> ActivateAsync(
        Guid id, VersionInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ActivateSampleItem(id, body.Version), cancellationToken)).ToOk();

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> DeactivateAsync(
        Guid id, VersionInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new DeactivateSampleItem(id, body.Version), cancellationToken)).ToOk();
}

internal sealed record SampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, decimal? Amount = null);

internal sealed record UpdateSampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, uint Version, decimal? Amount = null);

internal sealed record SubmitInput(uint Version, string? Comment);

internal sealed record VersionInput(uint Version);

internal sealed record EditorInput(Guid? EmployeeId);
