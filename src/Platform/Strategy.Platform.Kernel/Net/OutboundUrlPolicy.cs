using System.Net;
using System.Net.Sockets;

namespace Strategy.Platform.Kernel.Net;

/// <summary>
/// Options <c>Outbound:*</c> (ADR 0155). Private networks are allowed by default: on-premises installations reach their AI server, identity
/// provider or directory on the internal network. Loopback is refused by default (a URL pointing at the application host itself).
/// </summary>
public sealed record OutboundOptions(bool AllowPrivateNetworks = true, bool AllowLoopback = false);

/// <summary>Why an outbound address or URL was refused; <see cref="None"/> when allowed.</summary>
public enum OutboundRefusal
{
    None,
    InvalidUrl,
    Scheme,
    Credentials,
    Loopback,
    LinkLocal,
    PrivateNetwork,
    Reserved,
    Unresolvable,
}

/// <summary>
/// Server-side request forgery guard for URLs an administrator types in (AI provider endpoints, identity-provider authority / metadata /
/// discovery, LDAP hosts). Two layers: <see cref="Check(string)"/> when the URL is saved or tested (scheme, no credentials, literal addresses),
/// and <see cref="CreateHandler"/> for the HTTP client, which resolves the name itself and refuses a forbidden address <b>at connect time</b>
/// (no DNS rebinding) and never follows redirects. Link-local (incl. the 169.254.169.254 cloud metadata service), unspecified, multicast and
/// broadcast addresses are always refused.
/// </summary>
public sealed class OutboundUrlPolicy(OutboundOptions options)
{
    public const string MessageKey = "validation.outboundUrlRefused";

    public OutboundOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Syntactic check of a URL plus, for a literal IP host, the address rules. Host names are checked again when connecting.</summary>
    public OutboundRefusal Check(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return OutboundRefusal.InvalidUrl;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return OutboundRefusal.Scheme;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return OutboundRefusal.InvalidUrl;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return OutboundRefusal.Credentials;
        }

        return CheckHost(uri.Host);
    }

    /// <summary>Checks a host name or literal address (LDAP host, SMTP host): names pass here and are checked when connecting.</summary>
    public OutboundRefusal CheckHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return OutboundRefusal.InvalidUrl;
        }

        var trimmed = host.Trim().Trim('[', ']');
        if (IPAddress.TryParse(trimmed, out var address))
        {
            return CheckAddress(address);
        }

        return string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            ? Options.AllowLoopback ? OutboundRefusal.None : OutboundRefusal.Loopback
            : OutboundRefusal.None;
    }

    public OutboundRefusal CheckAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return Options.AllowLoopback ? OutboundRefusal.None : OutboundRefusal.Loopback;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254)
            {
                return OutboundRefusal.LinkLocal;
            }

            if (b[0] == 0 || b[0] >= 224 || (b[0] == 192 && b[1] == 0 && b[2] == 0))
            {
                // 0.0.0.0/8, multicast, reserved and broadcast (224.0.0.0 and above), IETF protocol assignments.
                return OutboundRefusal.Reserved;
            }

            var isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) ||
                (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 198 && b[1] is 18 or 19);
            return isPrivate && !Options.AllowPrivateNetworks ? OutboundRefusal.PrivateNetwork : OutboundRefusal.None;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal)
            {
                return OutboundRefusal.LinkLocal;
            }

            if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast)
            {
                return OutboundRefusal.Reserved;
            }

            var first = address.GetAddressBytes()[0];
            var uniqueLocal = (first & 0xFE) == 0xFC;
            return (uniqueLocal || address.IsIPv6SiteLocal) && !Options.AllowPrivateNetworks ? OutboundRefusal.PrivateNetwork : OutboundRefusal.None;
        }

        return OutboundRefusal.Reserved;
    }

    /// <summary>
    /// An HTTP handler that connects only to allowed addresses (each resolved address is checked; the first allowed one is used) and does not
    /// follow redirects. Use it for every client that calls an administrator-entered URL.
    /// </summary>
    public SocketsHttpHandler CreateHandler(TimeSpan? connectTimeout = null)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = IPAddress.TryParse(context.DnsEndPoint.Host.Trim('[', ']'), out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
                var allowed = addresses.Where(a => CheckAddress(a) == OutboundRefusal.None).ToList();
                if (allowed.Count == 0)
                {
                    var reason = addresses.Length == 0 ? OutboundRefusal.Unresolvable : CheckAddress(addresses[0]);
                    throw new OutboundRefusedException(context.DnsEndPoint.Host, reason);
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed.ToArray(), context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }
}

/// <summary>Thrown by the guarded handler when every address of a host is refused (surfaces as an <see cref="HttpRequestException"/>).</summary>
public sealed class OutboundRefusedException(string host, OutboundRefusal reason)
    : IOException($"Outbound connection to '{host}' refused: {reason}")
{
    public string Host { get; } = host;

    public OutboundRefusal Reason { get; } = reason;
}
