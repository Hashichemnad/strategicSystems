using Strategy.Platform.Organisation.Domain;

namespace Strategy.UnitTests.Organisation;

public class OrgUnitTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    private static OrgUnit Unit(string code, OrgUnit? parent = null) =>
        OrgUnit.Create(new UnitDetails(code, null, code, "department", null, null, null), parent, Start);

    [Fact]
    public void Path_is_the_chain_of_ids_from_the_root()
    {
        var root = Unit("ROOT");
        var child = Unit("CHILD", root);
        var grandchild = Unit("GRAND", child);

        root.Path.ShouldBe($"/{root.Id:N}/");
        grandchild.Path.ShouldBe($"/{root.Id:N}/{child.Id:N}/{grandchild.Id:N}/");
        grandchild.Depth.ShouldBe(2);
        root.IsAncestorOf(grandchild).ShouldBeTrue();
        grandchild.IsAncestorOf(root).ShouldBeFalse();
    }

    [Fact]
    public void Moving_under_itself_or_a_descendant_is_refused()
    {
        var root = Unit("ROOT");
        var child = Unit("CHILD", root);
        var grandchild = Unit("GRAND", child);

        child.MoveUnder(grandchild).Error!.Code.ShouldBe("org.unit.cycle");
        child.MoveUnder(child).Error!.Code.ShouldBe("org.unit.cycle");
    }

    [Fact]
    public void Moving_rebases_the_subtree()
    {
        var root = Unit("ROOT");
        var a = Unit("A", root);
        var b = Unit("B", root);
        var child = Unit("CHILD", a);
        var grandchild = Unit("GRAND", child);
        var oldPrefix = child.Path;

        child.MoveUnder(b).IsSuccess.ShouldBeTrue();
        grandchild.RebasePath(oldPrefix, child.Path, child.Depth - 2);

        child.ParentId.ShouldBe(b.Id);
        grandchild.Path.ShouldBe($"/{root.Id:N}/{b.Id:N}/{child.Id:N}/{grandchild.Id:N}/");
        grandchild.Depth.ShouldBe(3);
    }

    [Fact]
    public void A_moved_root_becomes_a_child_with_deeper_paths()
    {
        var root = Unit("ROOT");
        var other = Unit("OTHER");
        var oldPrefix = other.Path;
        var below = Unit("BELOW", other);

        other.MoveUnder(root).IsSuccess.ShouldBeTrue();
        below.RebasePath(oldPrefix, other.Path, other.Depth - 0);

        below.Depth.ShouldBe(2);
        below.Path.ShouldStartWith(root.Path);
    }

    [Fact]
    public void Search_text_covers_code_and_both_names_normalised()
    {
        var unit = OrgUnit.Create(new UnitDetails("FIN", "الإدارة الماليّة", "Finance", "department", null, null, null), null, Start);

        unit.NameSearch.ShouldBe("fin الاداره الماليه finance");
    }

    [Fact]
    public void Deactivate_and_activate_follow_the_state()
    {
        var unit = Unit("U");

        unit.Deactivate(Start).IsSuccess.ShouldBeTrue();
        unit.Active.ShouldBeFalse();
        unit.EffectiveTo.ShouldBe(Start);
        unit.Deactivate(Start).Error!.Code.ShouldBe("org.unit.alreadyInactive");
        unit.Activate().IsSuccess.ShouldBeTrue();
        unit.EffectiveTo.ShouldBeNull();
        unit.Activate().Error!.Code.ShouldBe("org.unit.alreadyActive");
    }
}

public class UnitHistoryTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    [Fact]
    public void A_change_closes_the_current_version_and_opens_a_new_one()
    {
        var unit = OrgUnit.Create(new UnitDetails("U", null, "Old name", "department", null, null, null), null, Start);
        var current = OrgUnitVersion.Snapshot(unit, Start);
        var effective = new DateOnly(2025, 6, 1);

        unit.UpdateDetails(new UnitDetails("U", null, "New name", "department", null, null, null));
        var next = UnitHistory.Record(unit, current, effective);

        current.ValidTo.ShouldBe(effective);
        current.NameEn.ShouldBe("Old name");
        next.ShouldNotBeNull();
        next.ValidFrom.ShouldBe(effective);
        next.ValidTo.ShouldBeNull();
        next.NameEn.ShouldBe("New name");
        current.IsValidOn(new DateOnly(2025, 5, 31)).ShouldBeTrue();
        current.IsValidOn(effective).ShouldBeFalse();
        next.IsValidOn(effective).ShouldBeTrue();
    }

    [Fact]
    public void A_change_on_the_start_date_rewrites_the_current_version()
    {
        var unit = OrgUnit.Create(new UnitDetails("U", null, "Old", "department", null, null, null), null, Start);
        var current = OrgUnitVersion.Snapshot(unit, Start);

        unit.UpdateDetails(new UnitDetails("U", null, "Fixed typo", "department", null, null, null));

        UnitHistory.Record(unit, current, Start).ShouldBeNull();
        current.NameEn.ShouldBe("Fixed typo");
        current.ValidTo.ShouldBeNull();
    }

    [Theory]
    [InlineData("2024-12-31", "org.unit.effectiveDateTooEarly")]
    [InlineData("2025-03-02", "org.unit.effectiveDateInFuture")]
    public void Effective_date_must_lie_between_the_current_version_and_today(string date, string code)
    {
        var unit = OrgUnit.Create(new UnitDetails("U", null, "U", "department", null, null, null), null, Start);
        var current = OrgUnitVersion.Snapshot(unit, Start);

        var result = UnitHistory.CanApply(current, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2025, 3, 1));

        result.Error!.Code.ShouldBe(code);
    }
}

public class EmployeeTests
{
    private static EmployeeDetails Details(Guid unit, Guid? manager = null, string title = "Analyst") =>
        new("E1", "موظف", "Employee", " Person@Example.ORG ", null, null, title, "G9", unit, manager, null);

    [Fact]
    public void Email_is_stored_trimmed_and_lower_case()
    {
        Employee.Create(Details(Guid.NewGuid())).Email.ShouldBe("person@example.org");
    }

    [Fact]
    public void Update_reports_placement_changes_only()
    {
        var unit = Guid.NewGuid();
        var employee = Employee.Create(Details(unit));

        employee.Update(Details(unit) with { Mobile = "+971500000000" }).ShouldBeFalse();
        employee.Update(Details(unit, title: "Senior analyst")).ShouldBeTrue();
        employee.Update(Details(Guid.NewGuid(), title: "Senior analyst")).ShouldBeTrue();
    }

    [Fact]
    public void Assignment_history_closes_and_opens_on_placement_change()
    {
        var employee = Employee.Create(Details(Guid.NewGuid()));
        var first = EmployeeAssignment.Snapshot(employee, new DateOnly(2024, 1, 1));
        var newUnit = Guid.NewGuid();
        employee.Update(Details(newUnit));

        var second = first.Record(employee, new DateOnly(2025, 1, 1));

        first.ValidTo.ShouldBe(new DateOnly(2025, 1, 1));
        second!.UnitId.ShouldBe(newUnit);
        second.ValidTo.ShouldBeNull();
    }

    [Fact]
    public void Status_change_to_the_same_status_is_refused()
    {
        var employee = Employee.Create(Details(Guid.NewGuid()));

        employee.ChangeStatus(EmployeeStatus.OnLeave).IsSuccess.ShouldBeTrue();
        employee.ChangeStatus(EmployeeStatus.OnLeave).Error!.Code.ShouldBe("org.employee.statusUnchanged");
        employee.ChangeStatus(EmployeeStatus.Left).IsSuccess.ShouldBeTrue();
        employee.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)] // "<svg": scripts possible, refused
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, null)] // PDF
    public void Photo_type_comes_from_the_magic_number(byte[] content, string? expected)
    {
        EmployeePhoto.DetectImageType(content).ShouldBe(expected);
    }

    [Fact]
    public void Photo_over_the_limit_is_refused()
    {
        var content = new byte[EmployeePhoto.MaxBytes + 1];
        content[0] = 0xFF;
        content[1] = 0xD8;
        content[2] = 0xFF;

        EmployeePhoto.Create(Guid.NewGuid(), content, DateTimeOffset.UtcNow).Error!.Code.ShouldBe("org.photo.tooLarge");
    }
}
