using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Notifications.Email;

/// <summary>The texts of one template in both languages (from <c>notify.template</c>).</summary>
public sealed record TemplateText(string SubjectAr, string SubjectEn, string BodyAr, string BodyEn, string? ButtonAr, string? ButtonEn)
{
    public static TemplateText From(NotificationTemplate t) => new(t.SubjectAr, t.SubjectEn, t.BodyAr, t.BodyEn, t.ButtonAr, t.ButtonEn);
}

/// <summary>
/// Renders the bilingual side-by-side e-mail (CLAUDE.md §10: Arabic column on the right, English on the left) with the installation's
/// branding. Every value is HTML-encoded; template text is plain text (no HTML from administrators), so nothing can inject markup.
/// </summary>
public static partial class EmailRenderer
{
    private static readonly Lazy<string> Layout = new(() =>
    {
        using var stream = typeof(EmailRenderer).Assembly.GetManifestResourceStream(
            typeof(EmailRenderer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("layout.html", StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <param name="template">Subject, body and button text.</param>
    /// <param name="layout">The <c>notifications.layout</c> texts: subject = default organisation name, body = default footer.</param>
    /// <param name="values">Placeholder values.</param>
    /// <param name="link">Absolute link for the button.</param>
    /// <param name="branding">Names, colour, header and footer from Settings → Branding.</param>
    public static (string Subject, string Html, string Text) Render(
        TemplateText template, TemplateText layout, IReadOnlyDictionary<string, string> values, string? link, BrandingSettings branding)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(branding);
        var subjectAr = Fill(template.SubjectAr, values);
        var subjectEn = Fill(template.SubjectEn, values);
        var organisation = $"{Blank(branding.OrganisationNameEn) ?? layout.SubjectEn} · {Blank(branding.OrganisationNameAr) ?? layout.SubjectAr}";
        var color = HexColor().IsMatch(branding.PrimaryColor ?? string.Empty) ? branding.PrimaryColor! : "#1f3a68";

        string Part(string lang)
        {
            var header = Blank(lang == "ar" ? branding.EmailHeaderAr : branding.EmailHeaderEn);
            var body = Body(lang == "ar" ? template.BodyAr : template.BodyEn, lang == "ar" ? template.ButtonAr : template.ButtonEn, values, link, color);
            return header is null ? body : $"<p style=\"margin:0 0 12px;line-height:1.6;font-weight:600\">{E(header)}</p>{body}";
        }

        var footerAr = Blank(branding.EmailFooterAr) ?? layout.BodyAr;
        var footerEn = Blank(branding.EmailFooterEn) ?? layout.BodyEn;
        var html = Layout.Value
            .Replace("{{subject_en}}", E(subjectEn), StringComparison.Ordinal)
            .Replace("{{subject_ar}}", E(subjectAr), StringComparison.Ordinal)
            .Replace("{{body_en}}", Part("en"), StringComparison.Ordinal)
            .Replace("{{body_ar}}", Part("ar"), StringComparison.Ordinal)
            .Replace("{{footer_en}}", E(footerEn), StringComparison.Ordinal)
            .Replace("{{footer_ar}}", E(footerAr), StringComparison.Ordinal)
            .Replace("{{color}}", color, StringComparison.Ordinal)
            .Replace("{{org}}", E(organisation), StringComparison.Ordinal);

        var text = new StringBuilder();
        foreach (var (body, subject) in new[] { (template.BodyAr, subjectAr), (template.BodyEn, subjectEn) })
        {
            text.AppendLine(subject).AppendLine();
            foreach (var line in Lines(body))
            {
                text.AppendLine(line.Trim() == "{{button}}" ? link ?? string.Empty : Fill(line, values));
            }

            text.AppendLine();
        }

        return ($"{subjectAr} | {subjectEn}", html, text.ToString());
    }

    /// <summary>Subject only, in one language (in-app titles).</summary>
    public static string Subject(string subject, IReadOnlyDictionary<string, string> values) => Fill(subject, values);

    private static string Body(string body, string? button, IReadOnlyDictionary<string, string> values, string? link, string color)
    {
        var html = new StringBuilder();
        foreach (var raw in Lines(body))
        {
            var line = raw.Trim();
            if (line == "{{button}}")
            {
                if (link is not null && button is not null)
                {
                    html.Append("<p style=\"margin:16px 0\"><a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\" style=\"display:inline-block;background:")
                        .Append(color).Append(";color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none\">").Append(E(button)).Append("</a></p>");
                }

                continue;
            }

            var style = line == "{{code}}" ? "font-size:28px;letter-spacing:6px;font-weight:700;margin:12px 0" : "margin:0 0 12px;line-height:1.6";
            html.Append("<p style=\"").Append(style).Append("\">").Append(E(Fill(line, values))).Append("</p>");
        }

        return html.ToString();
    }

    private static IEnumerable<string> Lines(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l));

    private static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : string.Empty);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string E(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
