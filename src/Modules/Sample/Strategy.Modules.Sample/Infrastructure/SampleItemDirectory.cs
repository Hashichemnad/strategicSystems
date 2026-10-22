using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Contracts;

namespace Strategy.Modules.Sample.Infrastructure;

internal sealed class SampleItemDirectory(SampleDbContext db) : ISampleItemDirectory
{
    public Task<SampleItemReference?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Items.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new SampleItemReference(x.Id, x.NameAr, x.NameEn, x.Status))
            .FirstOrDefaultAsync(cancellationToken);
}
