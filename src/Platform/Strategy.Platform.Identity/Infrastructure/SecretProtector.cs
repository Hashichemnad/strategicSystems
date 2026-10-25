using Microsoft.AspNetCore.DataProtection;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>Purposes of protected values; one per kind so ciphertext of one kind cannot be replayed as another (ADR 0012).</summary>
public static class SecretPurposes
{
    public const string ProviderSecret = "identity.provider.secret.v1";
    public const string SmtpPassword = "identity.smtp.password.v1";
    public const string SamlSigningKey = "identity.saml.spkey.v1";
    public const string AuthenticatorKey = "identity.authenticator.key.v1";
    public const string SessionTicket = "identity.session.ticket.v1";
}

/// <summary>Encrypts secrets at rest with ASP.NET Core Data Protection (keys persisted in the database).</summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

    string? Unprotect(string purpose, string? protectedValue);
}

internal sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string plaintext) => provider.CreateProtector(purpose).Protect(plaintext);

    public string? Unprotect(string purpose, string? protectedValue) =>
        string.IsNullOrEmpty(protectedValue) ? null : provider.CreateProtector(purpose).Unprotect(protectedValue);
}
