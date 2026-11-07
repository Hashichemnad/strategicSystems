using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Application;

/// <summary>
/// Sends the security e-mails of ADR 0162 to the account holder: once per event (the event id is the notification's idempotency key), in the
/// account's language, through the outbox. Never contains a secret; never blocks the action (an unusable mail server only means no e-mail).
/// </summary>
internal sealed class SecurityNotifier(
    INotificationService notifications,
    IdentitySettingsCache settings,
    ISettingsStore settingsStore,
    IRequestContext context,
    IClock clock,
    IdentityStoreDbContext db)
{
    /// <summary>Queues <paramref name="template"/> for <paramref name="user"/>. <paramref name="eventId"/> defaults to a new id (one e-mail per action).</summary>
    public async Task NotifyAsync(
        StrategyUser user, NotificationTemplateDefinition template, string? eventId = null, IReadOnlyDictionary<string, string>? values = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(template);
        if (!user.Active || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var regional = await settingsStore.GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false);
        var security = settings.Security;
        var all = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["time"] = Format(clock.UtcNow, regional.TimeZone),
            ["ip"] = string.IsNullOrWhiteSpace(context.IpAddress) ? "—" : context.IpAddress!,
            ["device"] = DeviceSummary(context.UserAgent),
            ["guidanceAr"] = security.SecurityContactAr ?? SecurityEmails.DefaultGuidanceAr,
            ["guidanceEn"] = security.SecurityContactEn ?? SecurityEmails.DefaultGuidanceEn,
        };
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
        {
            all[key] = value;
        }

        await notifications.SendAsync(
            new NotificationRequest(
                template.Key,
                [NotificationRecipient.User(user.Id)],
                all,
                Channels: NotificationChannels.Email,
                IdempotencyKey: eventId ?? $"{template.Key}:{Guid.CreateVersion7():N}"),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The account is locked: one e-mail per lockout. The event id is the lockout end, so the attempts made while the account stays locked
    /// do not send again.
    /// </summary>
    public async Task LockedOutAsync(StrategyUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.LockoutEnd is not { } end || end <= clock.UtcNow)
        {
            return;
        }

        var regional = await settingsStore.GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false);
        await NotifyAsync(user, SecurityEmails.LockedOut,
            string.Create(CultureInfo.InvariantCulture, $"{SecurityEmails.LockedOut.Key}:{user.Id:N}:{end.ToUnixTimeSeconds()}"),
            new Dictionary<string, string> { ["until"] = Format(end, regional.TimeZone) }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>MFA method labels for the e-mail (both languages; the template picks its own).</summary>
    public static Dictionary<string, string> Method(string? method) => method switch
    {
        MfaMethods.Totp => new() { ["methodAr"] = "تطبيق المصادقة", ["methodEn"] = "authenticator app" },
        MfaMethods.Email => new() { ["methodAr"] = "رمز عبر البريد الإلكتروني", ["methodEn"] = "e-mail code" },
        _ => new() { ["methodAr"] = "جميع الطرق (أعاد مسؤول النظام تعيينها)", ["methodEn"] = "all methods (reset by an administrator)" },
    };

    /// <summary>
    /// After every completed sign-in: remembers the device / network (fingerprint only) and, when the security settings ask for it, e-mails the
    /// user about a device not seen before. The first device of an account is never "new".
    /// </summary>
    public async Task SignedInAsync(StrategyUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var fingerprint = Fingerprint(context.UserAgent, context.IpAddress);
        var now = clock.UtcNow;
        var devices = await db.KnownDevices.Where(d => d.UserId == user.Id).OrderByDescending(d => d.LastSeenAt).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var known = devices.Find(d => d.Fingerprint == fingerprint);
        if (known is not null)
        {
            known.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        db.KnownDevices.Add(new KnownDevice { UserId = user.Id, Fingerprint = fingerprint, FirstSeenAt = now, LastSeenAt = now });
        db.KnownDevices.RemoveRange(devices.Skip(KnownDevice.MaxPerUser - 1));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return; // the same device signed in twice at once: already known
        }

        if (devices.Count > 0 && settings.Security.NotifyNewDevice)
        {
            await NotifyAsync(user, SecurityEmails.NewDevice, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>SHA-256 (hex) of browser family, OS family and IP prefix (IPv4 /24, IPv6 /48).</summary>
    public static string Fingerprint(string? userAgent, string? ipAddress)
    {
        var (browser, os) = Families(userAgent);
        var source = $"{browser}|{os}|{IpPrefix(ipAddress)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static string IpPrefix(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var ip))
        {
            return "-";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork
            ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24"
            : Convert.ToHexStringLower(bytes.AsSpan(0, 6)) + "/48";
    }

    /// <summary>"Chrome · Windows" — the browser and operating-system family of a user agent (no version, nothing identifying).</summary>
    public static string DeviceSummary(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "—";
        }

        var (browser, os) = Families(userAgent);
        return $"{browser} · {os}";
    }

    public static (string Browser, string Os) Families(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        bool Has(string value) => ua.Contains(value, StringComparison.OrdinalIgnoreCase);
        var browser = Has("Edg/") || Has("Edge/") ? "Edge"
            : Has("OPR/") || Has("Opera") ? "Opera"
            : Has("Firefox/") || Has("FxiOS") ? "Firefox"
            : Has("Chrome/") || Has("CriOS") || Has("Chromium") ? "Chrome"
            : Has("Safari/") ? "Safari"
            : ua.Length == 0 ? "Unknown" : "Other";
        var os = Has("Windows") ? "Windows"
            : Has("Android") ? "Android"
            : Has("iPhone") || Has("iPad") || Has("iOS") ? "iOS"
            : Has("Mac OS") || Has("Macintosh") ? "macOS"
            : Has("CrOS") ? "ChromeOS"
            : Has("Linux") ? "Linux"
            : "Other";
        return (browser, os);
    }

    private static string Format(DateTimeOffset at, string timeZone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (" + zone.Id + ")";
    }
}
