using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.Platform.Access;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Integration.Application;

/// <summary>
/// <c>integration.external_ref</c> (ADR 0164). Both uniqueness rules are checked first and, for a concurrent writer that passes the check, mapped
/// from the unique index violation to the same validation error. In a joined transaction EF Core wraps the save in a savepoint, so a violation
/// leaves the caller's transaction usable.
/// </summary>
internal sealed class ExternalReferenceStore(
    IntegrationDbContext db, IClock clock, ILinkRegistry links, IEnumerable<ReferenceableTypeDefinition> referenceable) : IExternalReferenceStore
{
    private readonly HashSet<string> _registered = referenceable.Select(d => d.ResourceType).ToHashSet(StringComparer.Ordinal);

    public bool IsReferenceable(string entityType) => _registered.Contains(entityType) || links.Find(entityType) is not null;

    public async Task<ExternalReference?> FindAsync(string sourceSystem, string entityType, string externalId, CancellationToken cancellationToken = default)
    {
        var found = await db.References.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SourceSystem == sourceSystem && r.EntityTypeCode == entityType && r.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
        return found?.ToContract();
    }

    public async Task<IReadOnlyList<ExternalReference>> ListForRecordAsync(string entityType, Guid recordId, CancellationToken cancellationToken = default)
    {
        var rows = await db.References.AsNoTracking().Where(r => r.EntityTypeCode == entityType && r.RecordId == recordId)
            .OrderBy(r => r.FirstSeenAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.ToContract()).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ExternalReference>>> ListForRecordsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        if (recordIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ExternalReference>>();
        }

        var ids = recordIds.Distinct().ToList();
        var rows = await db.References.AsNoTracking().Where(r => r.EntityTypeCode == entityType && ids.Contains(r.RecordId))
            .OrderBy(r => r.FirstSeenAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(r => r.RecordId).ToDictionary(g => g.Key, g => (IReadOnlyList<ExternalReference>)g.Select(r => r.ToContract()).ToList());
    }

    public async Task<Result<ExternalReference>> LinkAsync(ExternalReferenceInput input, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var externalId = input.ExternalId?.Trim();
        if (string.IsNullOrEmpty(externalId))
        {
            return ExternalReferenceErrors.Of("externalId", ExternalReferenceErrors.ExternalIdRequired);
        }

        if (externalId.Length > ExternalReferenceErrors.ExternalIdMaxLength)
        {
            return ExternalReferenceErrors.Of("externalId", ExternalReferenceErrors.ExternalIdTooLong,
                new Dictionary<string, object?> { ["maxLength"] = ExternalReferenceErrors.ExternalIdMaxLength });
        }

        if (input.SourceHash is { Length: > ExternalReferenceErrors.SourceHashMaxLength } || (input.SyncStatus is { } status && !SyncStatuses.All.Contains(status)))
        {
            return ExternalReferenceErrors.Of("sourceHash", "validation.invalid");
        }

        if (!IsReferenceable(input.EntityType))
        {
            return ExternalReferenceErrors.Of("entityType", ExternalReferenceErrors.UnknownType);
        }

        var source = await ValidateSourceAsync(input.SourceSystem, "sourceSystem", cancellationToken).ConfigureAwait(false);
        if (source.IsFailure)
        {
            return source.Error;
        }

        input = input with { ExternalId = externalId };
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var existing = await db.References
            .FirstOrDefaultAsync(r => r.SourceSystem == input.SourceSystem && r.EntityTypeCode == input.EntityType && r.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.RecordId != input.RecordId)
        {
            return Taken();
        }

        if (existing is null && await db.References.AnyAsync(
                r => r.SourceSystem == input.SourceSystem && r.EntityTypeCode == input.EntityType && r.RecordId == input.RecordId, cancellationToken).ConfigureAwait(false))
        {
            return AlreadyReferenced();
        }

        var reference = existing ?? ExternalRef.Create(input, clock.UtcNow);
        if (existing is null)
        {
            db.References.Add(reference);
        }
        else
        {
            existing.Refresh(input, clock.UtcNow);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            db.Entry(reference).State = EntityState.Detached;
            return violation.ConstraintName == ExternalRef.RecordIndex ? AlreadyReferenced() : Taken();
        }

        return reference.ToContract();
    }

    public async Task<Result> UnlinkAsync(string sourceSystem, string entityType, string externalId, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var reference = await db.References
            .FirstOrDefaultAsync(r => r.SourceSystem == sourceSystem && r.EntityTypeCode == entityType && r.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            return Error.NotFound();
        }

        db.References.Remove(reference);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ExternalRecordMatch>> FindByExternalIdAsync(string text, int limit = 20, CancellationToken cancellationToken = default)
    {
        var term = text?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length > ExternalReferenceErrors.ExternalIdMaxLength)
        {
            return [];
        }

        var exact = term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        var pattern = exact + "%";
        var rows = await db.References.AsNoTracking()
            .Where(r => EF.Functions.ILike(r.ExternalId, pattern))
            .OrderBy(r => EF.Functions.ILike(r.ExternalId, exact) ? 0 : 1).ThenByDescending(r => r.LastSyncedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(r => new ExternalRecordMatch(r.EntityTypeCode, r.RecordId, r.SourceSystem, r.ExternalId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<SourceSystemInfo?> GetSourceAsync(string code, CancellationToken cancellationToken = default)
    {
        var source = await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, cancellationToken).ConfigureAwait(false);
        return source is null ? null : new SourceSystemInfo(source.Id, source.Code, source.NameAr, source.NameEn, source.TypeCode, source.Active);
    }

    public async Task<Result<SourceSystemInfo>> ValidateSourceAsync(string? code, string field = "sourceSystem", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ExternalReferenceErrors.Of(field, ExternalReferenceErrors.SourceRequired);
        }

        var source = await GetSourceAsync(code.Trim(), cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return ExternalReferenceErrors.Of(field, ExternalReferenceErrors.UnknownSource);
        }

        return source.Active ? source : ExternalReferenceErrors.Of(field, ExternalReferenceErrors.InactiveSource);
    }

    public async Task<IAsyncDisposable> LockAsync(string sourceSystem, string entityType, string externalId, CancellationToken cancellationToken = default)
    {
        // A dedicated session: the context's own connection may later join a module's transaction (ADR 0023).
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var key = $"integration.external_ref|{sourceSystem}|{entityType}|{externalId}";
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@key, 0))", connection))
        {
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AdvisoryLock(connection, key);
    }

    private static Error Taken() => ExternalReferenceErrors.Of("externalId", ExternalReferenceErrors.ExternalIdTaken);

    private static Error AlreadyReferenced() => ExternalReferenceErrors.Of("externalId", ExternalReferenceErrors.RecordAlreadyReferenced);

    private sealed class AdvisoryLock(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key, 0))", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                // Closing the session releases the lock in any case.
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>The visibility check for referenced records (ADR 0164): the Links registry, or the registered referenceable source.</summary>
internal sealed class ReferencedRecordResolver(
    ILinkRegistry links, IEnumerable<ReferenceableTypeDefinition> definitions, IServiceProvider services, IAccessService access, ISystemState systems)
    : IReferencedRecordResolver
{
    private readonly Dictionary<string, ReferenceableTypeDefinition> _types =
        definitions.GroupBy(d => d.ResourceType, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    public ReferenceableTypeDefinition? Registered(string type) => _types.GetValueOrDefault(type);

    public IReadOnlyCollection<ReferenceableTypeDefinition> RegisteredTypes => _types.Values;

    public async Task<IReadOnlyDictionary<Guid, ResolvedRecord>> ResolveAsync(string resourceType, IReadOnlyCollection<Guid> ids, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new Dictionary<Guid, ResolvedRecord>();
        if (ids.Count == 0 || !user.IsInternal())
        {
            return result;
        }

        if (links.Find(resourceType) is not null)
        {
            var views = await links.ResolveAsync(ids.Distinct().Select(id => new LinkRef(resourceType, id)).ToList(), user, cancellationToken).ConfigureAwait(false);
            foreach (var v in views.Where(v => v.Visibility == LinkVisibility.Visible))
            {
                result[v.Id] = new ResolvedRecord(v.Type, v.Id, v.SystemCode!, v.TypeNameAr, v.TypeNameEn, v.Number, v.TitleAr, v.TitleEn, v.Link);
            }

            return result;
        }

        if (!_types.TryGetValue(resourceType, out var type)
            || services.GetServices<IReferenceableRecordSource>().LastOrDefault(s => s.ResourceType == resourceType) is not { } source
            || !await systems.IsEnabledAsync(type.SystemCode, cancellationToken).ConfigureAwait(false)
            || !await access.CanAsync(user, type.ReadPermission, null, cancellationToken).ConfigureAwait(false))
        {
            return result;
        }

        var records = await source.GetAsync(ids.Distinct().ToList(), user, cancellationToken).ConfigureAwait(false);
        foreach (var (id, record) in records)
        {
            result[id] = new ResolvedRecord(resourceType, id, type.SystemCode, type.NameAr, type.NameEn, record.Number, record.TitleAr, record.TitleEn, type.LinkOf(id));
        }

        return result;
    }
}
