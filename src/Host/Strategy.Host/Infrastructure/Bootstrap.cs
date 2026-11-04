using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Domain;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Mcp.Application;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// Install-time and development helpers:
/// <c>--create-admin --email &lt;e&gt; --password-file &lt;f&gt;</c> creates the first administrator (platform administrator roles, ADR 0016), and the
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

        var (result, created) = await CreateUserAsync(users, email, password, employeeId: null);
        if (result.Succeeded)
        {
            await scope.ServiceProvider.GetRequiredService<IUserRoleQuery>().GrantPlatformAdministratorAsync(created.Id);
        }
        else
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

    /// <summary>
    /// <c>--setup-db-roles</c>: moves an installation that runs as the database owner to the two-role setup (ADR 0018). Idempotent:
    /// a second run updates the password and re-applies the grants.
    /// </summary>
    public static async Task<int> SetupDatabaseRolesAsync(IServiceProvider services, TextWriter output)
    {
        var outcome = await services.GetRequiredService<RuntimeRoleSetup>().EnsureRoleAsync();
        switch (outcome)
        {
            case RuntimeRoleOutcome.NotConfigured:
                await output.WriteLineAsync(
                    "Set ConnectionStrings__StrategyMigrations (the owner) and ConnectionStrings__Strategy (the runtime role, with user name and password).");
                return 2;
            case RuntimeRoleOutcome.SameAsOwner:
                await output.WriteLineAsync("ConnectionStrings__Strategy uses the owner role; give it a separate runtime role first.");
                return 2;
        }

        await services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
        await output.WriteLineAsync(outcome == RuntimeRoleOutcome.Created
            ? "Runtime database role created and granted; audit.entry is SELECT + INSERT only for it. Restart the application with ConnectionStrings__Strategy."
            : "Runtime database role updated and grants re-applied; audit.entry is SELECT + INSERT only for it.");
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

        // Mariam heads the Strategy & Performance Department: the department director of the sample approval chain (Phase 4).
        var department = (await dispatcher.SendAsync(new GetUnit(strategy))).Value;
        await dispatcher.SendAsync(new UpdateUnit(
            strategy, department.Code, department.NameAr, department.NameEn, department.LevelCode, director, null, null, null, department.Version));

        // Phase 7: she also heads the organisation (the demo General Manager), so the seeded formation chain (Q-T5: department director →
        // General Manager) has approvers in the demo.
        var organisation = (await dispatcher.SendAsync(new GetUnit(root))).Value;
        await dispatcher.SendAsync(new UpdateUnit(
            root, organisation.Code, organisation.NameAr, organisation.NameEn, organisation.LevelCode, director, null, null, null, organisation.Version));

        var users = provider.GetRequiredService<StrategyUserManager>();
        var password = configuration["Seed:Password"] ?? "Strategy#Demo2026";
        var (admin, adminUser) = await CreateUserAsync(users, "mariam@strategy.local", password, director);
        var (user, analystUser) = await CreateUserAsync(users, "ahmed@strategy.local", password, analyst);
        if (admin.Succeeded)
        {
            await provider.GetRequiredService<IUserRoleQuery>().GrantPlatformAdministratorAsync(adminUser.Id);
            await AccessGrants.AssignRoleAsync(provider, "sample.editor", AccessGrants.User, adminUser.Id);

            // Phase 12 demo: Mariam is the Strategy Office (PMO) and the performance administrator.
            await AccessGrants.AssignRoleAsync(provider, "kpi.strategy-office", AccessGrants.User, adminUser.Id);
            await AccessGrants.AssignRoleAsync(provider, "kpi.administrator", AccessGrants.User, adminUser.Id);
        }

        // Phase 12 demo: everyone may open Performance Management (each person sees the KPIs they are responsible for).
        await AccessGrants.GrantSystemAccessAsync(provider, "kpi", AccessGrants.Everyone, null);

        // Demo: everyone may open the sample system; Ahmed edits his own items, his department sees them.
        await AccessGrants.GrantSystemAccessAsync(provider, "sample", AccessGrants.Everyone, null);
        await AccessGrants.AssignRoleAsync(provider, "sample.viewer", AccessGrants.Everyone, null);
        if (user.Succeeded)
        {
            await AccessGrants.AssignRoleAsync(provider, "sample.editor", AccessGrants.User, analystUser.Id);
        }

        // Phase 8 demo: everyone may open Portfolio Management (requester role); Mariam is the PM Office, DG Office, Budget Section and
        // Administration Dept of the demo, so the three request approval routes and the quotation hand-off run with two people.
        await AccessGrants.GrantSystemAccessAsync(provider, "portfolio", AccessGrants.Everyone, null);
        if (admin.Succeeded)
        {
            foreach (var role in new[]
                     {
                         "portfolio.administrator", "portfolio.pm-office", "portfolio.dg-office", "portfolio.budget-section", "portfolio.administration",
                         "portfolio.finance-section", "portfolio.gm-office",
                     })
            {
                await AccessGrants.AssignRoleAsync(provider, role, AccessGrants.User, adminUser.Id);
            }
        }

        // Phase 14 demo: everyone may open Requests, the Document Registry and IP; Mariam is the Strategy Office of all three, the request types
        // and Document Registry administrator and the IP administrator (Emirates IDs, MOET form).
        foreach (var system in new[] { "requests", "docreg", "ip" })
        {
            await AccessGrants.GrantSystemAccessAsync(provider, system, AccessGrants.Everyone, null);
        }

        if (admin.Succeeded)
        {
            foreach (var role in new[]
                     {
                         "requests.office", "requests.type-administrator", "docreg.office", "docreg.administrator", "ip.office", "ip.administrator",
                         "risk.risk-office", "risk.risk-committee",
                     })
            {
                await AccessGrants.AssignRoleAsync(provider, role, AccessGrants.User, adminUser.Id);
            }
        }

        // Phase 13 demo: everyone may open Risk Management (each person sees the risks they own); Mariam is the Risk Office and sits on the
        // Risk Committee (role assignment above).
        await AccessGrants.GrantSystemAccessAsync(provider, "risk", AccessGrants.Everyone, null);

        // Phase 9 (ADR 0075): strategic objectives live in the KPI strategy hierarchy, shared by Portfolio and Performance Management.
        if (provider.GetService<Strategy.Modules.Kpi.Contracts.IKpiStrategyCatalog>() is { } strategyCatalog)
        {
            await strategyCatalog.EnsureObjectivesAsync(
            [
                new("digital-government", "حكومة رقمية استباقية", "Proactive digital government", true, 10),
                new("financial-sustainability", "استدامة مالية", "Financial sustainability", true, 20),
                new("institutional-excellence", "تميز مؤسسي", "Institutional excellence", true, 30),
            ]);
        }

        if (configuration["Seed:AdminTotpKey"] is { Length: > 0 } totpKey && await users.FindByNameAsync("mariam@strategy.local") is { } seededAdmin)
        {
            // E2E only: a known authenticator key lets the browser tests compute codes.
            var store = (IUserAuthenticatorKeyStore<StrategyUser>)provider.GetRequiredService<IUserStore<StrategyUser>>();
            await store.SetAuthenticatorKeyAsync(seededAdmin, totpKey, CancellationToken.None);
            seededAdmin.TotpEnabled = true;
            seededAdmin.TwoFactorEnabled = true;
            await users.UpdateAsync(seededAdmin);
        }

        // Phase 5 demo: the offline demo AI provider (Development only, AI:EnableDemoProvider) switched on, and the MCP server on.
        if (configuration.GetValue("AI:EnableDemoProvider", defaultValue: false))
        {
            var ai = provider.GetRequiredService<AiAdministration>();
            var demo = await ai.SaveProviderAsync(null, new AiProviderInput("Demo provider (offline)", AiProviderTypes.Demo, null, null, false, "demo-small",
                ["demo-small", "demo-large"], null, 2000, 30, true), CancellationToken.None);
            if (demo.IsSuccess)
            {
                var editor = provider.GetRequiredService<ISettingsEditor>();
                var aiSettings = await editor.GetDocumentAsync<AiSettings>();
                await editor.SaveAsync(aiSettings.Value with { Enabled = true, DefaultProviderId = demo.Value.Id }, aiSettings.Version, _ => []);
            }
        }

        var mcp = await provider.GetRequiredService<ISettingsEditor>().GetDocumentAsync<McpSettings>();
        await provider.GetRequiredService<ISettingsEditor>().SaveAsync(mcp.Value with { Enabled = true }, mcp.Version, _ => []);

        LogSeeded(logger, admin.Succeeded && user.Succeeded);
    }

    private static async Task<(IdentityResult Result, StrategyUser User)> CreateUserAsync(StrategyUserManager users, string email, string password, Guid? employeeId)
    {
        var user = new StrategyUser
        {
            UserName = email.Trim().ToLowerInvariant(),
            Email = email.Trim(),
            EmailConfirmed = true,
            EmployeeId = employeeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return (await users.CreateAsync(user, password), user);
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed created the demo organisation (users created: {UsersCreated})")]
    private static partial void LogSeeded(ILogger logger, bool usersCreated);
}
