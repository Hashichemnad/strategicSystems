using Npgsql;
using NpgsqlTypes;
using Strategy.Platform.Kernel.Integration;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Integration.Sync;

/// <summary>
/// <see cref="IStandbyGuard"/> over the sync connections (ADR 0206). Reads through the data source (not a DbContext) so it can run inside another
/// context's save; the mode is cached for a few seconds and invalidated by mode changes in this process.
/// </summary>
public sealed class SyncStandbyGuard(NpgsqlDataSource dataSource, IClock clock) : IStandbyGuard
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);
    private readonly Lock _lock = new();
    private Snapshot? _snapshot;

    private sealed record Snapshot(DateTimeOffset At, IReadOnlyDictionary<string, string> Sources, IReadOnlySet<string> Suspended);

    /// <summary>Forget the cached mode (after make primary / return / close / activate).</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
        }
    }

    public async ValueTask<bool> IsActiveAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(cancellationToken).ConfigureAwait(false)).Sources.Count > 0;

    public async ValueTask<IReadOnlySet<string>> SuspendedSystemsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Sources.Count > 0 ? snapshot.Suspended : new HashSet<string>();
    }

    public async Task<IReadOnlyDictionary<RecordKey, string>> FindReadOnlyAsync(IReadOnlyCollection<RecordKey> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        var snapshot = await GetAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<RecordKey, string>();
        if (snapshot.Sources.Count == 0 || records.Count == 0)
        {
            return result;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // Matched by record id only: ids are Guid v7 and unique across types, and audit entity codes (e.g. org.org_unit) may differ from
        // the reference types (org.unit).
        var byId = records.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        await using var command = new NpgsqlCommand(
            """
            select distinct on (r.record_id) r.record_id, r.source_system
            from integration.external_ref r
            where r.is_origin and r.record_id = any(@ids) and r.source_system = any(@sources)
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = byId.Keys.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = snapshot.Sources.Keys.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[byId[reader.GetGuid(0)]] = snapshot.Sources[reader.GetString(1)];
        }

        return result;
    }

    public async Task OnApprovalSubmittedAsync(RecordKey record, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            update integration.sync_outage_record r set resubmit_required = false
            from integration.sync_outage o
            where o.id = r.outage_id and o.status <> 'closed' and r.entity_type = @type and r.record_id = @id and r.resubmit_required
            """, connection);
        command.Parameters.AddWithValue("type", record.EntityType);
        command.Parameters.AddWithValue("id", record.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Snapshot> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_snapshot is { } cached && clock.UtcNow - cached.At < CacheFor && clock.UtcNow >= cached.At)
            {
                return cached;
            }
        }

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var suspended = new HashSet<string>(SyncSettings.DefaultSuspendedSystems, StringComparer.Ordinal);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using (var command = new NpgsqlCommand(
                """
                select c.source_system, coalesce(s.name_en, s.name_ar, c.source_system)
                from integration.sync_connection c left join integration.source_system s on s.code = c.source_system
                where c.mode in ('standby', 'reconciling')
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    sources[reader.GetString(0)] = reader.GetString(1);
                }
            }

            await using var settings = new NpgsqlCommand("select value::text from settings.setting where key = @key", connection);
            settings.Parameters.AddWithValue("key", SyncSettings.Key);
            try
            {
                if (await settings.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string json
                    && SyncJson.Read<SyncSettingsDocument>(json).SuspendedSystems is { } configured)
                {
                    suspended = new HashSet<string>(configured, StringComparer.Ordinal);
                }
            }
            catch (PostgresException)
            {
                // settings table layout differs: keep the defaults
            }
        }

        var snapshot = new Snapshot(clock.UtcNow, sources, suspended);
        lock (_lock)
        {
            _snapshot = snapshot;
        }

        return snapshot;
    }

    private sealed class SyncSettingsDocument
    {
        public List<string>? SuspendedSystems { get; init; }
    }
}
