using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Files;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.ReportTypes;
using Strategy.Platform.Workflow;

namespace Strategy.Platform.Reporting.Dossiers;

public sealed record DossierPair(string LabelAr, string LabelEn, string? ValueAr, string? ValueEn);

public sealed record DossierTable(IReadOnlyList<string> HeadersAr, IReadOnlyList<string> HeadersEn, IReadOnlyList<IReadOnlyList<string?>> RowsAr, IReadOnlyList<IReadOnlyList<string?>> RowsEn);

public sealed record DossierChart(string Type, IReadOnlyList<string?> CategoriesAr, IReadOnlyList<string?> CategoriesEn, IReadOnlyList<(string? NameAr, string? NameEn, IReadOnlyList<decimal> Values)> Series);

public sealed record DossierGanttItem(string? LabelAr, string? LabelEn, DateOnly? Start, DateOnly? End, DateOnly? ActualStart, DateOnly? ActualEnd);

public sealed record DossierMatrix(int Size, int[,] Counts);

/// <summary>A rendered section: the content matching its kind (null for the others).</summary>
public sealed record DossierBlock(
    string Key, string TitleAr, string TitleEn, string Kind, bool PageBreak,
    IReadOnlyList<DossierPair>? Pairs = null, (string? Ar, string? En)? Text = null, DossierTable? Table = null, DossierChart? Chart = null,
    IReadOnlyList<DossierGanttItem>? Gantt = null, DossierMatrix? Matrix = null);

/// <summary>One record's dossier as the runner may see it: cover data and the sections that survived permissions and conditions.</summary>
public sealed record DossierDocument(
    string TemplateCode, string TemplateLabelAr, string TemplateLabelEn, Guid RecordId, string? Number, string? NameAr, string? NameEn, string? SubtitleAr, string? SubtitleEn,
    DossierDefinition Definition, IReadOnlyList<DossierBlock> Blocks, IReadOnlyList<string> OmittedSections);

/// <summary>
/// Builds a record's dossier as the runner (ADR 0184): the record through its dataset's scoped query (not visible → null = 404); every bound
/// section through the report engine (module scoped queries per object); fields without permission left out; sections bound to something the
/// runner may not read, or whose condition is false, omitted.
/// </summary>
public sealed partial class DossierBuilder(
    ReportTypeEngine engine, MetricRegistry registry, ReportValueFormatter formatter, IApprovalEngine approvals, IFileService files,
    IEnumerable<IFileOwnerAccess> fileOwners, ILogger<DossierBuilder> logger)
{
    private const string Root = "a";
    private static readonly string[] Aliases = ["b", "c", "d"];

    /// <summary>The section's objects as a report-type definition: the record's dataset, then each relationship of the path (outer joins).</summary>
    public static ReportTypeDefinition PathDefinition(string dataset, IReadOnlyList<string> path, IReportableMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(metadata);
        var nodes = new List<ReportTypeNode> { new() { Alias = Root, Dataset = dataset } };
        var current = dataset;
        for (var i = 0; i < path.Count && i < Aliases.Length; i++)
        {
            var relationship = metadata.Relationships.FirstOrDefault(r => r.Code == path[i] && r.ParentDataset == current);
            if (relationship is null)
            {
                return new ReportTypeDefinition();
            }

            nodes.Add(new ReportTypeNode { Alias = Aliases[i], Parent = nodes[^1].Alias, Dataset = relationship.ChildDataset, Relationship = relationship.Code });
            current = relationship.ChildDataset;
        }

        return new ReportTypeDefinition { Nodes = nodes };
    }

    public async Task<DossierDocument?> BuildAsync(string templateCode, string labelAr, string labelEn, string dataset, DossierDefinition definition, Guid recordId,
        ClaimsPrincipal user, CancellationToken ct, int sectionRowLimit = 200)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var rootType = await engine.ResolveDefinitionAsync(PathDefinition(dataset, [], registry), templateCode, string.Empty, labelAr, labelEn, false, user, ct).ConfigureAwait(false);
        if (rootType is null)
        {
            return null;
        }

        var rootNode = rootType.Nodes[0];
        var idFilter = new ReportFilter(Root + "." + rootNode.Dataset.KeyField, ReportOperators.Equal, [recordId.ToString("D")]);
        if (rootNode.Dataset.KeyField is null)
        {
            return null;
        }

        // The record with every field the runner may see.
        var rootColumns = rootNode.Offered.Keys.Where(f => !Datasets.ExternalReferenceColumns.IsVirtual(f)).Select(f => Root + "." + f).ToList();
        var (recordRows, _) = await engine.ComposeAsync(rootType, new ReportDefinition { ReportType = templateCode, Columns = rootColumns, Filters = [idFilter] }, user, ct)
            .ConfigureAwait(false);
        if (recordRows.Count == 0)
        {
            return null;
        }

        var record = recordRows[0];
        var blocks = new List<DossierBlock>();
        var omitted = new List<string>();
        foreach (var section in definition.Sections)
        {
            try
            {
                var block = await SectionAsync(dataset, templateCode, section, rootType, record, recordId, idFilter, sectionRowLimit, user, ct).ConfigureAwait(false);
                if (block is null)
                {
                    omitted.Add(section.Key);
                }
                else
                {
                    blocks.Add(block);
                }
            }
            catch (ArgumentException ex)
            {
                LogSectionFailed(logger, ex, templateCode, section.Key);
                omitted.Add(section.Key);
            }
        }

        string? Value(string? column, string language) =>
            column is not null && rootType.Offered(column) is { } f ? formatter.Format(f, record.Value(column), language) : null;
        await PrepareAsync(rootType, [record], rootColumns, ct).ConfigureAwait(false);
        return new DossierDocument(templateCode, labelAr, labelEn, recordId, Value(definition.NumberField, "en"), Value(definition.NameFieldAr, "ar"), Value(definition.NameFieldEn, "en"),
            Value(definition.Cover.SubtitleField, "ar"), Value(definition.Cover.SubtitleField, "en"), definition, blocks, omitted);
    }

    private Task PrepareAsync(ResolvedType type, IReadOnlyList<FlatRow> rows, IEnumerable<string> columns, CancellationToken ct) =>
        formatter.PrepareAsync(columns.Where(c => type.Offered(c) is not null).Select(c => (type.Offered(c)!, rows.Select(r => r.Value(c)))), ct);

    private async Task<DossierBlock?> SectionAsync(
        string dataset, string templateCode, DossierSection section, ResolvedType rootType, FlatRow record, Guid recordId, ReportFilter idFilter, int sectionRowLimit,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var titleAr = section.TitleAr ?? section.TitleEn ?? section.Key;
        var titleEn = section.TitleEn ?? section.TitleAr ?? section.Key;
        if (section.Condition is { } condition)
        {
            if (rootType.Offered(condition.Field) is null || !ReportAggregation.Matches(record.Value(condition.Field), condition.Operator, condition.Values))
            {
                return null;
            }
        }

        switch (section.Kind)
        {
            case DossierSectionKinds.Text:
                return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Text: (section.TextAr, section.TextEn));

            case DossierSectionKinds.Fields:
                {
                    var fields = (section.Fields ?? []).Where(f => rootType.Offered(f) is not null).ToList();
                    if (fields.Count == 0)
                    {
                        return null;
                    }

                    await PrepareAsync(rootType, [record], fields, ct).ConfigureAwait(false);
                    var pairs = fields.Select(f =>
                    {
                        var field = rootType.Offered(f)!;
                        return new DossierPair(field.LabelAr, field.LabelEn, formatter.Format(field, record.Value(f), "ar"), formatter.Format(field, record.Value(f), "en"));
                    }).ToList();
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Pairs: pairs);
                }

            case DossierSectionKinds.Approvals:
                {
                    if (string.IsNullOrWhiteSpace(section.ResourceType))
                    {
                        return null;
                    }

                    // The record was read through its scoped dataset: its approval history is part of what the runner may open.
                    var attempts = await approvals.ListAttemptsAsync(section.ResourceType, recordId, ct).ConfigureAwait(false);
                    var decisions = attempts.OrderBy(a => a.Instance.SubmittedAt).SelectMany(a => a.Decisions.Select(d => (a, d))).ToList();
                    string Stamp(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    var table = new DossierTable(
                        ["المحاولة", "الخطوة", "القرار", "التعليق", "التاريخ"], ["Attempt", "Step", "Decision", "Comment", "Date"],
                        decisions.Select(p => (IReadOnlyList<string?>)[p.a.Instance.Attempt.ToString(CultureInfo.InvariantCulture), p.d.StepNameAr ?? p.d.StepNameEn, Outcome(p.d.Outcome, true),
                            p.d.Comment, Stamp(p.d.At)]).ToList(),
                        decisions.Select(p => (IReadOnlyList<string?>)[p.a.Instance.Attempt.ToString(CultureInfo.InvariantCulture), p.d.StepNameEn ?? p.d.StepNameAr, Outcome(p.d.Outcome, false),
                            p.d.Comment, Stamp(p.d.At)]).ToList());
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Table: table);
                }

            case DossierSectionKinds.Attachments:
                {
                    var owner = fileOwners.FirstOrDefault(o => o.OwnerType == section.OwnerType);
                    if (owner is null || !await owner.CanReadAsync(user, recordId.ToString("D"), ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var list = await files.ListAsync(owner.OwnerType, recordId.ToString("D"), null, ct).ConfigureAwait(false);
                    IReadOnlyList<string?> Row(FileDto f) =>
                        [f.Name, f.VersionNo.ToString(CultureInfo.InvariantCulture), (f.SizeBytes / 1024m).ToString("#,0", CultureInfo.InvariantCulture) + " KB",
                            f.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)];
                    var table = new DossierTable(["الملف", "الإصدار", "الحجم", "آخر تحديث"], ["File", "Version", "Size", "Updated"],
                        list.Select(Row).ToList(), list.Select(Row).ToList());
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Table: table);
                }
        }

        // Bound to related objects (or to the record itself for a matrix without path).
        var path = section.Path ?? [];
        var definition = PathDefinition(dataset, path, registry);
        if (definition.Nodes.Count != path.Count + 1)
        {
            return null;
        }

        var type = await engine.ResolveDefinitionAsync(definition, templateCode, string.Empty, string.Empty, string.Empty, false, user, ct).ConfigureAwait(false);
        if (type is null || type.Nodes.Any(n => !n.Readable))
        {
            return null;
        }

        var leaf = type.Nodes[^1].Alias;
        var limit = Math.Clamp(section.Limit ?? sectionRowLimit, 1, Math.Max(1, sectionRowLimit));
        var wanted = section.Kind switch
        {
            DossierSectionKinds.Related => section.Fields ?? [],
            DossierSectionKinds.Chart => new[] { section.Chart?.Category, section.Chart?.Series, section.Chart?.Measure }.Where(x => x is not null).Cast<string>().ToList(),
            DossierSectionKinds.Trend => [.. section.Measures ?? [], section.Category ?? string.Empty],
            DossierSectionKinds.Gantt => new[] { section.LabelAr, section.LabelEn, section.Start, section.End, section.ActualStart, section.ActualEnd }
                .Where(x => x is not null).Cast<string>().ToList(),
            DossierSectionKinds.Matrix => new[] { section.Likelihood, section.Impact }.Where(x => x is not null).Cast<string>().ToList(),
            _ => [],
        };
        var columns = wanted.Where(c => type.Offered(c) is not null).Distinct(StringComparer.Ordinal).ToList();

        // Required bindings the runner may not see → the section is omitted (a related list keeps its visible columns).
        var required = section.Kind switch
        {
            DossierSectionKinds.Chart => new[] { section.Chart?.Category, section.Chart?.Measure }.Where(x => x is not null).Cast<string>(),
            DossierSectionKinds.Trend => [section.Category ?? string.Empty, .. section.Measures ?? []],
            DossierSectionKinds.Gantt => new[] { section.Start, section.End }.Where(x => x is not null).Cast<string>(),
            DossierSectionKinds.Matrix => new[] { section.Likelihood, section.Impact }.Where(x => x is not null).Cast<string>(),
            _ => [],
        };
        if (columns.Count == 0 || required.Any(r => type.Offered(r) is null))
        {
            return null;
        }

        var filters = new List<ReportFilter> { idFilter };
        filters.AddRange((section.Filters ?? []).Where(f => type.Offered(f.Field) is { Filterable: true }));
        var effective = new ReportDefinition { ReportType = templateCode, Columns = columns, Filters = filters, Sort = (section.Sort ?? []).Where(s => type.Offered(s.Field) is not null).ToList() };
        var (composed, _) = await engine.ComposeAsync(type, effective, user, ct).ConfigureAwait(false);
        var rows = path.Count == 0 ? composed : composed.Where(r => r.Parts.ContainsKey(leaf)).ToList();
        rows = ReportAggregation.Sort(rows, [], effective.Sort);
        var formatColumns = columns.Concat(section.Chart?.Category is { } cc ? [cc] : []).ToList();
        await PrepareAsync(type, rows, formatColumns, ct).ConfigureAwait(false);
        string? F(string column, object? value, string language) => type.Offered(column) is { } f ? formatter.Format(f, value, language) : null;

        switch (section.Kind)
        {
            case DossierSectionKinds.Related:
                {
                    var shown = rows.Take(limit).ToList();
                    var table = new DossierTable(
                        columns.Select(c => type.Offered(c)!.LabelAr).ToList(), columns.Select(c => type.Offered(c)!.LabelEn).ToList(),
                        shown.Select(r => (IReadOnlyList<string?>)columns.Select(c => F(c, r.Value(c), "ar")).ToList()).ToList(),
                        shown.Select(r => (IReadOnlyList<string?>)columns.Select(c => F(c, r.Value(c), "en")).ToList()).ToList());
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Table: table);
                }

            case DossierSectionKinds.Chart:
                {
                    var spec = section.Chart!;
                    var data = ReportAggregation.Chart(rows, spec);
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Chart: new DossierChart(spec.Type,
                        data.Categories.Select(c => F(spec.Category, c, "ar")).ToList(), data.Categories.Select(c => F(spec.Category, c, "en")).ToList(),
                        data.Series.Select(s => ((string?)null, (string?)null, s.Values)).ToList()));
                }

            case DossierSectionKinds.Trend:
                {
                    var category = section.Category!;
                    var measures = (section.Measures ?? []).Where(m => type.Offered(m) is not null).ToList();
                    var categories = rows.Select(r => r.Value(category)).Distinct().ToList();
                    var series = measures.Select(m => ((string?)type.Offered(m)!.LabelAr, (string?)type.Offered(m)!.LabelEn,
                        (IReadOnlyList<decimal>)categories.Select(c => rows.Where(r => Equals(r.Value(category), c)).Select(r => ReportAggregation.ToDecimal(r.Value(m)) ?? 0m).DefaultIfEmpty(0m).Last())
                            .ToList())).ToList();
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Chart: new DossierChart(ChartTypes.Line,
                        categories.Select(c => F(category, c, "ar")).ToList(), categories.Select(c => F(category, c, "en")).ToList(), series));
                }

            case DossierSectionKinds.Gantt:
                {
                    static DateOnly? D(object? v) => v switch
                    {
                        DateOnly d => d,
                        DateTimeOffset t => DateOnly.FromDateTime(t.UtcDateTime),
                        DateTime dt => DateOnly.FromDateTime(dt),
                        _ => null,
                    };
                    var items = rows.Take(limit).Select(r => new DossierGanttItem(
                        section.LabelAr is { } la && type.Offered(la) is not null ? F(la, r.Value(la), "ar") : null,
                        section.LabelEn is { } le && type.Offered(le) is not null ? F(le, r.Value(le), "en") : null,
                        D(r.Value(section.Start!)), D(r.Value(section.End!)),
                        section.ActualStart is { } s && type.Offered(s) is not null ? D(r.Value(s)) : null,
                        section.ActualEnd is { } e && type.Offered(e) is not null ? D(r.Value(e)) : null)).Where(i => i.Start is not null && i.End is not null).ToList();
                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Gantt: items);
                }

            case DossierSectionKinds.Matrix:
                {
                    var size = Math.Clamp(section.Size, 3, 7);
                    var counts = new int[size, size];
                    foreach (var r in rows)
                    {
                        if (ReportAggregation.ToDecimal(r.Value(section.Likelihood!)) is { } l && ReportAggregation.ToDecimal(r.Value(section.Impact!)) is { } i
                            && l >= 1 && l <= size && i >= 1 && i <= size)
                        {
                            counts[(int)l - 1, (int)i - 1]++;
                        }
                    }

                    return new DossierBlock(section.Key, titleAr, titleEn, section.Kind, section.PageBreak, Matrix: new DossierMatrix(size, counts));
                }

            default:
                return null;
        }
    }

    private static string Outcome(string outcome, bool ar) => outcome switch
    {
        "approve" or "approved" => ar ? "اعتماد" : "Approved",
        "reject" or "rejected" => ar ? "رفض" : "Rejected",
        "return" or "returned" => ar ? "إعادة للتصحيح" : "Returned",
        _ => outcome,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dossier {Template}: section {Section} could not be built and was left out")]
    private static partial void LogSectionFailed(ILogger logger, Exception exception, string template, string section);
}
