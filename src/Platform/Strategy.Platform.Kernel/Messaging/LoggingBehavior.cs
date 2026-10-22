using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Messaging;

/// <summary>Logs every request's name, outcome and duration. Never logs the request body (may hold personal data).</summary>
public sealed partial class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await next().ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (result.IsSuccess)
        {
            LogSucceeded(logger, typeof(TRequest).Name, elapsed);
        }
        else
        {
            LogFailed(logger, typeof(TRequest).Name, result.Error.Code, elapsed);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {Request} in {ElapsedMs:0.0} ms")]
    private static partial void LogSucceeded(ILogger logger, string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string request, string errorCode, double elapsedMs);
}
