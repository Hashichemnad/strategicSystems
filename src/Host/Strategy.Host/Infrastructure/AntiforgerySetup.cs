using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Host.Infrastructure;

/// <summary>The token the SPA sends back in <see cref="AntiforgerySetup.HeaderName"/> on every state-changing call.</summary>
public sealed record AntiforgeryToken(string Token, string HeaderName);

internal static class AntiforgerySetup
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string CookieName = "strategy.af";

    public static IServiceCollection AddStrategyAntiforgery(this IServiceCollection services) =>
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

    /// <summary><c>GET /api/v1/antiforgery/token</c>: sets the antiforgery cookie and returns the matching request token.</summary>
    public static void MapAntiforgeryEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/antiforgery/token", Ok<AntiforgeryToken> (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(new AntiforgeryToken(tokens.RequestToken!, HeaderName));
            })
            .WithName("GetAntiforgeryToken")
            .WithTags("platform")
            .AsPublic();
}

/// <summary>
/// Rejects state-changing <c>/api</c> calls without a valid antiforgery header + cookie pair (400 <c>antiforgery.invalid</c>).
/// Calls authenticated by a personal access token are exempt: they carry no ambient cookie a forged cross-site request
/// could ride on (the token is in the <c>Authorization</c> header, which browsers never add on their own).
/// </summary>
internal sealed class AntiforgeryValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (RequiresValidation(context.Request) && !context.User.IsToken() && !await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
        {
            await new Error("antiforgery.invalid", "errors.antiforgery", ErrorKind.Validation)
                .ToProblem()
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool RequiresValidation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
             || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method));
}
