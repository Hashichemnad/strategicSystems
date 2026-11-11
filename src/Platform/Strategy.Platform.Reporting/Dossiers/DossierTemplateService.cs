using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Reporting.ReportTypes;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Dossiers;

public sealed record DossierTemplateDto(Guid Id, string Code, string Dataset, string LabelAr, string LabelEn, DossierDefinition Definition, bool Active, bool Seeded, uint Version);

public sealed record DossierTemplateInput(string Code, string Dataset, string LabelAr, string LabelEn, DossierDefinition Definition, bool Active);

/// <summary>A template the caller may run (its record type is readable).</summary>
public sealed record UsableDossierTemplateDto(Guid Id, string Code, string Dataset, string LabelAr, string LabelEn, string Language, IReadOnlyList<ReportTypeFieldDto> FilterFields);

public sealed record DossierRecordDto(Guid Id, string? Number, string? NameAr, string? NameEn);

public sealed record DossierRenderInput(Guid RecordId, string Format, string? Language);

public sealed record DossierBulkInput(List<ReportFilter> Filters, string Format, string? Language);

public static class DossierFormats
{
    public const string Pdf = "pdf";
    public const string Word = "docx";
    public const string ZipPdf = "zip-pdf";
    public const string ZipWord = "zip-docx";
}

/// <summary>Validation of templates against the reportable metadata (pure; unit-tested).</summary>
public static class DossierRules
{
    public static IReadOnlyList<string> Validate(string dataset, DossierDefinition definition, IReportableMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(metadata);
        var errors = new List<string>();
        var root = metadata.Dataset(dataset);
        if (root is null || root.JoinOnly || root.KeyField is null)
        {
            return ["reporting.errors.dossier.dataset"];
        }

        if (!DossierLanguages.All.Contains(definition.Language, StringComparer.Ordinal))
        {
            errors.Add("reporting.errors.dossier.language");
        }

        if (definition.Sections is null || definition.Sections.Count is 0 or > 40 || definition.Sections.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != definition.Sections.Count
            || definition.Sections.Any(s => string.IsNullOrWhiteSpace(s.Key) || s.Key.Length > 50))
        {
            return [.. errors, "reporting.errors.dossier.sections"];
        }

        bool Known(IReadOnlyList<DatasetDefinition> levels, string? column, bool allowReference = false)
        {
            if (column is null || ReportTypeRules.Split(column) is not { } p)
            {
                return false;
            }

            var index = p.Alias switch { "a" => 0, "b" => 1, "c" => 2, "d" => 3, _ => -1 };
            return index >= 0 && index < levels.Count && levels[index].Fields.Any(f => f.Code == p.Field && (allowReference || f.Type != DatasetFieldType.Reference));
        }

        IReadOnlyList<DatasetDefinition> rootOnly = [root];
        foreach (string? field in new[] { definition.NumberField, definition.NameFieldAr, definition.NameFieldEn, definition.Cover?.SubtitleField })
        {
            if (field is not null && !Known(rootOnly, field))
            {
                errors.Add("reporting.errors.dossier.field");
            }
        }

        foreach (var section in definition.Sections)
        {
            if (!DossierSectionKinds.All.Contains(section.Kind, StringComparer.Ordinal) || (section.TitleAr?.Length ?? 0) > 200 || (section.TitleEn?.Length ?? 0) > 200
                || (section.TextAr?.Length ?? 0) > 4000 || (section.TextEn?.Length ?? 0) > 4000)
            {
                errors.Add("reporting.errors.dossier.kind");
                continue;
            }

            var levels = new List<DatasetDefinition> { root };
            foreach (var code in section.Path ?? [])
            {
                var relationship = metadata.Relationships.FirstOrDefault(r => r.Code == code && r.ParentDataset == levels[^1].Code);
                if (relationship is null || levels.Count > ReportTypeRules.MaxDepth)
                {
                    errors.Add("reporting.errors.dossier.path");
                    break;
                }

                levels.Add(metadata.Dataset(relationship.ChildDataset)!);
            }

            var needsPath = section.Kind is DossierSectionKinds.Related or DossierSectionKinds.Chart or DossierSectionKinds.Trend or DossierSectionKinds.Gantt;
            if (needsPath && (section.Path?.Count ?? 0) == 0)
            {
                errors.Add("reporting.errors.dossier.path");
            }

            var columns = section.Kind switch
            {
                DossierSectionKinds.Fields or DossierSectionKinds.Related => section.Fields ?? [],
                DossierSectionKinds.Chart => new[] { section.Chart?.Category, section.Chart?.Series, section.Chart?.Measure }.Where(x => x is not null).Cast<string>().ToList(),
                DossierSectionKinds.Trend => [.. section.Measures ?? [], section.Category ?? string.Empty],
                DossierSectionKinds.Gantt => new[] { section.LabelAr, section.LabelEn, section.Start, section.End, section.ActualStart, section.ActualEnd }.Where(x => x is not null).Cast<string>().ToList(),
                DossierSectionKinds.Matrix => [section.Likelihood ?? string.Empty, section.Impact ?? string.Empty],
                _ => [],
            };
            if ((section.Kind is DossierSectionKinds.Fields or DossierSectionKinds.Related && columns.Count == 0) || columns.Any(c => !Known(levels, c))
                || (section.Kind == DossierSectionKinds.Fields && columns.Any(c => !c.StartsWith("a.", StringComparison.Ordinal)))
                || (section.Kind == DossierSectionKinds.Chart && (section.Chart is null || !ChartTypes.All.Contains(section.Chart.Type, StringComparer.Ordinal)))
                || (section.Kind == DossierSectionKinds.Gantt && (section.Start is null || section.End is null))
                || (section.Kind == DossierSectionKinds.Trend && (section.Category is null || (section.Measures?.Count ?? 0) == 0))
                || (section.Kind == DossierSectionKinds.Matrix && section.Size is < 3 or > 7)
                || (section.Kind == DossierSectionKinds.Approvals && string.IsNullOrWhiteSpace(section.ResourceType))
                || (section.Kind == DossierSectionKinds.Attachments && string.IsNullOrWhiteSpace(section.OwnerType))
                || (section.Filters ?? []).Any(f => !Known(levels, f.Field) || !ReportOperators.All.Contains(f.Operator, StringComparer.Ordinal))
                || (section.Sort ?? []).Any(s => !Known(levels, s.Field))
                || (section.Condition is { } c && (!Known(rootOnly, c.Field) || !ReportOperators.All.Contains(c.Operator, StringComparer.Ordinal))))
            {
                errors.Add("reporting.errors.dossier.section");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }
}

/// <summary>Dossier templates and runs (ADRs 0184–0186): admin designer, seeding, one record (PDF / Word), a filtered set (merged PDF / ZIP).</summary>
public sealed class DossierTemplateService(
    ReportingDbContext db, MetricRegistry registry, ReportTypeEngine engine, DossierBuilder builder, IAccessService access, IPdfRenderer pdf, ISettingsStore settings,
    IAuditLog audit, ReportingClock clock)
{
    private static DossierTemplateDto ToDto(DossierTemplate t) => new(t.Id, t.Code, t.Dataset, t.LabelAr, t.LabelEn,
        JsonSerializer.Deserialize<DossierDefinition>(t.DefinitionJson, DashboardService.Json) ?? new DossierDefinition(), t.Status == DefinitionStatuses.Active, t.SeedHash is not null,
        t.Version);

    public async Task<IReadOnlyList<DossierTemplateDto>> ListAsync(CancellationToken ct) =>
        (await db.DossierTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto).ToList();

    public async Task<Result<DossierTemplateDto>> GetAsync(Guid id, CancellationToken ct) =>
        await db.DossierTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false) is { } t ? ToDto(t) : Error.NotFound();

    private async Task<DossierTemplateDto?> ActiveAsync(string code, CancellationToken ct) =>
        await db.DossierTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.Status == DefinitionStatuses.Active, ct).ConfigureAwait(false) is { } t ? ToDto(t) : null;

    /// <summary>Active templates whose record type the caller may read, with the record-type fields they may filter a set on.</summary>
    public async Task<IReadOnlyList<UsableDossierTemplateDto>> UsableAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var result = new List<UsableDossierTemplateDto>();
        foreach (var t in await db.DossierTemplates.AsNoTracking().Where(t => t.Status == DefinitionStatuses.Active).OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            var type = await engine.ResolveAsync(ReportTypeRules.ImplicitPrefix + t.Dataset, user, ct).ConfigureAwait(false);
            if (type is null)
            {
                continue;
            }

            var definition = JsonSerializer.Deserialize<DossierDefinition>(t.DefinitionJson, DashboardService.Json) ?? new DossierDefinition();
            var fields = ReportTypeService.Describe(type, null, null).Objects[0].Fields.Where(f => f.Filterable).ToList();
            result.Add(new UsableDossierTemplateDto(t.Id, t.Code, t.Dataset, t.LabelAr, t.LabelEn, definition.Language, fields));
        }

        return result;
    }

    public async Task<Result<DossierTemplateDto>> SaveAsync(Guid? id, DossierTemplateInput input, uint? version, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await access.CanAsync(user, ReportingPermissions.DossierTemplateManage, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (input?.Definition is null)
        {
            return Error.Validation([new FieldError("definition", "validation.required", "validation.required")]);
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 || !input.Code.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch is '.' or '-'))
        {
            errors.Add(new FieldError("code", "reporting.errors.code", "reporting.errors.code"));
        }

        if (string.IsNullOrWhiteSpace(input.LabelAr) || string.IsNullOrWhiteSpace(input.LabelEn) || input.LabelAr.Length > 200 || input.LabelEn.Length > 200)
        {
            errors.Add(new FieldError("label", "reporting.errors.name", "reporting.errors.name"));
        }

        errors.AddRange(DossierRules.Validate(input.Dataset, input.Definition, registry).Select(k => new FieldError("definition", k, k)));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var json = JsonSerializer.Serialize(input.Definition, DashboardService.Json);
        DossierTemplate template;
        if (id is { } existingId)
        {
            template = (await db.DossierTemplates.FirstOrDefaultAsync(t => t.Id == existingId, ct).ConfigureAwait(false))!;
            if (template is null)
            {
                return Error.NotFound();
            }

            if (version is { } v && template.Version != v)
            {
                return Error.Conflict();
            }

            if (template.Code != input.Code)
            {
                return Error.Validation([new FieldError("code", "reporting.errors.code", "reporting.errors.code")]);
            }

            template.Edited();
        }
        else
        {
            if (await db.DossierTemplates.AnyAsync(t => t.Code == input.Code, ct).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("code", "reporting.errors.codeTaken", "reporting.errors.codeTaken")]);
            }

            template = new DossierTemplate(input.Code, input.Dataset, input.LabelAr.Trim(), input.LabelEn.Trim(), json);
            db.DossierTemplates.Add(template);
        }

        template.Update(input.Dataset, input.LabelAr.Trim(), input.LabelEn.Trim(), json, input.Active);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("reporting.dossier-template.save", true, ReportingPermissions.SystemCode, "reporting.dossier-template", template.Id.ToString("D"),
            new { template.Code, active = input.Active }), ct).ConfigureAwait(false);
        return ToDto(template);
    }

    /// <summary>Records of the template's type the caller may see (record picker): number and names, optional text search on them.</summary>
    public async Task<Result<IReadOnlyList<DossierRecordDto>>> RecordsAsync(string code, string? search, ClaimsPrincipal user, CancellationToken ct)
    {
        if (await ActiveAsync(code, ct).ConfigureAwait(false) is not { } template
            || await engine.ResolveAsync(ReportTypeRules.ImplicitPrefix + template.Dataset, user, ct).ConfigureAwait(false) is not { } type)
        {
            return Error.NotFound();
        }

        var d = template.Definition;
        var key = "a." + type.Nodes[0].Dataset.KeyField;
        var columns = new[] { d.NumberField, d.NameFieldAr, d.NameFieldEn }.Where(c => c is not null && type.Offered(c) is not null).Cast<string>().ToList();
        if (columns.Count == 0)
        {
            return Error.NotFound();
        }

        var (rows, _) = await engine.ComposeAsync(type, new ReportDefinition { ReportType = template.Code, Columns = columns, Sort = [new ReportSort(columns[0], true)] }, user, ct)
            .ConfigureAwait(false);
        string? Text(FlatRow r, string? c) => c is not null && type.Offered(c) is not null ? Convert.ToString(r.Value(c), System.Globalization.CultureInfo.InvariantCulture) : null;
        var term = search?.Trim();
        return rows.Select(r => new DossierRecordDto(r.Parts["a"].RowId ?? Guid.Empty, Text(r, d.NumberField), Text(r, d.NameFieldAr), Text(r, d.NameFieldEn)))
            .Where(r => r.Id != Guid.Empty && (string.IsNullOrEmpty(term) || new[] { r.Number, r.NameAr, r.NameEn }.Any(x => x?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)))
            .OrderByDescending(r => r.Number, StringComparer.Ordinal).Take(50).ToList();
    }

    private async Task<DossierBranding> BrandingAsync(CancellationToken ct)
    {
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        return new DossierBranding(branding.OrganisationNameAr, branding.OrganisationNameEn, branding.PrimaryColor, clock.UtcNow);
    }

    public async Task<DossierDocument?> BuildAsync(DossierTemplateDto template, Guid recordId, ClaimsPrincipal user, CancellationToken ct)
    {
        var limit = (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).DossierSectionRowLimit;
        return await builder.BuildAsync(template.Code, template.LabelAr, template.LabelEn, template.Dataset, template.Definition, recordId, user, ct, limit).ConfigureAwait(false);
    }

    private static string Language(string? requested, DossierDefinition definition, string fallback) =>
        requested is { } l && DossierLanguages.All.Contains(l, StringComparer.Ordinal) ? l : definition.Language ?? fallback;

    private static string FileName(DossierDocument doc, string extension)
    {
        var name = new string((doc.Number ?? doc.RecordId.ToString("N")[..8]).Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return (string.IsNullOrEmpty(name) ? "dossier" : name) + extension;
    }

    /// <summary>One record → PDF or Word, as the caller. Record not visible → 404.</summary>
    public async Task<Result<ExportedFile>> RenderAsync(string code, DossierRenderInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input is null || input.Format is not (DossierFormats.Pdf or DossierFormats.Word))
        {
            return Error.Validation([new FieldError("format", "reporting.errors.format", "reporting.errors.format")]);
        }

        if (await ActiveAsync(code, ct).ConfigureAwait(false) is not { } template
            || await BuildAsync(template, input.RecordId, user, ct).ConfigureAwait(false) is not { } doc)
        {
            return Error.NotFound();
        }

        var language = Language(input.Language, template.Definition, "ar");
        var file = await FileAsync([doc], input.Format, language, ct).ConfigureAwait(false);
        if (file.IsSuccess)
        {
            await audit.WriteAsync(new AuditEvent("reporting.dossier.render", true, ReportingPermissions.SystemCode, "reporting.dossier-template", template.Id.ToString("D"),
                new { template.Code, record = input.RecordId, input.Format, language, sections = doc.Blocks.Count, omitted = doc.OmittedSections }), ct).ConfigureAwait(false);
        }

        return file;
    }

    /// <summary>The records of the template's type matching <paramref name="filters"/> that the caller may see (at most the bulk limit).</summary>
    public async Task<Result<(DossierTemplateDto Template, IReadOnlyList<Guid> Ids)>> SelectAsync(string code, IReadOnlyList<ReportFilter> filters, ClaimsPrincipal user, CancellationToken ct)
    {
        if (await ActiveAsync(code, ct).ConfigureAwait(false) is not { } template)
        {
            return Error.NotFound();
        }

        var keyField = registry.Dataset(template.Dataset)?.KeyField;
        var definition = new ReportDefinition { ReportType = ReportTypeRules.ImplicitPrefix + template.Dataset, Columns = [template.Definition.NumberField ?? "a." + keyField], Filters = [.. filters ?? []] };
        var prepared = await engine.PrepareAsync(definition, user, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var limit = (await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false)).DossierBulkLimit;
        var (rows, _) = await engine.ComposeAsync(prepared.Value.Type, prepared.Value.Effective, user, ct).ConfigureAwait(false);
        var sorted = ReportAggregation.Sort(rows, [], [new ReportSort(prepared.Value.Effective.Columns[0], false)]);
        return (template, sorted.Select(r => r.Parts["a"].RowId).Where(id => id is not null).Select(id => id!.Value).Distinct().Take(limit).ToList());
    }

    /// <summary>A filtered set → merged PDF or ZIP (PDF / Word per record); each dossier built as the caller.</summary>
    public async Task<Result<ExportedFile>> BulkAsync(string code, DossierBulkInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input is null || input.Format is not (DossierFormats.Pdf or DossierFormats.ZipPdf or DossierFormats.ZipWord))
        {
            return Error.Validation([new FieldError("format", "reporting.errors.format", "reporting.errors.format")]);
        }

        var selected = await SelectAsync(code, input.Filters ?? [], user, ct).ConfigureAwait(false);
        if (selected.IsFailure)
        {
            return selected.Error;
        }

        var (template, ids) = selected.Value;
        var docs = new List<DossierDocument>();
        foreach (var id in ids)
        {
            if (await BuildAsync(template, id, user, ct).ConfigureAwait(false) is { } doc)
            {
                docs.Add(doc);
            }
        }

        if (docs.Count == 0)
        {
            return Error.Rule("reporting.dossier.empty", "reporting.errors.dossierEmpty");
        }

        var language = Language(input.Language, template.Definition, "ar");
        var file = await FileAsync(docs, input.Format, language, ct).ConfigureAwait(false);
        if (file.IsSuccess)
        {
            await audit.WriteAsync(new AuditEvent("reporting.dossier.bulk", true, ReportingPermissions.SystemCode, "reporting.dossier-template", template.Id.ToString("D"),
                new { template.Code, records = docs.Count, input.Format, language }), ct).ConfigureAwait(false);
        }

        return file;
    }

    /// <summary>The file of one or more built dossiers (also used by schedules).</summary>
    public async Task<Result<ExportedFile>> FileAsync(IReadOnlyList<DossierDocument> docs, string format, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docs);
        var branding = await BrandingAsync(ct).ConfigureAwait(false);
        try
        {
            switch (format)
            {
                case DossierFormats.Pdf:
                    var bytes = await pdf.RenderAsync(DossierHtml.Build(docs, language, branding), ct).ConfigureAwait(false);
                    return new ExportedFile(bytes, "application/pdf", docs.Count == 1 ? FileName(docs[0], ".pdf") : "dossiers.pdf");
                case DossierFormats.Word when docs.Count == 1:
                    return new ExportedFile(DossierWord.Build(docs[0], language, branding), DossierWord.ContentType, FileName(docs[0], ".docx"));
                case DossierFormats.ZipPdf or DossierFormats.ZipWord:
                    using (var stream = new MemoryStream())
                    {
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var doc in docs)
                            {
                                var extension = format == DossierFormats.ZipPdf ? ".pdf" : ".docx";
                                var name = FileName(doc, extension);
                                for (var n = 2; !used.Add(name); n++)
                                {
                                    name = FileName(doc, $"-{n}{extension}");
                                }

                                var content = format == DossierFormats.ZipPdf
                                    ? await pdf.RenderAsync(DossierHtml.Build([doc], language, branding), ct).ConfigureAwait(false)
                                    : DossierWord.Build(doc, language, branding);
                                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                                await using var entryStream = await entry.OpenAsync(ct).ConfigureAwait(false);
                                await entryStream.WriteAsync(content, ct).ConfigureAwait(false);
                            }
                        }

                        return new ExportedFile(stream.ToArray(), "application/zip", "dossiers.zip");
                    }

                default:
                    return Error.Validation([new FieldError("format", "reporting.errors.format", "reporting.errors.format")]);
            }
        }
        catch (PdfRenderingException)
        {
            return Error.Rule("reporting.pdfFailed", "reporting.errors.pdfFailed");
        }
    }

    /// <summary>Seeds the templates of SPEC-ADDENDUM E3; an unedited seeded template follows the code, an edited one is left alone.</summary>
    public static async Task SeedAsync(ReportingDbContext db, IReportableMetadata metadata, CancellationToken ct)
    {
        var existing = await db.DossierTemplates.ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        foreach (var seed in DossierSeeds.All)
        {
            if (DossierRules.Validate(seed.Dataset, seed.Definition, metadata).Count > 0)
            {
                continue;
            }

            var json = JsonSerializer.Serialize(seed.Definition, DashboardService.Json);
            var hash = DashboardService.Hash(json + seed.Dataset + seed.LabelAr + seed.LabelEn);
            if (!existing.TryGetValue(seed.Code, out var template))
            {
                template = new DossierTemplate(seed.Code, seed.Dataset, seed.LabelAr, seed.LabelEn, json);
                template.Seeded(hash);
                db.DossierTemplates.Add(template);
            }
            else if (template.SeedHash is not null && template.SeedHash != hash)
            {
                template.Update(seed.Dataset, seed.LabelAr, seed.LabelEn, json, template.Status == DefinitionStatuses.Active);
                template.Seeded(hash);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
