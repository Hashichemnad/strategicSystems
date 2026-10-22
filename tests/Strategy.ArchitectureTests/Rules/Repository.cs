using System.Xml.Linq;

namespace Strategy.ArchitectureTests.Rules;

/// <summary>Locates the repository and reads project files, so rules run on source, not only on compiled references.</summary>
public static class Repository
{
    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<ProjectNode> SourceProjects() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(ReadProject)
            .ToList();

    public static IEnumerable<string> AssetsFiles() =>
        Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("node_modules", StringComparison.Ordinal))
            .Select(p => Path.Combine(Path.GetDirectoryName(p)!, "obj", "project.assets.json"));

    public static ProjectNode ReadProject(string path)
    {
        var xml = XDocument.Load(path);
        var references = xml.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
        var packages = xml.Descendants("PackageReference").Select(e => e.Attribute("Include")!.Value).ToList();
        var frameworks = xml.Descendants("FrameworkReference").Select(e => e.Attribute("Include")!.Value).ToList();
        return new ProjectNode(Path.GetFileNameWithoutExtension(path), references, packages, frameworks);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Strategy.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Strategy.slnx) not found.");
    }
}

/// <summary>A project and its direct references, as written in the .csproj.</summary>
public sealed record ProjectNode(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences);
