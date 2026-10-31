using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// A user created for a test. Administrators hold the platform administrator roles (ADR 0016) and get a known authenticator key
/// (MFA is required for them by default).
/// </summary>
public sealed record TestUser(Guid Id, string UserName, string Password, Guid? EmployeeId, Guid? UnitId, string? TotpKey, bool IsAdmin);

/// <summary>Creates organisation data and accounts directly through the services, and signs in through the real API.</summary>
public static class TestIdentity
{
    public const string Password = "Test#Password2026";

    /// <summary>Role key of the template module's editor (read all, create, update own).</summary>
    public const string SampleEditor = "sample.editor";

    public static async Task<TestUser> CreateUserAsync(
        this ApiFactory factory, bool admin = false, bool withEmployee = true, bool withMfa = false, Guid? unitId = null, Guid? managerEmployeeId = null,
        params string[] roles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        var suffix = Guid.CreateVersion7().ToString("N")[^10..];
        Guid? employeeId = null;
        var email = $"user.{suffix}@example.test";
        if (withEmployee)
        {
            var dispatcher = provider.GetRequiredService<IDispatcher>();
            if (unitId is null)
            {
                var unit = await dispatcher.SendAsync(new CreateUnit($"T-{suffix}", "وحدة اختبار", $"Test unit {suffix}", "organisation", null, null, null, null, null));
                unitId = unit.Value.Id;
            }

            var employee = await dispatcher.SendAsync(new CreateEmployee(
                $"T{suffix}", "موظف اختبار", $"Test employee {suffix}", email, null, null, null, null, unitId, managerEmployeeId, null));
            employee.IsSuccess.ShouldBeTrue(employee.IsFailure ? employee.Error.Code : null);
            employeeId = employee.Value.Id;
        }
        else
        {
            unitId = null;
        }

        var users = provider.GetRequiredService<StrategyUserManager>();
        var user = new StrategyUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            EmployeeId = employeeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, Password);
        created.Succeeded.ShouldBeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));

        string? totpKey = null;
        if (admin || withMfa)
        {
            totpKey = Totp.NewKey();
            var store = (IUserAuthenticatorKeyStore<StrategyUser>)provider.GetRequiredService<IUserStore<StrategyUser>>();
            await store.SetAuthenticatorKeyAsync(user, totpKey, CancellationToken.None);
            user.TotpEnabled = true;
            user.TwoFactorEnabled = true;
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        if (admin)
        {
            await provider.GetRequiredService<IUserRoleQuery>().GrantPlatformAdministratorAsync(user.Id);
        }

        foreach (var role in roles)
        {
            await TestAccess.GrantRoleAsync(provider, user.Id, role);
        }

        return new TestUser(user.Id, user.UserName, Password, employeeId, unitId, totpKey, admin);
    }

    /// <summary>A SPA-like client signed in as <paramref name="user"/> (password, then TOTP when the user has MFA).</summary>
    public static async Task<HttpClient> SignInAsync(this ApiFactory factory, TestUser user)
    {
        var client = await factory.CreateSpaClientAsync();
        var outcome = await client.LoginAsync(user.UserName, user.Password);
        if (outcome == SignInStatuses.MfaRequired)
        {
            var verify = await client.PostAsJsonAsync("/api/v1/identity/auth/mfa/verify", new { method = "totp", code = await Totp.NextCodeAsync(user.TotpKey!) });
            verify.StatusCode.ShouldBe(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
            outcome = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString();
        }

        outcome.ShouldBe(SignInStatuses.SignedIn);
        await client.RefreshAntiforgeryAsync();
        return client;
    }

    public static async Task<HttpClient> CreateSignedInClientAsync(this ApiFactory factory, bool admin = false, params string[] roles) =>
        await factory.SignInAsync(await factory.CreateUserAsync(admin, roles: roles));

    public static async Task<string?> LoginAsync(this HttpClient client, string login, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/identity/auth/login", new { login, password });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString();
    }

    /// <summary>Antiforgery tokens are bound to the user: fetch a new one after signing in or out (the SPA does the same).</summary>
    public static async Task RefreshAntiforgeryAsync(this HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryToken>("/api/v1/antiforgery/token"))!;
        client.DefaultRequestHeaders.Remove(token.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }

    /// <summary>A client that sends only a bearer token (no cookies, no antiforgery header).</summary>
    public static HttpClient CreateTokenClient(this ApiFactory factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

/// <summary>RFC 6238 TOTP (SHA-1, 6 digits, 30 s), as authenticator apps compute it.</summary>
public static class Totp
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewKey()
    {
        var builder = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in RandomNumberGenerator.GetBytes(20))
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        return builder.ToString();
    }

    private static readonly Dictionary<string, long> LastStepUsed = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim LastStepGate = new(1, 1);

    /// <summary>
    /// The code an authenticator app would show next for this key: the server accepts each time step once (and never an older
    /// one), so repeated sign-ins of one user take the following steps, waiting for the clock only when one step ahead is used.
    /// </summary>
    public static async Task<string> NextCodeAsync(string base32Key)
    {
        var key = base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        await LastStepGate.WaitAsync();
        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;
                var current = now.ToUnixTimeSeconds() / 30;
                var step = LastStepUsed.TryGetValue(key, out var last) ? Math.Max(current, last + 1) : current;
                if (step <= current + 1)
                {
                    LastStepUsed[key] = step;
                    return Code(key, DateTimeOffset.FromUnixTimeSeconds(step * 30));
                }

                await Task.Delay(TimeSpan.FromSeconds(30 - (now.ToUnixTimeSeconds() % 30)) + TimeSpan.FromMilliseconds(100));
            }
        }
        finally
        {
            LastStepGate.Release();
        }
    }

    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Decode(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

#pragma warning disable CA5350 // RFC 6238 authenticator codes are defined with HMAC-SHA1
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Decode(string input)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | Base32.IndexOf(c, StringComparison.Ordinal);
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
