using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Problems;

/// <summary>
/// RFC 9457 Problem Details for every error, with a stable <c>code</c>, a translatable <c>messageKey</c>,
/// optional <c>params</c> and, for validation, <c>errors</c> per field. Never English sentences.
/// </summary>
public static class ProblemResults
{
    public const string TypePrefix = "urn:strategy:problem:";

    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(ToProblemDetails(error));

    public static ProblemDetails ToProblemDetails(this Error error)
    {
        var problem = new ProblemDetails
        {
            Type = TypePrefix + error.Code,
            Title = error.Code,
            Status = StatusCodeFor(error.Kind),
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["messageKey"] = error.MessageKey;
        if (error.Params is { Count: > 0 })
        {
            problem.Extensions["params"] = error.Params;
        }

        if (error.Fields is { Count: > 0 })
        {
            problem.Extensions["errors"] = error.Fields;
        }

        return problem;
    }

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound or ErrorKind.Disabled => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>200 with the value, or the error as Problem Details.</summary>
    public static Results<Ok<T>, ProblemHttpResult> ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
}
