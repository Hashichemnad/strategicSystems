using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Strategy.Platform.Identity.Authentication;

namespace Strategy.Platform.Mcp.OAuth;

/// <summary>
/// Scheme <c>oauth</c>: validates the bearer with OpenIddict (local server, Data Protection tokens) and then builds the user's own principal,
/// re-read on every call (ADR 0032). Only the <c>mcp</c> scope exists, and the token-scope rule keeps these tokens on <c>/mcp</c>.
/// </summary>
internal sealed class OAuthAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TokenPrincipalFactory principals)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await Context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is null)
        {
            return result.None ? AuthenticateResult.NoResult() : AuthenticateResult.Fail("Invalid token");
        }

        var subject = result.Principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (subject is not null && subject.StartsWith(IntegrationClientKeys.ClientIdPrefix, StringComparison.Ordinal))
        {
            // A client-credentials token of an integration client (ADR 0191): re-checked on every call by the integration component.
            var authority = Context.RequestServices.GetService(typeof(IIntegrationClientAuthority)) as IIntegrationClientAuthority;
            var integration = authority is null
                ? null
                : await authority.CreatePrincipalAsync(subject, result.Principal.GetScopes().ToList(), Scheme.Name, Context.RequestAborted).ConfigureAwait(false);
            return integration is null ? AuthenticateResult.Fail("Invalid token") : AuthenticateResult.Success(new AuthenticationTicket(integration, Scheme.Name));
        }

        var scopes = result.Principal.GetScopes().Where(s => s == McpScopes.Mcp).ToList();
        var client = result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId) ?? result.Principal.GetPresenters().FirstOrDefault();
        if (!Guid.TryParse(subject, out var userId) || scopes.Count == 0)
        {
            return AuthenticateResult.Fail("Invalid token");
        }

        var principal = await principals.CreateAsync(userId, Scheme.Name, AuthMethods.OAuth, scopes, client).ConfigureAwait(false);
        return principal is null ? AuthenticateResult.Fail("Invalid token") : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
