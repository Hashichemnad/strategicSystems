using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Strategy.Modules.Requests.Domain;

/// <summary>Values of a template: scalars by path (<c>field.name</c>) and row lists by table code (<c>{{#rows code}}</c>).</summary>
public sealed record TemplateModel(
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> Rows);

/// <summary>
/// Mustache-like merge for PDF templates (ADR 0088): <c>{{path}}</c> and <c>{{#rows table}}…{{/rows}}</c> (inside, <c>{{column}}</c> or
/// <c>{{row.index}}</c>). Every value is HTML-encoded; unknown paths render empty. No expressions, helpers or partials.
/// </summary>
public static partial class TemplateRenderer
{
    public const int MaxTemplateLength = 100_000;

    public static string Render(string template, TemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var expanded = RowsBlock().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (!model.Rows.TryGetValue(name, out var rows))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var index = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                builder.Append(Placeholder().Replace(match.Groups["body"].Value, m =>
                {
                    var key = m.Groups["path"].Value;
                    if (key == "row.index")
                    {
                        return index;
                    }

                    return Encode(row.TryGetValue(key, out var v) ? v : model.Values.GetValueOrDefault(key));
                }));
            }

            return builder.ToString();
        });

        return Placeholder().Replace(expanded, m => Encode(model.Values.GetValueOrDefault(m.Groups["path"].Value)));
    }

    /// <summary>The placeholders a template uses (builder help and validation).</summary>
    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template ?? string.Empty).Select(m => m.Groups["path"].Value).Distinct(StringComparer.Ordinal).ToList();

    private static string Encode(string? value) => value is null ? string.Empty : WebUtility.HtmlEncode(value).Replace("\n", "<br/>", StringComparison.Ordinal);

    [GeneratedRegex(@"\{\{#rows\s+(?<name>[A-Za-z0-9_]+)\s*\}\}(?<body>.*?)\{\{/rows\}\}", RegexOptions.Singleline)]
    private static partial Regex RowsBlock();

    [GeneratedRegex(@"\{\{\s*(?<path>[A-Za-z0-9_.]+)\s*\}\}")]
    private static partial Regex Placeholder();
}
