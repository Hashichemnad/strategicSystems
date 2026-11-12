using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Datasets;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.ReportTypes;

public sealed record ReportTypeColumnDto(string Code, string Alias, string LabelAr, string LabelEn, string ObjectLabelAr, string ObjectLabelEn, string Type);

public sealed record ReportTypeRowDto(IReadOnlyList<string?> Cells, IReadOnlyList<string?> Tones, IReadOnlyDictionary<string, string> Links, IReadOnlyList<string> GroupKeys);

public sealed record ReportAggregateDto(string Column, string Function, string? Value, decimal? Raw);

public sealed record ReportGroupDto(int Level, IReadOnlyList<string> Keys, string? Label, int Count, IReadOnlyList<ReportAggregateDto> Aggregates);

public sealed record ReportChartSeriesDto(string? Key, string? Label, IReadOnlyList<decimal> Values);

public sealed record ReportChartDto(string Type, IReadOnlyList<string?> Categories, IReadOnlyList<ReportChartSeriesDto> Series, string MeasureLabelAr, string MeasureLabelEn);

public sealed record ReportTypeRunDto(
    string ReportType,
    string LabelAr,
    string LabelEn,
    IReadOnlyList<ReportTypeColumnDto> Columns,
    IReadOnlyList<ReportTypeRowDto> Rows,
    int Total,
    int Page,
    int PageSize,
    bool Truncated,
    IReadOnlyList<string> DroppedColumns,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<ReportGroupDto> Groups,
    ReportGroupDto GrandTotal,
    ReportChartDto? Chart);

/// <summary>One object of a type as resolved for a runner: its dataset, relationship, readability and the fields offered to them.</summary>
public sealed record ResolvedNode(
    ReportTypeNode Node, DatasetDefinition Dataset, DatasetRelationship? Relationship, bool Readable, IReadOnlyDictionary<string, DatasetField> Offered,
    string? ExternalResourceType)
{
    public string Alias => Node.Alias;
}

public sealed record ResolvedType(string Code, string SystemCode, string LabelAr, string LabelEn, bool Implicit, IReadOnlyList<ResolvedNode> Nodes)
{
    public ResolvedNode Node(string alias) => Nodes.First(n => n.Alias == alias);

    /// <summary>The field behind a qualified column, if offered to the runner.</summary>
    public DatasetField? Offered(string column) =>
        ReportTypeRules.Split(column) is { } p && Nodes.FirstOrDefault(n => n.Alias == p.Alias) is { } node && node.Offered.TryGetValue(p.Field, out var f) ? f : null;

    /// <summary>Declared by the type (offered to someone), whether or not the runner may see it.</summary>
    public bool Declared(string column) =>
        ReportTypeRules.Split(column) is { } p && Nodes.FirstOrDefault(n => n.Alias == p.Alias) is { } node
        && ((node.Node.Fields is null && node.Dataset.Fields.Any(f => f.Code == p.Field && f.Type != DatasetFieldType.Reference))
            || (node.Node.Fields?.Contains(p.Field) ?? false)
            || ExternalReferenceColumns.IsVirtual(p.Field));
}

/// <summary>
/// Runs reports on report types (ADRs 0181, 0182): resolves the type for the runner, validates the definition against what they may see,
/// queries every object through its module's scoped query, joins in memory, then sorts, groups, summarises, charts and highlights.
/// </summary>
public sealed class ReportTypeEngine(
    MetricRegistry registry, IAccessService access, ReportingDbContext db, ISettingsStore settings, ReportingClock clock, ReportValueFormatter formatter,
    ExternalReferenceColumns externalColumns)
{
    public const int MaxPageSize = 200;
    private const int KeyChunk = 500;

    private static FieldError Err(string field, string key) => new(field, key, key);

    public static string TypeName(DatasetFieldType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    /// <summary>The stored or implicit type, without access checks (null = unknown or inactive).</summary>
    public async Task<(ReportTypeDefinition Definition, string Code, string SystemCode, string LabelAr, string LabelEn, bool Implicit)?> FindAsync(string code, CancellationToken ct)
    {
        if (code.StartsWith(ReportTypeRules.ImplicitPrefix, StringComparison.Ordinal))
        {
            var dataset = registry.Dataset(code[ReportTypeRules.ImplicitPrefix.Length..]);
            return dataset is null || dataset.JoinOnly
                ? null
                : (ReportTypeRules.Implicit(dataset.Code), code, dataset.SystemCode, dataset.LabelAr, dataset.LabelEn, true);
        }

        var type = await db.ReportTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.Status == DefinitionStatuses.Active, ct).ConfigureAwait(false);
        if (type is null)
        {
            return null;
        }

        var definition = JsonSerializer.Deserialize<ReportTypeDefinition>(type.DefinitionJson, DashboardService.Json) ?? new ReportTypeDefinition();
        return (definition, type.Code, type.SystemCode, type.LabelAr, type.LabelEn, false);
    }

    /// <summary>Resolves a type for the runner. Unknown, invalid or primary object not readable → null (404).</summary>
    public async Task<ResolvedType?> ResolveAsync(string code, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || await FindAsync(code, ct).ConfigureAwait(false) is not { } found
            || ReportTypeRules.Validate(found.Definition, registry).Count > 0)
        {
            return null;
        }

        return await ResolveDefinitionAsync(found.Definition, found.Code, found.SystemCode, found.LabelAr, found.LabelEn, found.Implicit, user, ct).ConfigureAwait(false);
    }

    /// <summary>Resolves an already validated definition (stored type, or a dossier section's path) for the runner.</summary>
    public async Task<ResolvedType?> ResolveDefinitionAsync(
        ReportTypeDefinition definition, string code, string systemCode, string labelAr, string labelEn, bool isImplicit, ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var nodes = new List<ResolvedNode>();
        foreach (var node in definition.Nodes)
        {
            var dataset = registry.Dataset(node.Dataset)!;
            var relationship = node.Relationship is null ? null : registry.Relationship(node.Relationship);
            var parentReadable = node.Parent is null || nodes.First(n => n.Alias == node.Parent).Readable;
            var readable = parentReadable && (dataset.JoinOnly || await access.CanAsync(user, dataset.ReadPermission, cancellationToken: ct).ConfigureAwait(false));
            if (node.Parent is null && !readable)
            {
                return null;
            }

            var offered = new Dictionary<string, DatasetField>(StringComparer.Ordinal);
            string? externalType = null;
            if (readable)
            {
                foreach (var field in dataset.Fields.Where(f => f.Type != DatasetFieldType.Reference && (node.Fields is null || node.Fields.Contains(f.Code))))
                {
                    if (field.Permission is null || await access.CanAsync(user, field.Permission, cancellationToken: ct).ConfigureAwait(false))
                    {
                        offered[field.Code] = field;
                    }
                }

                externalType = dataset.JoinOnly ? null : await externalColumns.ResourceTypeOfAsync(dataset, ct).ConfigureAwait(false);
                if (externalType is not null)
                {
                    foreach (var field in ExternalReferenceColumns.Fields)
                    {
                        offered[field.Code] = field;
                    }
                }
            }

            nodes.Add(new ResolvedNode(node, dataset, relationship, readable, offered, externalType));
        }

        return new ResolvedType(code, systemCode, labelAr, labelEn, isImplicit, nodes);
    }

    private static bool OperatorAllowed(DatasetFieldType type, string op) => op switch
    {
        ReportOperators.Equal or ReportOperators.NotEqual or ReportOperators.IsEmpty or ReportOperators.IsNotEmpty => true,
        ReportOperators.In => type is DatasetFieldType.Lookup or DatasetFieldType.Text or DatasetFieldType.Unit or DatasetFieldType.Employee or DatasetFieldType.Number,
        ReportOperators.Contains => type == DatasetFieldType.Text,
        ReportOperators.GreaterOrEqual or ReportOperators.LessOrEqual or ReportOperators.Between =>
            type is DatasetFieldType.Number or DatasetFieldType.Money or DatasetFieldType.Ratio or DatasetFieldType.Date or DatasetFieldType.DateTime,
        RelativeDates.Operator => type is DatasetFieldType.Date or DatasetFieldType.DateTime,
        _ => false,
    };

    private static bool IsNumeric(DatasetFieldType type) => type is DatasetFieldType.Number or DatasetFieldType.Money or DatasetFieldType.Ratio;

    /// <summary>Validated for the runner: columns they may not see are dropped; anything else referring to them is refused (ADR 0144 rule).</summary>
    public async Task<Result<(ResolvedType Type, ReportDefinition Effective, List<string> Dropped)>> PrepareAsync(ReportDefinition definition, ClaimsPrincipal user, CancellationToken ct)
    {
        if (definition?.ReportType is null || definition.Columns is null || definition.Filters is null || definition.GroupBy is null || definition.Sort is null
            || definition.Summaries is null || definition.Highlights is null)
        {
            return Error.Validation([Err("reportType", "reporting.errors.columns")]);
        }

        if (await ResolveAsync(definition.ReportType, user, ct).ConfigureAwait(false) is not { } type)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        string Problem(string column) => type.Declared(column) ? "reporting.errors.fieldNotAllowed" : "reporting.errors.fieldUnknown";
        if (definition.Columns.Count is 0 or > 60)
        {
            errors.Add(Err("columns", "reporting.errors.columns"));
        }

        if (definition.Columns.Any(c => !type.Declared(c)))
        {
            errors.Add(Err("columns", "reporting.errors.fieldUnknown"));
        }

        if (definition.Filters.Count > 30)
        {
            errors.Add(Err("filters", "reporting.errors.operator"));
        }

        foreach (var filter in definition.Filters)
        {
            if (type.Offered(filter.Field) is not { Filterable: true } field || ExternalReferenceColumns.IsVirtual(ReportTypeRules.Split(filter.Field)!.Value.Field))
            {
                errors.Add(Err("filters", Problem(filter.Field)));
            }
            else if (filter.Values is null || !OperatorAllowed(field.Type, filter.Operator) || filter.Values.Count > 100 || filter.Values.Any(v => v is null || v.Length > 200)
                || (filter.Operator == RelativeDates.Operator && !RelativeDates.IsValid(filter.Values)))
            {
                errors.Add(Err("filters", "reporting.errors.operator"));
            }
        }

        if (definition.GroupBy.Count > 3 || definition.GroupBy.Distinct(StringComparer.Ordinal).Count() != definition.GroupBy.Count)
        {
            errors.Add(Err("groupBy", "reporting.errors.groupBy"));
        }

        foreach (var g in definition.GroupBy.Where(g => type.Offered(g) is not { Groupable: true }))
        {
            errors.Add(Err("groupBy", type.Offered(g) is null ? Problem(g) : "reporting.errors.groupBy"));
        }

        if (definition.Sort.Count > 3)
        {
            errors.Add(Err("sort", "reporting.errors.sort"));
        }

        foreach (var s in definition.Sort.Where(s => type.Offered(s.Field) is not { Sortable: true }))
        {
            errors.Add(Err("sort", type.Offered(s.Field) is null ? Problem(s.Field) : "reporting.errors.sort"));
        }

        if (definition.Summaries.Count > 20)
        {
            errors.Add(Err("summaries", "reporting.errors.summary"));
        }

        foreach (var summary in definition.Summaries)
        {
            if (type.Offered(summary.Column) is not { } field)
            {
                errors.Add(Err("summaries", Problem(summary.Column)));
            }
            else if (summary.Functions is null || summary.Functions.Count == 0 || summary.Functions.Any(f => !SummaryFunctions.All.Contains(f, StringComparer.Ordinal)
                || (f == SummaryFunctions.Sum && field.Type is not (DatasetFieldType.Number or DatasetFieldType.Money))
                || (f == SummaryFunctions.Average && !IsNumeric(field.Type))
                || (f is SummaryFunctions.Min or SummaryFunctions.Max && !IsNumeric(field.Type) && field.Type is not (DatasetFieldType.Date or DatasetFieldType.DateTime))))
            {
                errors.Add(Err("summaries", "reporting.errors.summary"));
            }
        }

        if (definition.Chart is { } chart)
        {
            if (!ChartTypes.All.Contains(chart.Type, StringComparer.Ordinal) || !SummaryFunctions.All.Contains(chart.Aggregate, StringComparer.Ordinal))
            {
                errors.Add(Err("chart", "reporting.errors.chart"));
            }

            foreach (var column in new[] { chart.Category, chart.Series }.Where(c => c is not null).Cast<string>())
            {
                if (type.Offered(column) is not { Groupable: true })
                {
                    errors.Add(Err("chart", type.Offered(column) is null ? Problem(column) : "reporting.errors.chart"));
                }
            }

            if (chart.Measure is { } measure && type.Offered(measure) is not { } m)
            {
                errors.Add(Err("chart", Problem(measure)));
            }
            else if (chart.Measure is { } measure2 && !IsNumeric(type.Offered(measure2)!.Type))
            {
                errors.Add(Err("chart", "reporting.errors.chart"));
            }
        }

        if (definition.Highlights.Count > 20)
        {
            errors.Add(Err("highlights", "reporting.errors.highlight"));
        }

        foreach (var h in definition.Highlights)
        {
            if (type.Offered(h.Column) is null)
            {
                errors.Add(Err("highlights", Problem(h.Column)));
            }
            else if (!ReportOperators.All.Contains(h.Operator, StringComparer.Ordinal) || !HighlightTones.All.Contains(h.Tone, StringComparer.Ordinal)
                || h.Values is null || h.Values.Count > 20)
            {
                errors.Add(Err("highlights", "reporting.errors.highlight"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors.DistinctBy(e => (e.Field, e.Code)).ToList());
        }

        var dropped = definition.Columns.Where(c => type.Offered(c) is null).ToList();
        var effective = definition with { Columns = definition.Columns.Where(c => type.Offered(c) is not null).Distinct(StringComparer.Ordinal).ToList() };
        if (effective.Columns.Count == 0)
        {
            return Error.Validation([Err("columns", "reporting.errors.fieldNotAllowed")]);
        }

        return (type, effective, dropped);
    }

    /// <summary>The composed rows of a prepared run (module queries + join), with the truncation flag.</summary>
    public async Task<(List<FlatRow> Rows, bool Truncated)> ComposeAsync(ResolvedType type, ReportDefinition effective, ClaimsPrincipal user, CancellationToken ct)
    {
        var config = await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false);
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var zone = ScheduleClock.Zone(regional.TimeZone);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.ReportTimeoutSeconds));
        var token = timeout.Token;

        // Every field a node needs: shown columns, filters are pushed down, group / sort / summary / chart / highlight fields, join keys.
        var used = effective.Columns.Concat(effective.GroupBy).Concat(effective.Sort.Select(s => s.Field)).Concat(effective.Summaries.Select(s => s.Column))
            .Concat(effective.Highlights.Select(h => h.Column))
            .Concat(effective.Chart is { } c ? new[] { c.Category, c.Series, c.Measure }.Where(x => x is not null).Cast<string>() : [])
            .Select(ReportTypeRules.Split).Where(p => p is not null).Select(p => p!.Value).ToList();
        var composer = new List<ComposerNode>();
        var rowsByAlias = new Dictionary<string, IReadOnlyList<NodeRow>>(StringComparer.Ordinal);
        var truncated = false;
        foreach (var node in type.Nodes)
        {
            var children = type.Nodes.Where(n => n.Node.Parent == node.Alias && n.Relationship is not null).ToList();
            var fields = used.Where(p => p.Alias == node.Alias).Select(p => p.Field).ToList();
            var virtuals = fields.Where(ExternalReferenceColumns.IsVirtual).Distinct(StringComparer.Ordinal).ToList();
            var moduleFields = fields.Where(f => !ExternalReferenceColumns.IsVirtual(f))
                .Concat(children.Select(ch => ch.Relationship!.ParentField))
                .Concat(node.Relationship is { } r ? [r.ChildField] : [])
                .Distinct(StringComparer.Ordinal).ToList();
            if (moduleFields.Count == 0)
            {
                moduleFields.Add(node.Dataset.KeyField ?? node.Dataset.Fields[0].Code);
            }

            var filters = effective.Filters.Where(f => ReportTypeRules.Split(f.Field)!.Value.Alias == node.Alias)
                .Select(f =>
                {
                    var field = ReportTypeRules.Split(f.Field)!.Value.Field;
                    if (f.Operator != RelativeDates.Operator)
                    {
                        return new ReportFilter(field, f.Operator, f.Values);
                    }

                    var (from, to) = RelativeDates.Resolve(f.Values, today, config.FiscalStartMonth);
                    return new ReportFilter(field, ReportOperators.Between, RelativeDates.BetweenValues(node.Offered[field].Type, from, to, zone));
                }).ToList();

            IReadOnlyList<NodeRow> rows;
            if (!node.Readable)
            {
                rows = [];
            }
            else if (node.Node.Parent is null)
            {
                var result = await Fetch(node, moduleFields, virtuals, filters, config.ReportPrimaryRowLimit + 1).ConfigureAwait(false);
                if (result.Count > config.ReportPrimaryRowLimit)
                {
                    truncated = true;
                    result = result.Take(config.ReportPrimaryRowLimit).ToList();
                }

                rows = result;
            }
            else
            {
                var keys = rowsByAlias[node.Node.Parent].Select(r => r[node.Relationship!.ParentField]).Where(k => k is not null)
                    .Select(ReportComposer.Key).Distinct(StringComparer.Ordinal).ToList();
                var collected = new List<NodeRow>();
                foreach (var chunk in keys.Chunk(KeyChunk))
                {
                    var keyed = filters.Append(new ReportFilter(node.Relationship!.ChildField, ReportOperators.In, chunk)).ToList();
                    collected.AddRange(await Fetch(node, moduleFields, virtuals, keyed, config.ExportRowLimit).ConfigureAwait(false));
                    if (collected.Count >= config.ExportRowLimit)
                    {
                        truncated = true;
                        break;
                    }
                }

                rows = collected;
            }

            rowsByAlias[node.Alias] = rows;
            composer.Add(new ComposerNode(node.Alias, node.Node.Parent, node.Node.Join, node.Relationship?.ParentField, node.Relationship?.ChildField, rows));
        }

        var composed = ReportComposer.Compose(composer, config.ExportRowLimit, out var cut);
        return (composed, truncated || cut);

        async Task<List<NodeRow>> Fetch(ResolvedNode node, List<string> moduleFields, List<string> virtuals, List<ReportFilter> filters, int take)
        {
            var result = await registry.RunAsync(new ReportQuery(node.Dataset.Code, moduleFields, filters, [], [], 0, take), user, token).ConfigureAwait(false);
            if (virtuals.Count > 0 && node.ExternalResourceType is { } resourceType)
            {
                result = await externalColumns.FillAsync(resourceType, moduleFields.Concat(virtuals).ToList(), result, token).ConfigureAwait(false);
            }

            return result.Rows.Select((row, i) => new NodeRow(
                result.Columns.Select((c, ci) => (c, ci)).ToDictionary(p => p.c, p => row[p.ci], StringComparer.Ordinal),
                result.RowIds is { } ids && i < ids.Count ? ids[i] : null)).ToList();
        }
    }

    public async Task<Result<ReportTypeRunDto>> RunAsync(ReportDefinition definition, int page, int pageSize, string language, ClaimsPrincipal user, CancellationToken ct, bool all = false)
    {
        var prepared = await PrepareAsync(definition, user, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var (type, effective, dropped) = prepared.Value;
        List<FlatRow> rows;
        bool truncated;
        try
        {
            (rows, truncated) = await ComposeAsync(type, effective, user, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return Error.Validation([Err("filters", "reporting.errors.value")]);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error.Validation([Err("reportType", "reporting.errors.timeout")]);
        }

        return await ShapeAsync(type, effective, dropped, rows, truncated, page, pageSize, language, all, ct).ConfigureAwait(false);
    }

    /// <summary>Sort, group, summarise, chart, highlight, page and format composed rows (pure apart from label look-ups).</summary>
    public async Task<ReportTypeRunDto> ShapeAsync(
        ResolvedType type, ReportDefinition effective, IReadOnlyList<string> dropped, IReadOnlyList<FlatRow> rows, bool truncated, int page, int pageSize, string language, bool all,
        CancellationToken ct)
    {
        var sorted = ReportAggregation.Sort(rows, effective.GroupBy, effective.Sort);
        var groups = ReportAggregation.Groups(sorted, effective.GroupBy, effective.Summaries);
        var grand = new GroupNode(0, [], sorted.Count, ReportAggregation.Aggregate(sorted, effective.Summaries));
        var chart = effective.Chart is { } c ? ReportAggregation.Chart(sorted, c) : null;
        var size = all ? Math.Max(1, sorted.Count) : Math.Clamp(pageSize, 1, MaxPageSize);
        var p = all ? 1 : Math.Max(1, page);
        var shown = effective.GroupBy.Count > 0 && !effective.ShowDetails ? [] : sorted.Skip((p - 1) * size).Take(size).ToList();

        // Labels for the shown values, group keys and chart categories.
        var formattedColumns = effective.Columns.Concat(effective.GroupBy).Concat(effective.Chart is { } ch ? new[] { ch.Category, ch.Series }.Where(x => x is not null).Cast<string>() : [])
            .Distinct(StringComparer.Ordinal).ToList();
        var sample = shown.Concat(sorted.Take(effective.GroupBy.Count > 0 || chart is not null ? 50_000 : 0)).ToList();
        await formatter.PrepareAsync(formattedColumns.Select(col => (type.Offered(col)!, sample.Select(r => r.Value(col)))), ct).ConfigureAwait(false);
        string? Label(string column, object? value) => formatter.Format(type.Offered(column)!, value, language);

        var columns = effective.Columns.Select(col =>
        {
            var (alias, _) = ReportTypeRules.Split(col)!.Value;
            var node = type.Node(alias);
            var field = type.Offered(col)!;
            return new ReportTypeColumnDto(col, alias, field.LabelAr, field.LabelEn, node.Dataset.LabelAr, node.Dataset.LabelEn, TypeName(field.Type));
        }).ToList();
        var outRows = shown.Select(r => new ReportTypeRowDto(
            effective.Columns.Select(col => Label(col, r.Value(col))).ToList(),
            effective.Columns.Select(col => ReportAggregation.Tone(r.Value(col), col, effective.Highlights)).ToList(),
            r.Parts.Where(part => part.Value.RowId is not null && type.Node(part.Key).Dataset.Link.Length > 0)
                .ToDictionary(part => part.Key, part => type.Node(part.Key).Dataset.Link.Replace("{id}", part.Value.RowId!.Value.ToString("D"), StringComparison.Ordinal), StringComparer.Ordinal),
            effective.GroupBy.Select(g => ReportComposer.Key(r.Value(g))).ToList())).ToList();

        ReportAggregateDto Agg(AggregateValue a)
        {
            var field = type.Offered(a.Column)!;
            var formatType = a.Function == SummaryFunctions.Average && field.Type == DatasetFieldType.Number ? DatasetFieldType.Number : field.Type;
            return new ReportAggregateDto(a.Column, a.Function, ReportValueFormatter.FormatPlain(formatType, a.Value, language == "ar"), ReportAggregation.ToDecimal(a.Value));
        }

        var groupDtos = groups.Select(g => new ReportGroupDto(g.Level, g.Keys.Select(ReportComposer.Key).ToList(), Label(effective.GroupBy[g.Level - 1], g.Keys[^1]), g.Count,
            g.Aggregates.Select(Agg).ToList())).ToList();
        var grandDto = new ReportGroupDto(0, [], null, grand.Count, grand.Aggregates.Select(Agg).ToList());
        ReportChartDto? chartDto = null;
        if (chart is not null && effective.Chart is { } spec)
        {
            var measure = spec.Measure is { } m ? type.Offered(m) : null;
            chartDto = new ReportChartDto(chart.Type, chart.Categories.Select(v => Label(spec.Category, v)).ToList(),
                chart.Series.Select(s => new ReportChartSeriesDto(s.Key is null ? null : ReportComposer.Key(s.Key), spec.Series is null ? null : Label(spec.Series, s.Key), s.Values)).ToList(),
                measure is null ? "العدد" : measure.LabelAr, measure is null ? "Count" : measure.LabelEn);
        }

        return new ReportTypeRunDto(type.Code, type.LabelAr, type.LabelEn, columns, outRows, sorted.Count, p, size, truncated, dropped, effective.GroupBy, groupDtos, grandDto, chartDto);
    }
}
