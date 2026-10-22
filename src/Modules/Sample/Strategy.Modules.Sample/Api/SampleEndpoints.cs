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
    }

    private static async Task<Results<Ok<PagedResult<SampleItemDto>>, ProblemHttpResult>> ListAsync(
        [AsParameters] ListSampleItemsRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page ?? 1, request.PageSize ?? PageRequest.DefaultPageSize, request.Sort);
        var result = await dispatcher.SendAsync(new ListSampleItems(paging, request.Search, request.Status), cancellationToken);
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

internal sealed record ListSampleItemsRequest(int? Page, int? PageSize, string? Sort, string? Search, string? Status);

internal sealed record SampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn);

internal sealed record UpdateSampleItemInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, uint Version);

internal sealed record VersionInput(uint Version);
