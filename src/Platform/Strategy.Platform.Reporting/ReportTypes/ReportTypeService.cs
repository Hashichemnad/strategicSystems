using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.ReportTypes;

public sealed record ReportTypeFieldDto(
    string Code, string Field, string LabelAr, string LabelEn, string Type, bool Filterable, bool Groupable, bool Sortable, string? LookupType);

public sealed record ReportTypeObjectDto(
    string Alias, string Dataset, string LabelAr, string LabelEn, string? Parent, string Join, string? RelationshipLabelAr, string? RelationshipLabelEn, bool Readable,
    IReadOnlyList<ReportTypeFieldDto> Fields);

/// <summary>A report type as the builder offers it to the caller: only objects and fields they may see.</summary>
public sealed record UsableReportTypeDto(
    string Code, string SystemCode, string LabelAr, string LabelEn, string? DescriptionAr, string? DescriptionEn, bool Implicit, IReadOnlyList<ReportTypeObjectDto> Objects);

public sealed record ReportTypeDto(
    Guid Id, string Code, string SystemCode, string LabelAr, string LabelEn, string? DescriptionAr, string? DescriptionEn, ReportTypeDefinition Definition, bool Active,
    bool Seeded, uint Version);

public sealed record ReportTypeInput(
    string Code, string SystemCode, string LabelAr, string LabelEn, string? DescriptionAr, string? DescriptionEn, ReportTypeDefinition Definition, bool Active);

public sealed record ReportableFieldDto(string Code, string LabelAr, string LabelEn, string Type, string? Permission);

public sealed record ReportableDatasetDto(string Code, string SystemCode, string LabelAr, string LabelEn, bool JoinOnly, string? KeyField, IReadOnlyList<ReportableFieldDto> Fields);

public sealed record ReportableRelationshipDto(string Code, string ParentDataset, string ParentField, string ChildDataset, string ChildField, string LabelAr, string LabelEn);

/// <summary>The reportable metadata for the report-type editor (ADR 0180): every dataset, field (with its permission) and relationship.</summary>
public sealed record ReportableMetadataDto(IReadOnlyList<ReportableDatasetDto> Datasets, IReadOnlyList<ReportableRelationshipDto> Relationships);

/// <summary>Report types: the builder's list for a user, administration in Settings → Reporting, seeding (ADR 0181).</summary>
public sealed class ReportTypeService(ReportingDbContext db, MetricRegistry registry, ReportTypeEngine engine, IAccessService access, IAuditLog audit)
{
    private static ReportTypeDto ToDto(ReportType t) => new(
        t.Id, t.Code, t.SystemCode, t.LabelAr, t.LabelEn, t.DescriptionAr, t.DescriptionEn,
        JsonSerializer.Deserialize<ReportTypeDefinition>(t.DefinitionJson, DashboardService.Json) ?? new ReportTypeDefinition(),
        t.Status == DefinitionStatuses.Active, t.SeedHash is not null, t.Version);

    public static UsableReportTypeDto Describe(ResolvedType type, string? descriptionAr, string? descriptionEn) =>
        new(type.Code, type.SystemCode, type.LabelAr, type.LabelEn, descriptionAr, descriptionEn, type.Implicit, type.Nodes.Select(n => new ReportTypeObjectDto(
            n.Alias, n.Dataset.Code, n.Dataset.LabelAr, n.Dataset.LabelEn, n.Node.Parent, n.Node.Join, n.Relationship?.LabelAr, n.Relationship?.LabelEn, n.Readable,
            n.Offered.Values.Select(f => new ReportTypeFieldDto(n.Alias + "." + f.Code, f.Code, f.LabelAr, f.LabelEn, ReportTypeEngine.TypeName(f.Type),
                f.Filterable && !ExternalReferenceColumnsIsVirtual(f.Code), f.Groupable, f.Sortable, f.LookupType)).ToList())).ToList());

    private static bool ExternalReferenceColumnsIsVirtual(string code) => Datasets.ExternalReferenceColumns.IsVirtual(code);

    /// <summary>Stored active types whose primary object the caller may read, then one implicit type per readable dataset.</summary>
    public async Task<IReadOnlyList<UsableReportTypeDto>> UsableAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var result = new List<UsableReportTypeDto>();
        var stored = await db.ReportTypes.AsNoTracking().Where(t => t.Status == DefinitionStatuses.Active).OrderBy(t => t.SystemCode).ThenBy(t => t.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var t in stored)
        {
            if (await engine.ResolveAsync(t.Code, user, ct).ConfigureAwait(false) is { } resolved)
            {
                result.Add(Describe(resolved, t.DescriptionAr, t.DescriptionEn));
            }
        }

        foreach (var dataset in ((IReportableMetadata)registry).Datasets.Where(d => !d.JoinOnly).OrderBy(d => d.SystemCode, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            if (await engine.ResolveAsync(ReportTypeRules.ImplicitPrefix + dataset.Code, user, ct).ConfigureAwait(false) is { } resolved)
            {
                result.Add(Describe(resolved, null, null));
            }
        }

        return result;
    }

    public async Task<Result<UsableReportTypeDto>> UsableAsync(string code, ClaimsPrincipal user, CancellationToken ct)
    {
        if (await engine.ResolveAsync(code, user, ct).ConfigureAwait(false) is not { } resolved)
        {
            return Error.NotFound();
        }

        var stored = resolved.Implicit ? null : await db.ReportTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct).ConfigureAwait(false);
        return Describe(resolved, stored?.DescriptionAr, stored?.DescriptionEn);
    }

    public ReportableMetadataDto Metadata() => new(
        ((IReportableMetadata)registry).Datasets.Select(d => new ReportableDatasetDto(d.Code, d.SystemCode, d.LabelAr, d.LabelEn, d.JoinOnly, d.KeyField,
            d.Fields.Select(f => new ReportableFieldDto(f.Code, f.LabelAr, f.LabelEn, ReportTypeEngine.TypeName(f.Type), f.Permission)).ToList())).ToList(),
        registry.Relationships.Select(r => new ReportableRelationshipDto(r.Code, r.ParentDataset, r.ParentField, r.ChildDataset, r.ChildField, r.LabelAr, r.LabelEn)).ToList());

    public async Task<IReadOnlyList<ReportTypeDto>> ListAsync(CancellationToken ct) =>
        (await db.ReportTypes.AsNoTracking().OrderBy(t => t.SystemCode).ThenBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto).ToList();

    public async Task<Result<ReportTypeDto>> GetAsync(Guid id, CancellationToken ct) =>
        await db.ReportTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false) is { } t ? ToDto(t) : Error.NotFound();

    public async Task<Result<ReportTypeDto>> SaveAsync(Guid? id, ReportTypeInput input, uint? version, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await access.CanAsync(user, ReportingPermissions.ReportTypeManage, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (input?.Definition is null)
        {
            return Error.Validation([new FieldError("definition", "validation.required", "validation.required")]);
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 || input.Code.StartsWith(ReportTypeRules.ImplicitPrefix, StringComparison.Ordinal)
            || !input.Code.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch is '.' or '-'))
        {
            errors.Add(new FieldError("code", "reporting.errors.code", "reporting.errors.code"));
        }

        if (string.IsNullOrWhiteSpace(input.LabelAr) || string.IsNullOrWhiteSpace(input.LabelEn) || input.LabelAr.Length > 200 || input.LabelEn.Length > 200
            || (input.DescriptionAr?.Length ?? 0) > 1000 || (input.DescriptionEn?.Length ?? 0) > 1000)
        {
            errors.Add(new FieldError("label", "reporting.errors.name", "reporting.errors.name"));
        }

        errors.AddRange(ReportTypeRules.Validate(input.Definition, registry).Select(k => new FieldError("definition", k, k)));
        var systemCode = registry.Dataset(input.Definition.Nodes.FirstOrDefault()?.Dataset ?? string.Empty)?.SystemCode ?? input.SystemCode;
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var json = JsonSerializer.Serialize(input.Definition, DashboardService.Json);
        ReportType type;
        if (id is { } existingId)
        {
            type = (await db.ReportTypes.FirstOrDefaultAsync(t => t.Id == existingId, ct).ConfigureAwait(false))!;
            if (type is null)
            {
                return Error.NotFound();
            }

            if (version is { } v && type.Version != v)
            {
                return Error.Conflict();
            }

            if (type.Code != input.Code)
            {
                return Error.Validation([new FieldError("code", "reporting.errors.code", "reporting.errors.code")]);
            }

            type.Edited();
        }
        else
        {
            if (await db.ReportTypes.AnyAsync(t => t.Code == input.Code, ct).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("code", "reporting.errors.codeTaken", "reporting.errors.codeTaken")]);
            }

            type = new ReportType(input.Code, systemCode, input.LabelAr.Trim(), input.LabelEn.Trim(), json);
            db.ReportTypes.Add(type);
        }

        type.Update(systemCode, input.LabelAr.Trim(), input.LabelEn.Trim(), input.DescriptionAr, input.DescriptionEn, json, input.Active);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("reporting.report-type.save", true, ReportingPermissions.SystemCode, "reporting.report-type", type.Id.ToString("D"),
            new { type.Code, active = input.Active }), ct).ConfigureAwait(false);
        return ToDto(type);
    }

    /// <summary>Seeds the report types of SPEC-ADDENDUM E1; an unedited seeded type follows the code, an edited one is left alone.</summary>
    public static async Task SeedAsync(ReportingDbContext db, IReportableMetadata metadata, CancellationToken ct)
    {
        var existing = await db.ReportTypes.ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        foreach (var seed in ReportTypeSeeds.All)
        {
            if (ReportTypeRules.Validate(seed.Definition, metadata).Count > 0)
            {
                continue;
            }

            var json = JsonSerializer.Serialize(seed.Definition, DashboardService.Json);
            var hash = DashboardService.Hash(json + seed.LabelAr + seed.LabelEn + seed.DescriptionAr + seed.DescriptionEn);
            if (!existing.TryGetValue(seed.Code, out var type))
            {
                type = new ReportType(seed.Code, seed.SystemCode, seed.LabelAr, seed.LabelEn, json);
                type.Update(seed.SystemCode, seed.LabelAr, seed.LabelEn, seed.DescriptionAr, seed.DescriptionEn, json, true);
                type.Seeded(hash);
                db.ReportTypes.Add(type);
            }
            else if (type.SeedHash is not null && type.SeedHash != hash)
            {
                type.Update(seed.SystemCode, seed.LabelAr, seed.LabelEn, seed.DescriptionAr, seed.DescriptionEn, json, type.Status == DefinitionStatuses.Active);
                type.Seeded(hash);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
