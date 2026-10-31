using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Modules;

public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Creates the module's route group <c>/api/v1/&lt;module&gt;</c>. Endpoints stay mapped (so the OpenAPI document is
    /// complete) but answer 404 <c>module.disabled</c> while the system is switched off in Settings → Systems
    /// (<see cref="ISystemState"/>; <c>Modules:&lt;Name&gt;:Enabled</c> only seeds the first value, ADR 0017).
    /// </summary>
    public static RouteGroupBuilder MapModuleGroup(this IEndpointRouteBuilder endpoints, IModule module)
    {
        var group = endpoints.MapGroup($"/api/v1/{module.Name}").WithTags(module.Name);
        group.AddEndpointFilter(async (context, next) =>
        {
            var systems = context.HttpContext.RequestServices.GetRequiredService<ISystemState>();
            return await systems.IsEnabledAsync(module.Name, context.HttpContext.RequestAborted).ConfigureAwait(false)
                ? await next(context).ConfigureAwait(false)
                : new Error("module.disabled", "errors.moduleDisabled", ErrorKind.Disabled).ToProblem();
        });
        return group;
    }
}
