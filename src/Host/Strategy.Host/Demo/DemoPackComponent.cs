using Microsoft.AspNetCore.Http.HttpResults;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Host.Demo;

/// <summary>Permission codes of the installation set-up area (system <c>platform</c>).</summary>
public static class PlatformSetupPermissions
{
    public const string SystemCode = "platform";

    /// <summary>Set-up wizard (stage 2) and the optional demo pack.</summary>
    public const string SetupManage = "platform.setup.manage";
}

/// <summary>
/// The installation's set-up area: system <c>platform</c> with the <c>platform.setup.manage</c> permission (on the platform administrator role),
/// and the demo pack endpoints under <c>/api/v1/setup/demo-pack</c> (ADR 0153).
/// </summary>
internal sealed class PlatformSetupCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(PlatformSetupPermissions.SystemCode, SystemKind.Platform, "تهيئة المنصة", "Platform set-up", 5);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(PlatformSetupPermissions.SetupManage, "معالج التهيئة وبيانات العرض التجريبي", "Set-up wizard and demo data", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول تهيئة المنصة", "Platform set-up administrator", [new(PlatformSetupPermissions.SetupManage)], RequiresMfa: true),
    ];
}

/// <summary>Request body of <c>POST /setup/demo-pack</c>: the password the demo users sign in with.</summary>
public sealed record DemoPackLoadInput(string? Password);

internal sealed class DemoPackComponent : IPlatformComponent
{
    public string Name => "setup";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAccessCatalogue(new PlatformSetupCatalogue());
        services.AddSingleton<DemoPackService>();
        services.AddScoped<IDemoPackContributor, PlatformDemoPack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapPlatformGroup(this);
        group.MapGet("/demo-pack", async (DemoPackService s, CancellationToken ct) => TypedResults.Ok(await s.StatusAsync(ct)))
            .WithName("GetDemoPack").RequirePermission(PlatformSetupPermissions.SetupManage);
        group.MapPost("/demo-pack", async Task<Results<Ok<DemoPackStatusDto>, ProblemHttpResult>> (
                DemoPackLoadInput body, DemoPackService s, StrategyUserManager users, IdentitySettingsCache identity, CancellationToken ct) =>
            {
                await identity.ReloadAsync(ct);
                var check = await CheckPasswordAsync(users, body.Password, identity.Security);
                return check is null ? TypedResults.Ok(await s.LoadAsync(body.Password!, ct)) : check.ToProblem();
            })
            .WithName("LoadDemoPack").RequirePermission(PlatformSetupPermissions.SetupManage);
        group.MapDelete("/demo-pack", async (DemoPackService s, CancellationToken ct) => TypedResults.Ok(await s.RemoveAsync(ct)))
            .WithName("RemoveDemoPack").RequirePermission(PlatformSetupPermissions.SetupManage);
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
