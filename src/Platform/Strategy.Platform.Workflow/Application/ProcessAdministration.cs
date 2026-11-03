using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

public sealed record ProcessSummaryDto(
    string Key, string SystemCode, string? SystemNameAr, string? SystemNameEn, string NameAr, string NameEn, string ResourceType, int StepCount, int VersionNo,
    DateTimeOffset? ChangedAt, int InProgress);

public sealed record VariableDto(string Name, string Type, string? LabelAr, string? LabelEn, bool BuiltIn);

public sealed record OptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record VersionSummaryDto(int VersionNo, string? Note, DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName, int StepCount);

public sealed record ProcessDetailDto(
    string Key, string SystemCode, string? SystemNameAr, string? SystemNameEn, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn,
    string ResourceType, int VersionNo, IReadOnlyList<ApprovalStepDefinition> Steps, IReadOnlyList<VersionSummaryDto> Versions, IReadOnlyList<VariableDto> Variables,
    IReadOnlyList<OptionDto> Roles, IReadOnlyList<OptionDto> RecordRoles, IReadOnlyList<OptionDto> Templates, IReadOnlyList<string> Functions,
    IReadOnlyList<OptionDto> BusinessHours);

public sealed record SaveProcessInput(IReadOnlyList<ApprovalStepDefinition>? Steps, string? Note, int? BaseVersionNo);

public sealed record ConditionInput(string? Condition);

public sealed record ConditionCheckDto(bool Valid, IReadOnlyList<Kernel.Results.FieldError> Errors, IReadOnlyList<string> Variables);

public sealed record PreviewInput(IReadOnlyList<ApprovalStepDefinition>? Steps, Guid? SubmitterEmployeeId, Guid? UnitId, Dictionary<string, JsonElement>? Variables);

public sealed record PreviewApproverDto(Guid UserId, string? NameAr, string? NameEn);

public sealed record PreviewStepDto(
    int Index, string Code, string? NameAr, string? NameEn, string Outcome, bool? ConditionResult, string? ConditionError,
    IReadOnlyList<PreviewApproverDto> Approvers, IReadOnlyList<string> Notes);

public sealed record PreviewFactDto(string Name, string? Value, string? DisplayAr, string? DisplayEn);

public sealed record PreviewDto(IReadOnlyList<PreviewStepDto> Steps, IReadOnlyList<PreviewFactDto> Facts, string Result);

public static class PreviewOutcomes
{
    public const string Included = "included";
    public const string Skipped = "skipped";
    public const string NoApprover = "noApprover";
    public const string Blocked = "blocked";
}

public sealed record ApprovalSettingsDto(ApprovalSettings Value, uint Version, IReadOnlyList<OptionDto> Levels);

public sealed record ApprovalSettingsInput(string? SectionLevelCode, string? DepartmentLevelCode, uint Version);

/// <summary>Approval Settings centre (design note §11–§12): process list, editor, versions, condition check, live preview, level settings.</summary>
internal sealed class ProcessAdministration(
    WorkflowDbContext db,
    ProcessRegistry registry,
    StepValidator validator,
    ApproverResolver resolver,
    OrgRelations relations,
    IRoleHolderQuery roles,
    IEmployeeDirectory employees,
    IUserDirectory users,
    ISettingsStore settings,
    ISettingsEditor settingsEditor,
    IEnumerable<IAccessCatalogueSource> catalogues,
    IEnumerable<NotificationTemplateDefinition> templates,
    Calendar.IBusinessHoursCalculator businessHours,
    IClock clock)
{
    private SystemDefinition? SystemOf(string code) => catalogues.Select(c => c.System).FirstOrDefault(s => s.Code == code);

    public async Task<IReadOnlyList<ProcessSummaryDto>> ListAsync(CancellationToken ct)
    {
        var keys = registry.All.Select(p => p.Key).ToList();
        var versions = await db.Versions.AsNoTracking().Where(v => keys.Contains(v.ProcessKey))
            .GroupBy(v => v.ProcessKey).Select(g => g.OrderByDescending(v => v.VersionNo).Select(v => new { v.ProcessKey, v.VersionNo, v.CreatedAt, Steps = v.Steps.Count }).First())
            .ToListAsync(ct).ConfigureAwait(false);
        var running = await db.Instances.AsNoTracking().Where(i => keys.Contains(i.ProcessKey) && i.Status == ApprovalInstanceStatuses.InProgress)
            .GroupBy(i => i.ProcessKey).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        return registry.All.OrderBy(p => SystemOf(p.SystemCode)?.Sort ?? 999).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p =>
        {
            var version = versions.FirstOrDefault(v => v.ProcessKey == p.Key);
            var system = SystemOf(p.SystemCode);
            return new ProcessSummaryDto(p.Key, p.SystemCode, system?.NameAr, system?.NameEn, p.NameAr, p.NameEn, p.ResourceType, version?.Steps ?? 0,
                version?.VersionNo ?? 0, version?.CreatedAt, running.FirstOrDefault(r => r.Key == p.Key)?.Count ?? 0);
        }).ToList();
    }

    public async Task<Result<ProcessDetailDto>> GetAsync(string key, int? versionNo, CancellationToken ct)
    {
        if (registry.Find(key) is not { } process)
        {
            return Error.NotFound();
        }

        var all = await db.Versions.AsNoTracking().Include(v => v.Steps).Where(v => v.ProcessKey == key).OrderByDescending(v => v.VersionNo)
            .ToListAsync(ct).ConfigureAwait(false);
        var shown = versionNo is { } wanted ? all.FirstOrDefault(v => v.VersionNo == wanted) : all.FirstOrDefault();
        if (versionNo is not null && shown is null)
        {
            return Error.NotFound();
        }

        var authors = await users.GetManyAsync(all.Where(v => v.CreatedBy is not null).Select(v => v.CreatedBy!.Value).Distinct().ToList(), ct).ConfigureAwait(false);
        var authorEmployees = await employees.GetManyAsync(authors.Values.Where(u => u.EmployeeId is not null).Select(u => u.EmployeeId!.Value).ToList(), ct)
            .ConfigureAwait(false);
        string? AuthorName(Guid? id) => id is { } u && authors.TryGetValue(u, out var user)
            ? (user.EmployeeId is { } e && authorEmployees.TryGetValue(e, out var emp) ? emp.NameEn ?? emp.NameAr : user.UserName)
            : null;

        var system = SystemOf(process.SystemCode);
        var roleOptions = (await roles.ListRolesAsync(ct).ConfigureAwait(false)).Where(r => r.Active)
            .Select(r => new OptionDto(r.Key, $"{SystemOf(r.SystemCode)?.NameAr ?? r.SystemCode} — {r.NameAr}", $"{SystemOf(r.SystemCode)?.NameEn ?? r.SystemCode} — {r.NameEn}"))
            .ToList();
        var recordRoleOptions = catalogues.SelectMany(c => c.RecordRoles).Where(r => r.ResourceType == process.ResourceType)
            .Select(r => new OptionDto(r.RoleCode, r.NameAr, r.NameEn)).ToList();
        var templateOptions = templates.Where(t => t.SystemCode == WorkflowPermissions.SystemCode || t.SystemCode == process.SystemCode)
            .Select(t => new OptionDto(t.Key, t.NameAr, t.NameEn)).ToList();
        var variables = BuiltInVariables.All.Select(v => ToDto(v, true)).Concat(process.Variables.Select(v => ToDto(v, false))).ToList();
        return new ProcessDetailDto(process.Key, process.SystemCode, system?.NameAr, system?.NameEn, process.NameAr, process.NameEn, process.DescriptionAr,
            process.DescriptionEn, process.ResourceType, shown?.VersionNo ?? 0,
            shown?.OrderedSteps.Select(s => s.ToDefinition()).ToList() ?? [.. process.DefaultSteps],
            all.Select(v => new VersionSummaryDto(v.VersionNo, v.Note, v.CreatedAt, v.CreatedBy, AuthorName(v.CreatedBy), v.Steps.Count)).ToList(),
            variables, roleOptions, recordRoleOptions, templateOptions, ExpressionFunctions.Names,
            (await businessHours.ListActiveAsync(ct).ConfigureAwait(false))
                .Select(b => new OptionDto(b.Id.ToString("D"), b.NameAr ?? b.NameEn, b.NameEn ?? b.NameAr)).ToList());
    }

    private static VariableDto ToDto(ExpressionVariable v, bool builtIn) =>
        new(v.Name, v.Type.ToString().ToLowerInvariant(), v.LabelAr, v.LabelEn, builtIn);

    public async Task<Result<ProcessDetailDto>> SaveAsync(string key, SaveProcessInput input, CancellationToken ct)
    {
        if (registry.Find(key) is not { } process)
        {
            return Error.NotFound();
        }

        var errors = await validator.ValidateAsync(process, input.Steps, ct).ConfigureAwait(false);
        if (input.Note is { Length: > 1000 })
        {
            errors.Add(new FieldError("note", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 1000 }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var latest = await db.Versions.AsNoTracking().Where(v => v.ProcessKey == key).MaxAsync(v => (int?)v.VersionNo, ct).ConfigureAwait(false) ?? 0;
        if ((input.BaseVersionNo ?? 0) != latest)
        {
            return Error.Conflict("workflow.process.versionConflict", "workflow.errors.versionConflict");
        }

        db.Versions.Add(ProcessVersion.Create(key, latest + 1, input.Note, input.Steps!));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("workflow.process.versionConflict", "workflow.errors.versionConflict");
        }

        return await GetAsync(key, null, ct).ConfigureAwait(false);
    }

    public Result<ConditionCheckDto> CheckCondition(string key, ConditionInput input)
    {
        if (registry.Find(key) is not { } process)
        {
            return Error.NotFound();
        }

        var compiled = ExpressionEngine.Compile(input.Condition, ProcessRegistry.VariablesOf(process), ExpressionValueType.Boolean);
        return new ConditionCheckDto(
            compiled.Success,
            compiled.Errors.Select(e => StepValidator.ExpressionField("condition", e)).ToList(),
            compiled.Expression?.Variables.Order(StringComparer.Ordinal).ToList() ?? []);
    }

    /// <summary>"Who would approve this sample record": evaluates the (unsaved) steps for a sample submitter, unit and values. Nothing is stored.</summary>
    public async Task<Result<PreviewDto>> PreviewAsync(string key, PreviewInput input, CancellationToken ct)
    {
        if (registry.Find(key) is not { } process)
        {
            return Error.NotFound();
        }

        if (input.Steps is null || input.Steps.Count == 0 || input.Steps.Count > StepValidator.MaxSteps)
        {
            return Error.Validation([new FieldError("steps", "workflow.errors.noSteps", "workflow.errors.noSteps")]);
        }

        var submitter = input.SubmitterEmployeeId is { } s ? await employees.GetAsync(s, ct).ConfigureAwait(false) : null;
        if (input.SubmitterEmployeeId is not null && submitter is null)
        {
            return Error.Validation([new FieldError("submitterEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        var submitterUser = submitter is null ? null : (await users.GetByEmployeesAsync([submitter.Id], ct).ConfigureAwait(false)).FirstOrDefault(u => u.Active);
        var subject = new ApprovalSubject(process.ResourceType, Guid.Empty, null, null, null, submitter?.Id, submitterUser?.Id, input.UnitId ?? submitter?.UnitId);
        var context = new ResolutionContext(process, subject, submitter?.Id, await settings.GetAsync<ApprovalSettings>(ct).ConfigureAwait(false));
        var variables = await relations.BuiltInsAsync(context, ct).ConfigureAwait(false);
        foreach (var variable in process.Variables)
        {
            variables[variable.Name] = input.Variables is not null
                && input.Variables.FirstOrDefault(v => string.Equals(v.Key, variable.Name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } provided
                ? VariableValues.Read(provided.Value, variable.Type)
                : null;
        }

        var processVariables = ProcessRegistry.VariablesOf(process);
        var result = new List<PreviewStepDto>();
        var blocked = false;
        for (var i = 0; i < input.Steps.Count; i++)
        {
            var step = input.Steps[i];
            if (step?.Rule is null)
            {
                continue;
            }

            bool? conditionResult = null;
            string? conditionError = null;
            if (!string.IsNullOrWhiteSpace(step.Condition))
            {
                var compiled = ExpressionEngine.Compile(step.Condition, processVariables, ExpressionValueType.Boolean);
                var evaluation = compiled.Expression?.Evaluate(variables, clock.Today);
                if (evaluation is { Success: true })
                {
                    conditionResult = evaluation.IsTrue;
                }
                else
                {
                    conditionError = evaluation?.Error?.MessageKey ?? (compiled.Errors.Count > 0 ? compiled.Errors[0].MessageKey : null);
                }
            }

            if (conditionResult == false)
            {
                result.Add(new PreviewStepDto(i, step.Code, step.NameAr, step.NameEn, PreviewOutcomes.Skipped, false, null, [], []));
                continue;
            }

            var resolution = await resolver.ResolveAsync(step.Rule, context, ct).ConfigureAwait(false);
            var notes = resolution.Notes.ToList();
            var approvers = resolution.Approvers.ToList();
            if (step.ExcludeSubmitter && submitterUser is not null && approvers.RemoveAll(a => a.UserId == submitterUser.Id) > 0)
            {
                notes.Add("workflow.preview.submitterExcluded");
            }

            var outcome = approvers.Count > 0 ? PreviewOutcomes.Included
                : step.OnNoApprover == NoApproverBehaviours.Skip ? PreviewOutcomes.NoApprover : PreviewOutcomes.Blocked;
            blocked |= outcome == PreviewOutcomes.Blocked;
            result.Add(new PreviewStepDto(i, step.Code, step.NameAr, step.NameEn, outcome, conditionResult, conditionError,
                approvers.Select(a => new PreviewApproverDto(a.UserId, a.NameAr, a.NameEn)).ToList(), notes));
        }

        var ids = variables.Values.OfType<string>().Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var people = await employees.GetManyAsync(ids, ct).ConfigureAwait(false);
        var facts = BuiltInVariables.All.Select(v =>
        {
            var value = variables.GetValueOrDefault(v.Name);
            var text = value switch { null => null, bool b => b ? "true" : "false", _ => value.ToString() };
            var person = text is not null && Guid.TryParse(text, out var id) ? people.GetValueOrDefault(id) : null;
            return new PreviewFactDto(v.Name, text, person?.NameAr ?? person?.NameEn, person?.NameEn ?? person?.NameAr);
        }).ToList();
        var overall = blocked ? PreviewOutcomes.Blocked : result.Any(r => r.Outcome == PreviewOutcomes.Included) ? PreviewOutcomes.Included : "autoApproved";
        return new PreviewDto(result, facts, overall);
    }

    public async Task<ApprovalSettingsDto> GetSettingsAsync(IReadOnlyList<OptionDto> levels, CancellationToken ct)
    {
        var document = await settingsEditor.GetDocumentAsync<ApprovalSettings>(ct).ConfigureAwait(false);
        return new ApprovalSettingsDto(document.Value, document.Version, levels);
    }

    public async Task<Result<ApprovalSettingsDto>> SaveSettingsAsync(ApprovalSettingsInput input, IReadOnlyList<OptionDto> levels, CancellationToken ct)
    {
        var value = new ApprovalSettings(input.SectionLevelCode?.Trim() ?? string.Empty, input.DepartmentLevelCode?.Trim() ?? string.Empty);
        var codes = levels.Select(l => l.Code).ToList();
        var saved = await settingsEditor.SaveAsync(value, input.Version, v => ApprovalSettings.Validate(v, codes), ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : new ApprovalSettingsDto(saved.Value.Value, saved.Value.Version, levels);
    }
}

/// <summary>Seeds version 1 of every registered process that has none (ADR 0024). Code defaults never overwrite a saved chain.</summary>
internal sealed class WorkflowSeeder : IDatabaseSeeder
{
    public int Order => 40;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = (WorkflowDbContext)services.GetService(typeof(WorkflowDbContext))!;
        var registry = (ProcessRegistry)services.GetService(typeof(ProcessRegistry))!;
        var existing = await db.Versions.AsNoTracking().Select(v => v.ProcessKey).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var process in registry.All.Where(p => !existing.Contains(p.Key) && p.DefaultSteps.Count > 0))
        {
            db.Versions.Add(ProcessVersion.Create(process.Key, 1, null, process.DefaultSteps));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
