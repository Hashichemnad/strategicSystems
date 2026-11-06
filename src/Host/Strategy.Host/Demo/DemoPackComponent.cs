using Microsoft.AspNetCore.Http.HttpResults;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Host.Demo;

/// <summary>Request body of <c>POST /setup/demo-pack</c>: the password the demo users sign in with.</summary>
public sealed record DemoPackLoadInput(string? Password);

/// <summary>
/// The demo pack endpoints (ADR 0153) under <c>/api/v1/setup/demo-pack</c>: <c>POST</c> loads, <c>DELETE</c> removes, <c>GET …/status</c> reports.
/// The set-up wizard probes the path with <c>GET</c> (405 when the demo pack is installed). Permission <c>settings.setup.manage</c> (ADR 0150).
/// </summary>
internal sealed class DemoPackComponent : IPlatformComponent
{
    public string Name => "setup";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DemoPackService>();
        services.AddScoped<IDemoPackContributor, PlatformDemoPack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapPlatformGroup(this);
        group.MapGet("/demo-pack/status", async (DemoPackService s, CancellationToken ct) => TypedResults.Ok(await s.StatusAsync(ct)))
            .WithName("GetDemoPack").RequirePermission(SettingsPermissions.SetupManage);
        group.MapPost("/demo-pack", async Task<Results<Ok<DemoPackStatusDto>, ProblemHttpResult>> (
                DemoPackLoadInput body, DemoPackService s, StrategyUserManager users, IdentitySettingsCache identity, CancellationToken ct) =>
            {
                await identity.ReloadAsync(ct);
                var check = await CheckPasswordAsync(users, body.Password, identity.Security);
                return check is null ? TypedResults.Ok(await s.LoadAsync(body.Password!, ct)) : check.ToProblem();
            })
            .WithName("LoadDemoPack").RequirePermission(SettingsPermissions.SetupManage);
        group.MapDelete("/demo-pack", async (DemoPackService s, CancellationToken ct) => TypedResults.Ok(await s.RemoveAsync(ct)))
            .WithName("RemoveDemoPack").RequirePermission(SettingsPermissions.SetupManage);
    }

    /// <summary>The demo users' password must satisfy the installation's password policy; checked before anything is created.</summary>
    public static async Task<Error?> CheckPasswordAsync(StrategyUserManager users, string? password, SecuritySettings security)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Error.Validation([new FieldError("password", "validation.required", "validation.required")]);
        }

        var probe = new StrategyUser { UserName = PlatformDemoPack.Email("probe"), Email = PlatformDemoPack.Email("probe") };
        var errors = new List<Microsoft.AspNetCore.Identity.IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(users, probe, password).ConfigureAwait(false)).Errors);
        }

        return errors.Count == 0 ? null : IdentityErrors.ToValidation(Microsoft.AspNetCore.Identity.IdentityResult.Failed([.. errors]), "password", security);
    }
}
