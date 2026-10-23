using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Application;

public sealed record AccessTokenDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    string State)
{
    public static AccessTokenDto From(PersonalAccessToken token, DateTimeOffset now) => new(
        token.Id, token.Name, token.Prefix, token.Scopes, token.CreatedAt, token.ExpiresAt, token.LastUsedAt, token.RevokedAt,
        token.RevokedAt is not null ? "revoked" : token.ExpiresAt <= now ? "expired" : "active");
}

/// <summary>The new token: <see cref="Token"/> is shown once and never stored.</summary>
public sealed record CreatedAccessTokenDto(AccessTokenDto Details, string Token);

/// <summary>
/// Personal access tokens: <c>sst_&lt;prefix&gt;_&lt;secret&gt;</c>. Only a SHA-256 hash is stored; lookup is by prefix and the
/// hash is compared in constant time.
/// </summary>
public sealed class AccessTokenService(IdentityStoreDbContext db, IdentitySettingsCache settings, IClock clock)
{
    public const string TokenPrefix = "sst_";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public async Task<Result<CreatedAccessTokenDto>> CreateAsync(
        Guid userId, string name, IReadOnlyCollection<string> scopes, int? days, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var security = settings.Security;
        var lifetime = days ?? security.TokenDefaultDays;
        if (lifetime < 1 || lifetime > security.TokenMaxDays)
        {
            return Error.Validation([new FieldError("days", "identity.token.lifetime", "identity.errors.tokenLifetime",
                new Dictionary<string, object?> { ["max"] = security.TokenMaxDays })]);
        }

        var prefix = Random(8);
        var secret = Random(32);
        var token = new PersonalAccessToken
        {
            UserId = userId,
            Name = name.Trim(),
            Prefix = prefix,
            SecretHash = Hash(secret),
            Scopes = scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(lifetime),
        };
        db.Tokens.Add(token);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CreatedAccessTokenDto(AccessTokenDto.From(token, clock.UtcNow), $"{TokenPrefix}{prefix}_{secret}");
    }

    public async Task<IReadOnlyList<AccessTokenDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await db.Tokens.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return tokens.Select(t => AccessTokenDto.From(t, clock.UtcNow)).ToList();
    }

    /// <summary>Revokes a token of <paramref name="userId"/> (null = any user, for administrators). 404 when not theirs.</summary>
    public async Task<Result<AccessTokenDto>> RevokeAsync(Guid tokenId, Guid? userId, CancellationToken cancellationToken)
    {
        var token = await db.Tokens.FirstOrDefaultAsync(t => t.Id == tokenId && (userId == null || t.UserId == userId), cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
        {
            return Error.NotFound();
        }

        token.RevokedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return AccessTokenDto.From(token, clock.UtcNow);
    }

    /// <summary>Finds the usable token for a presented value, or null. Records last use (at most once a minute).</summary>
    public async Task<PersonalAccessToken?> ValidateAsync(string presented, string? ip, CancellationToken cancellationToken)
    {
        if (!TryParseToken(presented, out var prefix, out var secret))
        {
            return null;
        }

        var token = await db.Tokens.FirstOrDefaultAsync(t => t.Prefix == prefix, cancellationToken).ConfigureAwait(false);
        if (token is null || !CryptographicOperations.FixedTimeEquals(token.SecretHash, Hash(secret)) || !token.IsUsable(clock.UtcNow))
        {
            return null;
        }

        if (token.LastUsedAt is null || clock.UtcNow - token.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            token.LastUsedAt = clock.UtcNow;
            token.LastUsedIp = ip;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return token;
    }

    public static bool LooksLikeToken(string? value) => value is not null && value.StartsWith(TokenPrefix, StringComparison.Ordinal);

    public static bool TryParseToken(string presented, out string prefix, out string secret)
    {
        prefix = secret = string.Empty;
        if (!LooksLikeToken(presented))
        {
            return false;
        }

        var parts = presented[TokenPrefix.Length..].Split('_');
        if (parts.Length != 2 || parts[0].Length != 8 || parts[1].Length != 32 || !parts.All(p => p.All(c => Alphabet.Contains(c, StringComparison.Ordinal))))
        {
            return false;
        }

        (prefix, secret) = (parts[0], parts[1]);
        return true;
    }

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.ASCII.GetBytes(secret));

    private static string Random(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] & 31];
        }

        return new string(chars);
    }
}
