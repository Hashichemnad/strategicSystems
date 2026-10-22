using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Sets <c>created_*</c> / <c>updated_*</c> on auditable entities. The field-level audit trail
/// (old → new values, hash chain) is added by Platform.Audit in Phase 3.
/// </summary>
public sealed class AuditFieldsInterceptor(IClock clock, IRequestContext requestContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // PostgreSQL stores microseconds; truncate so the value we return equals the value read back later.
        var utcNow = clock.UtcNow;
        var now = utcNow.AddTicks(-(utcNow.Ticks % TimeSpan.TicksPerMicrosecond));
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.MarkCreated(now, requestContext.UserId);
                    break;
                case EntityState.Modified:
                    entry.Entity.MarkUpdated(now, requestContext.UserId);
                    break;
                case EntityState.Deleted:
                    throw new InvalidOperationException(
                        $"Hard delete of {entry.Metadata.ClrType.Name} is not allowed; change its status instead (CLAUDE.md §6).");
            }
        }
    }
}
