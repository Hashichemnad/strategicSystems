using Strategy.ArchitectureTests.Rules;

namespace Strategy.ArchitectureTests;

/// <summary>Proves the rules fail on violations, using synthetic graphs (the real repository has no violations to show).</summary>
public class RuleSelfTests
{
    [Fact]
    public void Module_referencing_another_modules_internals_is_a_violation()
    {
        var violations = ModuleBoundaryRules.FindViolations(
        [
            Project("Strategy.Modules.Portfolio", "Strategy.Modules.Risk", "Strategy.Platform.Kernel"),
        ]);

        violations.ShouldHaveSingleItem().ShouldContain("must not reference Strategy.Modules.Risk");
    }

    [Fact]
    public void Module_referencing_contracts_and_platform_is_allowed() =>
        ModuleBoundaryRules.FindViolations(
        [
            Project("Strategy.Modules.Portfolio", "Strategy.Modules.Risk.Contracts", "Strategy.Modules.Portfolio.Contracts", "Strategy.Platform.Persistence"),
        ]).ShouldBeEmpty();

    [Theory]
    [InlineData("Strategy.Platform.Workflow", "Strategy.Modules.Portfolio")]
    [InlineData("Strategy.Platform.Workflow", "Strategy.Modules.Portfolio.Contracts")]
    [InlineData("Strategy.Platform.Kernel", "Strategy.Platform.Persistence")]
    [InlineData("Strategy.Modules.Risk.Contracts", "Strategy.Platform.Persistence")]
    [InlineData("Strategy.Modules.Risk.Contracts", "Strategy.Modules.Risk")]
    [InlineData("Strategy.Modules.Risk", "Strategy.Host")]
    public void Forbidden_references(string from, string to) =>
        ModuleBoundaryRules.FindViolations([Project(from, to)]).ShouldNotBeEmpty();

    [Fact]
    public void Contracts_using_ef_core_or_aspnet_is_a_violation()
    {
        var contracts = new ProjectNode(
            "Strategy.Modules.Risk.Contracts", [], ["Microsoft.EntityFrameworkCore.Relational"], ["Microsoft.AspNetCore.App"]);

        ModuleBoundaryRules.FindViolations([contracts]).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("MediatR")]
    [InlineData("AutoMapper.Extensions.Microsoft.DependencyInjection")]
    [InlineData("FluentAssertions")]
    [InlineData("QuestPDF")]
    [InlineData("Syncfusion.Blazor.Grid")]
    [InlineData("Duende.IdentityServer")]
    public void Banned_packages_are_violations(string id) =>
        PackagePolicy.FindViolations([new PackageInfo(id, "1.0.0", "MIT")]).ShouldHaveSingleItem().ShouldContain("banned");

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("Apache-2.0", true)]
    [InlineData("PostgreSQL", true)]
    [InlineData("MIT OR GPL-3.0-only", true)]
    [InlineData("(MIT AND BSD-3-Clause)", true)]
    [InlineData("MIT AND GPL-3.0-only", false)]
    [InlineData("GPL-2.0-only", false)]
    [InlineData("LicenseRef-Proprietary", false)]
    [InlineData(null, false)]
    public void Licence_expressions_are_checked_against_the_allow_list(string? expression, bool allowed) =>
        PackagePolicy.IsLicenseAllowed(expression).ShouldBe(allowed);

    private static ProjectNode Project(string name, params string[] references) => new(name, references, [], []);
}
