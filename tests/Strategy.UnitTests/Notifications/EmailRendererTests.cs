using Strategy.Platform.Identity.Email;
using Strategy.Platform.Notifications;
using Strategy.Platform.Notifications.Email;
using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Notifications;

public class EmailRendererTests
{
    private static TemplateText Text(NotificationTemplateDefinition d) => new(d.SubjectAr, d.SubjectEn, d.BodyAr, d.BodyEn, d.ButtonAr, d.ButtonEn);

    private static readonly TemplateText Layout = Text(NotificationTemplates.Layout);

    [Fact]
    public void Renders_arabic_right_and_english_left_and_encodes_values()
    {
        var (subject, html, text) = EmailRenderer.Render(Text(IdentityEmails.PasswordReset), Layout,
            new Dictionary<string, string> { ["name"] = "<b>Sara</b>" }, "https://app.example/reset?x=1&y=2", new BrandingSettings());

        subject.ShouldBe("إعادة تعيين كلمة المرور | Reset your password");
        html.ShouldContain("dir=\"rtl\" lang=\"ar\"");
        html.ShouldContain("dir=\"ltr\" lang=\"en\"");
        html.IndexOf("dir=\"ltr\" lang=\"en\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("dir=\"rtl\" lang=\"ar\"", StringComparison.Ordinal));
        html.ShouldContain("&lt;b&gt;Sara&lt;/b&gt;");
        html.ShouldNotContain("<b>Sara</b>");
        html.ShouldContain("href=\"https://app.example/reset?x=1&amp;y=2\"");
        html.ShouldContain("إعادة تعيين كلمة المرور</a>");
        html.ShouldContain("Reset password</a>");
        text.ShouldContain("https://app.example/reset?x=1&y=2");
        text.ShouldContain("Hello <b>Sara</b>,");
    }

    [Fact]
    public void Email_code_template_shows_the_code_large()
    {
        var (_, html, _) = EmailRenderer.Render(Text(IdentityEmails.EmailCode), Layout,
            new Dictionary<string, string> { ["name"] = "Sara", ["code"] = "123456" }, null, new BrandingSettings());

        html.ShouldContain("font-size:28px;letter-spacing:6px;font-weight:700;margin:12px 0\">123456</p>");
    }

    [Fact]
    public void Button_line_is_dropped_without_a_link_and_unknown_placeholders_are_blank()
    {
        var template = new TemplateText("S {{x}}", "S {{x}}", "a {{missing}} b\n{{button}}", "a {{missing}} b\n{{button}}", "Go", "Go");

        var (subject, html, _) = EmailRenderer.Render(template, Layout, new Dictionary<string, string> { ["x"] = "1" }, null, new BrandingSettings());

        subject.ShouldBe("S 1 | S 1");
        html.ShouldContain(">a  b</p>");
        html.ShouldNotContain("<a href");
    }

    [Fact]
    public void Branding_from_settings_sets_names_colour_header_and_footer_in_both_languages_encoded()
    {
        var branding = new BrandingSettings(
            OrganisationNameAr: "دائرة المالية", OrganisationNameEn: "Department of Finance", PrimaryColor: "#123456",
            EmailHeaderAr: "إشعار رسمي", EmailHeaderEn: "Official <notice>", EmailFooterAr: "أبوظبي", EmailFooterEn: "Abu Dhabi");

        var (_, html, _) = EmailRenderer.Render(Text(NotificationTemplates.Test), Layout, new Dictionary<string, string>(), null, branding);

        html.ShouldContain("Department of Finance");
        html.ShouldContain("دائرة المالية");
        html.ShouldContain("background:#123456");
        html.ShouldContain("إشعار رسمي");
        html.ShouldContain("Official &lt;notice&gt;");
        html.ShouldContain("Abu Dhabi");
        html.ShouldContain("أبوظبي");
    }

    [Fact]
    public void Defaults_come_from_the_layout_template_and_an_invalid_colour_is_ignored()
    {
        var (_, html, _) = EmailRenderer.Render(Text(NotificationTemplates.Test), Layout, new Dictionary<string, string>(), null,
            new BrandingSettings(PrimaryColor: "red;x:expression(1)"));

        html.ShouldContain("Integrated Strategy System");
        html.ShouldContain("نظام الاستراتيجية المتكامل");
        html.ShouldContain("This is an automated message.");
        html.ShouldNotContain("expression(");
    }
}
