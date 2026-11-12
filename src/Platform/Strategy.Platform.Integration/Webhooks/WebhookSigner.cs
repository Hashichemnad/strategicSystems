using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary>
/// <c>X-Strategy-Signature: v1=&lt;hex HMAC-SHA256(secret, "&lt;timestamp&gt;.&lt;body&gt;")&gt;</c> with <c>X-Strategy-Timestamp</c> in Unix
/// seconds (ADR 0194). <see cref="Verify"/> is what a receiver does (also shown in the developer portal).
/// </summary>
public static class WebhookSigner
{
    public const string SignatureHeader = "X-Strategy-Signature";
    public const string TimestampHeader = "X-Strategy-Timestamp";
    public const string EventHeader = "X-Strategy-Event";
    public const string DeliveryHeader = "X-Strategy-Delivery";

    public static string Sign(string secret, long timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var payload = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body);
        return "v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload));
    }

    /// <summary>True when the signature matches and the timestamp is within <paramref name="tolerance"/> of <paramref name="now"/>.</summary>
    public static bool Verify(string secret, string timestampHeader, string signatureHeader, string body, DateTimeOffset now, TimeSpan tolerance)
    {
        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp, body));
        var presented = Encoding.ASCII.GetBytes(signatureHeader ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>Delay before attempt <paramref name="attempt"/> + 1: <c>base · 2^(attempt − 1)</c>, at most 6 hours.</summary>
    public static TimeSpan Backoff(int attempt, int baseSeconds)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var seconds = Math.Min(baseSeconds * Math.Pow(2, exponent), TimeSpan.FromHours(6).TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
