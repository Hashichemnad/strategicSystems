using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration.Sync;

/// <summary>The sync targets declared by the modules (ADR 0202).</summary>
public sealed class SyncTargetRegistry(IEnumerable<SyncTargetDefinition> definitions)
{
    private readonly Dictionary<string, SyncTargetDefinition> _byCode = definitions
        .GroupBy(d => d.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    public IReadOnlyCollection<SyncTargetDefinition> All => _byCode.Values;

    public SyncTargetDefinition? Find(string? code) => code is not null && _byCode.TryGetValue(code, out var d) ? d : null;
}

/// <summary>Outcome of one write.</summary>
public sealed record SyncWriteOutcome(Guid RecordId, bool Created);

/// <summary>
/// Writes one synced record (ADR 0202): the generic path loads or creates the module entity in the module's own DbContext and sets only the
/// declared properties through the change tracker (audited, channel <c>sync</c>); custom targets go through their module's writer. The external
/// reference is linked in the same transaction. Use one DI scope per record (fresh contexts).
/// </summary>
public sealed class EntitySyncWriter(IServiceProvider services, IExternalReferenceStore references, IEnumerable<ISyncTargetWriter> customWriters)
{
    public async Task<Result<SyncWriteOutcome>> WriteAsync(
        SyncTargetDefinition target, Guid? existingId, IReadOnlyDictionary<string, object?> values, ExternalReferenceInput reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(reference);
        if (target.ContextType is null || target.EntityClrType is null)
        {
            var writer = customWriters.FirstOrDefault(w => w.TargetType == target.Code)
                ?? throw new InvalidOperationException($"No sync writer for '{target.Code}'.");
            var created = existingId is null;
            var written = await writer.WriteAsync(
                new SyncWrite(target.Code, existingId, values, reference.SourceSystem, reference.ExternalId),
                (context, id) => LinkAsync(reference with { RecordId = id }, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return written.IsSuccess ? new SyncWriteOutcome(written.Value, created) : written.Error!;
        }

        var db = (DbContext)services.GetRequiredService(target.ContextType);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var entity = existingId is { } id ? await db.FindAsync(target.EntityClrType, [id], cancellationToken).ConfigureAwait(false) : null;
        var isNew = entity is null;
        if (isNew)
        {
            entity = Activator.CreateInstance(target.EntityClrType, nonPublic: true)!;
            db.Add(entity);
        }

        var entry = db.Entry(entity!);
        var effective = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        if (isNew)
        {
            foreach (var (code, value) in target.Defaults)
            {
                if (!effective.TryGetValue(code, out var current) || current is null)
                {
                    effective[code] = value;
                }
            }
        }

        foreach (var (code, value) in effective)
        {
            var field = target.Field(code);
            if (field is null)
            {
                continue;
            }

            var property = entry.Property(field.Property);
            var clr = property.Metadata.ClrType;
            if (value is null && !property.Metadata.IsNullable)
            {
                if (field.Required)
                {
                    return Fail(field.Code, "integration.sync.exceptions.requiredMissing");
                }

                continue; // a non-nullable value the source left empty keeps its current (or initial) value
            }

            if (value is string text && property.Metadata.GetMaxLength() is { } max && text.Length > max && property.Metadata.GetColumnType() != "jsonb")
            {
                return Fail(field.Code, "integration.sync.exceptions.tooLong", new Dictionary<string, object?> { ["max"] = max });
            }

            if (!TryConvert(value, clr, property.Metadata.GetColumnType(), out var converted))
            {
                return Fail(field.Code, "integration.sync.exceptions.invalidValue");
            }

            if (!Equals(property.CurrentValue, converted))
            {
                property.CurrentValue = converted;
            }
        }

        var recordId = (Guid)entry.Property("Id").CurrentValue!;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (target.HookType is { } hookType && services.GetService(hookType) is ISyncTargetHook hook)
        {
            await hook.OnWrittenAsync(recordId, isNew, effective, db, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var linked = await LinkAsync(reference with { RecordId = recordId }, db, cancellationToken).ConfigureAwait(false);
        if (linked.IsFailure)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return linked.Error!;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SyncWriteOutcome(recordId, isNew);
    }

    private async Task<Result> LinkAsync(ExternalReferenceInput input, object context, CancellationToken cancellationToken)
    {
        var linked = await references.LinkAsync(input, context, cancellationToken).ConfigureAwait(false);
        return linked.IsSuccess ? Result.Success() : linked.Error!;
    }

    private static Error Fail(string field, string messageKey, IReadOnlyDictionary<string, object?>? parameters = null) =>
        Error.Validation([new FieldError(field, messageKey.Split('.')[^1], messageKey, parameters)]);

    /// <summary>Converts a mapped value (string, DateOnly, DateTimeOffset, decimal, int, bool, Guid, string[]) to the property's CLR type.</summary>
    public static bool TryConvert(object? value, Type clr, string? columnType, out object? converted)
    {
        ArgumentNullException.ThrowIfNull(clr);
        var type = Nullable.GetUnderlyingType(clr) ?? clr;
        converted = null;
        switch (value)
        {
            case null:
                return true;
            case string[] codes when type == typeof(string):
                converted = JsonSerializer.Serialize(codes);
                return true;
            case string[] codes when typeof(List<string>).IsAssignableTo(clr) || clr == typeof(List<string>):
                converted = codes.ToList();
                return true;
            case string text when type == typeof(string):
                converted = text;
                return true;
            case decimal d when type == typeof(decimal):
                converted = d;
                return true;
            case decimal d when type == typeof(double):
                converted = (double)d;
                return true;
            case decimal d when type == typeof(int) && d == decimal.Truncate(d):
                converted = (int)d;
                return true;
            case int i when type == typeof(int):
                converted = i;
                return true;
            case int i when type == typeof(decimal):
                converted = (decimal)i;
                return true;
            case int i when type == typeof(short):
                converted = (short)i;
                return true;
            case bool b when type == typeof(bool):
                converted = b;
                return true;
            case Guid g when type == typeof(Guid):
                converted = g;
                return true;
            case DateOnly date when type == typeof(DateOnly):
                converted = date;
                return true;
            case DateOnly date when type == typeof(DateTimeOffset):
                converted = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            case DateTimeOffset at when type == typeof(DateTimeOffset):
                converted = at.ToUniversalTime();
                return true;
            case DateTimeOffset at when type == typeof(DateOnly):
                converted = DateOnly.FromDateTime(at.UtcDateTime);
                return true;
            default:
                return false;
        }
    }
}
