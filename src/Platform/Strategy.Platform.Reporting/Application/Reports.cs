using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

public sealed record DatasetFieldDto(string Code, string LabelAr, string LabelEn, string Type, bool Filterable, bool Groupable, bool Sortable, string? LookupType);

public sealed record DatasetDto(string Code, string SystemCode, string LabelAr, string LabelEn, IReadOnlyList<DatasetFieldDto> Fields);

/// <summary>A report definition as the builder sends it (also stored as a saved report's definition).</summary>
public sealed record ReportDefinition
{
    public string Dataset { get; init; } = string.Empty;

    public List<string> Columns { get; init; } = [];

    public List<ReportFilter> Filters { get; init; } = [];

    public List<string> GroupBy { get; init; } = [];

    public List<ReportSort> Sort { get; init; } = [];
}

public sealed record ReportColumnDto(string Code, string LabelAr, string LabelEn, string Type);

public sealed record ReportRowDto(Guid? Id, IReadOnlyList<string?> Cells, string? Link);

public sealed record ReportRunDto(string Dataset, IReadOnlyList<ReportColumnDto> Columns, IReadOnlyList<ReportRowDto> Rows, int Total, int Page, int PageSize,
    IReadOnlyList<string> DroppedColumns);

public sealed record SavedReportDto(Guid Id, string Name, string? Description, string Dataset, ReportDefinition Definition, Guid OwnerUserId, bool CanEdit, uint Version);

public sealed record SavedReportInput(string Name, string? Description, ReportDefinition Definition);

public sealed record ExportedFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Report builder (ADR 0144): only datasets and fields the runner may see; hidden fields are dropped from columns and refused in filters,
/// grouping and sorting; every run (UI, export, schedule) uses the runner's rights, never the author's.
/// </summary>
public sealed class ReportService(
    MetricRegistry registry, IAccessService access, ReportingDbContext db, ILookupService lookups, IOrgDirectory org, IEmployeeDirectory employees,
    ISettingsStore settings, IPdfRenderer pdf, IAuditLog audit, DashboardService dashboards)
{
    public const int MaxPageSize = 200;

    private static FieldError Err(string field, string key) => new(field, key, key);

    private static string TypeName(DatasetFieldType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    private async Task<IReadOnlyList<DatasetField>> AllowedFieldsAsync(DatasetDefinition dataset, ClaimsPrincipal user, CancellationToken ct)
    {
        var allowed = new List<DatasetField>();
        foreach (var field in dataset.Fields)
        {
            if (field.Permission is null || await access.CanAsync(user, field.Permission, cancellationToken: ct).ConfigureAwait(false))
            {
                allowed.Add(field);
            }
        }

        return allowed;
    }

    public async Task<IReadOnlyList<DatasetDto>> DatasetsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var result = new List<DatasetDto>();
        foreach (var dataset in registry.Datasets.OrderBy(d => d.SystemCode, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            if (!await access.CanAsync(user, dataset.ReadPermission, cancellationToken: ct).ConfigureAwait(false))
            {
                continue;
            }

            var fields = await AllowedFieldsAsync(dataset, user, ct).ConfigureAwait(false);
            result.Add(new DatasetDto(dataset.Code, dataset.SystemCode, dataset.LabelAr, dataset.LabelEn,
                fields.Select(f => new DatasetFieldDto(f.Code, f.LabelAr, f.LabelEn, TypeName(f.Type), f.Filterable, f.Groupable, f.Sortable, f.LookupType)).ToList()));
        }

        return result;
    }

    /// <summary>Validates for the runner. Unknown dataset or no read permission → 404; hidden / unknown fields in filters, grouping or sort → 400.</summary>
    private async Task<Result<(DatasetDefinition Dataset, IReportingDatasetSource Source, IReadOnlyList<DatasetField> Fields, List<string> Dropped, ReportDefinition Effective)>> PrepareAsync(
        ReportDefinition definition, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!registry.TryDataset(definition.Dataset, out var dataset, out var source)
            || !await access.CanAsync(user, dataset.ReadPermission, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var allowed = await AllowedFieldsAsync(dataset, user, ct).ConfigureAwait(false);
        var byCode = allowed.ToDictionary(f => f.Code, StringComparer.Ordinal);
        var declared = dataset.Fields.Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
        var errors = new List<FieldError>();
        if (definition.Columns.Count is 0 or > 40)
        {
            errors.Add(new FieldError("columns", "reporting.errors.columns", "reporting.errors.columns"));
        }

        var unknownColumns = definition.Columns.Where(c => !declared.Contains(c)).ToList();
        if (unknownColumns.Count > 0)
        {
            errors.Add(new FieldError("columns", "reporting.errors.fieldUnknown", "reporting.errors.fieldUnknown"));
        }

        foreach (var filter in definition.Filters)
        {
            if (!byCode.TryGetValue(filter.Field, out var field) || !field.Filterable)
            {
                errors.Add(Err("filters", declared.Contains(filter.Field) ? "reporting.errors.fieldNotAllowed" : "reporting.errors.fieldUnknown"));
            }
            else if (!OperatorAllowed(field.Type, filter.Operator) || filter.Values.Count > 100 || filter.Values.Any(v => v.Length > 200))
            {
                errors.Add(new FieldError("filters", "reporting.errors.operator", "reporting.errors.operator"));
            }
        }

        if (definition.GroupBy.Count > 2 || definition.GroupBy.Any(g => !byCode.TryGetValue(g, out var f) || !f.Groupable))
        {
            errors.Add(Err("groupBy", definition.GroupBy.Any(g => declared.Contains(g) && !byCode.ContainsKey(g)) ? "reporting.errors.fieldNotAllowed" : "reporting.errors.groupBy"));
        }

        if (definition.Sort.Count > 3 || definition.Sort.Any(s => !byCode.TryGetValue(s.Field, out var f) || !f.Sortable))
        {
            errors.Add(Err("sort", definition.Sort.Any(s => declared.Contains(s.Field) && !byCode.ContainsKey(s.Field)) ? "reporting.errors.fieldNotAllowed" : "reporting.errors.sort"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        // Columns the runner may not see are dropped (a shared report still runs, without them).
        var dropped = definition.Columns.Where(c => !byCode.ContainsKey(c)).ToList();
        var effective = definition with { Columns = definition.Columns.Where(byCode.ContainsKey).ToList() };
        if (effective.Columns.Count == 0)
        {
            return Error.Validation([new FieldError("columns", "reporting.errors.fieldNotAllowed", "reporting.errors.fieldNotAllowed")]);
        }

        // Grouped results also carry the group fields first.
        if (effective.GroupBy.Count > 0)
        {
            effective = effective with { Columns = effective.GroupBy.Concat(effective.Columns.Where(c => !effective.GroupBy.Contains(c))).ToList() };
        }

        return (dataset, source, allowed, dropped, effective);
    }

    private static bool OperatorAllowed(DatasetFieldType type, string op) => op switch
    {
        ReportOperators.Equal or ReportOperators.NotEqual or ReportOperators.IsEmpty or ReportOperators.IsNotEmpty => true,
        ReportOperators.In => type is DatasetFieldType.Lookup or DatasetFieldType.Text or DatasetFieldType.Unit or DatasetFieldType.Employee or DatasetFieldType.Number,
        ReportOperators.Contains => type == DatasetFieldType.Text,
        ReportOperators.GreaterOrEqual or ReportOperators.LessOrEqual or ReportOperators.Between =>
            type is DatasetFieldType.Number or DatasetFieldType.Money or DatasetFieldType.Ratio or DatasetFieldType.Date or DatasetFieldType.DateTime,
        _ => false,
    };

    public async Task<Result<ReportRunDto>> RunAsync(ReportDefinition definition, int page, int pageSize, string language, ClaimsPrincipal user, CancellationToken ct)
    {
        var prepared = await PrepareAsync(definition, user, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var (dataset, source, fields, dropped, effective) = prepared.Value;
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var p = Math.Max(1, page);
        ReportResult result;
        try
        {
            result = await source.RunAsync(new ReportQuery(dataset.Code, effective.Columns, effective.Filters, effective.GroupBy, effective.Sort, (p - 1) * size, size), user, ct)
                .ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return Error.Validation([new FieldError("filters", "reporting.errors.value", "reporting.errors.value")]);
        }

        return await FormatAsync(dataset, fields, result, dropped, p, size, language, ct).ConfigureAwait(false);
    }

    /// <summary>All rows up to the export limit (exports and schedules).</summary>
    public async Task<Result<ReportRunDto>> RunAllAsync(ReportDefinition definition, string language, ClaimsPrincipal user, CancellationToken ct)
    {
        var limit = (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).ExportRowLimit;
        var prepared = await PrepareAsync(definition, user, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var (dataset, source, fields, dropped, effective) = prepared.Value;
        try
        {
            var result = await source.RunAsync(new ReportQuery(dataset.Code, effective.Columns, effective.Filters, effective.GroupBy, effective.Sort, 0, limit), user, ct)
                .ConfigureAwait(false);
            return await FormatAsync(dataset, fields, result, dropped, 1, limit, language, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return Error.Validation([new FieldError("filters", "reporting.errors.value", "reporting.errors.value")]);
        }
    }

    private async Task<ReportRunDto> FormatAsync(
        DatasetDefinition dataset, IReadOnlyList<DatasetField> fields, ReportResult result, IReadOnlyList<string> dropped, int page, int size, string language, CancellationToken ct)
    {
        var ar = language == "ar";
        var byCode = fields.ToDictionary(f => f.Code, StringComparer.Ordinal);
        var columns = result.Columns.Select(c => byCode.TryGetValue(c, out var f)
            ? new ReportColumnDto(c, f.LabelAr, f.LabelEn, TypeName(f.Type))
            : c == "count" ? new ReportColumnDto(c, "العدد", "Count", "number") : new ReportColumnDto(c, c, c, "text")).ToList();

        // Resolve labels once per run.
        var lookupLabels = new Dictionary<string, Dictionary<string, (string? Ar, string? En)>>(StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Type == DatasetFieldType.Lookup && f.LookupType is not null && result.Columns.Contains(f.Code)))
        {
            var values = await lookups.GetValuesAsync(f.LookupType!, includeInactive: true, ct).ConfigureAwait(false);
            lookupLabels[f.Code] = values.ToDictionary(v => v.Code, v => (v.LabelAr, v.LabelEn), StringComparer.Ordinal);
        }

        var unitIds = new HashSet<Guid>();
        var employeeIds = new HashSet<Guid>();
        for (var i = 0; i < result.Columns.Count; i++)
        {
            if (!byCode.TryGetValue(result.Columns[i], out var f))
            {
                continue;
            }

            foreach (var row in result.Rows)
            {
                if (row[i] is Guid g)
                {
                    (f.Type == DatasetFieldType.Unit ? unitIds : f.Type == DatasetFieldType.Employee ? employeeIds : []).Add(g);
                }
            }
        }

        var units = new Dictionary<Guid, UnitReference>();
        foreach (var id in unitIds)
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                units[id] = unit;
            }
        }

        var people = employeeIds.Count == 0 ? new Dictionary<Guid, EmployeeReference>() : await employees.GetManyAsync(employeeIds, ct).ConfigureAwait(false);
        string? Text(string column, object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (!byCode.TryGetValue(column, out var f))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return f.Type switch
            {
                DatasetFieldType.Lookup when value is string code && lookupLabels.TryGetValue(column, out var map) && map.TryGetValue(code, out var l) =>
                    (ar ? l.Ar ?? l.En : l.En ?? l.Ar) ?? code,
                DatasetFieldType.Unit when value is Guid g && units.TryGetValue(g, out var u) => (ar ? u.NameAr ?? u.NameEn : u.NameEn ?? u.NameAr) ?? u.Code,
                DatasetFieldType.Employee when value is Guid g && people.TryGetValue(g, out var e) => (ar ? e.NameAr ?? e.NameEn : e.NameEn ?? e.NameAr) ?? e.EmployeeNo,
                DatasetFieldType.Ratio => (Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %",
                DatasetFieldType.Money => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("#,0.00", CultureInfo.InvariantCulture),
                DatasetFieldType.Bool => value is true ? (ar ? "نعم" : "Yes") : (ar ? "لا" : "No"),
                DatasetFieldType.Date when value is DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DatasetFieldType.DateTime when value is DateTimeOffset t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }

        var rows = result.Rows.Select((row, index) =>
        {
            var id = result.RowIds is { } ids && index < ids.Count ? ids[index] : (Guid?)null;
            return new ReportRowDto(id, row.Select((v, i) => Text(result.Columns[i], v)).ToList(), id is { } rowId ? dataset.Link.Replace("{id}", rowId.ToString("D"), StringComparison.Ordinal) : null);
        }).ToList();
        return new ReportRunDto(dataset.Code, columns, rows, result.Total, page, size, dropped);
    }

    public async Task<Result<ExportedFile>> ExportAsync(ReportDefinition definition, string format, string language, string title, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await access.CanAsync(user, ReportingPermissions.ReportExport, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var run = await RunAllAsync(definition, language, user, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var file = await RenderAsync(run.Value, format, language, title, ct).ConfigureAwait(false);
        if (file.IsSuccess)
        {
            await audit.WriteAsync(new AuditEvent("reporting.report.export", true, ReportingPermissions.SystemCode, "reporting.dataset", definition.Dataset,
                new { format, rows = run.Value.Rows.Count, columns = run.Value.Columns.Select(c => c.Code) }), ct).ConfigureAwait(false);
        }

        return file;
    }

    public async Task<Result<ExportedFile>> RenderAsync(ReportRunDto run, string format, string language, string title, CancellationToken ct)
    {
        var ar = language == "ar";
        var safe = new string(title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        var name = string.IsNullOrEmpty(safe) ? "report" : safe;
        var headers = run.Columns.Select(c => ar ? c.LabelAr : c.LabelEn).ToList();
        if (format == "xlsx")
        {
            var bytes = ExcelExport.Build(title.Length > 30 ? title[..30] : title, headers, run.Rows.Select(r => (IReadOnlyList<object?>)r.Cells.Cast<object?>().ToList()), ar);
            return new ExportedFile(bytes, ExcelExport.ContentType, name + ".xlsx");
        }

        if (format != "pdf")
        {
            return Error.Validation([new FieldError("format", "reporting.errors.format", "reporting.errors.format")]);
        }

        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var document = new ReportDocument
        {
            TitleAr = title,
            TitleEn = title,
            OrganisationAr = branding.OrganisationNameAr,
            OrganisationEn = branding.OrganisationNameEn,
            Language = language,
            PrimaryColor = branding.PrimaryColor,
        };
        document.Table(run.Columns.Select(c => new ReportColumn(c.LabelAr, c.LabelEn)).ToList(), run.Rows.Select(r => r.Cells.ToArray()));
        try
        {
            var bytes = await pdf.RenderAsync(document.ToHtml(), ct).ConfigureAwait(false);
            return new ExportedFile(bytes, "application/pdf", name + ".pdf");
        }
        catch (PdfRenderingException)
        {
            return Error.Rule("reporting.pdfFailed", "reporting.errors.pdfFailed");
        }
    }

    // Saved reports

    private async Task<bool> CanSeeReportAsync(SavedReport report, ClaimsPrincipal user, CancellationToken ct) =>
        report.Status == "active" && (report.OwnerUserId == user.UserId()
            || await dashboards.MatchesAnyAsync(await db.Shares.AsNoTracking().Where(s => s.TargetKind == "report" && s.TargetId == report.Id).ToListAsync(ct).ConfigureAwait(false), user, ct)
                .ConfigureAwait(false));

    public async Task<IReadOnlyList<SavedReportDto>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var sharedIds = await db.Shares.AsNoTracking().Where(s => s.TargetKind == "report").Select(s => s.TargetId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var candidates = await db.Reports.AsNoTracking().Where(r => r.Status == "active" && (r.OwnerUserId == userId || sharedIds.Contains(r.Id)))
            .OrderBy(r => r.Name).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<SavedReportDto>();
        foreach (var r in candidates)
        {
            if (await CanSeeReportAsync(r, user, ct).ConfigureAwait(false))
            {
                result.Add(ToDto(r, user));
            }
        }

        return result;
    }

    private static SavedReportDto ToDto(SavedReport r, ClaimsPrincipal user) =>
        new(r.Id, r.Name, r.Description, r.Dataset, JsonSerializer.Deserialize<ReportDefinition>(r.DefinitionJson, DashboardService.Json) ?? new ReportDefinition(),
            r.OwnerUserId, r.OwnerUserId == user.UserId(), r.Version);

    public async Task<Result<SavedReportDto>> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var r = await db.Reports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return r is null || !await CanSeeReportAsync(r, user, ct).ConfigureAwait(false) ? Error.NotFound() : ToDto(r, user);
    }

    public async Task<Result<SavedReportDto>> SaveAsync(Guid? id, SavedReportInput input, uint? version, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || (input.Description?.Length ?? 0) > 1000)
        {
            return Error.Validation([new FieldError("name", "reporting.errors.name", "reporting.errors.name")]);
        }

        // The author may only save what they could run themselves.
        var prepared = await PrepareAsync(input.Definition, user, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        if (prepared.Value.Dropped.Count > 0)
        {
            return Error.Validation([new FieldError("columns", "reporting.errors.fieldNotAllowed", "reporting.errors.fieldNotAllowed")]);
        }

        var json = JsonSerializer.Serialize(input.Definition, DashboardService.Json);
        SavedReport report;
        if (id is { } existingId)
        {
            report = (await db.Reports.FirstOrDefaultAsync(r => r.Id == existingId && r.Status == "active", ct).ConfigureAwait(false))!;
            if (report is null || report.OwnerUserId != userId)
            {
                return Error.NotFound();
            }

            if (version is { } v && report.Version != v)
            {
                return Error.Conflict();
            }

            report.Update(input.Name.Trim(), input.Description, input.Definition.Dataset, json);
        }
        else
        {
            report = new SavedReport(userId, input.Name.Trim(), input.Definition.Dataset, json);
            report.Update(input.Name.Trim(), input.Description, input.Definition.Dataset, json);
            db.Reports.Add(report);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(report, user);
    }

    public async Task<Result> ArchiveAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.Status == "active", ct).ConfigureAwait(false);
        if (report is null || report.OwnerUserId != userId)
        {
            return Error.NotFound();
        }

        report.Archive();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<bool> CanShareAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        return await db.Reports.AnyAsync(r => r.Id == id && r.Status == "active" && r.OwnerUserId == userId, ct).ConfigureAwait(false)
            && await access.CanAsync(user, ReportingPermissions.ReportShare, cancellationToken: ct).ConfigureAwait(false);
    }
}
