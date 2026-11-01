using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Field-level audit trail (CLAUDE.md §8): every insert / update / delete of an audited entity becomes chain rows written on the
/// same connection and in the same transaction as the business change. When the caller has no transaction, the interceptor opens
/// one before EF saves and commits it after the audit rows are written, so data and audit commit together or not at all.
/// One instance serves every DbContext of a scope; state is kept per context.
/// </summary>
public sealed class AuditTrailInterceptor(IAuditSink sink, IAuditReason reason) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, State> _states = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        SavingChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var changes = AuditChangeCollector.Collect(context);
            if (changes.Count > 0)
            {
                var state = new State(changes);
                if (context.Database.CurrentTransaction is null)
                {
                    state.Owned = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                _states.AddOrUpdate(context, state);
            }
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        SavedChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context || !_states.TryGetValue(context, out var state))
        {
            return result;
        }

        _states.Remove(context);
        try
        {
            var transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            var records = state.Changes.SelectMany(c => c.ToRecords(state.ChangeSetId, reason.Reason)).ToList();
            await sink.AppendAsync(context.Database.GetDbConnection(), transaction, records, cancellationToken).ConfigureAwait(false);
            if (state.Owned is not null)
            {
                await state.Owned.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (state.Owned is not null)
            {
                await state.Owned.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (state.Owned is not null)
            {
                await state.Owned.DisposeAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        SaveChangesFailedAsync(eventData).GetAwaiter().GetResult();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) =>
        AbandonAsync(eventData.Context);

    public override void SaveChangesCanceled(DbContextEventData eventData) =>
        SaveChangesCanceledAsync(eventData).GetAwaiter().GetResult();

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) =>
        AbandonAsync(eventData.Context);

    private async Task AbandonAsync(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state))
        {
            return;
        }

        _states.Remove(context);
        if (state.Owned is not null)
        {
            await state.Owned.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await state.Owned.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class State(IReadOnlyList<AuditChange> changes)
    {
        public IReadOnlyList<AuditChange> Changes { get; } = changes;

        public Guid ChangeSetId { get; } = Guid.CreateVersion7();

        public IDbContextTransaction? Owned { get; set; }
    }
}

/// <summary>A captured change of one entity, turned into chain rows after the save.</summary>
public sealed record AuditChange(
    string Action,
    string SystemCode,
    string EntityType,
    string RecordId,
    string? ParentType,
    string? ParentId,
    IReadOnlyList<(string Field, string? Old, string? New)> Fields)
{
    public IEnumerable<AuditRecord> ToRecords(Guid changeSetId, string? reason)
    {
        switch (Action)
        {
            case AuditActions.Update:
                foreach (var (field, old, @new) in Fields)
                {
                    yield return new AuditRecord(AuditKinds.Change, Action, SystemCode, EntityType, RecordId, ParentType, ParentId, field, old, @new,
                        changeSetId, Reason: reason);
                }

                break;
            case AuditActions.Create:
                yield return new AuditRecord(AuditKinds.Change, Action, SystemCode, EntityType, RecordId, ParentType, ParentId,
                    NewValue: AuditValues.Snapshot(Fields.Select(f => (f.Field, f.New))), ChangeSetId: changeSetId, Reason: reason);
                break;
            default:
                yield return new AuditRecord(AuditKinds.Change, Action, SystemCode, EntityType, RecordId, ParentType, ParentId,
                    OldValue: AuditValues.Snapshot(Fields.Select(f => (f.Field, f.Old))), ChangeSetId: changeSetId, Reason: reason);
                break;
        }
    }
}

/// <summary>Finds the audited entries of a context and captures their values before the save.</summary>
public static class AuditChangeCollector
{
    /// <summary>Never audited: the standard stamps (the entry carries user and time) and the row version.</summary>
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        nameof(AuditableEntity.Version), nameof(AuditableEntity.CreatedAt), nameof(AuditableEntity.CreatedBy),
        nameof(AuditableEntity.UpdatedAt), nameof(AuditableEntity.UpdatedBy), "ConcurrencyStamp", "AccessFailedCount",
    };

    /// <summary>Masked by name wherever they appear (Identity base classes cannot carry attributes).</summary>
    private static readonly HashSet<string> MaskedProperties = new(StringComparer.Ordinal)
    {
        "PasswordHash", "SecurityStamp", "Hash", "TokenHash", "AuthenticatorKey",
    };

    public static IReadOnlyList<AuditChange> Collect(DbContext context)
    {
        var changes = new List<AuditChange>();
        var systemCode = context is ModuleDbContext module ? module.AuditSystemCode : context.GetType().Name;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || !IsAudited(entry.Metadata))
            {
                continue;
            }

            var fields = new List<(string, string?, string?)>();
            foreach (var property in entry.Properties)
            {
                var meta = property.Metadata;
                if (SkippedProperties.Contains(meta.Name) || meta.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is not null)
                {
                    continue;
                }

                var masked = IsMasked(meta);
                var json = string.Equals(meta.GetColumnType(), "jsonb", StringComparison.OrdinalIgnoreCase);
                string? Format(object? value) => masked ? (value is null ? null : AuditValues.Mask)
                    : json && value is string text ? AuditValues.CompactJson(text) : AuditValues.Format(value);
                switch (entry.State)
                {
                    case EntityState.Added:
                        fields.Add((meta.Name, null, Format(property.CurrentValue)));
                        break;
                    case EntityState.Deleted:
                        fields.Add((meta.Name, Format(property.OriginalValue), null));
                        break;
                    default:
                        var old = Format(property.OriginalValue);
                        var current = Format(property.CurrentValue);
                        // Masked values compare as "***": compare the raw values to see a change of a secret.
                        var changed = masked
                            ? !string.Equals(AuditValues.Format(property.OriginalValue), AuditValues.Format(property.CurrentValue), StringComparison.Ordinal)
                            : !string.Equals(old, current, StringComparison.Ordinal);
                        if (property.IsModified && changed)
                        {
                            fields.Add((meta.Name, old, current));
                        }

                        break;
                }
            }

            if (entry.State == EntityState.Modified && fields.Count == 0)
            {
                continue; // nothing audited really changed (e.g. only a stamp)
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Create,
                EntityState.Deleted => AuditActions.Delete,
                _ => AuditActions.Update,
            };
            var parent = entry.Metadata.ClrType.GetCustomAttribute<AuditParentAttribute>();
            var parentId = parent is null ? null : AuditValues.Format(entry.Property(parent.Property).CurrentValue ?? entry.Property(parent.Property).OriginalValue);
            var parentType = parent is null ? null
                : parent.TypeProperty is { } typeProperty ? AuditValues.Format(entry.Property(typeProperty).CurrentValue) : parent.EntityType;
            changes.Add(new AuditChange(action, systemCode, EntityTypeOf(entry.Metadata, systemCode), KeyOf(entry), parentType, parentId, fields));
        }

        return changes;
    }

    public static bool IsAudited(IEntityType type)
    {
        var clr = type.ClrType;
        if (type.IsOwned() || clr.GetCustomAttribute<NotAuditedAttribute>() is not null)
        {
            return false;
        }

        return typeof(IAuditable).IsAssignableFrom(clr) || clr.GetCustomAttribute<AuditedAttribute>() is not null;
    }

    /// <summary>The attribute's code, else <c>&lt;system&gt;.&lt;table&gt;</c> (e.g. <c>org.employee</c>).</summary>
    public static string EntityTypeOf(IEntityType type, string systemCode) =>
        type.ClrType.GetCustomAttribute<AuditedAttribute>()?.EntityType ?? $"{systemCode}.{type.GetTableName()}";

    private static bool IsMasked(IProperty property) =>
        property.PropertyInfo?.GetCustomAttribute<AuditMaskedAttribute>() is not null
        || MaskedProperties.Contains(property.Name)
        || property.Name.EndsWith("Protected", StringComparison.Ordinal);

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? string.Empty
            : string.Join('/', key.Properties.Select(p => AuditValues.Format(entry.Property(p.Name).CurrentValue ?? entry.Property(p.Name).OriginalValue)));
    }
}

/// <summary>How values are written into the audit trail: invariant, compact and never longer than <see cref="MaxLength"/>.</summary>
public static class AuditValues
{
    public const string Mask = "***";
    public const int MaxLength = 4000;

    public static string? Format(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            DateTimeOffset d => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Guid g => g.ToString("D"),
            Enum e => e.ToString(),
            byte[] bytes => $"<{bytes.Length} bytes>",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable sequence => JsonSerializer.Serialize(sequence.Cast<object?>().Select(Format), Readable),
            _ => value.ToString(),
        };
        return text is { Length: > MaxLength } ? text[..MaxLength] : text;
    }

    /// <summary>
    /// jsonb comes back from PostgreSQL re-formatted (spaces, key order); both sides are compacted so only real changes show.
    /// </summary>
    public static string? CompactJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return Format(JsonSerializer.Serialize(Sorted(document.RootElement), Readable));
        }
        catch (JsonException)
        {
            return Format(text);
        }
    }

    private static object? Sorted(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new SortedDictionary<string, object?>(
            element.EnumerateObject().ToDictionary(p => p.Name, p => Sorted(p.Value)), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(Sorted).ToList(),
        _ => element.Clone(),
    };

    /// <summary>JSON object of field → value with keys in a stable order (create and delete snapshots).</summary>
    public static string Snapshot(IEnumerable<(string Field, string? Value)> fields) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string?>(fields.ToDictionary(f => f.Field, f => f.Value), StringComparer.Ordinal), Readable);

    /// <summary>Arabic and other text stay readable in stored values (they are always HTML-encoded when shown).</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
