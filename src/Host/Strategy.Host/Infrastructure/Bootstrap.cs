using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// Install-time and development helpers:
/// <c>--create-admin --email &lt;e&gt; --password-file &lt;f&gt;</c> creates the first system administrator (ADR 0009), and the
/// Development seed builds a small demo organisation with an administrator and a regular user.
/// </summary>
internal static partial class Bootstrap
{
    public static async Task<int> CreateAdminAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        var email = Argument(args, "--email");
        var passwordFile = Argument(args, "--password-file");
        if (email is null || passwordFile is null || !File.Exists(passwordFile))
        {
            await output.WriteLineAsync("Usage: --create-admin --email <email> --password-file <file containing the password>");
            return 2;
        }

        var password = (await File.ReadAllTextAsync(passwordFile)).TrimEnd('\r', '\n');
        await services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        await scope.ServiceProvider.GetRequiredService<IdentitySettingsCache>().ReloadAsync();
        var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
        if (await users.FindByNameAsync(email) is not null)
        {
            await output.WriteLineAsync($"A user named {email} already exists; nothing changed.");
            return 1;
        }

        var result = await CreateUserAsync(users, email, password, employeeId: null, isAdmin: true);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                await output.WriteLineAsync($"{error.Code}: {error.Description}");
            }

            return 1;
        }

        await output.WriteLineAsync($"System administrator {email} created. Multi-factor sign-in is set up at the first sign-in.");
        return 0;
    }

    /// <summary>Development only: demo structure (4 levels), employees, an administrator and a regular user. Idempotent.</summary>
    public static async Task SeedDevelopmentAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        var org = provider.GetRequiredService<OrgDbContext>();
        if (await org.Units.AnyAsync())
        {
            return;
        }

        await provider.GetRequiredService<IdentitySettingsCache>().ReloadAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var effective = new DateOnly(2024, 1, 1);
        async Task<Guid> Unit(string code, string ar, string en, string level, Guid? parent) =>
            (await dispatcher.SendAsync(new CreateUnit(code, ar, en, level, parent, null, null, null, effective))).Value.Id;

        var root = await Unit("ORG", "المؤسسة التجريبية", "Demo Organisation", "organisation", null);
        var sector = await Unit("SEC-CORP", "قطاع الخدمات المؤسسية", "Corporate Services Sector", "sector", root);
        var strategy = await Unit("DEP-STR", "إدارة الاستراتيجية والأداء", "Strategy & Performance Department", "department", sector);
        var finance = await Unit("DEP-FIN", "إدارة المالية", "Finance Department", "department", sector);
        await Unit("SEC-PMO", "قسم إدارة المشاريع", "Project Management Office", "section", strategy);
        await Unit("SEC-BUD", "قسم الميزانية", "Budget Section", "section", finance);

        async Task<Guid> Employee(string number, string ar, string en, string email, string titleAr, string titleEn, Guid unit, Guid? manager) =>
            (await dispatcher.SendAsync(new CreateEmployee(number, ar, en, email, null, titleAr, titleEn, null, unit, manager, effective))).Value.Id;

        var director = await Employee("E0001", "مريم الحمادي", "Mariam Al Hammadi", "mariam@strategy.local", "مديرة الإدارة", "Department Director", strategy, null);
        var analyst = await Employee("E0002", "أحمد السويدي", "Ahmed Al Suwaidi", "ahmed@strategy.local", "محلل استراتيجي", "Strategy Analyst", strategy, director);
        await Employee("E0003", "فاطمة الكتبي", "Fatima Al Ketbi", "fatima@strategy.local", "محاسبة", "Accountant", finance, null);

        var users = provider.GetRequiredService<StrategyUserManager>();
        var password = configuration["Seed:Password"] ?? "Strategy#Demo2026";
        var admin = await CreateUserAsync(users, "mariam@strategy.local", password, director, isAdmin: true);
        var user = await CreateUserAsync(users, "ahmed@strategy.local", password, analyst, isAdmin: false);
        if (configuration["Seed:AdminTotpKey"] is { Length: > 0 } totpKey && await users.FindByNameAsync("mariam@strategy.local") is { } seededAdmin)
        {
            // E2E only: a known authenticator key lets the browser tests compute codes.
            var store = (IUserAuthenticatorKeyStore<StrategyUser>)provider.GetRequiredService<IUserStore<StrategyUser>>();
            await store.SetAuthenticatorKeyAsync(seededAdmin, totpKey, CancellationToken.None);
            seededAdmin.TotpEnabled = true;
            seededAdmin.TwoFactorEnabled = true;
            await users.UpdateAsync(seededAdmin);
        }

        LogSeeded(logger, admin.Succeeded && user.Succeeded);
    }

    private static async Task<IdentityResult> CreateUserAsync(StrategyUserManager users, string email, string password, Guid? employeeId, bool isAdmin)
    {
        var user = new StrategyUser
        {
            UserName = email.Trim().ToLowerInvariant(),
            Email = email.Trim(),
            EmailConfirmed = true,
            EmployeeId = employeeId,
            IsSystemAdmin = isAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return await users.CreateAsync(user, password);
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed created the demo organisation (users created: {UsersCreated})")]
    private static partial void LogSeeded(ILogger logger, bool usersCreated);
}
