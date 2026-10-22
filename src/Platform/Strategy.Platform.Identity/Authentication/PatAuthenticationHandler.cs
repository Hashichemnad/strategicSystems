using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.Platform.Identity.Authentication;

/// <summary>
/// <c>Authorization: Bearer sst_…</c>. Builds the same principal as a session (so access rules are identical) plus the
/// token's scopes. Refuses revoked / expired tokens and tokens of inactive or locked-out users.
/// </summary>
internal sealed class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccessTokenService tokens,
    StrategyUserManager users,
    IUserClaimsPrincipalFactory<StrategyUser> claimsFactory,
    AuthEventLog events)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public static string? BearerValue(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = BearerValue(Request);
        if (presented is null)
        {
            return AuthenticateResult.NoResult();
        }

        var token = await tokens.ValidateAsync(presented, Context.Connection.RemoteIpAddress?.ToString(), Context.RequestAborted).ConfigureAwait(false);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString()).ConfigureAwait(false);
        if (token is null || user is null || !user.Active || await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            await events.WriteAsync(AuthEventTypes.TokenRejected, false, user?.Id, user?.UserName, detail: token is null ? "invalid" : "user",
                cancellationToken: Context.RequestAborted).ConfigureAwait(false);
            return AuthenticateResult.Fail("Invalid token");
        }

        var principal = await claimsFactory.CreateAsync(user).ConfigureAwait(false);
        var identity = new ClaimsIdentity(principal.Claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, AuthMethods.Token));
        identity.AddClaim(new Claim(StrategyClaims.TokenScopes, string.Join(' ', token.Scopes)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}

/// <summary>Scopes of a token-authenticated request: <c>api.read</c> allows only GET / HEAD on the REST API.</summary>
public static class TokenScopeRules
{
    public static bool IsToken(this ClaimsPrincipal principal) =>
        principal.FindFirst(StrategyClaims.AuthMethod)?.Value == AuthMethods.Token && principal.Identity?.AuthenticationType == AuthSchemes.PersonalAccessToken;

    public static bool Allows(ClaimsPrincipal principal, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.IsToken())
        {
            return true;
        }

        var scopes = (principal.FindFirst(StrategyClaims.TokenScopes)?.Value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return scopes.Contains(TokenScopes.Mcp);
        }

        var readOnly = Microsoft.AspNetCore.Http.HttpMethods.IsGet(method) || Microsoft.AspNetCore.Http.HttpMethods.IsHead(method);
        return scopes.Contains(TokenScopes.ApiWrite) || (readOnly && scopes.Contains(TokenScopes.ApiRead));
    }
}
