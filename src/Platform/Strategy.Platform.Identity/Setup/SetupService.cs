using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Setup;

/// <summary>Public state of the first-run setup (ADR 0150). Reveals nothing but whether the wizard is open.</summary>
public sealed record SetupStatusDto(bool NeedsSetup, bool SetupCompleted);

/// <summary>
/// First stage of the setup wizard (ADR 0150): organisation profile, branding, languages and the first administrator, in one call
/// authorised by the one-time setup token.
/// </summary>
public sealed record SetupBootstrapInput(
    string? OrganisationNameAr,
    string? OrganisationNameEn,
    string? OrganisationCode,
    string? TimeZone,
    string? CurrencyCode,
    string? DateFormat,
    string? PrimaryColor,
    string? EmailHeaderAr,
    string? EmailHeaderEn,
    string? EmailFooterAr,
    string? EmailFooterEn,
    string? DefaultLanguage,
    bool HijriDisplay,
    bool ArabicIndicDigits,
    string? AdminEmail,
    string? AdminPassword,
    string? AdminNameAr,
    string? AdminNameEn,
    string? AdminEmployeeNo,
    string? AdminJobTitleAr,
    string? AdminJobTitleEn);

public sealed record SetupBootstrapResultDto(Guid UserId, string UserName);

/// <summary>
/// The one-time setup token and the wizard state (ADR 0150). The token is 32 random bytes (base64url); only its SHA-256 is stored, the
/// clear value is written to the log once when issued. It is valid only while no platform administrator exists, for
/// <c>Setup:TokenLifetimeHours</c> (24) hours, and is consumed by the bootstrap.
/// </summary>
public sealed partial class SetupService(
    IdentityStoreDbContext db,
    IUserRoleQuery roles,
    IClock clock,
    IAuditLog audit,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<SetupService> logger)
{
    public const string TokenHeader = "X-Setup-Token";
    public const string AuditEntity = "identity.setup";
    private const int MaxTokenLength = 128;

    private TimeSpan TokenLifetime => TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Setup:TokenLifetimeHours", 24), 1, 168));

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var adminExists = await roles.AnyPlatformAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var state = await db.SetupStates.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new SetupStatusDto(!adminExists, state?.CompletedAt is not null);
    }

    /// <summary>
    /// Start-up (and <c>--setup-token</c>): while no administrator exists, issues a new token (the previous one stops working) and logs it.
    /// </summary>
    /// <returns>The clear token, or null when an administrator exists (the wizard is closed).</returns>
    public async Task<string?> IssueTokenAsync(CancellationToken cancellationToken = default)
    {
        if (await roles.AnyPlatformAdministratorAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var state = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        state.TokenHash = Hash(token);
        state.TokenIssuedAt = now;
        state.TokenExpiresAt = now + TokenLifetime;
        state.TokenConsumedAt = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("setup.token.issued", true, IdentityComponentInfo.SystemCode, AuditEntity), cancellationToken)
            .ConfigureAwait(false);
        LogToken(logger, token, state.TokenExpiresAt.Value);
        return token;
    }

    /// <summary>True when <paramref name="token"/> is the current, unexpired, unconsumed token and no administrator exists. Audited.</summary>
    public async Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken = default)
    {
        var valid = await IsValidAsync(token, cancellationToken).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("setup.token.verify", valid, IdentityComponentInfo.SystemCode, AuditEntity), cancellationToken)
            .ConfigureAwait(false);
        return valid;
    }

    /// <summary>Stage 1: saves the organisation profile, branding and languages, creates the first administrator, consumes the token.</summary>
    public async Task<Result<SetupBootstrapResultDto>> BootstrapAsync(string? token, SetupBootstrapInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await IsValidAsync(token, cancellationToken).ConfigureAwait(false))
        {
            await audit.WriteAsync(new AuditEvent("setup.bootstrap", false, IdentityComponentInfo.SystemCode, AuditEntity), cancellationToken)
                .ConfigureAwait(false);
            return Error.Forbidden("setup.tokenInvalid");
        }

        var regional = new RegionalSettings(
            input.TimeZone?.Trim() ?? string.Empty, input.HijriDisplay, input.ArabicIndicDigits, input.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty,
            input.DateFormat ?? string.Empty, input.DefaultLanguage ?? string.Empty);
        var branding = new BrandingSettings(
            Trimmed(input.OrganisationNameAr), Trimmed(input.OrganisationNameEn), input.PrimaryColor ?? string.Empty,
            Trimmed(input.EmailHeaderAr), Trimmed(input.EmailHeaderEn), Trimmed(input.EmailFooterAr), Trimmed(input.EmailFooterEn));
        var fields = new List<FieldError>();
        fields.AddRange(SettingsValidation.Regional(regional));
        fields.AddRange(SettingsValidation.Branding(branding));
        Required(fields, "organisationNameAr", input.OrganisationNameAr);
        Required(fields, "organisationNameEn", input.OrganisationNameEn);
        Required(fields, "organisationCode", input.OrganisationCode);
        Required(fields, "adminEmail", input.AdminEmail);
        Required(fields, "adminPassword", input.AdminPassword);
        Required(fields, "adminNameAr", input.AdminNameAr);
        Required(fields, "adminNameEn", input.AdminNameEn);
        Required(fields, "adminEmployeeNo", input.AdminEmployeeNo);
        if (input.AdminEmail is { Length: > 0 } email && (email.Length > 256 || !email.Contains('@', StringComparison.Ordinal)))
        {
            fields.Add(new FieldError("adminEmail", "validation.email", "validation.email"));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields);
        }

        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        await provider.GetRequiredService<IdentitySettingsCache>().ReloadAsync(cancellationToken).ConfigureAwait(false);
        var users = provider.GetRequiredService<StrategyUserManager>();
        var userName = input.AdminEmail!.Trim().ToLowerInvariant();
        if (await users.FindByNameAsync(userName).ConfigureAwait(false) is not null)
        {
            return Error.Validation([new FieldError("adminEmail", "identity.duplicateUserName", "identity.errors.duplicateUserName")]);
        }

        // The password is checked before anything is written, so a weak password leaves no half-created organisation behind.
        var candidate = new StrategyUser { UserName = userName, Email = input.AdminEmail.Trim() };
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, candidate, input.AdminPassword).ConfigureAwait(false);
            if (!check.Succeeded)
            {
                return IdentityErrors.ToValidation(check, "adminPassword", provider.GetRequiredService<IdentitySettingsCache>().Security);
            }
        }

        var settings = provider.GetRequiredService<ISettingsEditor>();
        var regionalDocument = await settings.GetDocumentAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false);
        var savedRegional = await settings.SaveAsync(regional, regionalDocument.Version, SettingsValidation.Regional, cancellationToken).ConfigureAwait(false);
        if (savedRegional.IsFailure)
        {
            return savedRegional.Error;
        }

        var brandingDocument = await settings.GetDocumentAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var savedBranding = await settings.SaveAsync(branding, brandingDocument.Version, SettingsValidation.Branding, cancellationToken).ConfigureAwait(false);
        if (savedBranding.IsFailure)
        {
            return savedBranding.Error;
        }

        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var root = await RootUnitAsync(dispatcher, provider.GetRequiredService<OrgDbContext>(), input, cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
        {
            return Prefixed(root.Error, "organisation");
        }

        var employee = await dispatcher.SendAsync(new CreateEmployee(
            input.AdminEmployeeNo!.Trim(), input.AdminNameAr!.Trim(), input.AdminNameEn!.Trim(), input.AdminEmail.Trim(), null, Trimmed(input.AdminJobTitleAr),
            Trimmed(input.AdminJobTitleEn), null, root.Value, null, clock.Today), cancellationToken).ConfigureAwait(false);
        if (employee.IsFailure)
        {
            return Prefixed(employee.Error, "admin");
        }

        var user = new StrategyUser
        {
            UserName = userName,
            Email = input.AdminEmail.Trim(),
            EmailConfirmed = true,
            EmployeeId = employee.Value.Id,
            Language = regional.DefaultLanguage,
            CreatedAt = clock.UtcNow,
        };
        var created = await users.CreateAsync(user, input.AdminPassword!).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return IdentityErrors.ToValidation(created, "adminPassword", provider.GetRequiredService<IdentitySettingsCache>().Security);
        }

        await provider.GetRequiredService<IUserRoleQuery>().GrantPlatformAdministratorAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var state = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        state.TokenHash = null;
        state.TokenConsumedAt = now;
        state.BootstrappedAt = now;
        state.BootstrappedBy = user.Id;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await audit.WriteAsync(
            new AuditEvent("setup.bootstrap", true, IdentityComponentInfo.SystemCode, AuditEntity, user.Id.ToString("D"))
            {
                ActorId = user.Id,
                ActorName = user.UserName,
            },
            cancellationToken).ConfigureAwait(false);
        LogBootstrapped(logger, user.UserName);
        return new SetupBootstrapResultDto(user.Id, user.UserName);
    }

    /// <summary>Stage 2 finished (or closed): administrators are no longer sent to the wizard.</summary>
    public async Task<SetupStatusDto> CompleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var state = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        if (state.CompletedAt is null)
        {
            state.CompletedAt = clock.UtcNow;
            state.CompletedBy = userId;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsValidAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var state = await db.SetupStates.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (state?.TokenHash is not { Length: 32 } stored || state.TokenConsumedAt is not null || state.TokenExpiresAt is null
            || state.TokenExpiresAt <= clock.UtcNow)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(stored, Hash(token.Trim()))
            && !await roles.AnyPlatformAdministratorAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SetupState> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        var state = await db.SetupStates.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            state = new SetupState();
            db.SetupStates.Add(state);
        }

        return state;
    }

    /// <summary>The organisation's root unit: created at the highest configured level, or the existing root when units already exist.</summary>
    private static async Task<Result<Guid>> RootUnitAsync(IDispatcher dispatcher, OrgDbContext org, SetupBootstrapInput input, CancellationToken cancellationToken)
    {
        var existing = await org.Units.AsNoTracking().Where(u => u.Active && u.ParentId == null).OrderBy(u => u.Id).Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is { } rootId)
        {
            return rootId;
        }

        var levels = await dispatcher.SendAsync(new ListLevels(), cancellationToken).ConfigureAwait(false);
        var level = levels.IsSuccess ? levels.Value.Where(l => l.Active).OrderBy(l => l.Sort).Select(l => l.Code).FirstOrDefault() : null;
        var unit = await dispatcher.SendAsync(new CreateUnit(
            input.OrganisationCode!.Trim().ToUpperInvariant(), input.OrganisationNameAr!.Trim(), input.OrganisationNameEn!.Trim(), level, null, null, null, null, null),
            cancellationToken).ConfigureAwait(false);
        return unit.IsSuccess ? unit.Value.Id : unit.Error;
    }

    /// <summary>Field errors of the organisation commands named as the wizard's fields (<c>code</c> → <c>organisationCode</c>).</summary>
    private static Error Prefixed(Error error, string prefix) =>
        error.Fields is { Count: > 0 } fields
            ? error with { Fields = fields.Select(f => f with { Field = prefix + char.ToUpperInvariant(f.Field[0]) + f.Field[1..] }).ToList() }
            : error;

    private static void Required(List<FieldError> fields, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new FieldError(field, "validation.notEmpty", "validation.notEmpty"));
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "\n==================================================================\n" +
        " FIRST-RUN SETUP: no administrator exists yet.\n" +
        " Open /setup in the browser and enter this one-time setup token:\n\n" +
        "     {SetupToken}\n\n" +
        " It is valid until {ExpiresAt:u} and stops working once the first\n" +
        " administrator is created. Print a new one with --setup-token.\n" +
        "==================================================================")]
    private static partial void LogToken(ILogger logger, string setupToken, DateTimeOffset expiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "First-run setup created the first administrator {UserName}; the setup token is consumed")]
    private static partial void LogBootstrapped(ILogger logger, string userName);
}
