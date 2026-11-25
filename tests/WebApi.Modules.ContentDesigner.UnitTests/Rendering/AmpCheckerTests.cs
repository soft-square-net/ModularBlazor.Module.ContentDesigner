using soft.square.net.ContentDesigner.Rendering.Amp;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.Rendering;

/// <summary>The built-in AMP checks catch what the official validator would reject.</summary>
public class AmpCheckerTests
{
    private const string Boilerplate =
        "<style amp-boilerplate>body{visibility:hidden}</style><noscript><style amp-boilerplate>body{visibility:visible}</style></noscript>";

    private static string Page(string body = "<p>Hi</p>", string head = "", string css = "p{margin:0}") =>
        "<!doctype html><html amp lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
        "<link rel=\"canonical\" href=\"https://news.example/a\">" +
        $"<script async src=\"{AmpChecker.Runtime}\"></script>{head}{Boilerplate}<style amp-custom>{css}</style></head><body>{body}</body></html>";

    private static IEnumerable<string> Rules(string html) => AmpChecker.Check(html).Select(i => i.Rule);

    [Fact]
    public void AMinimalPagePasses() => AmpChecker.Check(Page()).ShouldBeEmpty();

    [Fact]
    public void ThePageIsMarkedAmp() => Rules(Page().Replace("<html amp ", "<html ")).ShouldContain("html-amp");

    [Fact]
    public void ThePageLinksItsCanonical() => Rules(Page().Replace("<link rel=\"canonical\" href=\"https://news.example/a\">", "")).ShouldContain("canonical");

    [Fact]
    public void CharsetComesFirst() =>
        Rules(Page().Replace("<head><meta charset=\"utf-8\">", "<head><title>x</title><meta charset=\"utf-8\">")).ShouldContain("charset");

    [Theory]
    [InlineData("<img src=\"a.jpg\">", "tag")]
    [InlineData("<iframe src=\"https://x\"></iframe>", "tag")]
    [InlineData("<p style=\"color:red\">x</p>", "style-attribute")]
    [InlineData("<button onclick=\"go()\">x</button>", "event-handler")]
    [InlineData("<amp-img src=\"a.jpg\"></amp-img>", "layout")]
    [InlineData("<amp-carousel layout=\"fill\"></amp-carousel>", "extension")]
    public void TheBodyUsesAmpOnly(string body, string rule) => Rules(Page(body)).ShouldContain(rule);

    [Fact]
    public void AnImageInNoscriptIsAllowed() =>
        AmpChecker.Check(Page("<amp-img src=\"a.jpg\" width=\"4\" height=\"3\" layout=\"responsive\"><noscript><img src=\"a.jpg\"></noscript></amp-img>")).ShouldBeEmpty();

    [Fact]
    public void OnlyAmpScriptsAndJsonLdRun()
    {
        Rules(Page(head: "<script src=\"https://example.com/app.js\"></script>")).ShouldContain("script");
        AmpChecker.Check(Page(head: "<script type=\"application/ld+json\">{}</script>")).ShouldBeEmpty();
        AmpChecker.Check(Page("<amp-carousel layout=\"fill\"></amp-carousel>", AmpChecker.ExtensionScript("amp-carousel"))).ShouldBeEmpty();
    }

    [Fact]
    public void CssStaysInAmpCustom()
    {
        Rules(Page(head: "<link rel=\"stylesheet\" href=\"https://x/site.css\">")).ShouldContain("stylesheet");
        Rules(Page(head: "<style>p{}</style>")).ShouldContain("style");
        Rules(Page(css: "p{color:red!important}")).ShouldContain("css-important");
        Rules(Page(css: "p{}" + new string(' ', AmpStyles.MaxBytes))).ShouldContain("css-size");
    }

    [Fact]
    public void MediaLoadsOverHttps() =>
        Rules(Page("<amp-iframe src=\"http://player.example/1\" layout=\"fill\" sandbox=\"allow-scripts\"></amp-iframe>", AmpChecker.ExtensionScript("amp-iframe")))
            .ShouldContain("https");

    [Fact]
    public void TheGeneratedCssFitsAmp()
    {
        System.Text.Encoding.UTF8.GetByteCount(AmpStyles.Css).ShouldBeLessThan(AmpStyles.MaxBytes);
        AmpStyles.Css.ShouldNotContain("!important");
    }
}
