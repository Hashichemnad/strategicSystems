using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.External;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Identity.Wasel;

/// <summary>
/// Whether vendors may sign in to Wasel with UAE Pass (Settings → Portfolio → Wasel, <c>uaePassForVendors</c>). Implemented by the module that
/// owns the vendor register (Portfolio); without an implementation the option is off.
/// </summary>
public interface IVendorUaePassPolicy
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sign-in options the Wasel login page shows (public: says only whether the UAE Pass button is offered).</summary>
public sealed record WaselSignInOptionsDto(bool UaePass, string? UaePassNameAr, string? UaePassNameEn);

/// <summary>
/// UAE Pass for Wasel vendors (ADR 0154). The installation's UAE Pass provider (same client registration and callback path as the internal
/// sign-in) authenticates the person; the Wasel callback then matches a <b>vendor</b> account by the verified e-mail (never creates one, never an
/// internal account) and leaves the vendor half-signed-in: the second factor is still required before the Wasel session cookie is issued.
/// </summary>
internal static class WaselUaePassEndpoints
{
    public const string Flow = "wasel";
    public const string FlowItem = "flow";
    public const string AuditProvider = "wasel:uaepass";
    private const string CallbackPath = "/api/wasel/v1/auth/uae-pass/callback";

    public static void Map(RouteGroupBuilder auth)
    {
        auth.MapGet("/options", OptionsAsync).WithName("GetWaselSignInOptions").AsPublic();
        auth.MapGet("/uae-pass/challenge", ChallengeAsync).WithName("WaselUaePassChallenge").AsPublic()
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).ExcludeFromDescription();
        auth.MapGet("/uae-pass/callback", CallbackAsync).WithName("WaselUaePassCallback").AsPublic()
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).ExcludeFromDescription();
    }

    /// <summary>The enabled UAE Pass provider when the vendor option is on, otherwise <c>null</c>.</summary>
    internal static async Task<IdentityProvider?> ProviderAsync(
        IdentityStoreDbContext db, IEnumerable<IVendorUaePassPolicy> policies, CancellationToken ct)
    {
        var on = false;
        foreach (var policy in policies)
        {
            on = await policy.IsEnabledAsync(ct).ConfigureAwait(false);
            if (!on)
            {
                return null;
            }
        }

        return on
            ? await db.Providers.AsNoTracking().Where(p => p.Enabled && p.Type == ProviderTypes.UaePass)
                .OrderBy(p => p.Sort).ThenBy(p => p.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
    }

    private static async Task<Ok<WaselSignInOptionsDto>> OptionsAsync(
        IdentityStoreDbContext db, IEnumerable<IVendorUaePassPolicy> policies, CancellationToken ct)
    {
        var provider = await ProviderAsync(db, policies, ct);
        return TypedResults.Ok(new WaselSignInOptionsDto(provider is not null, provider?.DisplayNameAr, provider?.DisplayNameEn));
    }

    private static async Task<IResult> ChallengeAsync(
        IdentityStoreDbContext db, IEnumerable<IVendorUaePassPolicy> policies, ExternalSchemeRegistry registry, HttpContext context)
    {
        var provider = await ProviderAsync(db, policies, context.RequestAborted);
        if (provider is null)
        {
            return Error.NotFound("identity.provider.notFound").ToProblem();
        }

        if (!registry.TryGet(provider.Code, out _))
        {
            await registry.ReloadAsync(context.RequestAborted);
            if (!registry.TryGet(provider.Code, out _))
            {
                return Error.NotFound("identity.provider.notFound").ToProblem();
            }
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = CallbackPath,
            Items = { ["provider"] = provider.Code, [FlowItem] = Flow },
        };
        return TypedResults.Challenge(properties, [provider.Code]);
    }

    /// <summary>Back from UAE Pass: <c>/wasel/login/mfa?methods=…[&amp;enrol=1]</c>, or <c>/wasel/login?error=…</c>.</summary>
    private static async Task<IResult> CallbackAsync(
        HttpContext context, IdentityStoreDbContext db, IEnumerable<IVendorUaePassPolicy> policies, StrategyUserManager users,
        WaselSignInService signIn, AuthEventLog events)
    {
        var ct = context.RequestAborted;
        var result = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var code = result.Properties?.Items.TryGetValue("provider", out var c) == true ? c : null;
        var flow = result.Properties?.Items.TryGetValue(FlowItem, out var f) == true ? f : null;
        if (!result.Succeeded || code is null || flow != Flow)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: AuditProvider, detail: "noTicket", cancellationToken: ct);
            return Fail("auth.errors.externalFailed");
        }

        // The option or the provider may have been switched off while the person was at UAE Pass.
        var provider = await ProviderAsync(db, policies, ct);
        if (provider is null || provider.Code != code)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: AuditProvider, detail: "disabled", cancellationToken: ct);
            return Fail("auth.errors.externalFailed");
        }

        var identity = ExternalEndpoints.MapIdentity(provider, result.Principal);
        if (identity is null)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: AuditProvider, detail: "noSubject", cancellationToken: ct);
            return Fail("auth.errors.externalFailed");
        }

        if (!ExternalEndpoints.UserTypeAllowed(provider, identity.UserType))
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, userName: identity.Email, provider: AuditProvider, detail: "userType",
                cancellationToken: ct);
            return Fail(AccountLinker.UserTypeNotAllowed);
        }

        // Only a verified e-mail identifies the vendor: UAE Pass verifies it for its verified account types; an explicit "false" is refused.
        var verified = result.Principal.FindFirst("email_verified")?.Value;
        if (string.IsNullOrWhiteSpace(identity.Email) || string.Equals(verified, "false", StringComparison.OrdinalIgnoreCase))
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: AuditProvider, detail: "noVerifiedEmail", cancellationToken: ct);
            return Fail("wasel.auth.errors.uaePassNoAccount");
        }

        var email = identity.Email.Trim();
        var normalised = users.NormalizeEmail(email);
        var user = await users.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalised && u.PrincipalType == StrategyClaims.VendorPrincipal, ct);
        var (outcome, failure) = await signIn.ExternalFirstFactorAsync(user, AuditProvider, email, ct);
        if (outcome is null)
        {
            // One message for unknown, internal, inactive and expired accounts: no account enumeration through the portal.
            return Fail(failure == "lockedOut" ? "auth.errors.lockedOut" : "wasel.auth.errors.uaePassNoAccount");
        }

        var methods = Uri.EscapeDataString(string.Join(',', outcome.Methods));
        return TypedResults.Redirect(outcome.Status == SignInStatuses.MfaEnrolmentRequired
            ? $"/wasel/login/mfa?methods={methods}&enrol=1"
            : $"/wasel/login/mfa?methods={methods}");

        static IResult Fail(string key) => TypedResults.Redirect("/wasel/login?error=" + Uri.EscapeDataString(key));
    }
}
