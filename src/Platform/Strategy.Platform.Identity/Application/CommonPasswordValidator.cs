using System.Collections.Frozen;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Identity.Domain;

namespace Strategy.Platform.Identity.Application;

/// <summary>
/// Refuses well-known passwords offline (OWASP ASVS V2.1.7): the password, or its base word once leading / trailing digits and symbols are
/// removed (e.g. <c>Password2026!</c> → <c>password</c>), must not be on the bundled common-password list, and the password must not
/// contain the local part of the user's e-mail address. Works in restricted networks (no breach API is called).
/// </summary>
internal sealed class CommonPasswordValidator : IPasswordValidator<StrategyUser>
{
    public const string ErrorCode = "PasswordCommon";

    private static readonly Lazy<FrozenSet<string>> List = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<StrategyUser> manager, StrategyUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(IsCommon(password, user.Email ?? user.UserName)
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = ErrorCode })
            : IdentityResult.Success);
    }

    public static bool IsCommon(string? password, string? email)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var lower = password.ToLower(CultureInfo.InvariantCulture);
        if (List.Value.Contains(lower) || List.Value.Contains(BaseWord(lower)))
        {
            return true;
        }

        var local = email?.Split('@')[0].ToLower(CultureInfo.InvariantCulture);
        return local is { Length: >= 4 } && lower.Contains(local, StringComparison.Ordinal);
    }

    /// <summary>The password without leading and trailing digits and symbols.</summary>
    internal static string BaseWord(string lower) => lower.Trim().Trim("0123456789!@#$%^&*()_+-=[]{};:'\",.<>/?\\|`~ ".ToCharArray());

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(CommonPasswordValidator).Assembly.GetManifestResourceStream("Strategy.Platform.Identity.common-passwords.txt")
            ?? throw new InvalidOperationException("The common-password list is missing from the assembly.");
        using var reader = new StreamReader(stream);
        var words = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                words.Add(line.Trim());
            }
        }

        return words.ToFrozenSet(StringComparer.Ordinal);
    }
}
