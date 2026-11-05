using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

/// <summary>Every metric, list and dataset declared by the modules (ADR 0140).</summary>
public sealed class MetricRegistry(
    IEnumerable<IReportingMetricSource> metricSources, IEnumerable<IReportingListSource> listSources, IEnumerable<IReportingDatasetSource> datasetSources)
{
    private readonly Dictionary<string, (MetricDefinition Definition, IReportingMetricSource Source)> _metrics =
        metricSources.SelectMany(s => s.Metrics.Select(m => (m, s))).GroupBy(p => p.m.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().m, g.First().s), StringComparer.Ordinal);

    private readonly Dictionary<string, (ReportingListDefinition Definition, IReportingListSource Source)> _lists =
        listSources.SelectMany(s => s.Lists.Select(l => (l, s))).GroupBy(p => p.l.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().l, g.First().s), StringComparer.Ordinal);

    private readonly Dictionary<string, (DatasetDefinition Definition, IReportingDatasetSource Source)> _datasets =
        datasetSources.SelectMany(s => s.Datasets.Select(d => (d, s))).GroupBy(p => p.d.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().d, g.First().s), StringComparer.Ordinal);

    public IReadOnlyList<IReportingMetricSource> MetricSources { get; } = metricSources.ToList();

    public IEnumerable<MetricDefinition> Metrics => _metrics.Values.Select(v => v.Definition);

    public IEnumerable<ReportingListDefinition> Lists => _lists.Values.Select(v => v.Definition);

    public IEnumerable<DatasetDefinition> Datasets => _datasets.Values.Select(v => v.Definition);

    public bool TryMetric(string code, out MetricDefinition definition, out IReportingMetricSource source)
    {
        if (_metrics.TryGetValue(code, out var found))
        {
            (definition, source) = found;
            return true;
        }

        definition = null!;
        source = null!;
        return false;
    }

    public bool TryList(string code, out ReportingListDefinition definition, out IReportingListSource source)
    {
        if (_lists.TryGetValue(code, out var found))
        {
            (definition, source) = found;
            return true;
        }

        definition = null!;
        source = null!;
        return false;
    }

    public bool TryDataset(string code, out DatasetDefinition definition, out IReportingDatasetSource source)
    {
        if (_datasets.TryGetValue(code, out var found))
        {
            (definition, source) = found;
            return true;
        }

        definition = null!;
        source = null!;
        return false;
    }
}

/// <summary>
/// Dirty marks (ADR 0142): told by the audit interceptor which system's data changed. In memory (a lost mark only waits for the next full
/// refresh, every <see cref="ReportingSettings.RefreshMinutes"/>).
/// </summary>
public sealed class ReportingChangeTracker(TimeProvider time) : ISaveChangesObserver
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dirty = new(StringComparer.Ordinal);

    /// <summary>Systems built on the Request Framework: a request change can move their figures.</summary>
    private static readonly string[] RequestSystems = ["docreg", "ip", "bench"];

    public void OnSaved(string systemCode)
    {
        if (string.IsNullOrEmpty(systemCode) || systemCode == ReportingDbContext.SchemaName)
        {
            return;
        }

        Mark(systemCode.Replace("_", string.Empty, StringComparison.Ordinal));
        if (systemCode == "requests")
        {
            foreach (var system in RequestSystems)
            {
                Mark(system);
            }
        }
    }

    public void Mark(string systemCode) => _dirty.TryAdd(systemCode, time.GetUtcNow());

    public bool HasDirty => !_dirty.IsEmpty;

    /// <summary>Takes the systems marked at least <paramref name="settle"/> ago (a short debounce lets a burst of saves commit).</summary>
    public IReadOnlyList<string> Take(TimeSpan settle)
    {
        var limit = time.GetUtcNow() - settle;
        var taken = new List<string>();
        foreach (var (system, at) in _dirty)
        {
            if (at <= limit && _dirty.TryRemove(system, out _))
            {
                taken.Add(system);
            }
        }

        return taken;
    }
}

/// <summary>Today in the installation's time zone (Settings → Regional).</summary>
public sealed class ReportingClock(IClock clock, ISettingsStore settings)
{
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(regional.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    public DateTimeOffset UtcNow => clock.UtcNow;
}

/// <summary>Writes facts (ADR 0140 / 0142). Runs as the system: sources return every record.</summary>
public sealed partial class FactRefresher(
    MetricRegistry registry, ReportingDbContext db, ReportingClock clock, ISystemState systems, ILogger<FactRefresher> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Recomputes <paramref name="from"/>–<paramref name="to"/> for the given systems (all when null). Stock metrics only ever replace the current month.</summary>
    public async Task<int> RefreshAsync(IReadOnlyCollection<string>? systemCodes, DateOnly from, DateOnly to, string kind, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var current = ReportPeriod.MonthOf(today);
        var total = 0;
        foreach (var source in registry.MetricSources)
        {
            if ((systemCodes is not null && !systemCodes.Contains(source.SystemCode, StringComparer.Ordinal))
                || !await systems.IsEnabledAsync(source.SystemCode, ct).ConfigureAwait(false))
            {
                continue;
            }

            total += await RefreshSourceAsync(source, from, to, current, today, kind, ct).ConfigureAwait(false);
        }

        return total;
    }

    private async Task<int> RefreshSourceAsync(IReportingMetricSource source, DateOnly from, DateOnly to, DateOnly current, DateOnly today, string kind, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var codes = source.Metrics.Select(m => m.Code).ToList();
        var stock = source.Metrics.Where(m => m.Kind == MetricKind.Stock).Select(m => m.Code).ToList();
        var flow = source.Metrics.Where(m => m.Kind == MetricKind.Flow).Select(m => m.Code).ToList();
        var log = new RefreshLog { SystemCode = source.SystemCode, Kind = kind, FromMonth = from, ToMonth = to, At = clock.UtcNow };
        try
        {
            var facts = await source.ComputeAsync(new MetricComputation(codes, from, to, current, today, null), ct).ConfigureAwait(false);
            var known = codes.ToHashSet(StringComparer.Ordinal);
            var rows = facts.Where(f => known.Contains(f.Metric) && f.Month >= from && f.Month <= to)
                .Where(f => !stock.Contains(f.Metric) || f.Month == current)
                .Select(f => new MetricFactRow
                {
                    Metric = f.Metric, Month = f.Month, UnitId = f.UnitId, Partition = f.Partition, DimsJson = JsonSerializer.Serialize(f.Dimensions, Json),
                    Value = f.Value, Weight = f.Weight, ComputedAt = log.At,
                })
                .ToList();
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                if (flow.Count > 0)
                {
                    await db.Facts.Where(f => flow.Contains(f.Metric) && f.Month >= from && f.Month <= to).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                }

                if (stock.Count > 0 && current >= from && current <= to)
                {
                    await db.Facts.Where(f => stock.Contains(f.Metric) && f.Month == current).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                }

                db.Facts.AddRange(rows);
                log.Facts = rows.Count;
                log.DurationMs = (int)watch.ElapsedMilliseconds;
                db.RefreshLogs.Add(log);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return rows.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failing source keeps its previous facts (and their "as of" time).
            LogFailed(logger, ex, source.SystemCode);
            db.ChangeTracker.Clear();
            db.RefreshLogs.Add(new RefreshLog
            {
                SystemCode = source.SystemCode, Kind = kind, FromMonth = from, ToMonth = to, At = clock.UtcNow, Error = ex.GetType().Name + ": " + Truncate(ex.Message),
                DurationMs = (int)watch.ElapsedMilliseconds,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return 0;
        }
    }

    private static string Truncate(string text) => text.Length > 1500 ? text[..1500] : text;

    [LoggerMessage(Level = LogLevel.Error, Message = "Reporting refresh of {System} failed; previous facts kept")]
    private static partial void LogFailed(ILogger logger, Exception exception, string system);
}

/// <summary>A fact as read for one viewer (cache or live path).</summary>
public sealed record ViewFact(DateOnly Month, Guid? UnitId, IReadOnlyDictionary<string, string> Dims, decimal Value, decimal Weight);

/// <summary>The facts of one metric a viewer may see over a month range, with where they came from.</summary>
public sealed record FactSet(MetricDefinition Definition, IReadOnlyList<ViewFact> Facts, DateOnly CurrentMonth, DateTimeOffset? AsOf, bool Live);

/// <summary>One aggregated value: <c>Value</c> is Σ value (sum) or Σ value ÷ Σ weight (ratio, null when Σ weight = 0).</summary>
public sealed record Aggregate(decimal? Value, decimal Num, decimal Den, int Facts)
{
    public static Aggregate Of(MetricDefinition definition, IEnumerable<ViewFact> facts)
    {
        decimal num = 0, den = 0;
        var count = 0;
        foreach (var f in facts)
        {
            num += f.Value;
            den += f.Weight;
            count++;
        }

        decimal? value = definition.Aggregation == MetricAggregation.Sum ? num : den == 0 ? null : num / den;
        return new Aggregate(value, num, den, count);
    }
}

/// <summary>
/// Reads metrics for a viewer (ADR 0141): from the cache when it is provably exact for their scope, otherwise live from the module on
/// their scoped query. Per-request memo; live results are cached 60 s per user.
/// </summary>
public sealed class MetricEvaluator(
    MetricRegistry registry, ReportingDbContext db, IAccessService access, ReportingClock clock, IMemoryCache cache)
{
    private readonly Dictionary<(string, DateOnly, DateOnly), FactSet?> _memo = [];
    private readonly Dictionary<string, EffectiveScope> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyCollection<string>> _partitions = new(StringComparer.Ordinal);

    public async Task<DateOnly> TodayAsync(CancellationToken ct) => await clock.TodayAsync(ct).ConfigureAwait(false);

    /// <summary>Loads the facts of several metrics at once (one database round trip for the cached ones).</summary>
    public async Task PreloadAsync(ClaimsPrincipal user, IReadOnlyCollection<string> metrics, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var cached = new List<string>();
        foreach (var code in metrics.Distinct(StringComparer.Ordinal))
        {
            if (_memo.ContainsKey((code, from, to)) || !registry.TryMetric(code, out var definition, out _))
            {
                continue;
            }

            var scope = await ScopeAsync(user, definition, ct).ConfigureAwait(false);
            if (scope is { Allowed: true } && !NeedsLive(scope, definition))
            {
                cached.Add(code);
            }
        }

        if (cached.Count == 0)
        {
            return;
        }

        var rows = await db.Facts.AsNoTracking().Where(f => cached.Contains(f.Metric) && f.Month >= from && f.Month <= to).ToListAsync(ct).ConfigureAwait(false);
        var byMetric = rows.ToLookup(r => r.Metric, StringComparer.Ordinal);
        foreach (var code in cached)
        {
            _memo[(code, from, to)] = await FromRowsAsync(user, code, byMetric[code].ToList(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Null when the viewer may not see the metric at all (no system access, no permission, unknown metric).</summary>
    public async Task<FactSet?> FactsAsync(ClaimsPrincipal user, string metric, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (_memo.TryGetValue((metric, from, to), out var known))
        {
            return known;
        }

        FactSet? result = null;
        if (registry.TryMetric(metric, out var definition, out var source))
        {
            var scope = await ScopeAsync(user, definition, ct).ConfigureAwait(false);
            if (scope is { Allowed: true })
            {
                if (NeedsLive(scope, definition))
                {
                    result = await LiveAsync(user, definition, source, from, to, ct).ConfigureAwait(false);
                }
                else
                {
                    var rows = await db.Facts.AsNoTracking().Where(f => f.Metric == metric && f.Month >= from && f.Month <= to).ToListAsync(ct).ConfigureAwait(false);
                    result = await FromRowsAsync(user, metric, rows, ct).ConfigureAwait(false);
                }
            }
        }

        _memo[(metric, from, to)] = result;
        return result;
    }

    /// <summary>Whether the viewer reads this metric live (diagnostics and tests).</summary>
    public async Task<bool?> IsLiveAsync(ClaimsPrincipal user, string metric, CancellationToken ct)
    {
        if (!registry.TryMetric(metric, out var definition, out _))
        {
            return null;
        }

        var scope = await ScopeAsync(user, definition, ct).ConfigureAwait(false);
        return scope.Allowed ? NeedsLive(scope, definition) : null;
    }

    private async Task<FactSet?> FromRowsAsync(ClaimsPrincipal user, string metric, List<MetricFactRow> rows, CancellationToken ct)
    {
        registry.TryMetric(metric, out var definition, out var source);
        var scope = await ScopeAsync(user, definition, ct).ConfigureAwait(false);
        var partitions = await PartitionsAsync(user, source, ct).ConfigureAwait(false);
        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var visible = rows.Where(r => partitions.Contains(r.Partition))
            .Where(r => scope.All || (r.UnitId is { } unit && scope.UnitIds.Contains(unit)))
            .Select(r => new ViewFact(r.Month, r.UnitId, Dims(r.DimsJson), r.Value, r.Weight))
            .ToList();
        var asOf = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(r => r.ComputedAt);
        return new FactSet(definition, visible, ReportPeriod.MonthOf(today), asOf, false);
    }

    private async Task<FactSet> LiveAsync(ClaimsPrincipal user, MetricDefinition definition, IReportingMetricSource source, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var key = ("reporting-live", user.UserId(), definition.Code, from, to);
        if (!cache.TryGetValue(key, out IReadOnlyList<MetricFact>? facts) || facts is null)
        {
            facts = await source.ComputeAsync(new MetricComputation([definition.Code], from, to, ReportPeriod.MonthOf(today), today, user), ct).ConfigureAwait(false);
            cache.Set(key, facts, TimeSpan.FromSeconds(60));
        }

        var view = facts.Where(f => f.Metric == definition.Code && f.Month >= from && f.Month <= to)
            .Select(f => new ViewFact(f.Month, f.UnitId, f.Dimensions, f.Value, f.Weight)).ToList();
        return new FactSet(definition, view, ReportPeriod.MonthOf(today), clock.UtcNow, true);
    }

    private static Dictionary<string, string> Dims(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, FactRefresher.Json) ?? [];

    /// <summary>The cache is exact only for scope All or unit scopes without record-role grants (ADR 0141).</summary>
    private static bool NeedsLive(EffectiveScope scope, MetricDefinition definition) =>
        !scope.All && (scope.OwnOrTeam || scope.RecordGrantTypes.Count > 0 || scope.UnitIds.Count == 0);

    private async Task<EffectiveScope> ScopeAsync(ClaimsPrincipal user, MetricDefinition definition, CancellationToken ct)
    {
        if (!_scopes.TryGetValue(definition.ScopePermission, out var scope))
        {
            scope = await access.GetScopeAsync(user, definition.ScopePermission, ct).ConfigureAwait(false);
            _scopes[definition.ScopePermission] = scope;
        }

        return scope;
    }

    private async Task<IReadOnlyCollection<string>> PartitionsAsync(ClaimsPrincipal user, IReportingMetricSource source, CancellationToken ct)
    {
        if (!_partitions.TryGetValue(source.SystemCode, out var partitions))
        {
            var visible = new HashSet<string>(StringComparer.Ordinal) { ReportingPartitions.Public };
            if (user.IsSystem())
            {
                visible.UnionWith(await db.Facts.AsNoTracking().Select(f => f.Partition).Distinct().ToListAsync(ct).ConfigureAwait(false));
            }
            else
            {
                visible.UnionWith((await source.VisiblePartitionsAsync(user, ct).ConfigureAwait(false)).Where(p => p != ReportingPartitions.Confidential));
            }

            partitions = visible;
            _partitions[source.SystemCode] = partitions;
        }

        return partitions;
    }
}

/// <summary>Maps units to their department (Settings → Reporting department level) for the comparison scorecard; cached 5 minutes.</summary>
public sealed class DepartmentMap(IOrgDirectory org, ISettingsStore settings, IMemoryCache cache)
{
    public sealed record Department(Guid Id, string Code, string? NameAr, string? NameEn, IReadOnlySet<Guid> Units);

    public async Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct)
    {
        var level = (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).DepartmentLevelCode;
        var key = ("reporting-departments", level);
        if (cache.TryGetValue(key, out IReadOnlyList<Department>? known) && known is not null)
        {
            return known;
        }

        var result = new List<Department>();
        foreach (var unit in await org.ListActiveUnitsAtLevelAsync(level, ct).ConfigureAwait(false))
        {
            var below = await org.GetSelfAndDescendantIdsAsync(unit.Id, ct).ConfigureAwait(false);
            result.Add(new Department(unit.Id, unit.Code, unit.NameAr, unit.NameEn, below.ToHashSet()));
        }

        cache.Set(key, (IReadOnlyList<Department>)result, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<IReadOnlySet<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct)
    {
        var key = ("reporting-unit-below", unitId);
        if (cache.TryGetValue(key, out IReadOnlySet<Guid>? known) && known is not null)
        {
            return known;
        }

        var set = (await org.GetSelfAndDescendantIdsAsync(unitId, ct).ConfigureAwait(false)).ToHashSet();
        cache.Set(key, (IReadOnlySet<Guid>)set, TimeSpan.FromMinutes(5));
        return set;
    }
}

/// <summary>The last refresh of each system (as-of times on the job / settings screen).</summary>
public sealed class FactStatus(ReportingDbContext db, MetricRegistry registry)
{
    public async Task<IReadOnlyList<Api.RefreshStatusDto>> ListAsync(CancellationToken ct)
    {
        var systems = registry.MetricSources.Select(s => s.SystemCode).Distinct(StringComparer.Ordinal).ToList();
        var result = new List<Api.RefreshStatusDto>();
        foreach (var system in systems)
        {
            var last = await db.RefreshLogs.AsNoTracking().Where(l => l.SystemCode == system).OrderByDescending(l => l.At).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            result.Add(new Api.RefreshStatusDto(system, last?.At, last?.Kind, last?.Facts ?? 0, last?.Error));
        }

        return result;
    }
}
