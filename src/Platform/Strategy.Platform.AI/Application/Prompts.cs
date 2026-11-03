using System.Text;
using System.Text.RegularExpressions;

namespace Strategy.Platform.AI.Application;

/// <summary>A prompt template: versioned, from an embedded resource <c>…Prompts.&lt;feature&gt;.v&lt;n&gt;.md</c> (ADR 0029).</summary>
public sealed record PromptTemplate(string Feature, int Version, string System, string User)
{
    public string Id => $"{Feature}.v{Version}";
}

/// <summary>
/// Loads the newest version of each template from the AI platform and every registered prompt assembly. A template has front matter
/// (<c>feature</c>, <c>version</c>), a system part and a user part separated by a line <c>=== user ===</c>; <c>{{name}}</c> placeholders are
/// filled with trusted values only. Untrusted text never goes through placeholders: see <see cref="UntrustedContent"/>.
/// </summary>
public sealed partial class PromptLibrary
{
    private readonly Dictionary<string, PromptTemplate> _templates = new(StringComparer.Ordinal);

    public PromptLibrary(IEnumerable<AiPromptAssembly> assemblies)
    {
        foreach (var assembly in assemblies.Select(a => a.Assembly).Prepend(typeof(PromptLibrary).Assembly).Distinct())
        {
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Prompts.", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var template = Parse(reader.ReadToEnd(), name);
                if (!_templates.TryGetValue(template.Feature, out var existing) || existing.Version < template.Version)
                {
                    _templates[template.Feature] = template;
                }
            }
        }
    }

    public IReadOnlyCollection<PromptTemplate> All => _templates.Values;

    public PromptTemplate Get(string feature) =>
        _templates.TryGetValue(feature, out var template) ? template : throw new InvalidOperationException($"No prompt template for '{feature}'.");

    public static PromptTemplate Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = FrontMatter().Match(text);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Prompt '{source}' has no front matter.");
        }

        var meta = match.Groups["meta"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(':', 2, StringSplitOptions.TrimEntries)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        var body = text[match.Length..];
        var parts = body.Split("=== user ===", 2);
        if (parts.Length != 2 || !meta.TryGetValue("feature", out var feature) || !meta.TryGetValue("version", out var version))
        {
            throw new InvalidOperationException($"Prompt '{source}' needs feature, version and a '=== user ===' part.");
        }

        return new PromptTemplate(feature, int.Parse(version, System.Globalization.CultureInfo.InvariantCulture), parts[0].Trim(), parts[1].Trim());
    }

    /// <summary>Fills <c>{{name}}</c> placeholders; an unknown placeholder is an error (a template bug, not user input).</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        return Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? value
            : throw new InvalidOperationException($"Missing prompt value '{m.Groups[1].Value}'."));
    }

    [GeneratedRegex(@"\A---\s*\n(?<meta>.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    [GeneratedRegex(@"\{\{([a-zA-Z]+)\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>
/// Untrusted text (uploaded documents, record values typed by people) is passed as data inside a tag with a per-call random nonce. Anything
/// in the text that looks like the tag is neutralised first, so the content cannot close the block or open a new one (prompt-injection
/// defence, docs/design/ai.md §6).
/// </summary>
public static partial class UntrustedContent
{
    public const string Tag = "untrusted_document";

    public static string NewNonce() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static string Wrap(string id, string content, string nonce)
    {
        ArgumentNullException.ThrowIfNull(content);
        return $"<{Tag} id=\"{Clean(id)}\" nonce=\"{nonce}\">\n{Neutralise(content)}\n</{Tag} nonce=\"{nonce}\">";
    }

    /// <summary>Removes tag look-alikes (<c>&lt;untrusted_document</c>, <c>&lt;/untrusted_document</c>, any case or spacing) and control characters.</summary>
    public static string Neutralise(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = TagLike().Replace(content, "[removed-tag]");
        return new string(text.Where(c => c == '\n' || c == '\t' || !char.IsControl(c)).ToArray());
    }

    private static string Clean(string id) => new(id.Where(char.IsLetterOrDigit).Take(40).ToArray());

    [GeneratedRegex(@"<\s*/?\s*untrusted[\s_\-]*document[^>]*>?", RegexOptions.IgnoreCase)]
    private static partial Regex TagLike();
}
