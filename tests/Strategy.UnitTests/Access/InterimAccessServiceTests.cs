using System.Security.Claims;
using Strategy.Platform.Access;

namespace Strategy.UnitTests.Access;

public class InterimAccessServiceTests
{
    private static ClaimsPrincipal Principal(string? type, bool admin = false)
    {
        if (type is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(StrategyClaims.PrincipalType, type) };
        if (admin)
        {
            claims.Add(new Claim(StrategyClaims.SystemAdmin, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    // permission, anonymous, vendor, user, admin
    [InlineData("org.unit.read", false, false, true, true)]
    [InlineData("org.employee.read", false, false, true, true)]
    [InlineData("org.unit.manage", false, false, false, true)]
    [InlineData("org.import.manage", false, false, false, true)]
    [InlineData("identity.user.manage", false, false, false, true)]
    [InlineData("identity.provider.manage", false, false, false, true)]
    public void Platform_permissions_follow_ADR_0009(string permission, bool anonymous, bool vendor, bool user, bool admin)
    {
        var access = new InterimAccessService(allowModulesForDevelopment: false);

        access.Can(Principal(null), permission).ShouldBe(anonymous);
        access.Can(Principal(StrategyClaims.VendorPrincipal, admin: true), permission).ShouldBe(vendor);
        access.Can(Principal(StrategyClaims.InternalPrincipal), permission).ShouldBe(user);
        access.Can(Principal(StrategyClaims.InternalPrincipal, admin: true), permission).ShouldBe(admin);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Module_permissions_are_open_only_in_development_and_never_to_anonymous(bool development)
    {
        var access = new InterimAccessService(development);

        access.Can(Principal(StrategyClaims.InternalPrincipal), "sample.item.read").ShouldBe(development);
        access.Can(Principal(StrategyClaims.InternalPrincipal, admin: true), "sample.item.update").ShouldBe(development);
        access.Can(Principal(null), "sample.item.read").ShouldBeFalse();
    }

    [Fact]
    public void System_principal_is_allowed_but_a_forged_system_claim_is_not()
    {
        var access = new InterimAccessService(false);
        var forged = new ClaimsPrincipal(new ClaimsIdentity([new Claim(StrategyClaims.PrincipalType, StrategyClaims.SystemPrincipal)], "Identity.Application"));

        access.Can(StrategyClaims.CreateSystemPrincipal(), "identity.user.manage").ShouldBeTrue();
        access.Can(forged, "org.unit.read").ShouldBeFalse();
    }

    [Fact]
    public void Scope_returns_nothing_when_refused()
    {
        var access = new InterimAccessService(false);
        var data = new[] { "a", "b" }.AsQueryable();

        access.ApplyScope(data, Principal(StrategyClaims.InternalPrincipal), "org.unit.read").Count().ShouldBe(2);
        access.ApplyScope(data, Principal(StrategyClaims.InternalPrincipal), "org.unit.manage").Count().ShouldBe(0);
    }
}
