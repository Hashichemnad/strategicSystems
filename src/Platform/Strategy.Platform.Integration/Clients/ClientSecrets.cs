using System.Security.Cryptography;
using System.Text;
using Strategy.Platform.Identity.Authentication;

namespace Strategy.Platform.Integration.Clients;

/// <summary>
/// Identifiers and secrets of integration clients (ADR 0191): client id <c>ic_&lt;16&gt;</c>; API key <c>sik_&lt;prefix&gt;_&lt;secret&gt;</c>;
/// OAuth secret <c>sis_&lt;prefix&gt;_&lt;secret&gt;</c>. Only SHA-256 hashes are stored; lookup is by prefix.
/// </summary>
public static class ClientSecrets
{
    public const string OAuthSecretPrefix = "sis_";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string NewClientId() => IntegrationClientKeys.ClientIdPrefix + Random(16);

    public static (string Value, string Prefix, byte[] Hash) NewApiKey() => New(IntegrationClientKeys.KeyPrefix);

    public static (string Value, string Prefix, byte[] Hash) NewOAuthSecret() => New(OAuthSecretPrefix);

    public static bool TryParse(string presented, out string prefix, out byte[] hash)
    {
        prefix = string.Empty;
        hash = [];
        string body;
        if (presented.StartsWith(IntegrationClientKeys.KeyPrefix, StringComparison.Ordinal))
        {
            body = presented[IntegrationClientKeys.KeyPrefix.Length..];
        }
        else if (presented.StartsWith(OAuthSecretPrefix, StringComparison.Ordinal))
        {
            body = presented[OAuthSecretPrefix.Length..];
        }
        else
        {
            return false;
        }

        var parts = body.Split('_');
        if (parts.Length != 2 || parts[0].Length != 8 || parts[1].Length != 32 || !parts.All(p => p.All(c => Alphabet.Contains(c, StringComparison.Ordinal))))
        {
            return false;
        }

        prefix = parts[0];
        hash = Hash(parts[1]);
        return true;
    }

    public static bool Matches(byte[] stored, byte[] presented) => CryptographicOperations.FixedTimeEquals(stored, presented);

    public static string Random(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] & 31];
        }

        return new string(chars);
    }

    private static (string, string, byte[]) New(string kind)
    {
        var prefix = Random(8);
        var secret = Random(32);
        return ($"{kind}{prefix}_{secret}", prefix, Hash(secret));
    }

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.ASCII.GetBytes(secret));
}
