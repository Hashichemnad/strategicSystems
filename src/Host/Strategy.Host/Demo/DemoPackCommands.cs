using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Demo;

/// <summary>Host commands <c>--seed-demo --password-file &lt;f&gt;</c> and <c>--remove-demo</c> (ADR 0153).</summary>
internal static class DemoPackCommands
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        if (await services.GetRequiredService<ModuleMigrator>().HasPendingMigrationsAsync())
        {
            await output.WriteLineAsync("The database is not up to date. Run --migrate first, then the demo pack command.");
            return 3;
        }

        var demo = services.GetRequiredService<DemoPackService>();
        DemoPackStatusDto outcome;
        if (args.Contains("--remove-demo", StringComparer.Ordinal))
        {
            outcome = await demo.RemoveAsync(CancellationToken.None);
        }
        else
        {
            var index = Array.IndexOf(args, "--password-file");
            var file = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            if (file is null || (!File.Exists(file) && file != "/dev/stdin"))
            {
                await output.WriteLineAsync("Usage: --seed-demo --password-file <file containing the demo users' password, or /dev/stdin>");
                return 2;
            }

            var password = (await File.ReadAllTextAsync(file)).TrimEnd('\r', '\n');
            await using (var scope = services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentitySettingsCache>();
                await identity.ReloadAsync();
                if (await DemoPackComponent.CheckPasswordAsync(scope.ServiceProvider.GetRequiredService<StrategyUserManager>(), password, identity.Security) is { } error)
                {
                    await output.WriteLineAsync("The password does not meet the password policy: " +
                        string.Join(", ", error.Fields?.Select(f => f.Code) ?? []));
                    return 2;
                }
            }

            outcome = await demo.LoadAsync(password, CancellationToken.None);
        }

        foreach (var step in outcome.Contributors)
        {
            await output.WriteLineAsync($"{step.Code,-12} {step.Status,-8} {step.Records,5} record(s){(step.ErrorCode is null ? string.Empty : " — " + step.ErrorCode)}");
        }

        if (outcome.Contributors.Any(s => s.Status == DemoPackService.Failed))
        {
            return 1;
        }

        await output.WriteLineAsync(args.Contains("--remove-demo", StringComparer.Ordinal)
            ? "Demo data removed (deactivated / cancelled; demo users disabled)."
            : $"Demo data loaded. Demo users sign in as <name>@demo.example.org, e.g. {PlatformDemoPack.Email("director")}.");
        return 0;
    }
}
