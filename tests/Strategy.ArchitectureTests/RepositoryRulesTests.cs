using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Strategy.ArchitectureTests.Rules;

namespace Strategy.ArchitectureTests;

/// <summary>The rules applied to the real repository. Any new violation fails the build.</summary>
public class RepositoryRulesTests
{
    [Fact]
    public void Project_references_respect_module_boundaries()
    {
        var projects = Repository.SourceProjects();

        projects.ShouldContain(p => ModuleBoundaryRules.KindOf(p.Name) == ProjectKind.Module);
        ModuleBoundaryRules.FindViolations(projects).ShouldBeEmpty();
    }

    [Fact]
    public void Module_types_do_not_use_other_modules_internals()
    {
        var moduleAssemblies = ModuleAssemblies().ToList();
        moduleAssemblies.ShouldNotBeEmpty();

        foreach (var assembly in moduleAssemblies.Where(a => !a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal)))
        {
            var own = assembly.GetName().Name!;
            var otherInternals = moduleAssemblies
                .Select(a => a.GetName().Name!)
                .Where(n => n != own && n != own + ".Contracts" && !n.EndsWith(".Contracts", StringComparison.Ordinal))
                .ToArray();
            if (otherInternals.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(otherInternals).GetResult();
            (result.FailingTypeNames ?? []).ShouldBeEmpty();
        }
    }

    [Fact]
    public void Kernel_and_contracts_do_not_depend_on_infrastructure()
    {
        var assemblies = ModuleAssemblies().Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .Append(typeof(Strategy.Platform.Kernel.Domain.Entity).Assembly);

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
                .GetResult();
            (result.FailingTypeNames ?? []).ShouldBeEmpty();
        }
    }

    [Fact]
    public void Platform_does_not_depend_on_modules()
    {
        var platform = Directory.EnumerateFiles(AppContext.BaseDirectory, "Strategy.Platform.*.dll").Select(Assembly.LoadFrom).ToList();
        platform.Count.ShouldBeGreaterThanOrEqualTo(4);

        foreach (var assembly in platform)
        {
            Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Strategy.Modules").GetResult().IsSuccessful.ShouldBeTrue();
        }
    }

    [Fact]
    public void Packages_are_allowed_and_not_banned()
    {
        var packages = PackagePolicy.ResolvedPackages();

        packages.Count.ShouldBeGreaterThan(20, "restore must have run before the architecture tests");
        PackagePolicy.FindViolations(packages).ShouldBeEmpty();
    }

    [Fact]
    public void Every_direct_package_is_listed_in_third_party_licenses()
    {
        var central = XDocument.Load(Path.Combine(Repository.Root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(e => e.Attribute("Include")!.Value);
        var notice = File.ReadAllText(Path.Combine(Repository.Root, "docs", "THIRD_PARTY_LICENSES.md"));

        central.Where(id => !notice.Contains(id, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    private static IEnumerable<Assembly> ModuleAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Strategy.Modules.*.dll").Select(Assembly.LoadFrom);
}
