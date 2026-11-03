using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;

namespace Strategy.IntegrationTests.Tasks;

internal static class TaskTestHelpers
{
    /// <summary>A service scope whose request context is <paramref name="user"/> with the claims the sign-in issues (channel ui).</summary>
    public static Task<AsyncServiceScope> UserScopeAsync(ApiFactory factory, TestUser user)
    {
        var scope = factory.Services.CreateAsyncScope();
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
                new Claim(StrategyClaims.EmployeeId, user.EmployeeId!.Value.ToString()),
            ],
            "test");
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Ui, new ClaimsPrincipal(identity));
        return Task.FromResult(scope);
    }
}
