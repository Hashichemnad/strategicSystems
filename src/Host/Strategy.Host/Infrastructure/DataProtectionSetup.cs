using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// Data Protection keys in PostgreSQL (<c>core.data_protection_keys</c>), optionally encrypted with a certificate
/// (<c>DataProtection:CertificatePath</c> / <c>DataProtection:CertificatePassword</c>). ADR 0012.
/// </summary>
internal static class DataProtectionSetup
{
    public const string ApplicationName = "Strategy";

    public static IServiceCollection AddStrategyDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DataProtectionKeysDbContext>(DataProtectionKeysDbContext.SchemaName, DataProtectionKeysDbContext.HistoryTable);
        var builder = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeysDbContext>();

        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["DataProtection:CertificatePassword"]);
            builder.ProtectKeysWithCertificate(certificate);
        }

        return services;
    }
}
