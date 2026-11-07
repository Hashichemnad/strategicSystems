using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;

namespace Strategy.UnitTests.Access;

/// <summary>The Members lists' default role mapping per system (ADR 0166) and the synthesised delegation permission (ADR 0167).</summary>
public class MemberRolesTests
{
    private static readonly IReadOnlyList<IAccessCatalogueSource> Catalogues = new[]
        {
            typeof(Strategy.Modules.Benchmarking.BenchModule), typeof(Strategy.Modules.Sample.SampleModule),
            typeof(Strategy.Modules.DocumentRegistry.DocumentRegistryModule), typeof(Strategy.Modules.IpRegistry.IpRegistryModule),
            typeof(Strategy.Modules.Kpi.KpiModule), typeof(Strategy.Modules.AuditManagement.AuditMgmtModule), typeof(Strategy.Modules.Teams.TeamsModule),
            typeof(Strategy.Modules.Cap.CapModule), typeof(Strategy.Modules.Risk.RiskModule), typeof(Strategy.Modules.Tasks.TasksModule),
            typeof(Strategy.Modules.Requests.RequestsModule), typeof(Strategy.Modules.Portfolio.PortfolioModule),
            typeof(Strategy.Platform.Organisation.OrganisationComponent),
        }
        .Select(t => t.Assembly)
        .SelectMany(a => a.GetTypes())
        .Where(t => typeof(IAccessCatalogueSource).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false } && t.GetConstructor(Type.EmptyTypes) is not null)
        .Select(t => (IAccessCatalogueSource)Activator.CreateInstance(t)!)
        .ToList();

    public static TheoryData<string, string[], string[], string[]> Expected => new()
    {
        { "portfolio", ["administrator"], ["requester"], ["pm-office", "viewer", "department-viewer"] },
        { "kpi", ["administrator"], ["owner"], ["strategy-office", "department-manager", "executive"] },
        { "risk", ["risk-office"], ["owner"], ["coordinator", "viewer", "risk-committee", "internal-audit"] },
        { "teams", ["administrator"], ["employee"], ["department-manager", "viewer"] },
        { "tasks", ["administrator"], ["employee"], ["secretary", "hr"] },
        { "requests", ["office"], ["requester"], ["type-administrator"] },
        { "docreg", ["administrator"], ["reader"], ["office", "leadership"] },
        { "ip", ["administrator"], ["requester"], ["office", "viewer"] },
        { "bench", ["bench-office"], ["participant"], ["coordinator", "viewer"] },
        { "cap", ["cap-office"], ["participant"], ["department-manager", "viewer"] },
        { "auditmgmt", ["audit-office"], ["department-user"], ["chief-audit-executive", "audit-committee", "viewer"] },
        { "sample", ["editor"], ["viewer"], ["approver"] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Each_system_declares_its_default_lists_from_roles_it_seeds(string system, string[] administrators, string[] users, string[] extra)
    {
        var catalogue = Catalogues.Single(c => c.System.Code == system && c.MemberRoles is not null);
        var roles = catalogue.MemberRoles!;
        roles.AdministratorRoles.ShouldBe(administrators);
        roles.UserRoles.ShouldBe(users);
        roles.ExtraRoles.ShouldBe(extra);

        var seeded = catalogue.DefaultRoles.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        roles.AllRoles.ShouldAllBe(code => seeded.Contains(code));
        roles.AllRoles.Distinct(StringComparer.Ordinal).Count().ShouldBe(roles.AllRoles.Count(), "the three lists are disjoint");
    }

    [Fact]
    public void Only_the_twelve_business_systems_have_a_members_page()
    {
        Catalogues.Where(c => c.MemberRoles is not null).Select(c => c.System.Code).Order(StringComparer.Ordinal)
            .ShouldBe(Expected.Select(row => row.Data.Item1).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_members_permission_is_synthesised_and_granted_to_the_administrator_roles_only()
    {
        var portfolio = Catalogues.Single(c => c.System.Code == "portfolio" && c.MemberRoles is not null);
        var permission = MemberCatalogue.PermissionsOf(portfolio).Single(p => p.Code == "portfolio.members.manage");
        permission.LabelAr.ShouldNotBeNullOrWhiteSpace();
        permission.LabelEn.ShouldNotBeNullOrWhiteSpace();
        permission.Scopable.ShouldBeFalse();
        SystemMemberRoles.ManagePermission("portfolio").ShouldBe("portfolio.members.manage");

        var roles = MemberCatalogue.RolesOf(portfolio).ToList();
        roles.Where(r => r.Permissions.Any(p => p.Permission == permission.Code)).Select(r => r.Code).ShouldBe(["administrator"]);
        roles.Count.ShouldBe(portfolio.DefaultRoles.Count());

        var organisation = Catalogues.First(c => c.System.Code == "org");
        MemberCatalogue.PermissionsOf(organisation).ShouldNotContain(p => p.Code.EndsWith(".members.manage", StringComparison.Ordinal));
    }
}
