using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>RFC 6238 authenticator codes (HMAC-SHA1, 6 digits, 30-second steps), as authenticator apps compute them.</summary>
internal static class TotpCode
{
    public const int StepSeconds = 30;

    /// <summary>Steps accepted on each side of the current one, as ASP.NET Core Identity's own provider (clock drift).</summary>
    public const int Window = 2;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] key, long step)
    {
        Span<byte> message = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, step);
#pragma warning disable CA5350 // RFC 6238 authenticator codes are defined with HMAC-SHA1
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>The step within ±<see cref="Window"/> of <paramref name="now"/> whose code is <paramref name="code"/>, or null.</summary>
    public static long? MatchingStep(byte[] key, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(code);
        var current = StepAt(now);
        long? match = null;
        for (var step = current - Window; step <= current + Window; step++)
        {
            // Every candidate is compared (constant time, no early exit), so timing does not reveal which step matched.
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step)), expected))
            {
                match ??= step;
            }
        }

        return match;
    }

    public static byte[]? DecodeBase32(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant())
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
