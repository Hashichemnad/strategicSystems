using Strategy.Platform.Access;
using Strategy.Platform.Access.Domain;

namespace Strategy.UnitTests.Access;

public class AccessDomainTests
{
    [Fact]
    public void Locked_role_keeps_its_permissions_and_stays_active_with_mfa()
    {
        var role = Role.Create("access", "administrator", "مسؤول", "Administrator", null, null, requiresMfa: true, builtin: true, locked: true);
        role.SetPermissions([("access.role.manage", "all")]).IsSuccess.ShouldBeTrue("a new locked role can be seeded");

        role.SetPermissions([]).Error!.Code.ShouldBe("access.role.locked");
        role.SetPermissions([("access.role.manage", "own")]).Error!.Code.ShouldBe("access.role.locked");
        role.SetPermissions([("access.role.manage", "all"), ("access.role.read", "all")]).IsSuccess.ShouldBeTrue("adding is allowed");
        role.Update("x", "y", null, null, requiresMfa: true, active: false).Error!.Code.ShouldBe("access.role.locked");
        role.Update("x", "y", null, null, requiresMfa: false, active: true).Error!.Code.ShouldBe("access.role.locked");
    }

    [Fact]
    public void Role_permissions_are_replaced_and_scopes_updated()
    {
        var role = Role.Create("sample", "editor", null, "Editor", null, null, requiresMfa: false);
        role.SetPermissions([("sample.item.read", "all"), ("sample.item.update", "own")]);
        role.SetPermissions([("sample.item.update", "unit")]);

        role.Permissions.Select(p => $"{p.PermissionCode}:{p.Scope}").ShouldBe(["sample.item.update:unit"]);
        role.Key.ShouldBe("sample.editor");
    }

    [Theory]
    [InlineData("2026-01-01", null, "2026-01-01", true)]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-31", true)]
    [InlineData("2026-01-01", "2026-01-31", "2026-02-01", false)]
    [InlineData("2026-02-01", null, "2026-01-31", false)]
    public void Assignment_dates_are_inclusive(string from, string? to, string on, bool active)
    {
        var assignment = RoleAssignment.Create(Guid.CreateVersion7(), PrincipalKinds.User, Guid.CreateVersion7(), Date(from), to is null ? null : Date(to), null);
        assignment.IsActiveOn(Date(on)).ShouldBe(active);
    }

    [Fact]
    public void Ending_an_assignment_that_has_not_started_ends_it_before_it_starts()
    {
        var assignment = RoleAssignment.Create(Guid.CreateVersion7(), PrincipalKinds.Everyone, Guid.CreateVersion7(), new DateOnly(2026, 6, 1), null, " note ");
        assignment.PrincipalId.ShouldBeNull("everyone has no principal id");
        assignment.Note.ShouldBe("note");

        assignment.End(new DateOnly(2026, 5, 1));

        assignment.ValidTo.ShouldBe(new DateOnly(2026, 5, 31));
        assignment.IsActiveOn(new DateOnly(2026, 6, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Platform_systems_cannot_be_switched_off()
    {
        var platform = AccessSystem.Create(new SystemDefinition("org", SystemKind.Platform, "ت", "Org", 10), enabled: false);
        platform.Enabled.ShouldBeTrue();
        platform.Configure(false, null).Error!.Code.ShouldBe("access.system.platformAlwaysOn");

        var module = AccessSystem.Create(new SystemDefinition("sample", SystemKind.Module, "ت", "Sample", 900), enabled: false);
        module.Enabled.ShouldBeFalse();
        module.Configure(true, Guid.CreateVersion7()).IsSuccess.ShouldBeTrue();
        module.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Record_role_type_permission_set_is_replaced()
    {
        var type = RecordRoleType.Create("sample", new RecordRoleDefinition("sample.item", "editor", "م", "Editor", ["sample.item.read", "sample.item.update"]));
        type.SetPermissions(["sample.item.read", "sample.item.read"]);
        type.Permissions.Select(p => p.PermissionCode).ShouldBe(["sample.item.read"]);
    }

    private static DateOnly Date(string value) => DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
