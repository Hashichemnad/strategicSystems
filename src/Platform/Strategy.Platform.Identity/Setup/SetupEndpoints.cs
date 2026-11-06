using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Setup;

/// <summary>
/// First-run setup (<c>/api/v1/setup</c>, ADR 0150). Status is public; token check and bootstrap are public but need the one-time token
/// and are rate-limited like sign-in; completing the guided configuration needs <c>settings.setup.manage</c>.
/// </summary>
internal static class SetupEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var setup = endpoints.MapGroup("/api/v1/setup").WithTags("setup");
        setup.MapGet("/status", StatusAsync).WithName("GetSetupStatus").AsPublic();
        setup.MapPost("/verify-token", VerifyAsync).WithName("VerifySetupToken").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        setup.MapPost("/bootstrap", BootstrapAsync).WithName("BootstrapSetup").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        setup.MapPost("/complete", CompleteAsync).WithName("CompleteSetup").RequirePermission(SettingsPermissions.SetupManage);
    }

    private static async Task<Ok<SetupStatusDto>> StatusAsync(SetupService setup, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await setup.GetStatusAsync(ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> VerifyAsync(SetupService setup, HttpContext context, CancellationToken ct) =>
        await setup.VerifyAsync(context.Request.Headers[SetupService.TokenHeader], ct)
            ? TypedResults.NoContent()
            : Error.Forbidden("setup.tokenInvalid").ToProblem();

    private static async Task<Results<Ok<SetupBootstrapResultDto>, ProblemHttpResult>> BootstrapAsync(
        SetupBootstrapInput body, SetupService setup, HttpContext context, CancellationToken ct) =>
        (await setup.BootstrapAsync(context.Request.Headers[SetupService.TokenHeader], body, ct)).ToOk();

    private static async Task<Results<Ok<SetupStatusDto>, ProblemHttpResult>> CompleteAsync(SetupService setup, HttpContext context, CancellationToken ct)
    {
        if (context.User.UserId() is not { } userId)
        {
            return Error.Forbidden().ToProblem();
        }

        return TypedResults.Ok(await setup.CompleteAsync(userId, ct));
    }
}

/// <summary>Issues (and logs) a setup token at start-up while no administrator exists (ADR 0150). Never stops the host.</summary>
internal sealed partial class SetupTokenIssuer(IServiceScopeFactory scopes, ILogger<SetupTokenIssuer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SetupService>().IssueTokenAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A database that is not migrated yet must not stop the host; the wizard then has no token until restart.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "The first-run setup token could not be issued (is the database migrated?). Run --setup-token after --migrate.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
