using System.Globalization;
using FluentValidation.Results;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Validation;

/// <summary>
/// Turns a FluentValidation failure into a <see cref="FieldError"/> with a translatable message key.
/// Built-in validators map by convention (<c>NotEmptyValidator</c> → <c>validation.notEmpty</c>);
/// custom rules set <c>WithErrorCode("validation.&lt;module&gt;.&lt;rule&gt;")</c> and the code is used as the key.
/// </summary>
public static class ValidationErrorMapper
{
    private static readonly HashSet<string> InternalPlaceholders =
        new(StringComparer.Ordinal) { "PropertyName", "PropertyValue", "PropertyPath" };

    public static FieldError ToFieldError(ValidationFailure failure)
    {
        var code = string.IsNullOrEmpty(failure.ErrorCode) ? "Unknown" : failure.ErrorCode;
        var messageKey = code.StartsWith("validation.", StringComparison.Ordinal)
            ? code
            : "validation." + ToCamelCase(code.EndsWith("Validator", StringComparison.Ordinal) ? code[..^"Validator".Length] : code);

        var parameters = failure.FormattedMessagePlaceholderValues?
            .Where(p => !InternalPlaceholders.Contains(p.Key))
            .ToDictionary(p => ToCamelCase(p.Key), p => (object?)p.Value);

        return new FieldError(
            ToFieldPath(failure.PropertyName),
            code,
            messageKey,
            parameters is { Count: > 0 } ? parameters : null);
    }

    /// <summary><c>Items[0].NameAr</c> → <c>items[0].nameAr</c>, matching the JSON property names.</summary>
    public static string ToFieldPath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(ToCamelCase));

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLower(value[0], CultureInfo.InvariantCulture) + value[1..];
}
