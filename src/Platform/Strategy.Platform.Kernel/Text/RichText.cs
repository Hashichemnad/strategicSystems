using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Strategy.Platform.Kernel.Text;

/// <summary>
/// Rich-text fields (SPEC-ADDENDUM A2, ADR 0161): stored as sanitised HTML from a fixed allow-list; values without HTML markup are
/// legacy / plain text and stay exactly as they are (never rewritten). Every rich-text field is saved through <see cref="Sanitize"/>,
/// measured with <see cref="PlainLength"/> / <see cref="StoredLength"/>, and leaves the system as plain text (<see cref="ToPlainText"/>)
/// for exports, e-mail plain parts, search columns and AI prompts, or as safe HTML (<see cref="ToSafeHtml"/>) for PDFs and HTML e-mails.
/// The SPA renders the same allow-list with DOMPurify (<c>web/src/ui/rich-text.ts</c>).
/// </summary>
public static partial class RichText
{
    /// <summary>Stored size of a rich-text value (sanitised HTML, characters) unless a field sets its own.</summary>
    public const int DefaultMaxLength = 20000;

    /// <summary>Message key of the "too long" validation error (translated by the SPA, parameter <c>max</c>).</summary>
    public const string TooLongKey = "validation.richTextTooLong";

    /// <summary>Allowed elements (ADR 0161). Anything else is removed; its text is kept unless it is a dangerous container.</summary>
    public static readonly IReadOnlyList<string> AllowedTags =
    [
        "p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "a", "table", "thead", "tbody",
        "tr", "th", "td", "colgroup", "col", "code", "pre", "hr",
    ];

    /// <summary>Elements that may carry <c>dir</c> (per-paragraph direction).</summary>
    private static readonly HashSet<string> DirTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "table", "th", "td", "pre",
    };

    /// <summary>Removed together with everything inside them (their content is code or foreign markup, never text to keep).</summary>
    private static readonly HashSet<string> DropWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "applet", "svg", "math", "template", "noscript", "noembed",
        "noframes", "xmp", "plaintext", "textarea", "select", "option", "button", "form", "input", "link", "meta", "base", "title", "head",
        "audio", "video", "source", "track", "canvas", "img", "picture", "map", "area", "portal", "dialog",
    };

    private static readonly HashSet<string> Schemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>Every HTML element name (and the dangerous ones): a value containing one of them as a tag is HTML.</summary>
    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo", "blockquote", "body", "br", "button",
        "canvas", "caption", "center", "cite", "code", "col", "colgroup", "data", "datalist", "dd", "del", "details", "dfn", "dialog", "div",
        "dl", "dt", "em", "embed", "fieldset", "figcaption", "figure", "font", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4",
        "h5", "h6", "head", "header", "hr", "html", "i", "iframe", "img", "input", "ins", "kbd", "label", "legend", "li", "link", "main",
        "map", "mark", "marquee", "math", "menu", "meta", "meter", "nav", "noembed", "noframes", "noscript", "object", "ol", "optgroup",
        "option", "output", "p", "param", "picture", "plaintext", "portal", "pre", "progress", "q", "rp", "rt", "ruby", "s", "samp",
        "script", "section", "select", "slot", "small", "source", "span", "strike", "strong", "style", "sub", "summary", "sup", "svg",
        "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track", "tt", "u", "ul", "var",
        "video", "wbr", "xmp", "applet", "image",
    };

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// True when the value contains HTML markup (an element tag of a known HTML element or a comment). A value without markup is plain
    /// text: the editor loads it as paragraphs and every view shows it escaped, with its line breaks.
    /// </summary>
    public static bool IsHtml(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('<', StringComparison.Ordinal) < 0)
        {
            return false;
        }

        if (value.Contains("<!--", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (Match match in TagPattern().Matches(value))
        {
            if (KnownTags.Contains(match.Groups["name"].Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Value to store for a rich-text field: blank → null; plain text (no markup) → unchanged; HTML → the allow-listed subset
    /// (no scripts, no event handlers, no style / class, links http / https / mailto only with <c>rel="noopener noreferrer"</c>).
    /// Markup whose formatting is all removed and that holds no text becomes null.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!IsHtml(value))
        {
            return value.Trim();
        }

        var clean = Sanitizer.Sanitize(value).Trim();
        if (clean.Length == 0 || (ToPlainText(clean).Length == 0 && !clean.Contains("<hr", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return clean;
    }

    /// <summary>Number of characters the reader sees (plain-text projection): the length a SPEC limit such as "5000" refers to.</summary>
    public static int PlainLength(string? value) => ToPlainText(value).Length;

    /// <summary>
    /// The "too long" error of a sanitised rich-text field, or null when it fits: at most <paramref name="plainMax"/> visible characters
    /// and <paramref name="storedMax"/> stored characters (message key <see cref="TooLongKey"/>, parameter <c>maxLength</c>).
    /// </summary>
    public static Results.FieldError? LengthError(string field, string? sanitised, int plainMax, int storedMax = DefaultMaxLength) =>
        Fits(sanitised, plainMax, storedMax)
            ? null
            : new Results.FieldError(field, TooLongKey, TooLongKey, new Dictionary<string, object?> { ["maxLength"] = plainMax });

    /// <summary>Stored length (characters of the sanitised value).</summary>
    public static int StoredLength(string? value) => value?.Length ?? 0;

    /// <summary>True when a sanitised value fits the plain-text limit and the stored size.</summary>
    public static bool Fits(string? sanitised, int plainMax, int storedMax = DefaultMaxLength) =>
        PlainLength(sanitised) <= plainMax && StoredLength(sanitised) <= storedMax;

    /// <summary>
    /// Plain-text projection (exports, e-mail plain parts, search columns, AI prompts): blocks and line breaks become new lines, list items
    /// get "• " / "1. ", table cells are joined with " | " (one row per line), entities are decoded. Plain values are returned unchanged
    /// except for normalised line endings and trimming.
    /// </summary>
    public static string ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!IsHtml(value))
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }

        var document = Parser.ParseDocument("<!doctype html><html><body></body></html>");
        var body = document.Body!;
        body.InnerHtml = Sanitizer.Sanitize(value);
        var writer = new PlainWriter();
        writer.Walk(body);
        return writer.ToString();
    }

    /// <summary>
    /// Safe HTML for server-rendered HTML (PDF templates, HTML e-mail bodies): HTML values are sanitised again, plain values are escaped
    /// with one paragraph per blank-line block and &lt;br&gt; for single line breaks. Never returns markup outside the allow-list.
    /// </summary>
    public static string ToSafeHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (IsHtml(value))
        {
            return Sanitizer.Sanitize(value);
        }

        var builder = new StringBuilder();
        foreach (var paragraph in ParagraphBreak().Split(value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim()))
        {
            builder.Append("<p>")
                .Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>", StringComparison.Ordinal))
                .Append("</p>");
        }

        return builder.ToString();
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(AllowedTags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(["dir", "href", "rel", "target", "colspan", "rowspan"], StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(Schemes, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
            AllowedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowDataAttributes = false,
        };
        var sanitizer = new HtmlSanitizer(options) { KeepChildNodes = true };
        sanitizer.RemovingTag += (_, e) =>
        {
            if (DropWithContent.Contains(e.Tag.LocalName))
            {
                // Remove the content too: script text, SVG / MathML children and form controls are never text the author wrote.
                while (e.Tag.FirstChild is { } child)
                {
                    e.Tag.RemoveChild(child);
                }
            }
        };
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement element)
            {
                PostProcess(element);
            }
        };
        return sanitizer;
    }

    private static void PostProcess(IElement element)
    {
        var name = element.LocalName;
        if (element.GetAttribute("dir") is { } dir)
        {
            var value = dir.Trim().ToLowerInvariant();
            if (DirTags.Contains(name) && value is "ltr" or "rtl" or "auto")
            {
                element.SetAttribute("dir", value);
            }
            else
            {
                element.RemoveAttribute("dir");
            }
        }

        if (name is not ("td" or "th"))
        {
            element.RemoveAttribute("colspan");
            element.RemoveAttribute("rowspan");
        }
        else
        {
            foreach (var span in new[] { "colspan", "rowspan" })
            {
                if (element.GetAttribute(span) is { } v &&
                    !(int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 100))
                {
                    element.RemoveAttribute(span);
                }
            }
        }

        if (name != "a")
        {
            element.RemoveAttribute("href");
            element.RemoveAttribute("rel");
            element.RemoveAttribute("target");
            return;
        }

        if (element.GetAttribute("href") is { } href && !HasAllowedScheme(href))
        {
            element.RemoveAttribute("href");
        }

        if (element.GetAttribute("target") is { } target && target != "_blank")
        {
            element.RemoveAttribute("target");
        }

        element.SetAttribute("rel", "noopener noreferrer");
    }

    private static bool HasAllowedScheme(string href)
    {
        var colon = href.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        var scheme = href[..colon].Trim();
        return Schemes.Contains(scheme) &&
               Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri) && Schemes.Contains(uri.Scheme);
    }

    [GeneratedRegex(@"</?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?:[\s/][^<>]*)?>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\n\s*\n", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ParagraphBreak();

    /// <summary>Writes the text of a sanitised DOM with block structure as line breaks.</summary>
    private sealed class PlainWriter
    {
        private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "h2", "h3", "h4", "blockquote", "pre", "ul", "ol", "li", "table", "thead", "tbody", "tr", "hr",
        };

        private readonly StringBuilder _builder = new();

        /// <summary>Position right after a list bullet: a block inside the item starts on the bullet's line.</summary>
        private int _bulletEnd = -1;

        public void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                Visit(child);
            }
        }

        public override string ToString() =>
            string.Join('\n', _builder.ToString().Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0));

        private void Visit(INode node)
        {
            switch (node)
            {
                case IText text:
                    AppendText(text);
                    return;
                case IElement element:
                    VisitElement(element);
                    return;
                default:
                    return;
            }
        }

        private void AppendText(IText text)
        {
            var inPre = text.ParentElement?.Closest("pre") is not null;
            var content = inPre ? text.Data : Whitespace().Replace(text.Data, " ");
            if (!inPre && (_builder.Length == 0 || _builder[^1] is '\n' or ' '))
            {
                content = content.TrimStart(' ');
            }

            _builder.Append(content);
        }

        private void VisitElement(IElement element)
        {
            var name = element.LocalName;
            switch (name)
            {
                case "br":
                    _builder.Append('\n');
                    return;
                case "hr":
                    NewLine();
                    _builder.Append("---");
                    NewLine();
                    return;
                case "li":
                    NewLine();
                    _builder.Append(Bullet(element));
                    _bulletEnd = _builder.Length;
                    Walk(element);
                    NewLine();
                    return;
                case "tr":
                    NewLine();
                    var first = true;
                    foreach (var cell in element.Children.Where(c => c.LocalName is "td" or "th"))
                    {
                        if (!first)
                        {
                            _builder.Append(" | ");
                        }

                        first = false;
                        var inner = new PlainWriter();
                        inner.Walk(cell);
                        _builder.Append(inner.ToString().Replace('\n', ' '));
                    }

                    NewLine();
                    return;
                case "colgroup":
                case "col":
                    return;
                default:
                    if (Blocks.Contains(name))
                    {
                        NewLine();
                        Walk(element);
                        NewLine();
                    }
                    else
                    {
                        Walk(element);
                    }

                    return;
            }
        }

        private static string Bullet(IElement li)
        {
            if (li.ParentElement is { LocalName: "ol" } list)
            {
                var index = list.Children.Where(c => c.LocalName == "li").ToList().IndexOf(li) + 1;
                return string.Create(CultureInfo.InvariantCulture, $"{index}. ");
            }

            return "• ";
        }

        private void NewLine()
        {
            if (_builder.Length > 0 && _builder[^1] != '\n' && _builder.Length != _bulletEnd)
            {
                _builder.Append('\n');
            }
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Whitespace();
}
