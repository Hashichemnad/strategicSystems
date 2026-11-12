using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Integration;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Standby read-only rule (SPEC-ADDENDUM §G4, ADR 0206): while a source system is primary, an update or delete of an audited entity that the
/// sync created, or any change of an audited child row whose audit parent is such a record, is refused — whatever the entry point (UI, REST,
/// MCP, import, job). Only the sync's own channel writes synced records.
/// </summary>
public sealed class StandbyWriteInterceptor(IRequestContext requestContext, IEnumerable<IStandbyGuard> guards) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        SavingChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not ModuleDbContext context || requestContext.Channel == Channels.Sync)
        {
            return result;
        }

        foreach (var guard in guards)
        {
            if (!await guard.IsActiveAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var keys = Collect(context);
            if (keys.Count == 0)
            {
                return result;
            }

            var readOnly = await guard.FindReadOnlyAsync(keys, cancellationToken).ConfigureAwait(false);
            if (readOnly.Count > 0)
            {
                var (record, source) = readOnly.First();
                throw new RecordReadOnlyException(record, source);
            }
        }

        return result;
    }

    /// <summary>Records touched by the pending changes: updated / deleted audited entities and the audit parents of changed child rows.</summary>
    public static IReadOnlyCollection<RecordKey> Collect(ModuleDbContext context)
    {
        var keys = new HashSet<RecordKey>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || !AuditChangeCollector.IsAudited(entry.Metadata))
            {
                continue;
            }

            if (entry.State != EntityState.Added && IdOf(entry) is { } id)
            {
                keys.Add(new RecordKey(AuditChangeCollector.EntityTypeOf(entry.Metadata, context.AuditSystemCode), id));
            }

            var parent = entry.Metadata.ClrType.GetCustomAttribute<AuditParentAttribute>();
            if (parent is not null && Value(entry, parent.Property) is Guid parentId)
            {
                var parentType = parent.TypeProperty is { } typeProperty ? Value(entry, typeProperty) as string : parent.EntityType;
                if (!string.IsNullOrEmpty(parentType))
                {
                    keys.Add(new RecordKey(parentType, parentId));
                }
            }
        }

        return keys;
    }

    private static Guid? IdOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is { Properties.Count: 1 } && entry.Property(key.Properties[0].Name).CurrentValue is Guid id ? id : null;
    }

    private static object? Value(EntityEntry entry, string property)
    {
        var p = entry.Property(property);
        return p.CurrentValue ?? p.OriginalValue;
    }
}
