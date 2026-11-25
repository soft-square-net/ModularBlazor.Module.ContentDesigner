using System.Diagnostics;
using System.Text.RegularExpressions;
using soft.square.net.ContentDesigner.Rendering.Amp;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.Rendering;

/// <summary>The sample news post rendered through the workbench: the responsive page and the AMP page.</summary>
public class PageRenderingTests
{
    private static async Task<string> Canonical(DesignViews? views = null, string? culture = null) =>
        (await Bench.Workbench().RenderDraftAsync(views ?? Samples.Design(), Samples.Post(), amp: false, culture, TestContext.Current.CancellationToken)).Html;

    private static Task<soft.square.net.ContentDesigner.Rendering.RenderedPage> Amp(DesignViews? views = null) =>
        Bench.Workbench().RenderDraftAsync(views ?? Samples.Design(), Samples.Post(), amp: true, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheCanonicalPageIsAWholeDocument()
    {
        var html = await Canonical();
        html.ShouldStartWith("<!doctype html><html lang=\"en\" dir=\"ltr\"><head><meta charset=\"utf-8\">");
        html.ShouldContain("<title>Final day at the venue</title>");
        html.ShouldContain("<link rel=\"canonical\" href=\"https://news.example/news/final-day\">");
        html.ShouldContain("<link rel=\"amphtml\" href=\"/api/contentdesigner/render/pub-7/amp\">");
        html.ShouldContain("<meta name=\"description\" content=\"The last day, in pictures.\">");
        html.ShouldContain("<article data-mb-page");
    }

    [Fact]
    public async Task JsonLdCannotCloseItsScript()
    {
        var html = await Canonical();
        html.ShouldContain("<script type=\"application/ld+json\">");
        html.ShouldNotContain("Final </script> day");
        html.ShouldContain("Final \\u003c/script\\u003e day");
    }

    [Fact]
    public async Task TheViewsMergeIntoBreakpointAttributes()
    {
        var html = await Canonical();
        html.ShouldContain("data-mb-region=\"row\" data-mb-region-id=\"body\"");
        // The body has one column on phones, three on desktops.
        html.ShouldContain("data-mb-columns=\"1\" data-mb-columns-lg=\"3\" data-mb-region=\"row\" data-mb-region-id=\"body\"");
        // The image's own style: full width, wide from tablets on.
        html.ShouldContain("data-mb-width-md=\"wide\"");
        // Desktop shows the image after the items: the DOM keeps Mobile's order, with order attributes for desktop.
        html.ShouldContain("data-mb-order-lg=");
        html.IndexOf("data-mb-block=\"block.image\"", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("data-mb-block=\"block.heading\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AViewCanHideAPart()
    {
        var views = Samples.Design();
        var desktop = views.Desktop with
        {
            Placements = [.. views.Desktop.AllPlacements.Select(p => p.Part == "prop:description" ? p with { Hidden = true } : p)]
        };
        (await Canonical(views with { Desktop = desktop })).ShouldContain("data-mb-hidden-lg=\"true\"");
    }

    [Fact]
    public async Task TextIsEncodedAndParagraphsSplit()
    {
        var html = await Canonical();
        html.ShouldNotContain("<b>was</b>");
        html.ShouldContain("&lt;b&gt;was&lt;/b&gt;");
        html.ShouldContain("Doors opened at nine.");
        html.ShouldContain("The main stage");
        html.ShouldContain("Photo: Ali");
    }

    [Fact]
    public async Task ARightToLeftCultureTurnsThePage() =>
        (await Canonical(culture: "ar")).ShouldStartWith("<!doctype html><html lang=\"ar\" dir=\"rtl\">");

    [Fact]
    public async Task TheAmpPagePassesTheAmpChecks()
    {
        var page = await Amp();
        page.AmpIssues.ShouldBeEmpty(string.Join("; ", page.AmpIssues.Select(i => i.Message)));
        page.CssBytes.ShouldBeGreaterThan(0);
        page.CssBytes.ShouldBeLessThan(AmpStyles.MaxBytes);
    }

    [Fact]
    public async Task TheAmpPageUsesAmpComponents()
    {
        var html = (await Amp()).Html;
        html.ShouldStartWith("<!doctype html><html amp ");
        html.ShouldContain("<amp-img");
        html.ShouldContain("<amp-carousel");
        html.ShouldContain("<amp-iframe");
        html.ShouldContain("https://www.youtube.com/embed/abc123");
        html.ShouldContain(AmpChecker.ExtensionScript("amp-carousel"));
        html.ShouldContain(AmpChecker.ExtensionScript("amp-iframe"));
        html.ShouldNotContain(AmpChecker.ExtensionScript("amp-audio"));
        html.ShouldNotContain("rel=\"stylesheet\"");
        Regex.IsMatch(html, "<img[\\s>]").ShouldBeFalse();
    }

    /// <summary>
    /// The official validator (the amphtml-validator npm package, through npx) on the AMP page. Opt in with
    /// CONTENTDESIGNER_AMP_VALIDATOR=1; it needs Node and the network.
    /// </summary>
    [Fact]
    public async Task TheOfficialValidatorPassesTheAmpPage()
    {
        if (Environment.GetEnvironmentVariable("CONTENTDESIGNER_AMP_VALIDATOR") != "1")
            Assert.Skip("Set CONTENTDESIGNER_AMP_VALIDATOR=1 to run the official AMP validator (needs Node and the network).");

        var file = Path.Combine(Path.GetTempPath(), $"contentdesigner-amp-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, (await Amp()).Html, TestContext.Current.CancellationToken);
        try
        {
            var npx = OperatingSystem.IsWindows() ? "npx.cmd" : "npx";
            using var process = Process.Start(new ProcessStartInfo(npx, ["--yes", "amphtml-validator", file])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)
                + await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.ShouldBe(0, output);
        }
        finally { File.Delete(file); }
    }
}
