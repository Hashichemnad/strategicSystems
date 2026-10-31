using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Http.Access;
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
            new CreateSampleItem(body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/sample/items/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateSampleItemInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(
            new UpdateSampleItem(id, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.Version),
            cancellationToken)).ToOk();

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> ActivateAsync(
        Guid id, VersionInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ActivateSampleItem(id, body.Version), cancellationToken)).ToOk();

    private static async Task<Results<Ok<SampleItemDto>, ProblemHttpResult>> DeactivateAsync(
        Guid id, VersionInput body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new DeactivateSampleItem(id, body.Version), cancellationToken)).ToOk();
}

internal sealed record SampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn);

internal sealed record UpdateSampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, uint Version);

internal sealed record VersionInput(uint Version);

internal sealed record EditorInput(Guid? EmployeeId);
