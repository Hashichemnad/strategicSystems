using System.Globalization;
using System.Security.Claims;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

public static class WidgetTypes
{
    public const string Tile = "tile";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Bubble = "bubble";
    public const string Heatmap = "heatmap";
    public const string Table = "table";
    public const string List = "list";
    public const string StrategyMap = "strategyMap";
    public const string Scorecard = "scorecard";
    public const string Gauge = "gauge";
    public const string Brief = "brief";

    public static readonly IReadOnlyList<string> All = [Tile, Bar, Line, Bubble, Heatmap, Table, List, StrategyMap, Scorecard, Gauge, Brief];

    /// <summary>Widget types that read one metric.</summary>
    public static readonly IReadOnlyList<string> SingleMetric = [Tile, Bar, Line, Heatmap, Table, StrategyMap, Gauge];
}

/// <summary>RAG limits of a tile / gauge (green from / amber from when higher is better, up to otherwise).</summary>
public sealed record Thresholds(decimal Green, decimal Amber, bool HigherIsBetter = true)
{
    public string Rag(decimal? value)
    {
        if (value is not { } v)
        {
            return "none";
        }

        if (HigherIsBetter ? v >= Green : v <= Green)
        {
            return "green";
        }

        return (HigherIsBetter ? v >= Amber : v <= Amber) ? "amber" : "red";
    }
}

/// <summary>
/// One widget of a dashboard definition (ADR 0143). Grid: 12 columns, <c>X</c> / <c>Y</c> / <c>W</c> / <c>H</c> in grid units.
/// <c>Dimension</c>: breakdown (category axis, table rows, heat-map rows); <c>Series</c>: second breakdown (stacks, heat-map columns).
/// The pseudo-dimension <c>unit</c> breaks down by owning unit.
/// </summary>
public sealed record WidgetConfig
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = WidgetTypes.Tile;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string? Metric { get; init; }

    /// <summary>Bubble: x, y, size metrics, broken down by <see cref="Dimension"/>.</summary>
    public List<string>? Metrics { get; init; }

    public string? Dimension { get; init; }

    public string? Series { get; init; }

    public Dictionary<string, List<string>>? Filters { get; init; }

    public int? Top { get; init; }

    public string? List { get; init; }

    /// <summary>Bar: <c>horizontal</c>, <c>stacked</c>.</summary>
    public string? Variant { get; init; }

    public Thresholds? Thresholds { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int W { get; init; } = 4;

    public int H { get; init; } = 2;
}

public sealed record DashboardDefinition
{
    public List<WidgetConfig> Widgets { get; init; } = [];
}

public sealed record DimensionLabelDto(string Dimension, string Code, string? LabelAr, string? LabelEn);

public sealed record TrendPointDto(string Month, decimal? Value);

public sealed record BreakdownRowDto(IReadOnlyDictionary<string, string> Key, decimal? Value, decimal? Previous, string? Link);

public sealed record BubblePointDto(string Code, decimal? X, decimal? Y, decimal? Size);

public sealed record ScorecardCellDto(string Column, decimal? Value, string Rag, string? Link);

public sealed record ScorecardRowDto(Guid UnitId, string Code, string? NameAr, string? NameEn, IReadOnlyList<ScorecardCellDto> Cells, string Link);

public sealed record ScorecardColumnDto(string Code, string LabelAr, string LabelEn, string Metric, string ValueKind, bool HigherIsBetter, decimal Green, decimal Amber);

public sealed record ScorecardDto(IReadOnlyList<ScorecardColumnDto> Columns, IReadOnlyList<ScorecardRowDto> Rows);

public sealed record WidgetListItemDto(
    string ResourceType, Guid Id, string? Number, string? TitleAr, string? TitleEn, string? StatusCode, string Tone, decimal? Value, DateOnly? Date, string Link,
    string? DetailAr, string? DetailEn);

/// <summary>
/// A widget's data for one viewer and period. <c>State</c>: <c>ok</c>, <c>unavailable</c> (the viewer may not see this metric — never 0),
/// <c>empty</c>. Labels resolve dimension codes in both languages.
/// </summary>
public sealed record WidgetDataDto(
    string Id,
    string Type,
    string State,
    string? Metric,
    string? MetricLabelAr,
    string? MetricLabelEn,
    string? ValueKind,
    bool? HigherIsBetter,
    decimal? Value,
    decimal? Previous,
    string? Rag,
    IReadOnlyList<TrendPointDto>? Trend,
    IReadOnlyList<BreakdownRowDto>? Rows,
    IReadOnlyList<DimensionLabelDto>? Labels,
    IReadOnlyList<WidgetListItemDto>? Items,
    IReadOnlyList<BubblePointDto>? Bubbles,
    ScorecardDto? Scorecard,
    string? DrillLink,
    DateTimeOffset? AsOf,
    bool Live);

/// <summary>What a widget is evaluated against: the period, comparison and an optional unit filter (unit and below).</summary>
public sealed record WidgetContext(ReportPeriod Period, bool Compare, Guid? UnitId, IReadOnlySet<Guid>? Units);

/// <summary>Validates widget configurations against the catalogue for a builder (ADR 0143: build only on what you may see).</summary>
public sealed class WidgetValidator(MetricRegistry registry, MetricEvaluator evaluator, IAccessService access)
{
    public async Task<List<FieldError>> ValidateAsync(DashboardDefinition definition, ClaimsPrincipal builder, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (definition.Widgets.Count > 40)
        {
            errors.Add(new FieldError("widgets", "reporting.errors.tooManyWidgets", "reporting.errors.tooManyWidgets"));
        }

        var today = await evaluator.TodayAsync(ct).ConfigureAwait(false);
        var month = ReportPeriod.MonthOf(today);
        for (var i = 0; i < definition.Widgets.Count; i++)
        {
            var w = definition.Widgets[i];
            var field = $"widgets[{i}]";
            if (string.IsNullOrWhiteSpace(w.Id) || w.Id.Length > 50 || !ids.Add(w.Id))
            {
                errors.Add(new FieldError(field + ".id", "reporting.errors.widgetId", "reporting.errors.widgetId"));
            }

            if (!WidgetTypes.All.Contains(w.Type, StringComparer.Ordinal))
            {
                errors.Add(new FieldError(field + ".type", "reporting.errors.widgetType", "reporting.errors.widgetType"));
                continue;
            }

            if (w.X < 0 || w.Y < 0 || w.W < 1 || w.H < 1 || w.X + w.W > 12 || w.H > 12 || (w.TitleAr?.Length ?? 0) > 200 || (w.TitleEn?.Length ?? 0) > 200)
            {
                errors.Add(new FieldError(field, "reporting.errors.widgetLayout", "reporting.errors.widgetLayout"));
            }

            var metrics = w.Type switch
            {
                WidgetTypes.Bubble => w.Metrics ?? [],
                WidgetTypes.List or WidgetTypes.Scorecard or WidgetTypes.Brief => [],
                _ => w.Metric is null ? [] : [w.Metric],
            };
            if (WidgetTypes.SingleMetric.Contains(w.Type, StringComparer.Ordinal) && metrics.Count != 1)
            {
                errors.Add(new FieldError(field + ".metric", "reporting.errors.metricRequired", "reporting.errors.metricRequired"));
            }

            if (w.Type == WidgetTypes.Bubble && metrics.Count != 3)
            {
                errors.Add(new FieldError(field + ".metrics", "reporting.errors.bubbleMetrics", "reporting.errors.bubbleMetrics"));
            }

            foreach (var code in metrics)
            {
                if (!registry.TryMetric(code, out var definitionOf, out _)
                    || await evaluator.IsLiveAsync(builder, code, ct).ConfigureAwait(false) is null)
                {
                    errors.Add(new FieldError(field + ".metric", "reporting.errors.metricNotAllowed", "reporting.errors.metricNotAllowed"));
                    continue;
                }

                foreach (var dim in new[] { w.Dimension, w.Series }.Where(d => d is not null).Concat(w.Filters?.Keys ?? Enumerable.Empty<string>()))
                {
                    if (dim != "unit" && !definitionOf.Dimensions.Contains(dim!, StringComparer.Ordinal))
                    {
                        errors.Add(new FieldError(field + ".dimension", "reporting.errors.dimension", "reporting.errors.dimension"));
                    }
                }
            }

            if (w.Type == WidgetTypes.List)
            {
                if (w.List is null || !registry.TryList(w.List, out var list, out _)
                    || !await access.CanAsync(builder, list.Permission, cancellationToken: ct).ConfigureAwait(false))
                {
                    errors.Add(new FieldError(field + ".list", "reporting.errors.listNotAllowed", "reporting.errors.listNotAllowed"));
                }
            }

            if (w.Top is < 1 or > 50)
            {
                errors.Add(new FieldError(field + ".top", "reporting.errors.top", "reporting.errors.top"));
            }
        }

        _ = month;
        return errors;
    }
}

/// <summary>Evaluates widgets for a viewer (ADR 0141 / 0143): every figure through <see cref="MetricEvaluator"/>, lists through the modules.</summary>
public sealed class WidgetEvaluator(
    MetricRegistry registry, MetricEvaluator evaluator, ILookupService lookups, IAccessService access, ScorecardBuilder scorecards, ISettingsStore settings)
{
    public IEnumerable<string> ScopePermissionsOf(string systemCode) =>
        registry.Metrics.Where(m => m.SystemCode == systemCode).Select(m => m.ScopePermission).Distinct(StringComparer.Ordinal);

    public async Task<int> FiscalStartAsync(CancellationToken ct) =>
        (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).FiscalStartMonth;

    private readonly Dictionary<string, IReadOnlyList<LookupValueReference>> _lookups = new(StringComparer.Ordinal);

    /// <summary>The month range a set of widgets needs: the period, its comparison and a 12-month trend.</summary>
    public static (DateOnly From, DateOnly To) Range(WidgetContext context)
    {
        var previous = context.Period.Previous();
        var trendFrom = context.Period.To.AddMonths(-11);
        var from = new[] { context.Period.From, previous.From, trendFrom }.Min();
        return (from, context.Period.To);
    }

    public async Task<IReadOnlyList<WidgetDataDto>> EvaluateAsync(
        IReadOnlyList<WidgetConfig> widgets, WidgetContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var (from, to) = Range(context);
        var metrics = widgets.SelectMany(w => (w.Metrics ?? []).Append(w.Metric)).OfType<string>().ToList();
        if (widgets.Any(w => w.Type == WidgetTypes.Scorecard))
        {
            metrics.AddRange(await scorecards.MetricsAsync(ct).ConfigureAwait(false));
        }

        await evaluator.PreloadAsync(user, metrics, from, to, ct).ConfigureAwait(false);
        var result = new List<WidgetDataDto>();
        foreach (var widget in widgets)
        {
            result.Add(await EvaluateAsync(widget, context, user, from, to, ct).ConfigureAwait(false));
        }

        return result;
    }

    private async Task<WidgetDataDto> EvaluateAsync(WidgetConfig w, WidgetContext context, ClaimsPrincipal user, DateOnly from, DateOnly to, CancellationToken ct)
    {
        switch (w.Type)
        {
            case WidgetTypes.List:
                return await ListAsync(w, context, user, ct).ConfigureAwait(false);
            case WidgetTypes.Scorecard:
                var card = await scorecards.BuildAsync(context, user, ct).ConfigureAwait(false);
                return Empty(w, card.Rows.Count == 0 && card.Columns.Count == 0 ? "unavailable" : "ok") with { Scorecard = card };
            case WidgetTypes.Brief:
                return Empty(w, "ok");
            case WidgetTypes.Bubble:
                return await BubbleAsync(w, context, user, from, to, ct).ConfigureAwait(false);
        }

        if (w.Metric is null)
        {
            return Empty(w, "unavailable");
        }

        var facts = await evaluator.FactsAsync(user, w.Metric, from, to, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return Empty(w, "unavailable") with { Metric = w.Metric };
        }

        var def = facts.Definition;
        var filtered = Filter(facts.Facts, w.Filters, context.Units);
        var current = Select(def, filtered, context.Period, facts.CurrentMonth);
        var previousPeriod = context.Period.Previous();
        var previousFacts = Select(def, filtered, previousPeriod, facts.CurrentMonth);
        var total = Aggregate.Of(def, current);
        var previousTotal = context.Compare && previousFacts.Count > 0 ? Aggregate.Of(def, previousFacts).Value : null;
        var groupBy = new[] { w.Dimension, w.Series }.OfType<string>().ToList();
        IReadOnlyList<BreakdownRowDto>? rows = null;
        IReadOnlyList<DimensionLabelDto>? labels = null;
        if (groupBy.Count > 0)
        {
            rows = Breakdown(def, current, previousFacts, groupBy, context, w);
            labels = await LabelsAsync(def, groupBy, rows, ct).ConfigureAwait(false);
        }

        var trend = w.Type is WidgetTypes.Tile or WidgetTypes.Line ? Trend(def, filtered, context.Period.To, facts.CurrentMonth) : null;
        var rag = w.Thresholds?.Rag(total.Value);
        var link = Drill(def.DrillLink, context, null);
        var state = current.Count == 0 && def.Aggregation == MetricAggregation.Ratio ? "empty" : "ok";
        return new WidgetDataDto(
            w.Id, w.Type, state, def.Code, def.LabelAr, def.LabelEn, def.ValueKind.ToString().ToLowerInvariant(), def.HigherIsBetter, total.Value, previousTotal, rag,
            trend, rows, labels, null, null, null, link, facts.AsOf, facts.Live);
    }

    private static WidgetDataDto Empty(WidgetConfig w, string state) =>
        new(w.Id, w.Type, state, w.Metric, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false);

    /// <summary>Stock metrics read the period's last month (the current month when the period reaches it); flow metrics sum the months.</summary>
    public static IReadOnlyList<ViewFact> Select(MetricDefinition def, IEnumerable<ViewFact> facts, ReportPeriod period, DateOnly currentMonth)
    {
        if (def.Kind == MetricKind.Flow)
        {
            return facts.Where(f => f.Month >= period.From && f.Month <= period.To).ToList();
        }

        var month = period.To >= currentMonth ? currentMonth : period.To;
        return month < period.From ? [] : facts.Where(f => f.Month == month).ToList();
    }

    public static IReadOnlyList<ViewFact> Filter(IEnumerable<ViewFact> facts, IReadOnlyDictionary<string, List<string>>? filters, IReadOnlySet<Guid>? units)
    {
        var result = facts;
        if (units is not null)
        {
            result = result.Where(f => f.UnitId is { } u && units.Contains(u));
        }

        foreach (var (dim, values) in filters ?? new Dictionary<string, List<string>>())
        {
            if (values.Count == 0)
            {
                continue;
            }

            result = dim == "unit"
                ? result.Where(f => f.UnitId is { } u && values.Contains(u.ToString("D")))
                : result.Where(f => f.Dims.TryGetValue(dim, out var v) && values.Contains(v, StringComparer.Ordinal));
        }

        return result.ToList();
    }

    private static string KeyOf(ViewFact f, string dim) =>
        dim == "unit" ? f.UnitId?.ToString("D") ?? string.Empty : f.Dims.TryGetValue(dim, out var v) ? v : string.Empty;

    private static List<BreakdownRowDto> Breakdown(
        MetricDefinition def, IReadOnlyList<ViewFact> current, IReadOnlyList<ViewFact> previous, List<string> groupBy, WidgetContext context, WidgetConfig w)
    {
        string Key(ViewFact f) => string.Join('\u001f', groupBy.Select(d => KeyOf(f, d)));
        var previousByKey = previous.GroupBy(Key).ToDictionary(g => g.Key, g => Aggregate.Of(def, g).Value, StringComparer.Ordinal);
        var rows = current.GroupBy(Key)
            .Select(g =>
            {
                var first = g.First();
                var key = groupBy.ToDictionary(d => d, d => KeyOf(first, d), StringComparer.Ordinal);
                var value = Aggregate.Of(def, g).Value;
                return new BreakdownRowDto(key, value, context.Compare ? previousByKey.GetValueOrDefault(g.Key) : null, Drill(def.DrillLink, context, key));
            })
            .OrderByDescending(r => r.Value ?? decimal.MinValue)
            .ToList();
        if (w.Top is { } top && groupBy.Count == 1 && rows.Count > top)
        {
            rows = rows.Take(top).ToList();
        }

        return rows;
    }

    private static List<TrendPointDto> Trend(MetricDefinition def, IReadOnlyList<ViewFact> facts, DateOnly to, DateOnly currentMonth)
    {
        var points = new List<TrendPointDto>();
        for (var m = to.AddMonths(-11); m <= to; m = m.AddMonths(1))
        {
            var month = m;
            var inMonth = facts.Where(f => f.Month == month).ToList();
            points.Add(new TrendPointDto(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), inMonth.Count == 0 && month > currentMonth ? null : inMonth.Count == 0 && def.Kind == MetricKind.Stock ? null : Aggregate.Of(def, inMonth).Value));
        }

        return points;
    }

    /// <summary>Replaces <c>{unit}</c>, <c>{from}</c>, <c>{to}</c> and <c>{dim:code}</c> in a module drill link; drops unresolved placeholders.</summary>
    public static string? Drill(string? template, WidgetContext context, IReadOnlyDictionary<string, string>? key)
    {
        if (template is null)
        {
            return null;
        }

        var link = template.Replace("{unit}", context.UnitId?.ToString("D") ?? string.Empty, StringComparison.Ordinal)
            .Replace("{from}", context.Period.From.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{to}", context.Period.To.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        foreach (var (dim, value) in key ?? new Dictionary<string, string>())
        {
            link = dim == "unit"
                ? link.Replace("{unit}", value, StringComparison.Ordinal)
                : link.Replace("{dim:" + dim + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        // Unresolved {dim:x}: remove the whole query parameter.
        var parts = link.Split('?', 2);
        if (parts.Length == 2)
        {
            var query = parts[1].Split('&').Where(p => !p.Contains('{', StringComparison.Ordinal) && !p.EndsWith('=')).ToList();
            link = query.Count == 0 ? parts[0] : parts[0] + "?" + string.Join('&', query);
        }

        return link;
    }

    private async Task<IReadOnlyList<DimensionLabelDto>> LabelsAsync(MetricDefinition def, IReadOnlyList<string> dims, IReadOnlyList<BreakdownRowDto> rows, CancellationToken ct)
    {
        var labels = new List<DimensionLabelDto>();
        foreach (var dim in dims.Where(d => d != "unit"))
        {
            var codes = rows.Select(r => r.Key[dim]).Distinct(StringComparer.Ordinal).ToList();
            if (def.DimensionLabels?.TryGetValue(dim, out var fixedLabels) == true)
            {
                labels.AddRange(codes.Select(c => fixedLabels.TryGetValue(c, out var l) ? new DimensionLabelDto(dim, c, l.Ar, l.En) : new DimensionLabelDto(dim, c, null, null)));
            }
            else if (def.DimensionLookups?.TryGetValue(dim, out var type) == true)
            {
                if (!_lookups.TryGetValue(type, out var values))
                {
                    values = await lookups.GetValuesAsync(type, includeInactive: true, ct).ConfigureAwait(false);
                    _lookups[type] = values;
                }

                labels.AddRange(codes.Select(c => values.FirstOrDefault(v => v.Code == c) is { } v
                    ? new DimensionLabelDto(dim, c, v.LabelAr, v.LabelEn) : new DimensionLabelDto(dim, c, null, null)));
            }
        }

        return labels;
    }

    private async Task<WidgetDataDto> ListAsync(WidgetConfig w, WidgetContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        if (w.List is null || !registry.TryList(w.List, out var definition, out var source)
            || !await access.CanAsync(user, definition.Permission, cancellationToken: ct).ConfigureAwait(false))
        {
            return Empty(w, "unavailable");
        }

        var today = await evaluator.TodayAsync(ct).ConfigureAwait(false);
        var items = await source.GetAsync(w.List, new ReportingListQuery(context.Period.From, context.Period.LastDay, today, context.Units, w.Top ?? 10), user, ct)
            .ConfigureAwait(false);
        var dto = items.Take(w.Top ?? 10)
            .Select(i => new WidgetListItemDto(i.ResourceType, i.Id, i.Number, i.TitleAr, i.TitleEn, i.StatusCode, i.Tone, i.Value, i.Date, i.Link, i.DetailAr, i.DetailEn))
            .ToList();
        return Empty(w, dto.Count == 0 ? "empty" : "ok") with { Items = dto, MetricLabelAr = definition.LabelAr, MetricLabelEn = definition.LabelEn };
    }

    private async Task<WidgetDataDto> BubbleAsync(WidgetConfig w, WidgetContext context, ClaimsPrincipal user, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var metrics = w.Metrics ?? [];
        if (metrics.Count != 3 || w.Dimension is null)
        {
            return Empty(w, "unavailable");
        }

        var values = new List<Dictionary<string, decimal?>>();
        foreach (var code in metrics)
        {
            var facts = await evaluator.FactsAsync(user, code, from, to, ct).ConfigureAwait(false);
            if (facts is null)
            {
                return Empty(w, "unavailable");
            }

            var selected = Select(facts.Definition, Filter(facts.Facts, w.Filters, context.Units), context.Period, facts.CurrentMonth);
            values.Add(selected.GroupBy(f => KeyOf(f, w.Dimension)).ToDictionary(g => g.Key, g => Aggregate.Of(facts.Definition, g).Value, StringComparer.Ordinal));
        }

        var keys = values.SelectMany(v => v.Keys).Distinct(StringComparer.Ordinal).ToList();
        var bubbles = keys.Select(k => new BubblePointDto(k, values[0].GetValueOrDefault(k), values[1].GetValueOrDefault(k), values[2].GetValueOrDefault(k))).ToList();
        registry.TryMetric(metrics[0], out var first, out _);
        var rows = keys.Select(k => new BreakdownRowDto(new Dictionary<string, string> { [w.Dimension] = k }, null, null, null)).ToList();
        var labels = await LabelsAsync(first, [w.Dimension], rows, ct).ConfigureAwait(false);
        return Empty(w, bubbles.Count == 0 ? "empty" : "ok") with { Bubbles = bubbles, Labels = labels };
    }
}

/// <summary>The department comparison scorecard (ADR 0146): one row per department the viewer can see, one cell per Settings column.</summary>
public sealed class ScorecardBuilder(MetricRegistry registry, MetricEvaluator evaluator, DepartmentMap departments, ISettingsStore settings)
{
    public async Task<IReadOnlyList<string>> MetricsAsync(CancellationToken ct) =>
        (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).Scorecard.Select(c => c.Metric).Distinct(StringComparer.Ordinal).ToList();

    public async Task<ScorecardDto> BuildAsync(WidgetContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var config = await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false);
        var (from, to) = WidgetEvaluator.Range(context);
        var columns = new List<(ScorecardColumn Column, MetricDefinition Definition, IReadOnlyList<ViewFact> Facts)>();
        foreach (var column in config.Scorecard)
        {
            if (!registry.TryMetric(column.Metric, out _, out _))
            {
                continue;
            }

            var facts = await evaluator.FactsAsync(user, column.Metric, from, to, ct).ConfigureAwait(false);
            if (facts is null)
            {
                continue;
            }

            var filtered = WidgetEvaluator.Filter(facts.Facts, column.Filters, context.Units);
            columns.Add((column, facts.Definition, WidgetEvaluator.Select(facts.Definition, filtered, context.Period, facts.CurrentMonth)));
        }

        var rows = new List<ScorecardRowDto>();
        var period = $"from={context.Period.From:yyyy-MM}&to={context.Period.To:yyyy-MM}";
        foreach (var department in await departments.DepartmentsAsync(ct).ConfigureAwait(false))
        {
            if (context.Units is not null && !department.Units.Overlaps(context.Units))
            {
                continue;
            }

            var cells = new List<ScorecardCellDto>();
            var any = false;
            foreach (var (column, definition, facts) in columns)
            {
                var mine = facts.Where(f => f.UnitId is { } u && department.Units.Contains(u)).ToList();
                any |= mine.Count > 0;
                var aggregate = Aggregate.Of(definition, mine);
                var value = definition.Aggregation == MetricAggregation.Sum && mine.Count == 0 ? 0m : aggregate.Value;
                var thresholds = new Thresholds(column.Green, column.Amber, column.HigherIsBetter);
                var link = column.Dashboard is null ? null : $"/dashboards/{column.Dashboard}?unit={department.Id:D}&{period}";
                cells.Add(new ScorecardCellDto(column.Code, value, mine.Count == 0 && definition.Aggregation == MetricAggregation.Ratio ? "none" : thresholds.Rag(value), link));
            }

            if (any)
            {
                rows.Add(new ScorecardRowDto(department.Id, department.Code, department.NameAr, department.NameEn, cells,
                    $"/dashboards/executive?unit={department.Id:D}&{period}"));
            }
        }

        var columnDtos = columns.Select(c => new ScorecardColumnDto(
            c.Column.Code, c.Column.LabelAr, c.Column.LabelEn, c.Column.Metric, c.Definition.ValueKind.ToString().ToLowerInvariant(), c.Column.HigherIsBetter,
            c.Column.Green, c.Column.Amber)).ToList();
        return new ScorecardDto(columnDtos, rows.OrderBy(r => r.Code, StringComparer.Ordinal).ToList());
    }
}
