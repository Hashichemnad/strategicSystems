using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// <c>ForwardedHeaders:Enabled</c> (off by default): trust <c>X-Forwarded-For / -Proto / -Host</c> from the reverse proxies listed in
/// <c>ForwardedHeaders:KnownProxies</c> (IP addresses). With no list, any sender is trusted: only when the application port is reachable by the
/// proxy alone (docker network), as in the shipped compose file (deploy/README.md).
/// </summary>
internal static class ForwardedHeadersSetup
{
    public static IServiceCollection AddStrategyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in proxies.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
        return services;
    }
}
