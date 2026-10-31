using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;

namespace Strategy.UnitTests.Access;

/// <summary>ADR 0015 table: each data scope against owner / team / unit / subtree, on single records and as a query predicate.</summary>
public class ScopeEvaluationTests
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid MyUser = Guid.CreateVersion7();
    private static readonly Guid Report = Guid.CreateVersion7();
    private static readonly Guid ReportUser = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();
    private static readonly Guid MyUnit = Guid.CreateVersion7();
    private static readonly Guid ChildUnit = Guid.CreateVersion7();
    private static readonly Guid OtherUnit = Guid.CreateVersion7();

    private static readonly ScopeMap<Item> Map = new("test.item", x => x.Id, ownerEmployee: x => x.OwnerEmployeeId, ownerUser: x => x.OwnerUserId, unit: x => x.UnitId);

    private static readonly UserAccess Access = new()
    {
        UserId = MyUser,
        EmployeeId = Me,
        UnitId = MyUnit,
        Systems = new HashSet<string>(),
        Permissions = new Dictionary<string, IReadOnlySet<DataScope>>(),
        Roles = [],
        SubtreeUnitIds = [MyUnit, ChildUnit],
        TeamEmployeeIds = [Me, Report],
        TeamUserIds = [MyUser, ReportUser],
    };

    private static readonly Dictionary<string, Item> Records = new[]
    {
        new Item("mine", Guid.CreateVersion7(), Me, null, MyUnit),
        new Item("mine-by-user", Guid.CreateVersion7(), null, MyUser, null),
        new Item("report", Guid.CreateVersion7(), Report, null, OtherUnit),
        new Item("report-by-user", Guid.CreateVersion7(), null, ReportUser, null),
        new Item("colleague", Guid.CreateVersion7(), Stranger, null, MyUnit),
        new Item("child", Guid.CreateVersion7(), Stranger, null, ChildUnit),
        new Item("other", Guid.CreateVersion7(), Stranger, null, OtherUnit),
        new Item("nothing", Guid.CreateVersion7(), null, null, null),
    }.ToDictionary(i => i.Name);

    public static TheoryData<DataScope, string, bool> Cases() => new()
    {
        { DataScope.Own, "mine", true }, { DataScope.Own, "mine-by-user", true }, { DataScope.Own, "report", false },
        { DataScope.Own, "colleague", false }, { DataScope.Own, "child", false }, { DataScope.Own, "other", false },
        { DataScope.Team, "mine", true }, { DataScope.Team, "report", true }, { DataScope.Team, "report-by-user", true },
        { DataScope.Team, "colleague", false }, { DataScope.Team, "other", false },
        { DataScope.Unit, "mine", true }, { DataScope.Unit, "colleague", true }, { DataScope.Unit, "child", false }, { DataScope.Unit, "other", false },
        { DataScope.UnitAndBelow, "colleague", true }, { DataScope.UnitAndBelow, "child", true }, { DataScope.UnitAndBelow, "other", false },
        { DataScope.All, "other", true }, { DataScope.All, "nothing", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Record_check_matches_the_scope_table(DataScope scope, string record, bool expected) =>
        AccessService.InScope(scope, Access, Map.RefOf(Records[record])).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Query_predicate_matches_the_record_check(DataScope scope, string record, bool expected)
    {
        if (scope == DataScope.All)
        {
            return; // All never builds a predicate: the query is returned unchanged.
        }

        var builder = new ScopePredicateBuilder<Item>(Map);
        builder.Add(scope, Access);
        var predicate = builder.Build();

        var visible = predicate is not null && new[] { Records[record] }.AsQueryable().Where(predicate).Any();
        visible.ShouldBe(expected);
    }

    [Fact]
    public void Record_role_ids_widen_the_predicate_and_no_scope_means_nothing()
    {
        new ScopePredicateBuilder<Item>(Map).Build().ShouldBeNull();

        var builder = new ScopePredicateBuilder<Item>(Map);
        builder.Add(DataScope.Own, Access);
        builder.AddIds([Records["other"].Id]);
        var predicate = builder.Build()!;

        Records.Values.AsQueryable().Where(predicate).Select(r => r.Name).Order().ShouldBe(["mine", "mine-by-user", "other"]);
    }

    [Fact]
    public void A_user_without_an_employee_only_owns_by_user()
    {
        var noEmployee = new UserAccess
        {
            UserId = MyUser,
            EmployeeId = null,
            UnitId = null,
            Systems = new HashSet<string>(),
            Permissions = new Dictionary<string, IReadOnlySet<DataScope>>(),
            Roles = [],
            SubtreeUnitIds = [],
            TeamEmployeeIds = [],
            TeamUserIds = [MyUser],
        };
        AccessService.InScope(DataScope.Own, noEmployee, Map.RefOf(Records["mine"])).ShouldBeFalse();
        AccessService.InScope(DataScope.Own, noEmployee, Map.RefOf(Records["mine-by-user"])).ShouldBeTrue();
        AccessService.InScope(DataScope.Unit, noEmployee, Map.RefOf(Records["colleague"])).ShouldBeFalse();
        AccessService.InScope(DataScope.Own, noEmployee, new ResourceRef("t", Guid.Empty, OwnerEmployeeId: Guid.Empty)).ShouldBeFalse();
    }

    [Fact]
    public void Scope_codes_round_trip()
    {
        foreach (var scope in Enum.GetValues<DataScope>())
        {
            DataScopes.TryParse(scope.ToCode(), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(scope);
        }

        DataScopes.TryParse("everything", out _).ShouldBeFalse();
    }

    public sealed record Item(string Name, Guid Id, Guid? OwnerEmployeeId, Guid? OwnerUserId, Guid? UnitId);
}
