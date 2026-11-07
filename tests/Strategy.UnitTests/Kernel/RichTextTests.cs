using Strategy.Platform.Kernel.Text;

namespace Strategy.UnitTests.Kernel;

/// <summary>Server-side rich-text allow-list (ADR 0161): XSS corpus, plain-text projection, legacy plain-text detection.</summary>
public class RichTextTests
{
    /// <summary>Each payload must come out without anything executable (the second value is text that must survive, if any).</summary>
    public static TheoryData<string, string?> XssCorpus => new()
    {
        { "<p>ok</p><script>alert(1)</script>", "ok" },
        { "<SCRIPT SRC=//evil.example/x.js></SCRIPT><p>ok</p>", "ok" },
        { "<scr<script>ipt>alert(1)</script>", null },
        { "<p>a<iframe src=\"https://evil.example\"></iframe>b</p>", "ab" },
        { "<object data=\"x.swf\"><param name=a value=b></object><p>ok</p>", "ok" },
        { "<embed src=\"x.swf\"><p>ok</p>", "ok" },
        { "<svg onload=alert(1)><script>alert(1)</script></svg><p>ok</p>", "ok" },
        { "<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>", null },
        { "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>", null },
        { "<img src=x onerror=alert(1)><p>ok</p>", "ok" },
        { "<p onclick=\"alert(1)\" onmouseover='alert(2)'>ok</p>", "ok" },
        { "<p ONCLICK=alert(1)>ok</p>", "ok" },
        { "<a href=\"javascript:alert(1)\">x</a>", "x" },
        { "<a href=\"JaVaScRiPt:alert(1)\">x</a>", "x" },
        { "<a href=\" javascript:alert(1)\">x</a>", "x" },
        { "<a href=\"java&#x09;script:alert(1)\">x</a>", "x" },
        { "<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)\">x</a>", "x" },
        { "<a href=\"&#x6A;avascript&colon;alert(1)\">x</a>", "x" },
        { "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "x" },
        { "<a href=\"vbscript:msgbox(1)\">x</a>", "x" },
        { "<a href=\"//evil.example\">x</a>", "x" },
        { "<p style=\"background:url(javascript:alert(1))\">ok</p>", "ok" },
        { "<p style=\"width: expression(alert(1))\">ok</p>", "ok" },
        { "<style>p{background:url(javascript:alert(1))}</style><p>ok</p>", "ok" },
        { "<p class=\"x\" id=\"y\" data-x=\"1\">ok</p>", "ok" },
        { "<form action=\"https://evil.example\"><input name=a><button>go</button></form><p>ok</p>", "ok" },
        { "<base href=\"https://evil.example\"><meta http-equiv=refresh content=0><link rel=stylesheet href=x><p>ok</p>", "ok" },
        { "<p>ok<!-- <script>alert(1)</script> --></p>", "ok" },
        { "<template><script>alert(1)</script></template><p>ok</p>", "ok" },
        { "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript><p>ok</p>", null },
        { "<p>unclosed <b>bold <i>both</p><script>alert(1)", "unclosed bold both" },
        { "<div><span onmouseover=alert(1)>kept</span></div>", "kept" },
        { "<details open ontoggle=alert(1)><summary>s</summary></details>", "s" },
        { "<video><source onerror=alert(1)></video><p>ok</p>", "ok" },
        { "<table background=\"javascript:alert(1)\"><tr><td onclick=alert(1)>c</td></tr></table>", "c" },
    };

    private static readonly string[] Forbidden =
    [
        "<script", "</script", "<iframe", "<object", "<embed", "<svg", "<math", "<img", "<style", "<form", "<input", "<button", "<base", "<meta",
        "<link", "<template", "<noscript", "<video", "<source", "<details", "javascript:", "vbscript:", "data:", "onerror", "onload", "onclick",
        "onmouseover", "ontoggle", "style=", "class=", "id=", "data-x", "background=", "<!--", "//evil",
    ];

    [Theory]
    [MemberData(nameof(XssCorpus))]
    public void Removes_everything_executable(string payload, string? keptText)
    {
        var clean = RichText.Sanitize(payload);

        foreach (var marker in Forbidden)
        {
            (clean ?? string.Empty).ShouldNotContain(marker, Case.Insensitive);
        }

        if (keptText is not null)
        {
            RichText.ToPlainText(clean).ShouldBe(keptText);
        }
    }

    [Fact]
    public void Keeps_the_allowed_formatting()
    {
        const string html =
            "<h2 dir=\"rtl\">عنوان</h2><p dir=\"ltr\"><strong>b</strong> <em>i</em> <u>u</u> <s>s</s> <code>c</code></p>" +
            "<ul><li>one</li></ul><ol><li>two</li></ol><blockquote>q</blockquote><pre>x  y</pre>" +
            "<table><thead><tr><th colspan=\"2\">h</th></tr></thead><tbody><tr><td rowspan=\"1\">a</td><td>b</td></tr></tbody></table>";

        RichText.Sanitize(html).ShouldBe(html);
    }

    [Fact]
    public void Links_keep_safe_schemes_and_always_get_rel()
    {
        RichText.Sanitize("<p><a href=\"https://example.org/a?b=1\" target=\"_blank\">x</a></p>")
            .ShouldBe("<p><a href=\"https://example.org/a?b=1\" target=\"_blank\" rel=\"noopener noreferrer\">x</a></p>");
        RichText.Sanitize("<p><a href=\"mailto:a@example.org\" rel=\"opener\">m</a></p>")
            .ShouldBe("<p><a href=\"mailto:a@example.org\" rel=\"noopener noreferrer\">m</a></p>");
        RichText.Sanitize("<p><a href=\"http://example.org\" target=\"_self\">h</a></p>")
            .ShouldBe("<p><a href=\"http://example.org\" rel=\"noopener noreferrer\">h</a></p>");
    }

    [Theory]
    [InlineData("<p dir=\"RTL\">a</p>", "<p dir=\"rtl\">a</p>")]
    [InlineData("<p dir=\"sideways\">a</p>", "<p>a</p>")]
    [InlineData("<p><strong dir=\"rtl\">a</strong></p>", "<p><strong>a</strong></p>")]
    [InlineData("<table><tbody><tr><td colspan=\"x\" rowspan=\"999\">a</td></tr></tbody></table>", "<table><tbody><tr><td>a</td></tr></tbody></table>")]
    [InlineData("<p colspan=\"2\">a</p>", "<p>a</p>")]
    [InlineData("<h1>t</h1>", "t")]
    public void Normalises_attributes_and_unknown_tags(string input, string expected) => RichText.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<script>alert(1)</script>")]
    public void Blank_or_empty_markup_becomes_null(string? input) => RichText.Sanitize(input).ShouldBeNull();

    [Theory]
    [InlineData("Plain minutes.\nSecond line.")]
    [InlineData("a < b and c > d")]
    [InlineData("x <y> z")]
    [InlineData("< p >x</ p >")]
    [InlineData("1 <2 & 3> 0")]
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<img src=x onerror=alert(1)")]
    public void Legacy_plain_text_is_detected_and_kept_unchanged(string value)
    {
        RichText.IsHtml(value).ShouldBeFalse();
        RichText.Sanitize(value).ShouldBe(value);
    }

    [Theory]
    [InlineData("<p>x</p>")]
    [InlineData("a<br>b")]
    [InlineData("<B>bold</B>")]
    [InlineData("text <script>alert(1)</script>")]
    [InlineData("<!-- c -->")]
    [InlineData("<p >x</p >")]
    [InlineData("a <b and c> d")]
    public void Markup_is_detected(string value) => RichText.IsHtml(value).ShouldBeTrue();

    public static TheoryData<string?, string> PlainCases => new()
    {
        { null, "" },
        { "plain\r\nline", "plain\nline" },
        { "<p>a</p><p>b</p>", "a\nb" },
        { "<p>a<br>b</p>", "a\nb" },
        { "<h2>Title</h2><p>Body &amp; more</p>", "Title\nBody & more" },
        { "<ul><li>one</li><li>two</li></ul>", "• one\n• two" },
        { "<ol><li>one</li><li>two</li></ol>", "1. one\n2. two" },
        { "<ul><li><p>nested p</p></li></ul>", "• nested p" },
        { "<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>", "A | B\n1 | 2" },
        { "<table><colgroup><col></colgroup><tbody><tr><td><p>x</p><p>y</p></td><td></td></tr></tbody></table>", "x y |" },
        { "<blockquote><p>quoted</p></blockquote>", "quoted" },
        { "<pre>a\n  b</pre>", "a\n  b" },
        { "<p>  many    spaces </p>", "many spaces" },
        { "<p dir=\"rtl\">مرحبا <strong>بالعالم</strong></p>", "مرحبا بالعالم" },
        { "<p><a href=\"https://example.org\">link</a></p>", "link" },
        { "<p>a</p><hr><p>b</p>", "a\n---\nb" },
    };

    [Theory]
    [MemberData(nameof(PlainCases))]
    public void Projects_to_plain_text(string? html, string expected) => RichText.ToPlainText(html).ShouldBe(expected);

    [Fact]
    public void Safe_html_escapes_plain_values_and_sanitises_html()
    {
        RichText.ToSafeHtml("a < b\nc\n\nd").ShouldBe("<p>a &lt; b<br>c</p><p>d</p>");
        RichText.ToSafeHtml("<p>x</p><script>alert(1)</script>").ShouldBe("<p>x</p>");
        RichText.ToSafeHtml(null).ShouldBe(string.Empty);
    }

    [Fact]
    public void Lengths_measure_plain_and_stored_size()
    {
        const string html = "<p><strong>abc</strong></p>";
        RichText.PlainLength(html).ShouldBe(3);
        RichText.StoredLength(html).ShouldBe(html.Length);
        RichText.Fits(html, 3).ShouldBeTrue();
        RichText.Fits(html, 2).ShouldBeFalse();
        RichText.Fits(html, 10, 5).ShouldBeFalse();
    }
}
