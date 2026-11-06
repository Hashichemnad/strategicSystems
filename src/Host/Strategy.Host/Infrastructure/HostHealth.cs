using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// Health page (ADR 0151), host-level checks: version, build and uptime; the Data Protection key ring (a key valid now and its expiry); and
/// certificate expiry of the Data Protection certificate (<c>DataProtection:CertificatePath</c>) and of every public certificate listed in
/// <c>Health:CertificatePaths</c> (PEM / DER, e.g. the reverse proxy's TLS certificate mounted read-only). Warning 30 days before expiry,
/// error when expired or unreadable. Only subjects and dates are shown, never keys or passwords.
/// </summary>
internal sealed class HostHealthContributor(IConfiguration configuration, IKeyManager keys, IClock clock) : IAdminHealthContributor
{
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);
    /// <summary>When the host type was first used in this process (close to the process start).</summary>
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;

    public string Code => "host";

    public string Group => AdminHealthGroups.Platform;

    public int Order => -10;

    public Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var assembly = typeof(HostHealthContributor).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0";
        var since = Started;
        var checks = new List<AdminHealthCheck>
        {
            new("version", Group, AdminHealthStatuses.Ok, "health.version.ok", HealthParams.Of(
                ("version", version), ("startedAt", since), ("uptimeMinutes", (long)(now - since).TotalMinutes),
                ("runtime", Environment.Version.ToString()), ("os", Environment.OSVersion.Platform.ToString()))),
            KeyRing(now),
        };

        var items = new List<AdminHealthItem>();
        if (configuration["DataProtection:CertificatePath"] is { Length: > 0 } dataProtection)
        {
            items.Add(Certificate("dataProtection", dataProtection, configuration["DataProtection:CertificatePassword"], pkcs12: true, now));
        }

        var listed = configuration.GetSection("Health:CertificatePaths").Get<string[]>() ?? [];
        items.AddRange(listed.Where(p => !string.IsNullOrWhiteSpace(p)).Select((path, i) => Certificate($"certificate{i + 1}", path, null, pkcs12: false, now)));
        checks.Add(items.Count == 0
            ? new AdminHealthCheck("certificates", AdminHealthGroups.Security, AdminHealthStatuses.NotConfigured, "health.certificates.none")
            : new AdminHealthCheck("certificates", AdminHealthGroups.Security, AdminHealthStatuses.Worst(items.Select(i => i.Status)),
                items.All(i => i.Status == AdminHealthStatuses.Ok) ? "health.certificates.ok" : "health.certificates.attention", null, items));
        return Task.FromResult<IReadOnlyList<AdminHealthCheck>>(checks);
    }

    private AdminHealthCheck KeyRing(DateTimeOffset now)
    {
        var all = keys.GetAllKeys();
        var current = all.Where(k => !k.IsRevoked && k.ActivationDate <= now && k.ExpirationDate > now).OrderByDescending(k => k.ExpirationDate).FirstOrDefault();
        var latest = all.Where(k => !k.IsRevoked).Select(k => (DateTimeOffset?)k.ExpirationDate).Max();
        // The key ring renews itself before a key expires; no usable key means stored secrets and sessions cannot be protected.
        return current is null
            ? new AdminHealthCheck("dataProtection", AdminHealthGroups.Security, AdminHealthStatuses.Warning, "health.dataProtection.noCurrentKey",
                HealthParams.Of(("keys", all.Count)))
            : new AdminHealthCheck("dataProtection", AdminHealthGroups.Security, AdminHealthStatuses.Ok, "health.dataProtection.ok",
                HealthParams.Of(("keys", all.Count), ("expiresAt", latest),
                    ("protectedWithCertificate", !string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]))));
    }

    /// <summary>Reads a certificate's validity; pure apart from the file read (unit-tested through <see cref="ExpiryStatus"/>).</summary>
    public static AdminHealthItem Certificate(string code, string path, string? password, bool pkcs12, DateTimeOffset now)
    {
        try
        {
            using var certificate = pkcs12 ? X509CertificateLoader.LoadPkcs12FromFile(path, password) : LoadPublic(path);
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var (status, key) = ExpiryStatus(notAfter, now);
            return new AdminHealthItem(code, status, key, HealthParams.Of(
                ("subject", certificate.GetNameInfo(X509NameType.SimpleName, false)), ("notAfter", notAfter),
                ("daysLeft", (long)Math.Floor((notAfter - now).TotalDays)), ("path", Path.GetFileName(path))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new AdminHealthItem(code, AdminHealthStatuses.Error, "health.certificates.unreadable",
                HealthParams.Of(("path", Path.GetFileName(path)), ("error", exception.GetType().Name)));
        }
    }

    public static (string Status, string MessageKey) ExpiryStatus(DateTimeOffset notAfter, DateTimeOffset now) =>
        notAfter <= now ? (AdminHealthStatuses.Error, "health.certificates.expired")
        : notAfter - now <= ExpiryWarning ? (AdminHealthStatuses.Warning, "health.certificates.expiresSoon")
        : (AdminHealthStatuses.Ok, "health.certificates.valid");

    private static X509Certificate2 LoadPublic(string path)
    {
        var text = File.ReadAllText(path);
        return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificateFromFile(path);
    }
}
