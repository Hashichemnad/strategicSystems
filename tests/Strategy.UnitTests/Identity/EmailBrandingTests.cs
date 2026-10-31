using Strategy.Platform.Identity.Email;
using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Identity;

public class EmailBrandingTests
{
    [Fact]
    public void Branding_from_settings_sets_the_organisation_header_and_footer_in_both_languages_encoded()
    {
        var branding = new BrandingSettings(
            OrganisationNameAr: "دائرة المالية", OrganisationNameEn: "Department of Finance",
            EmailHeaderAr: "إشعار رسمي", EmailHeaderEn: "Official <notice>", EmailFooterAr: "أبوظبي", EmailFooterEn: "Abu Dhabi");

        var (_, html, _) = IdentityEmailRenderer.Render(new IdentityEmail("a@b.c", "test", new Dictionary<string, string>()), branding);

        html.ShouldContain("Department of Finance");
        html.ShouldContain("دائرة المالية");
        html.ShouldContain("إشعار رسمي");
        html.ShouldContain("Official &lt;notice&gt;");
        html.ShouldContain("Abu Dhabi");
        html.ShouldContain("أبوظبي");
    }
}
