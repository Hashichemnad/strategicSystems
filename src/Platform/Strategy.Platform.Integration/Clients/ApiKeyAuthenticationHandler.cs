using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Strategy.Platform.Identity.Authentication;

namespace Strategy.Platform.Integration.Clients;

/// <summary>
/// Scheme <c>integration</c> (ADR 0191): <c>Authorization: Bearer sik_…</c>, or HTTP Basic <c>client_id:sik_…</c> for BI tools (Power BI / Excel
/// OData connectors). Every call re-checks the client through <see cref="IntegrationClientAuthority"/>.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IntegrationClientAuthority authority)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string BasicRealm = "Basic realm=\"Integrated Strategy System\", charset=\"UTF-8\"";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The OAuth token endpoint reads client_secret_basic itself.
        if (Request.Path.StartsWithSegments("/connect", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var header = Request.Headers.Authorization.ToString();
        string? clientId = null;
        string key;
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            }
            catch (FormatException)
            {
                return AuthenticateResult.Fail("Invalid credentials");
            }

            var colon = decoded.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return AuthenticateResult.Fail("Invalid credentials");
            }

            clientId = decoded[..colon];
            key = decoded[(colon + 1)..];
        }
        else if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && IntegrationClientKeys.LooksLikeKey(header["Bearer ".Length..].Trim()))
        {
            key = header["Bearer ".Length..].Trim();
        }
        else
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await authority.AuthenticateKeyAsync(clientId, key, Scheme.Name, Context.RequestAborted).ConfigureAwait(false);
        return principal is null
            ? AuthenticateResult.Fail("Invalid credentials")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = BasicRealm;
        return Task.CompletedTask;
    }
}
