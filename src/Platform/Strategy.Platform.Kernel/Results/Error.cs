namespace Strategy.Platform.Kernel.Results;

/// <summary>Category of an error; decides the HTTP status in the API layer.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    BusinessRule,
    Disabled,
    Unexpected,
}

/// <summary>
/// A failure with a stable machine <see cref="Code"/> and a translatable <see cref="MessageKey"/>.
/// The server never returns sentences; the client renders <see cref="MessageKey"/> with <see cref="Params"/>.
/// </summary>
public sealed record Error(
    string Code,
    string MessageKey,
    ErrorKind Kind,
    IReadOnlyDictionary<string, object?>? Params = null,
    IReadOnlyList<FieldError>? Fields = null)
{
    public static Error Validation(IReadOnlyList<FieldError> fields) =>
        new("validation.failed", "errors.validationFailed", ErrorKind.Validation, Fields: fields);

    public static Error NotFound(string code = "record.notFound") =>
        new(code, "errors.notFound", ErrorKind.NotFound);

    public static Error Conflict(string code = "record.concurrencyConflict", string messageKey = "errors.concurrencyConflict") =>
        new(code, messageKey, ErrorKind.Conflict);

    public static Error Forbidden(string code = "access.forbidden") =>
        new(code, "errors.forbidden", ErrorKind.Forbidden);

    public static Error Rule(string code, string messageKey, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(code, messageKey, ErrorKind.BusinessRule, parameters);
}

/// <summary>A validation failure on one field.</summary>
public sealed record FieldError(
    string Field,
    string Code,
    string MessageKey,
    IReadOnlyDictionary<string, object?>? Params = null);
