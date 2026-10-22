using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Modules;

public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Creates the module's route group <c>/api/v1/&lt;module&gt;</c>. Endpoints stay mapped (so the OpenAPI document is
    /// complete) but answer 404 <c>module.disabled</c> while <c>Modules:&lt;Name&gt;:Enabled</c> is false.
    /// Phase 2 moves the switch to the Settings centre.
    /// </summary>
    public static RouteGroupBuilder MapModuleGroup(this IEndpointRouteBuilder endpoints, IModule module)
    {
        var group = endpoints.MapGroup($"/api/v1/{module.Name}").WithTags(module.Name);
        group.AddEndpointFilter(async (context, next) =>
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            return IsEnabled(configuration, module.Name)
                ? await next(context).ConfigureAwait(false)
                : new Error("module.disabled", "errors.moduleDisabled", ErrorKind.Disabled).ToProblem();
        });
        return group;
    }

    public static bool IsEnabled(IConfiguration configuration, string moduleName) =>
        configuration.GetValue($"Modules:{moduleName}:Enabled", defaultValue: false);
}
