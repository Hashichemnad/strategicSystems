using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Application;

public sealed record GetSampleItem(Guid Id) : IQuery<SampleItemDto>;

internal sealed class GetSampleItemHandler(SampleDbContext db, IAccessService access, IRequestContext context)
    : IQueryHandler<GetSampleItem, SampleItemDto>
{
    public async Task<Result<SampleItemDto>> HandleAsync(GetSampleItem query, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken).ConfigureAwait(false);

        // Missing and out-of-scope look the same (404), so record existence does not leak (CLAUDE.md §7).
        if (item is null
            || !await access.CanAsync(context.User, SamplePermissions.Read, new ResourceRef(SamplePermissions.ResourceType, item.Id), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return SampleItemDto.From(item);
    }
}
