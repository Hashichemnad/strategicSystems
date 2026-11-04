using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Requests.Application;

public sealed record TypeSummaryDto(Guid Id, string Code, string SystemCode, string Kind, string? NameAr, string? NameEn, string? Category, string Icon, bool Active,
    bool ShowInCatalogue, bool Embeddable, int Fields, int Stages, int Requests, uint Version);

public sealed record TypeDetailDto(
    Guid Id,
    string Code,
    string SystemCode,
    string Kind,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Category,
    string Icon,
    string NumberPrefix,
    Guid? OwningUnitId,
    SubmitterRule Submitters,
    FormSchema Form,
    IReadOnlyList<StageDefinition> Stages,
    IReadOnlyList<PdfTemplate> Templates,
    bool Active,
    bool ShowInCatalogue,
    bool Embeddable,
    int SchemaVersion,
    int Sort,
    uint Version,
    SimpleStagesInput? Simple,
    TypeOptionsDto Options);

public sealed record RequestOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record TypeOptionsDto(IReadOnlyList<string> FieldTypes, IReadOnlyList<RequestOptionDto> LookupTypes, IReadOnlyList<RequestOptionDto> RecordTypes,
    IReadOnlyList<RequestOptionDto> Roles, IReadOnlyList<RequestOptionDto> Categories);

/// <summary>The form builder's input. Custom types: everything; built-in types: labels, extra fields, catalogue / submitter settings, SLA.</summary>
public sealed record TypeInput(
    string? Code,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Category,
    string? Icon,
    string? NumberPrefix,
    Guid? OwningUnitId,
    SubmitterRule? Submitters,
    FormSchema? Form,
    SimpleStagesInput? Stages,
    IReadOnlyList<StageDefinition>? BuiltInStages,
    IReadOnlyList<PdfTemplate>? Templates,
    bool Active = true,
    bool ShowInCatalogue = true,
    bool Embeddable = false,
    int Sort = 100,
    uint Version = 0);

/// <summary>The simple stage template of custom types (ADR 0080): approval and / or processing.</summary>
public sealed record SimpleStagesInput(
    bool Approval = true,
    string? ApprovalNameAr = null,
    string? ApprovalNameEn = null,
    bool Processing = false,
    string? ProcessingNameAr = null,
    string? ProcessingNameEn = null,
    IReadOnlyList<string>? ProcessorRoleKeys = null,
    int? SlaHours = null,
    FormSchema? CompletionForm = null,
    bool AllowReturn = true,
    bool AllowReject = true);

/// <summary>Settings → Request types (docs/design/requests.md §2.1–2.2, §2.8): list, create, edit, preview.</summary>
internal sealed partial class TypeAdministration(
    RequestsDbContext db,
    RequestTypeCache cache,
    IEnumerable<RequestTypeRegistration> registrations,
    IEnumerable<LookupTypeDefinition> lookupTypes,
    ILinkRegistry links,
    IRoleHolderQuery roles,
    ILookupService lookups)
{
    public async Task<IReadOnlyList<TypeSummaryDto>> ListAsync(CancellationToken ct)
    {
        var counts = await db.Requests.AsNoTracking().GroupBy(r => r.TypeId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var rows = await db.Types.AsNoTracking().OrderBy(t => t.Sort).ThenBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(t => new TypeSummaryDto(t.Id, t.Code, t.SystemCode, t.Kind, t.NameAr, t.NameEn, t.Category, t.Icon, t.Active, t.ShowInCatalogue, t.Embeddable,
            t.Form.AllFields.Count(), t.Stages.Count, counts.FirstOrDefault(c => c.Key == t.Id)?.Count ?? 0, t.Version)).ToList();
    }

    public async Task<Result<TypeDetailDto>> GetAsync(string code, CancellationToken ct) =>
        await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct).ConfigureAwait(false) is { } type
            ? await ToDtoAsync(type, ct).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<TypeOptionsDto> OptionsAsync(CancellationToken ct)
    {
        var record = (await links.TypesAsync(cancellationToken: ct).ConfigureAwait(false)).Select(t => new RequestOptionDto(t.ResourceType, t.NameAr, t.NameEn)).ToList();
        var roleOptions = (await roles.ListRolesAsync(ct).ConfigureAwait(false)).Where(r => r.Active).Select(r => new RequestOptionDto(r.Key, r.NameAr, r.NameEn)).ToList();
        var categories = (await lookups.GetValuesAsync(RequestPermissions.CategoryLookup, cancellationToken: ct).ConfigureAwait(false))
            .Select(v => new RequestOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        return new TypeOptionsDto(FieldTypes.All, lookupTypes.Select(l => new RequestOptionDto(l.Code, l.NameAr, l.NameEn)).OrderBy(o => o.Code).ToList(), record, roleOptions,
            categories);
    }

    public async Task<Result<TypeDetailDto>> CreateAsync(TypeInput input, CancellationToken ct)
    {
        var code = input.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CustomCode().IsMatch(code) || code.Contains('.', StringComparison.Ordinal))
        {
            return Error.Validation([new FieldError("code", "requests.errors.typeCode", "requests.errors.typeCode")]);
        }

        if (await db.Types.AnyAsync(t => t.Code == code, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("code", "requests.errors.typeCodeTaken", "requests.errors.typeCodeTaken")]);
        }

        var type = RequestType.CreateCustom(code, RequestPermissions.SystemCode);
        var applied = await ApplyAsync(type, input, ct).ConfigureAwait(false);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        db.Types.Add(type);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.Invalidate();
        return await ToDtoAsync(type, ct).ConfigureAwait(false);
    }

    public async Task<Result<TypeDetailDto>> UpdateAsync(string code, TypeInput input, CancellationToken ct)
    {
        var type = await db.Types.FirstOrDefaultAsync(t => t.Code == code, ct).ConfigureAwait(false);
        if (type is null)
        {
            return Error.NotFound();
        }

        db.Entry(type).Property(x => x.Version).OriginalValue = input.Version;
        var applied = await ApplyAsync(type, input, ct).ConfigureAwait(false);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        cache.Invalidate();
        return await ToDtoAsync(type, ct).ConfigureAwait(false);
    }

    private async Task<Result> ApplyAsync(RequestType type, TypeInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.notEmpty", "validation.notEmpty"));
        }

        var prefix = input.NumberPrefix?.Trim().ToUpperInvariant();
        if (prefix is not null && !Prefix().IsMatch(prefix))
        {
            errors.Add(new FieldError("numberPrefix", "requests.errors.prefix", "requests.errors.prefix"));
        }

        var form = input.Form ?? type.Form;
        var known = (await links.TypesAsync(cancellationToken: ct).ConfigureAwait(false)).Select(t => t.ResourceType).ToHashSet(StringComparer.Ordinal);
        errors.AddRange(FormSchemaValidator.Validate(form, "form", known));
        foreach (var (field, index) in form.AllFields.Select((f, i) => (f, i)))
        {
            if (field.Type is FieldTypes.Picklist or FieldTypes.MultiPicklist && lookupTypes.All(l => l.Code != field.LookupType))
            {
                errors.Add(new FieldError($"form.fields[{index}].lookupType", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        if (type.IsBuiltIn)
        {
            // Code-defined fields stay exactly as the module declared them.
            var codeFields = Registration(type.Code)?.Form.AllFields.ToList() ?? [];
            foreach (var field in codeFields)
            {
                var kept = form.Find(field.Code);
                if (kept is null || kept.Type != field.Type || !kept.System)
                {
                    errors.Add(new FieldError("form", "requests.errors.systemField", "requests.errors.systemField",
                        new Dictionary<string, object?> { ["field"] = field.Code }));
                }
            }
        }

        IReadOnlyList<StageDefinition> stages = type.Stages;
        if (!type.IsBuiltIn)
        {
            var built = BuildStages(type.Code, input.Stages ?? new SimpleStagesInput(), errors);
            stages = built;
        }
        else if (input.BuiltInStages is { } edited)
        {
            // Built-in stages: only names, SLA and processor roles change.
            stages = type.Stages.Select(s => edited.FirstOrDefault(e => e.Code == s.Code) is { } e
                ? s with { NameAr = e.NameAr ?? s.NameAr, NameEn = e.NameEn ?? s.NameEn, SlaHours = e.SlaHours is > 0 and <= 2000 ? e.SlaHours : null, ProcessorRoleKeys = e.ProcessorRoleKeys }
                : s).ToList();
        }

        var templates = input.Templates ?? type.Templates;
        foreach (var (template, i) in templates.Select((t, i) => (t, i)))
        {
            if (!TemplateCode().IsMatch(template.Code ?? string.Empty) || (template.HtmlAr?.Length ?? 0) > TemplateRenderer.MaxTemplateLength
                || (template.HtmlEn?.Length ?? 0) > TemplateRenderer.MaxTemplateLength || template.Audience is not ("requester" or "processor" or "admin"))
            {
                errors.Add(new FieldError($"templates[{i}]", "requests.errors.template", "requests.errors.template"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        type.Edit(input.NameAr, input.NameEn, input.DescriptionAr, input.DescriptionEn, input.Category, input.Icon, prefix ?? type.NumberPrefix, input.OwningUnitId,
            input.Submitters ?? new SubmitterRule(), input.Active, input.ShowInCatalogue, input.Embeddable, input.Sort);
        type.SetForm(form);
        type.SetStages(stages);
        type.SetTemplates(templates);
        return Result.Success();
    }

    /// <summary>The simple template: review (approval, process <c>requests.type.&lt;code&gt;</c>) and / or processing (ADR 0080).</summary>
    public static List<StageDefinition> BuildStages(string code, SimpleStagesInput input, List<FieldError> errors)
    {
        var stages = new List<StageDefinition>();
        if (!input.Approval && !input.Processing)
        {
            errors.Add(new FieldError("stages", "requests.errors.noStage", "requests.errors.noStage"));
            return stages;
        }

        if (input.Approval)
        {
            stages.Add(new StageDefinition
            {
                Code = "review",
                Kind = StageKinds.Approval,
                NameAr = input.ApprovalNameAr ?? "المراجعة والاعتماد",
                NameEn = input.ApprovalNameEn ?? "Review and approval",
                ProcessKey = RequestPermissions.ProcessKeyOf(code),
            });
        }

        if (input.Processing)
        {
            var form = input.CompletionForm ?? new FormSchema();
            errors.AddRange(FormSchemaValidator.Validate(form, "stages.completionForm"));
            if (form.AllFields.Any(f => f.Type == FieldTypes.File))
            {
                // Mini-form files are attached to the request first.
            }

            var actions = new List<StageAction>
            {
                new() { Code = "complete", Kind = ActionKinds.Complete, LabelAr = "إكمال الطلب", LabelEn = "Complete", Form = form.IsEmpty ? null : form },
            };
            if (input.AllowReturn)
            {
                actions.Add(new StageAction { Code = "return", Kind = ActionKinds.Return, LabelAr = "إعادة لمقدم الطلب", LabelEn = "Return to requester", Tone = "secondary" });
            }

            if (input.AllowReject)
            {
                actions.Add(new StageAction { Code = "reject", Kind = ActionKinds.Reject, LabelAr = "رفض", LabelEn = "Reject", Tone = "danger" });
            }

            if (input.SlaHours is < 1 or > 2000)
            {
                errors.Add(new FieldError("stages.slaHours", "requests.errors.sla", "requests.errors.sla"));
            }

            stages.Add(new StageDefinition
            {
                Code = "processing",
                Kind = StageKinds.Processing,
                NameAr = input.ProcessingNameAr ?? "المعالجة",
                NameEn = input.ProcessingNameEn ?? "Processing",
                ProcessorRoleKeys = input.ProcessorRoleKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList() ?? [],
                SlaHours = input.SlaHours,
                Actions = actions,
            });
        }

        return stages;
    }

    /// <summary>The simple-template settings of saved custom stages (for the builder).</summary>
    public static SimpleStagesInput SimpleOf(IReadOnlyList<StageDefinition> stages)
    {
        var approval = stages.FirstOrDefault(s => s.Kind == StageKinds.Approval);
        var processing = stages.FirstOrDefault(s => s.Kind == StageKinds.Processing);
        return new SimpleStagesInput(approval is not null, approval?.NameAr, approval?.NameEn, processing is not null, processing?.NameAr, processing?.NameEn,
            processing?.ProcessorRoleKeys, processing?.SlaHours, processing?.Actions.FirstOrDefault(a => a.Kind == ActionKinds.Complete)?.Form,
            processing?.Actions.Any(a => a.Kind == ActionKinds.Return) ?? true, processing?.Actions.Any(a => a.Kind == ActionKinds.Reject) ?? true);
    }

    private RequestTypeRegistration? Registration(string code) => registrations.FirstOrDefault(r => r.Code == code);

    private async Task<TypeDetailDto> ToDtoAsync(RequestType t, CancellationToken ct) =>
        new(t.Id, t.Code, t.SystemCode, t.Kind, t.NameAr, t.NameEn, t.DescriptionAr, t.DescriptionEn, t.Category, t.Icon, t.NumberPrefix, t.OwningUnitId, t.Submitters,
            t.Form, t.Stages, t.Templates, t.Active, t.ShowInCatalogue, t.Embeddable, t.SchemaVersion, t.Sort, t.Version, t.IsBuiltIn ? null : SimpleOf(t.Stages),
            await OptionsAsync(ct).ConfigureAwait(false));

    [GeneratedRegex("^[a-z][a-z0-9-]{1,40}$")]
    private static partial Regex CustomCode();

    [GeneratedRegex("^[A-Z][A-Z0-9]{0,9}$")]
    private static partial Regex Prefix();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,40}$")]
    private static partial Regex TemplateCode();
}

/// <summary>
/// Seeds every built-in type once and keeps its code-defined fields and stages in step with the module's registration; the admin's extra
/// fields, labels and settings are kept (ADR 0080).
/// </summary>
internal sealed class RequestTypeSeeder : IDatabaseSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = (RequestsDbContext)services.GetService(typeof(RequestsDbContext))!;
        var registrations = (IEnumerable<RequestTypeRegistration>)services.GetService(typeof(IEnumerable<RequestTypeRegistration>))!;
        foreach (var registration in registrations)
        {
            var existing = await db.Types.FirstOrDefaultAsync(t => t.Code == registration.Code, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                db.Types.Add(RequestType.FromRegistration(registration));
                continue;
            }

            existing.SyncRegistration(registration, Merge(registration.Form, existing.Form));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ((RequestTypeCache)services.GetService(typeof(RequestTypeCache))!).Invalidate();
    }

    /// <summary>The code sections and fields as declared, with the admin's labels on them, then the admin's extra fields.</summary>
    internal static FormSchema Merge(FormSchema code, FormSchema saved)
    {
        var codeFields = code.AllFields.Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
        var sections = code.Sections.Select(s => s with
        {
            System = true,
            Fields = s.Fields.Select(f => saved.Find(f.Code) is { } mine
                ? f with { System = true, LabelAr = mine.LabelAr ?? f.LabelAr, LabelEn = mine.LabelEn ?? f.LabelEn, HelpAr = mine.HelpAr ?? f.HelpAr, HelpEn = mine.HelpEn ?? f.HelpEn }
                : f with { System = true }).ToList(),
        }).ToList();
        foreach (var section in saved.Sections)
        {
            var extra = section.Fields.Where(f => !codeFields.Contains(f.Code) && !f.System).ToList();
            if (extra.Count == 0)
            {
                continue;
            }

            var index = sections.FindIndex(s => s.Code == section.Code);
            if (index >= 0)
            {
                sections[index] = sections[index] with { Fields = [.. sections[index].Fields, .. extra] };
            }
            else
            {
                sections.Add(section with { System = false, Fields = extra });
            }
        }

        return new FormSchema { Sections = sections };
    }
}
