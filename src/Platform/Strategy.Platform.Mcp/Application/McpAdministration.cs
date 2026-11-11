using Strategy.Platform.Identity.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Mcp.OAuth;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Mcp.Application;

public sealed record McpToolDto(
    string Name, string SystemCode, string Kind, string TitleAr, string TitleEn, string Description, string? Permission, bool DefaultEnabled, bool Enabled,
    int? RateLimitPerMinute);

public sealed record McpSettingsDto(McpSettings Settings, uint Version, string Endpoint, string Issuer, IReadOnlyList<McpToolDto> Tools);

public sealed record McpSettingsInput(McpSettings Settings, uint Version);

public sealed record McpClientDto(string ClientId, string? Name, IReadOnlyList<string> RedirectUris, DateTimeOffset? RegisteredAt);

public sealed record McpConsentInfoDto(string ClientId, string? ClientName, string? RedirectHost, IReadOnlyList<string> Scopes, bool Enabled);

public sealed record McpConsentInput(string? ClientId, string? RedirectUri, string? CodeChallenge);

public sealed record McpConsentTicketDto(string Ticket);

/// <summary>Settings → MCP, OAuth clients and the consent step of the authorization flow (docs/design/mcp.md §2, §4).</summary>
public sealed class McpAdministration(
    ISettingsEditor editor,
    ISettingsStore store,
    ToolCatalog catalog,
    IOpenIddictApplicationManager applications,
    IDataProtectionProvider protection,
    IHttpContextAccessor http,
    IRequestContext context,
    IAuditLog audit)
{
    private string BaseUrl => http.HttpContext is { } c ? AuthorizationEndpoints.BaseUrl(c.Request) : string.Empty;

    public async Task<McpSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var document = await editor.GetDocumentAsync<McpSettings>(ct).ConfigureAwait(false);
        var settings = document.Value;
        return new McpSettingsDto(settings, document.Version, BaseUrl + "/mcp", BaseUrl, catalog.All.Select(t => t.Definition)
            .OrderBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new McpToolDto(d.Name, d.SystemCode, d.Kind.ToString().ToLowerInvariant(), d.TitleAr, d.TitleEn, d.Description, d.Permission,
                d.DefaultEnabled && d.Kind == ToolKind.Read, McpToolHandlers.IsEnabled(settings, d), settings.Tool(d.Name).RateLimitPerMinute))
            .ToList());
    }

    public async Task<Result<McpSettingsDto>> SaveSettingsAsync(McpSettingsInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var names = catalog.All.Select(t => t.Definition.Name).ToHashSet(StringComparer.Ordinal);
        var saved = await editor.SaveAsync(input.Settings, input.Version, value =>
        {
            var errors = new List<FieldError>();
            Range(errors, "accessTokenMinutes", value.AccessTokenMinutes, 5, 1440);
            Range(errors, "refreshTokenDays", value.RefreshTokenDays, 1, 365);
            Range(errors, "rateLimitPerMinute", value.RateLimitPerMinute, 1, 10_000);
            if (value.AllowedRedirectHosts is { } hosts && (hosts.Count > 50 || hosts.Any(h => string.IsNullOrWhiteSpace(h) || h.Length > 200 || h.Contains('/', StringComparison.Ordinal))))
            {
                errors.Add(new FieldError("allowedRedirectHosts", "mcp.validation.host", "mcp.validation.host"));
            }

            foreach (var (name, tool) in value.Tools ?? new Dictionary<string, McpToolSettings>())
            {
                if (!names.Contains(name))
                {
                    errors.Add(new FieldError($"tools.{name}", "validation.unknownCode", "validation.unknownCode"));
                }
                else if (tool.RateLimitPerMinute is { } limit)
                {
                    Range(errors, $"tools.{name}.rateLimitPerMinute", limit, 1, 10_000);
                }
            }

            return errors;
        }, ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : await SettingsAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpClientDto>> ClientsAsync(CancellationToken ct)
    {
        var result = new List<McpClientDto>();
        await foreach (var application in applications.ListAsync(500, 0, ct).ConfigureAwait(false))
        {
            var clientId = (await applications.GetClientIdAsync(application, ct).ConfigureAwait(false))!;
            if (clientId.StartsWith(IntegrationClientKeys.ClientIdPrefix, StringComparison.Ordinal))
            {
                continue; // integration clients: Settings → Integrations (ADR 0191)
            }

            var properties = await applications.GetPropertiesAsync(application, ct).ConfigureAwait(false);
            DateTimeOffset? registered = properties.TryGetValue("registered_at", out var at) && at.TryGetDateTimeOffset(out var value) ? value : null;
            result.Add(new McpClientDto(
                clientId,
                await applications.GetDisplayNameAsync(application, ct).ConfigureAwait(false),
                (await applications.GetRedirectUrisAsync(application, ct).ConfigureAwait(false)).ToList(),
                registered));
        }

        return result.OrderByDescending(c => c.RegisteredAt).ToList();
    }

    /// <summary>Removes a client: its tokens and authorizations are revoked at once (OAuth configuration, not a business record). Audited.</summary>
    public async Task<Result> RevokeClientAsync(string clientId, CancellationToken ct)
    {
        var application = clientId.StartsWith(IntegrationClientKeys.ClientIdPrefix, StringComparison.Ordinal)
            ? null // integration clients are revoked in Settings → Integrations
            : await applications.FindByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (application is null)
        {
            return Error.NotFound();
        }

        // Deleting the client removes its authorizations and tokens with it; validation checks token entries, so they stop at once.
        await applications.DeleteAsync(application, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("mcp.client.revoke", true, "mcp", "mcp.client", clientId), ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<McpConsentInfoDto>> ConsentInfoAsync(string? clientId, string? redirectUri, CancellationToken ct)
    {
        var settings = await store.GetAsync<McpSettings>(ct).ConfigureAwait(false);
        var application = string.IsNullOrEmpty(clientId) ? null : await applications.FindByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (application is null || string.IsNullOrEmpty(redirectUri)
            || !await applications.ValidateRedirectUriAsync(application, redirectUri, ct).ConfigureAwait(false))
        {
            return Error.NotFound("mcp.client.notFound");
        }

        return new McpConsentInfoDto(clientId!, await applications.GetDisplayNameAsync(application, ct).ConfigureAwait(false), new Uri(redirectUri).Host,
            [McpScopes.Mcp], settings.Enabled);
    }

    /// <summary>The signed-in user allows the client: a protected ticket bound to the user, client, redirect URI and PKCE challenge (5 minutes).</summary>
    public async Task<Result<McpConsentTicketDto>> ConsentAsync(McpConsentInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var info = await ConsentInfoAsync(input.ClientId, input.RedirectUri, ct).ConfigureAwait(false);
        if (info.IsFailure)
        {
            return info.Error;
        }

        if (!info.Value.Enabled || string.IsNullOrEmpty(input.CodeChallenge) || context.UserId is not { } userId)
        {
            return Error.Rule("mcp.consentRefused", "mcp.errors.consentRefused");
        }

        return new McpConsentTicketDto(AuthorizationEndpoints.IssueTicket(protection, new ConsentTicket(userId, input.ClientId!, input.RedirectUri!, input.CodeChallenge)));
    }

    private static void Range(List<FieldError> errors, string field, int value, int from, int to)
    {
        if (value < from || value > to)
        {
            errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = from, ["to"] = to }));
        }
    }
}
