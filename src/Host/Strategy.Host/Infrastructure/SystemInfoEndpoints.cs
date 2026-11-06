using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Strategy.Platform.Http.Access;

namespace Strategy.Host.Infrastructure;

/// <summary>Version of this installation (ADR 0152): product version, source commit when known, build time of the host assembly.</summary>
public sealed record SystemVersionDto(string Version, string InformationalVersion, string? Commit, DateTimeOffset BuildDate);

/// <summary>Release notes of one version in one language (Markdown, shown in the About dialog).</summary>
public sealed record ReleaseNoteDto(string Version, string Language, string Markdown);

/// <summary>
/// <c>/api/v1/system/version</c> and <c>/api/v1/system/release-notes</c> (signed-in users only: the version is not shown to anonymous
/// callers, ASVS 14.3.3). Release notes are embedded resources <c>ReleaseNotes/{en,ar}/&lt;version&gt;.md</c>.
/// </summary>
internal static class SystemInfoEndpoints
{
    private const string ResourcePrefix = "Strategy.Host.ReleaseNotes.";

    public static SystemVersionDto Current { get; } = ReadVersion();

    public static void MapSystemInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var system = endpoints.MapGroup("/api/v1/system").WithTags("system");
        system.MapGet("/version", () => TypedResults.Ok(Current)).WithName("GetSystemVersion").RequireSignedIn();
        system.MapGet("/release-notes", ReleaseNotes).WithName("ListReleaseNotes").RequireSignedIn();
    }

    private static Ok<IReadOnlyList<ReleaseNoteDto>> ReleaseNotes(string? language)
    {
        var lang = language is "ar" ? "ar" : "en";
        var assembly = typeof(SystemInfoEndpoints).Assembly;
        var prefix = ResourcePrefix + lang + ".";
        var notes = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => (Name: n, Version: n[prefix.Length..^3]))
            .OrderByDescending(n => Version.TryParse(n.Version, out var v) ? v : new Version(0, 0))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n.Name)!;
                using var reader = new StreamReader(stream);
                return new ReleaseNoteDto(n.Version, lang, reader.ReadToEnd());
            })
            .ToList();
        return TypedResults.Ok<IReadOnlyList<ReleaseNoteDto>>(notes);
    }

    private static SystemVersionDto ReadVersion()
    {
        var assembly = typeof(SystemInfoEndpoints).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var version = plus >= 0 ? informational[..plus] : informational;
        var commit = plus >= 0 ? informational[(plus + 1)..] : null;
        if (commit is { Length: > 12 })
        {
            commit = commit[..12];
        }

        var built = string.IsNullOrEmpty(assembly.Location) ? DateTime.UtcNow : File.GetLastWriteTimeUtc(assembly.Location);
        return new SystemVersionDto(version, informational, commit, new DateTimeOffset(built, TimeSpan.Zero));
    }

    /// <summary>The version line written to the log at start-up.</summary>
    public static string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Current.Version}{(Current.Commit is null ? string.Empty : " (" + Current.Commit + ")")}");
}
