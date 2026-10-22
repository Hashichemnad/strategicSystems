using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Host.Infrastructure;

internal static class ProblemDetailsSetup
{
    /// <summary>
    /// Every error response is RFC 9457 Problem Details carrying <c>code</c>, <c>messageKey</c>, <c>traceId</c> and
    /// <c>correlationId</c> — including framework errors (404 route, 405, 415, 429) that do not come from a handler.
    /// </summary>
    public static IServiceCollection AddStrategyProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            if (!problem.Extensions.ContainsKey("code"))
            {
                var (code, messageKey) = DefaultFor(status);
                problem.Type = ProblemResults.TypePrefix + code;
                problem.Title = code;
                problem.Extensions["code"] = code;
                problem.Extensions["messageKey"] = messageKey;
            }

            problem.Instance ??= context.HttpContext.Request.Path;
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            problem.Extensions["correlationId"] =
                context.HttpContext.RequestServices.GetRequiredService<IRequestContext>().CorrelationId;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    public static (string Code, string MessageKey) DefaultFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ("request.invalid", "errors.badRequest"),
        StatusCodes.Status401Unauthorized => ("auth.required", "errors.unauthorized"),
        StatusCodes.Status403Forbidden => ("access.forbidden", "errors.forbidden"),
        StatusCodes.Status404NotFound => ("route.notFound", "errors.notFound"),
        StatusCodes.Status405MethodNotAllowed => ("request.methodNotAllowed", "errors.badRequest"),
        StatusCodes.Status409Conflict => ("record.concurrencyConflict", "errors.concurrencyConflict"),
        StatusCodes.Status415UnsupportedMediaType => ("request.unsupportedMediaType", "errors.badRequest"),
        StatusCodes.Status429TooManyRequests => ("request.rateLimited", "errors.tooManyRequests"),
        _ => ("unexpected", "errors.unexpected"),
    };
}

/// <summary>Last line of defence: maps known exceptions to Problem Details, logs the rest without leaking details.</summary>
internal sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            DbUpdateConcurrencyException => Error.Conflict(),
            BadHttpRequestException => new Error("request.malformed", "errors.badRequest", ErrorKind.Validation),
            _ => null,
        };

        if (error is null)
        {
            LogUnhandled(logger, exception);
            error = new Error("unexpected", "errors.unexpected", ErrorKind.Unexpected);
        }

        var problem = error.ToProblemDetails();
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
