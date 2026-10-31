using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Strategy.Platform.Settings.Infrastructure;

namespace Strategy.Platform.Settings.Application;

/// <summary>Shared cache for settings reads; every change removes the matching entries (other instances converge in 60 s).</summary>
internal sealed class SettingsCache(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private long _generation;

    public async Task<T> GetAsync<T>(string key, Func<Task<T>> load)
    {
        var cacheKey = (nameof(SettingsCache), Interlocked.Read(ref _generation), key);
        if (cache.TryGetValue(cacheKey, out T? value) && value is not null)
        {
            return value;
        }

        value = await load().ConfigureAwait(false);
        cache.Set(cacheKey, value, Lifetime);
        return value;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);
}

internal sealed class SettingsStore(SettingsDbContext db, SettingsCache cache) : ISettingsStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<T> GetAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettingsArea =>
        cache.GetAsync("setting:" + T.Key, async () =>
        {
            var key = T.Key;
            var entry = await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, cancellationToken).ConfigureAwait(false);
            return Deserialize<T>(entry?.Value);
        });

    internal static T Deserialize<T>(string? json)
        where T : class => JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json)!;
}

internal sealed class LookupService(SettingsDbContext db, SettingsCache cache) : ILookupService
{
    public async Task<IReadOnlyList<LookupValueReference>> GetValuesAsync(string typeCode, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var all = await cache.GetAsync("lookup:" + typeCode, async () => (IReadOnlyList<LookupValueReference>)await db.LookupValues.AsNoTracking()
            .Where(v => v.TypeCode == typeCode)
            .OrderBy(v => v.Sort).ThenBy(v => v.Code)
            .Select(v => new LookupValueReference(v.Id, v.Code, v.LabelAr, v.LabelEn, v.Sort, v.Active, v.ParentValueId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        return includeInactive ? all : all.Where(v => v.Active).ToList();
    }

    public async Task<bool> IsActiveCodeAsync(string typeCode, string code, CancellationToken cancellationToken = default) =>
        (await GetValuesAsync(typeCode, false, cancellationToken).ConfigureAwait(false)).Any(v => v.Code == code);
}

internal sealed class FeatureFlags(SettingsDbContext db, SettingsCache cache) : IFeatureFlags
{
    public async Task<bool> IsEnabledAsync(string code, CancellationToken cancellationToken = default)
    {
        var flags = await cache.GetAsync("features", async () => (IReadOnlyDictionary<string, bool>)await db.FeatureFlags.AsNoTracking()
            .ToDictionaryAsync(f => f.Code, f => f.Enabled, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        return flags.TryGetValue(code, out var enabled) && enabled;
    }
}

/// <summary>ADR 0017: the counter increment runs on the caller's connection and joins its transaction.</summary>
internal sealed class NumberingService(SettingsDbContext db, SettingsCache cache) : INumberingService
{
    private const string Upsert = """
        INSERT INTO settings.numbering_counter (format_key, partition, last_value) VALUES (@key, @partition, 1)
        ON CONFLICT (format_key, partition) DO UPDATE SET last_value = settings.numbering_counter.last_value + 1
        RETURNING last_value
        """;

    public async Task<string> NextAsync(DbContext caller, string formatKey, NumberingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var pattern = await PatternAsync(formatKey, cancellationToken).ConfigureAwait(false);
        var partition = NumberingPattern.Partition(pattern, context);

        var connection = caller.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await caller.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Upsert;
            command.Transaction = caller.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "key", formatKey);
            Add(command, "partition", partition);
            var next = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            return NumberingPattern.Render(pattern, context, next);
        }
        finally
        {
            if (opened)
            {
                await caller.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<string> PatternAsync(string formatKey, CancellationToken cancellationToken) =>
        cache.GetAsync("numbering:" + formatKey, async () =>
            await db.NumberingFormats.AsNoTracking().Where(f => f.Key == formatKey).Select(f => f.Pattern).FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Numbering format '{formatKey}' is not registered."));

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
