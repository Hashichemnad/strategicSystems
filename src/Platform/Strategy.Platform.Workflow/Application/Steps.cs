using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Workflow.Application;

/// <summary>Validates a chain before it is saved as a new version (design note §5): every message key is translatable, fields are indexed.</summary>
internal sealed partial class StepValidator(
    IRoleHolderQuery roles,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    IEnumerable<IAccessCatalogueSource> catalogues,
    IEnumerable<NotificationTemplateDefinition> templates)
{
    public const int MaxSteps = 30;

    [GeneratedRegex("^[a-z][a-z0-9-]{0,49}$")]
    private static partial Regex CodePattern();

    public async Task<List<FieldError>> ValidateAsync(ApprovalProcessDefinition process, IReadOnlyList<ApprovalStepDefinition>? steps, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (steps is null || steps.Count == 0)
        {
            errors.Add(Field("steps", "workflow.errors.noSteps"));
            return errors;
        }

        if (steps.Count > MaxSteps)
        {
            errors.Add(Field("steps", "validation.maximumLength", ("maxLength", MaxSteps)));
            return errors;
        }

        var roleKeys = (await roles.ListRolesAsync(ct).ConfigureAwait(false)).Where(r => r.Active).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var recordRoles = catalogues.SelectMany(c => c.RecordRoles).Where(r => r.ResourceType == process.ResourceType).Select(r => r.RoleCode)
            .ToHashSet(StringComparer.Ordinal);
        var templateKeys = templates.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        var variables = ProcessRegistry.VariablesOf(process);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var at = $"steps[{i}]";
            if (step is null)
            {
                errors.Add(Field(at, "validation.notEmpty"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.Code) || !CodePattern().IsMatch(step.Code.Trim()))
            {
                errors.Add(Field($"{at}.code", "validation.codeFormat"));
            }
            else if (!codes.Add(step.Code.Trim()))
            {
                errors.Add(Field($"{at}.code", "workflow.errors.duplicateStepCode"));
            }

            if (string.IsNullOrWhiteSpace(step.NameAr) && string.IsNullOrWhiteSpace(step.NameEn))
            {
                errors.Add(Field($"{at}.nameEn", "validation.atLeastOneLanguage"));
            }

            foreach (var (value, name) in new[] { (step.NameAr, "nameAr"), (step.NameEn, "nameEn") })
            {
                if (value is { Length: > 200 })
                {
                    errors.Add(Field($"{at}.{name}", "validation.maximumLength", ("maxLength", 200)));
                }
            }

            await ValidateRuleAsync(step.Rule, $"{at}.rule", roleKeys, recordRoles, errors, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(step.Condition))
            {
                var compiled = ExpressionEngine.Compile(step.Condition, variables, ExpressionValueType.Boolean);
                errors.AddRange(compiled.Errors.Select(e => ExpressionField($"{at}.condition", e)));
            }

            var actions = step.EffectiveActions;
            if (actions.Any(a => !ApprovalActions.All.Contains(a)))
            {
                errors.Add(Field($"{at}.actions", "validation.unknownReference"));
            }
            else if (!actions.Contains(ApprovalActions.Approve))
            {
                errors.Add(Field($"{at}.actions", "workflow.errors.approveRequired"));
            }

            var customCodes = new HashSet<string>(StringComparer.Ordinal);
            for (var c = 0; c < (step.CustomActions?.Count ?? 0); c++)
            {
                var custom = step.CustomActions![c];
                var cat = $"{at}.customActions[{c}]";
                if (string.IsNullOrWhiteSpace(custom.Code) || !CodePattern().IsMatch(custom.Code) || ApprovalActions.All.Contains(custom.Code)
                    || !customCodes.Add(custom.Code))
                {
                    errors.Add(Field($"{cat}.code", "validation.codeFormat"));
                }

                if (string.IsNullOrWhiteSpace(custom.LabelAr) || string.IsNullOrWhiteSpace(custom.LabelEn))
                {
                    errors.Add(Field($"{cat}.labelEn", "validation.notEmpty"));
                }

                if (!actions.Contains(custom.Outcome))
                {
                    errors.Add(Field($"{cat}.outcome", "validation.unknownReference"));
                }
            }

            if (step.SlaHours is { } sla && (sla < 1 || sla > 8760))
            {
                errors.Add(Field($"{at}.slaHours", "validation.inclusiveBetween", ("from", 1), ("to", 8760)));
            }

            if (step.Escalation is { } escalation)
            {
                if (step.SlaHours is null)
                {
                    errors.Add(Field($"{at}.slaHours", "workflow.errors.escalationNeedsSla"));
                }

                await ValidateRuleAsync(escalation, $"{at}.escalation", roleKeys, recordRoles, errors, ct).ConfigureAwait(false);
            }

            if (!NoApproverBehaviours.All.Contains(step.OnNoApprover))
            {
                errors.Add(Field($"{at}.onNoApprover", "validation.unknownReference"));
            }

            if (step.NotificationTemplate is { Length: > 0 } template && !templateKeys.Contains(template))
            {
                errors.Add(Field($"{at}.notificationTemplate", "validation.unknownReference"));
            }
        }

        return errors;
    }

    private async Task ValidateRuleAsync(
        ApproverRule? rule, string at, HashSet<string> roleKeys, HashSet<string> recordRoles, List<FieldError> errors, CancellationToken ct)
    {
        if (rule is null || !ApproverRuleKinds.All.Contains(rule.Kind))
        {
            errors.Add(Field($"{at}.kind", "validation.unknownReference"));
            return;
        }

        if (rule.Of is not null && rule.Of is not (ApproverRuleSubjects.Record or ApproverRuleSubjects.Submitter))
        {
            errors.Add(Field($"{at}.of", "validation.unknownReference"));
        }

        switch (rule.Kind)
        {
            case ApproverRuleKinds.Role when rule.RoleKey is null || !roleKeys.Contains(rule.RoleKey):
                errors.Add(Field($"{at}.roleKey", "validation.unknownReference"));
                break;
            case ApproverRuleKinds.Employee when rule.EmployeeId is not { } employee || await employees.GetAsync(employee, ct).ConfigureAwait(false) is null:
                errors.Add(Field($"{at}.employeeId", "validation.unknownReference"));
                break;
            case ApproverRuleKinds.HeadOfUnit when rule.UnitId is { } unit && await org.GetUnitAsync(unit, ct).ConfigureAwait(false) is null:
                errors.Add(Field($"{at}.unitId", "validation.unknownReference"));
                break;
            case ApproverRuleKinds.RecordRole when rule.RoleCode is null || !recordRoles.Contains(rule.RoleCode):
                errors.Add(Field($"{at}.roleCode", "validation.unknownReference"));
                break;
            case ApproverRuleKinds.TeamRole when string.IsNullOrWhiteSpace(rule.RoleCode):
                errors.Add(Field($"{at}.roleCode", "validation.notEmpty"));
                break;
        }
    }

    internal static FieldError ExpressionField(string field, ExpressionError error)
    {
        var parameters = new Dictionary<string, object?>(error.Params ?? new Dictionary<string, object?>(), StringComparer.Ordinal)
        {
            ["position"] = error.Position,
        };
        return new FieldError(field, error.Code, error.MessageKey, parameters);
    }

    private static FieldError Field(string field, string key, params (string Key, object? Value)[] parameters) =>
        new(field, key, key, parameters.Length == 0 ? null : parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
}

/// <summary>Condition variable values: validation of module values, JSON snapshot and typed read-back.</summary>
internal static class VariableValues
{
    public static Result<Dictionary<string, object?>> FromModule(ApprovalProcessDefinition process, IReadOnlyDictionary<string, object?> values)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, raw) in values)
        {
            var declared = process.Variables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            var value = ExpressionEngine.Normalise(raw);
            if (declared is null || (value is not null && !Fits(declared.Type, value)))
            {
                return Error.Rule("workflow.variable.invalid", "workflow.errors.variableInvalid", new Dictionary<string, object?> { ["name"] = name });
            }

            result[declared.Name] = value;
        }

        foreach (var variable in process.Variables.Where(v => !result.ContainsKey(v.Name)))
        {
            result[variable.Name] = null;
        }

        return result;
    }

    private static bool Fits(ExpressionValueType type, object value) => type switch
    {
        ExpressionValueType.Number => value is decimal,
        ExpressionValueType.Text => value is string,
        ExpressionValueType.Boolean => value is bool,
        ExpressionValueType.Date => value is DateOnly,
        _ => true,
    };

    public static string ToJson(IReadOnlyDictionary<string, object?> values) =>
        JsonSerializer.Serialize(values.ToDictionary(v => v.Key, v => v.Value is DateOnly d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : v.Value));

    public static Dictionary<string, object?> FromJson(string json, IReadOnlyList<ExpressionVariable> variables)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var type = variables.FirstOrDefault(v => string.Equals(v.Name, property.Name, StringComparison.OrdinalIgnoreCase))?.Type ?? ExpressionValueType.Any;
            result[property.Name] = Read(property.Value, type);
        }

        return result;
    }

    /// <summary>Reads a JSON value as the declared type (preview input and snapshots); a value that does not fit becomes null.</summary>
    public static object? Read(JsonElement element, ExpressionValueType type) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.String when type == ExpressionValueType.Date =>
            DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
        JsonValueKind.String when type == ExpressionValueType.Number =>
            decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null,
        JsonValueKind.String when type == ExpressionValueType.Boolean => bool.TryParse(element.GetString(), out var flag) ? flag : null,
        JsonValueKind.String => element.GetString(),
        _ => null,
    };
}
