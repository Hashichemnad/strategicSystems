using System.Net;
using System.Net.Sockets;

namespace Strategy.Platform.Integration.Clients;

/// <summary>An IP allow-list entry: a single address or a CIDR block (IPv4 or IPv6; IPv4-mapped IPv6 is judged as IPv4).</summary>
public static class IpAllowList
{
    public static bool TryParse(string entry, out (IPAddress Network, int PrefixLength) block)
    {
        block = default;
        var text = entry.Trim();
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        var addressText = slash < 0 ? text : text[..slash];
        if (!IPAddress.TryParse(addressText, out var address))
        {
            return false;
        }

        var max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var length = max;
        if (slash >= 0 && (!int.TryParse(text[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length)
            || length < 0 || length > max))
        {
            return false;
        }

        block = (address, length);
        return true;
    }

    /// <summary>Empty list = any address. Unparseable entries never match.</summary>
    public static bool Allows(IReadOnlyCollection<string> entries, IPAddress? remote)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return true;
        }

        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        foreach (var entry in entries)
        {
            if (TryParse(entry, out var block) && Contains(block.Network.IsIPv4MappedToIPv6 ? block.Network.MapToIPv4() : block.Network, block.PrefixLength, remote))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IPAddress network, int prefix, IPAddress address)
    {
        if (network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var a = network.GetAddressBytes();
        var b = address.GetAddressBytes();
        var full = prefix / 8;
        for (var i = 0; i < full; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        var rest = prefix % 8;
        if (rest == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - rest));
        return (a[full] & mask) == (b[full] & mask);
    }
}
