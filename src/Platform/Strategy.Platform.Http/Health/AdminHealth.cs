using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Http.Health;

/// <summary>Status of one administrator health check (ADR 0151). Ordered from best to worst except <see cref="NotConfigured"/>.</summary>
public static class AdminHealthStatuses
{
    public const string Ok = "ok";
    public const string NotConfigured = "notConfigured";
    public const string Warning = "warning";
    public const string Error = "error";

    /// <summary>Severity used to compute the overall status: error &gt; warning &gt; ok / not configured.</summary>
    public static int Severity(string status) => status switch
    {
        Error => 3,
        Warning => 2,
        _ => 0,
    };

    public static string Worst(IEnumerable<string> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        var worst = Ok;
        foreach (var status in statuses)
        {
            if (Severity(status) > Severity(worst))
            {
                worst = status;
            }
        }

        return worst;
    }
}

/// <summary>
/// One check on the administrator Health page. Never English text: <paramref name="MessageKey"/> and <paramref name="Params"/> are translated by
/// the client (namespace <c>health</c>). <paramref name="Params"/> values are invariant strings (numbers, ISO 8601 dates, codes); never secrets.
/// </summary>
public sealed record AdminHealthCheck(
    string Code,
    string Group,
    string Status,
    string MessageKey,
    IReadOnlyDictionary<string, string>? Params = null,
    IReadOnlyList<AdminHealthItem>? Items = null);

/// <summary>A line inside a check (e.g. one scheduled job, one module's migrations). Labels are AR / EN where the item has a name.</summary>
public sealed record AdminHealthItem(
    string Code,
    string Status,
    string MessageKey,
    IReadOnlyDictionary<string, string>? Params = null,
    string? LabelAr = null,
    string? LabelEn = null);

/// <summary>
/// Contributes checks to the Health page. Each platform component registers its own (database, jobs, outbox, e-mail, AI, OCR, storage,
/// PDF, audit chain…). Called in its own DI scope with a time-out; an exception is reported as an error for <see cref="Code"/>.
/// Contributors only read; they must not change data or write audit entries.
/// </summary>
public interface IAdminHealthContributor
{
    /// <summary>Stable code of the contributor, used when it fails or times out.</summary>
    string Code { get; }

    /// <summary>Group the checks are shown in: <c>platform</c>, <c>jobs</c>, <c>integrations</c>, <c>storage</c>, <c>security</c>.</summary>
    string Group { get; }

    /// <summary>Lower comes first on the page.</summary>
    int Order { get; }

    Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken);
}

public static class AdminHealthGroups
{
    public const string Platform = "platform";
    public const string Jobs = "jobs";
    public const string Integrations = "integrations";
    public const string Storage = "storage";
    public const string Security = "security";
}

/// <summary>Builds the <see cref="AdminHealthCheck.Params"/> dictionary with invariant formatting.</summary>
public static class HealthParams
{
    public static IReadOnlyDictionary<string, string> Of(params (string Key, object? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            var text = value switch
            {
                null => null,
                DateTimeOffset at => at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                DateTime at => at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (text is not null)
            {
                result[key] = text;
            }
        }

        return result;
    }
}

public static class AdminHealthServiceCollectionExtensions
{
    /// <summary>Registers a contributor (scoped). The Health page resolves it by its type in a scope of its own.</summary>
    public static IServiceCollection AddAdminHealthContributor<T>(this IServiceCollection services)
        where T : class, IAdminHealthContributor
    {
        services.AddScoped<T>();
        services.AddScoped<IAdminHealthContributor>(sp => sp.GetRequiredService<T>());
        return services;
    }
}
