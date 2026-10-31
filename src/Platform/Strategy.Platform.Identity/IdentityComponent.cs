using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.External;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Import;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity;

/// <summary>Accounts and sign-in (<c>/api/v1/identity</c>, schema <c>identity</c>). docs/design/identity.md.</summary>
public sealed class IdentityComponent : IPlatformComponent
{
    public const string SessionCookieName = "strategy.session";

    public string Name => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityStoreDbContext>(IdentityStoreDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(IdentityComponent).Assembly);
        services.AddHttpContextAccessor();

        services.AddSingleton<IdentitySettingsCache>();
        services.AddHostedService<IdentitySettingsRefresher>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<SessionTicketStore>();
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, SessionStoreCookieSetup>();
        services.AddScoped<AuthEventLog>();
        services.AddScoped<SignInService>();
        services.AddScoped<AccessTokenService>();
        services.AddScoped<UserAdministration>();
        services.AddScoped<IIdentityEmailSender, SmtpIdentityEmailSender>();
        services.AddScoped<IEmployeeLifecycleHook, DeactivateUserWhenEmployeeLeaves>();
        services.AddScoped<IUserProvisioning, ImportedUserProvisioning>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddAccessCatalogue(new IdentityAccessCatalogue());
        services.AddFeature(IdentityFeatures.AccessTokensFeature);

        services.AddIdentityCore<StrategyUser>(options =>
            {
                options.User.RequireUniqueEmail = false; // internal users: unique through the employee; checked on creation
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789-._@+";
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Tokens.ProviderMap[SignInService.InvitationTokenProvider] =
                    new TokenProviderDescriptor(typeof(InvitationTokenProvider));
            })
            .AddUserStore<StrategyUserStore>()
            .AddUserManager<StrategyUserManager>()
            .AddSignInManager<StrategySignInManager>()
            .AddClaimsPrincipalFactory<StrategyClaimsFactory>()
            .AddPasswordValidator<PasswordHistoryValidator>()
            .AddDefaultTokenProviders()
            // Replaces the built-in authenticator provider: same codes and window, but each time step is accepted once.
            .AddTokenProvider<ReplayProtectedAuthenticatorTokenProvider>(TokenOptions.DefaultAuthenticatorProvider);
        services.AddTransient<InvitationTokenProvider>();
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
        services.Configure<InvitationTokenProviderOptions>(o =>
        {
            o.Name = SignInService.InvitationTokenProvider;
            o.TokenLifespan = TimeSpan.FromDays(3);
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = AuthSchemes.Default;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddPolicyScheme(AuthSchemes.Default, null, options => options.ForwardDefaultSelector = context =>
                AccessTokenService.LooksLikeToken(PatAuthenticationHandler.BearerValue(context.Request))
                    ? AuthSchemes.PersonalAccessToken
                    : IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(AuthSchemes.PersonalAccessToken, null)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(24); // upper bound; idle / absolute limits are enforced by the session store
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => WriteProblemAsync(context.HttpContext,
                new Error("auth.required", "errors.unauthorized", ErrorKind.Unauthorized));
            options.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, Error.Forbidden());
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = "strategy.mfa";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.Name = "strategy.external";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        services.AddExternalProviders();
    }

    public void UseMiddleware(IApplicationBuilder app) => app.UseMiddleware<TokenScopeMiddleware>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapPlatformGroup(this);
        AuthEndpoints.Map(group);
        MeEndpoints.Map(group);
        AdminEndpoints.Map(group);
        ExternalEndpoints.Map(group, endpoints);
    }

    private static Task WriteProblemAsync(HttpContext context, Error error) => error.ToProblem().ExecuteAsync(context);
}

/// <summary>Enforces token scopes (403 <c>auth.tokenScope</c>) and labels token calls with channel <c>api</c>.</summary>
internal sealed class TokenScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RequestContext requestContext)
    {
        if (context.User.IsToken())
        {
            requestContext.SetChannel(Channels.Api);
            if (!TokenScopeRules.Allows(context.User, context.Request.Method, context.Request.Path))
            {
                await new Error("auth.tokenScope", "auth.errors.tokenScope", ErrorKind.Forbidden).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}

public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Invitation links ("set your password") live longer than reset links: 3 days.</summary>
internal sealed class InvitationTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    Microsoft.Extensions.Logging.ILogger<DataProtectorTokenProvider<StrategyUser>> logger)
    : DataProtectorTokenProvider<StrategyUser>(dataProtectionProvider, options, logger);
