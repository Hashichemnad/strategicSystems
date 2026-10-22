namespace Strategy.ArchitectureTests.Rules;

public enum ProjectKind
{
    Kernel,
    Platform,
    Module,
    Contracts,
    Host,
    Other,
}

/// <summary>
/// CLAUDE.md §5 boundaries, evaluated on the project graph:
/// <list type="bullet">
/// <item>a module references Platform projects and <c>.Contracts</c> only — never another module's internals or the host;</item>
/// <item>Contracts reference the Kernel and other Contracts only, and no EF Core / ASP.NET Core / Npgsql;</item>
/// <item>Platform projects never reference modules or the host; the Kernel references nothing internal and no EF / ASP.NET.</item>
/// </list>
/// </summary>
public static class ModuleBoundaryRules
{
    private static readonly string[] InfrastructurePackagePrefixes = ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore"];

    public static ProjectKind KindOf(string projectName) => projectName switch
    {
        "Strategy.Platform.Kernel" => ProjectKind.Kernel,
        _ when projectName.StartsWith("Strategy.Platform.", StringComparison.Ordinal) => ProjectKind.Platform,
        _ when projectName.StartsWith("Strategy.Modules.", StringComparison.Ordinal)
            && projectName.EndsWith(".Contracts", StringComparison.Ordinal) => ProjectKind.Contracts,
        _ when projectName.StartsWith("Strategy.Modules.", StringComparison.Ordinal) => ProjectKind.Module,
        "Strategy.Host" => ProjectKind.Host,
        _ => ProjectKind.Other,
    };

    public static IReadOnlyList<string> FindViolations(IEnumerable<ProjectNode> projects)
    {
        var violations = new List<string>();
        foreach (var project in projects)
        {
            var kind = KindOf(project.Name);
            foreach (var reference in project.ProjectReferences)
            {
                if (!IsAllowed(kind, KindOf(reference)))
                {
                    violations.Add($"{project.Name} ({kind}) must not reference {reference} ({KindOf(reference)})");
                }
            }

            if (kind is ProjectKind.Kernel or ProjectKind.Contracts)
            {
                violations.AddRange(project.PackageReferences
                    .Where(p => InfrastructurePackagePrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal)))
                    .Select(p => $"{project.Name} ({kind}) must not use infrastructure package {p}"));
                violations.AddRange(project.FrameworkReferences
                    .Select(f => $"{project.Name} ({kind}) must not use framework {f}"));
            }
        }

        return violations;
    }

    private static bool IsAllowed(ProjectKind from, ProjectKind to) => from switch
    {
        ProjectKind.Kernel => false,
        ProjectKind.Platform => to is ProjectKind.Kernel or ProjectKind.Platform,
        ProjectKind.Contracts => to is ProjectKind.Kernel or ProjectKind.Contracts,
        ProjectKind.Module => to is ProjectKind.Kernel or ProjectKind.Platform or ProjectKind.Contracts,
        ProjectKind.Host => to is not ProjectKind.Host and not ProjectKind.Other,
        _ => true,
    };
}
