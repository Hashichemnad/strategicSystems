using System.Text.Json;
using System.Text.RegularExpressions;

namespace Strategy.ArchitectureTests.Rules;

/// <summary>A resolved NuGet package (direct or transitive) and the licence expression from its nuspec.</summary>
public sealed record PackageInfo(string Id, string Version, string? LicenseExpression);

/// <summary>
/// CLAUDE.md §4.3: only free / OSI-licensed dependencies, and none of the named commercial or restricted packages.
/// Packages are banned by id whatever the version: the in-house replacements are mandatory (ADR 0003, Shouldly…).
/// </summary>
public static partial class PackagePolicy
{
    public static readonly IReadOnlySet<string> AllowedLicenses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MIT", "MIT-0", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "MPL-2.0", "Unlicense", "0BSD",
        "PostgreSQL", // OSI-approved permissive licence of Npgsql (mandated by CLAUDE.md §4.1)
        "OFL-1.1",
    };

    /// <summary>Legacy nuspec <c>licenseUrl</c> values that unambiguously name one licence.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownLicenseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["http://opensource.org/licenses/mit-license.php"] = "MIT",
        ["https://opensource.org/licenses/MIT"] = "MIT",
        ["http://www.apache.org/licenses/LICENSE-2.0"] = "Apache-2.0",
        ["https://www.apache.org/licenses/LICENSE-2.0"] = "Apache-2.0",
    };

    /// <summary>
    /// Packages whose nuspec carries no licence metadata, verified by hand against their source repository.
    /// Keep this list short; every entry is also noted in docs/THIRD_PARTY_LICENSES.md.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ManuallyVerified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["NetArchTest.Rules"] = "MIT", // github.com/BenMorris/NetArchTest/blob/master/LICENSE
    };

    public static readonly IReadOnlyList<string> BannedPackagePatterns =
    [
        "MediatR*", "AutoMapper*", "FluentAssertions*", "MassTransit*", "QuestPDF*", "Hangfire.Pro*",
        "Telerik.*", "DevExpress.*", "Syncfusion.*", "Kendo.*", "Duende.*", "AgGrid*", "Highsoft.*", "Bryntum*", "DHTMLX*",
    ];

    public static IEnumerable<string> FindViolations(IEnumerable<PackageInfo> packages)
    {
        foreach (var package in packages)
        {
            if (BannedPackagePatterns.Any(pattern => Matches(pattern, package.Id)))
            {
                yield return $"{package.Id} {package.Version} is banned (CLAUDE.md §4.3)";
            }
            else if (!IsLicenseAllowed(package.LicenseExpression))
            {
                yield return $"{package.Id} {package.Version} has licence '{package.LicenseExpression ?? "(none)"}' outside the allow-list";
            }
        }
    }

    /// <summary>SPDX expression: <c>A OR B</c> needs one allowed side; <c>A AND B</c> needs both; parentheses are ignored.</summary>
    public static bool IsLicenseAllowed(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var cleaned = expression.Replace("(", string.Empty, StringComparison.Ordinal).Replace(")", string.Empty, StringComparison.Ordinal);
        return cleaned.Split(" OR ", StringSplitOptions.TrimEntries)
            .Any(alternative => alternative.Split(" AND ", StringSplitOptions.TrimEntries).All(AllowedLicenses.Contains));
    }

    /// <summary>Every resolved package of every project, with its nuspec licence expression.</summary>
    public static IReadOnlyList<PackageInfo> ResolvedPackages()
    {
        var packages = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetsFile in Repository.AssetsFiles().Where(File.Exists))
        {
            using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
            var packageFolder = assets.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
            foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package" || packages.ContainsKey(library.Name))
                {
                    continue;
                }

                var path = Path.Combine(packageFolder, library.Value.GetProperty("path").GetString()!);
                var nuspec = Directory.EnumerateFiles(path, "*.nuspec").FirstOrDefault();
                var parts = library.Name.Split('/');
                var license = nuspec is null ? null : ReadLicense(File.ReadAllText(nuspec));
                packages[library.Name] = new PackageInfo(parts[0], parts[1], license ?? ManuallyVerified.GetValueOrDefault(parts[0]));
            }
        }

        return packages.Values.ToList();
    }

    private static string? ReadLicense(string nuspec)
    {
        if (LicenseExpression().Match(nuspec) is { Success: true } expression)
        {
            return expression.Groups[1].Value;
        }

        return LicenseUrl().Match(nuspec) is { Success: true } url ? KnownLicenseUrls.GetValueOrDefault(url.Groups[1].Value.Trim()) : null;
    }

    private static bool Matches(string pattern, string id) =>
        pattern.EndsWith('*')
            ? id.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, id, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("<license type=\"expression\">([^<]+)</license>")]
    private static partial Regex LicenseExpression();

    [GeneratedRegex("<licenseUrl>([^<]+)</licenseUrl>")]
    private static partial Regex LicenseUrl();
}
