using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using soft.square.net.ContentDesigner.Rendering.Amp;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>A rendered page: its HTML, and for AMP the size of its custom CSS and the AMP rules it breaks.</summary>
public sealed record RenderedPage(string Html, bool Amp, int CssBytes, IReadOnlyList<AmpIssue> AmpIssues);

/// <summary>
/// Renders a designed post to a whole HTML page on the server, with .NET's <see cref="HtmlRenderer"/> running the same
/// Razor components the studio and the site use (<see cref="DesignBody"/>):
/// <list type="bullet">
/// <item><see cref="CanonicalAsync"/>: the responsive page (Mobile, Tablet and Desktop merged), the editorial
/// stylesheets, its JSON-LD and the <c>rel=amphtml</c> link.</item>
/// <item><see cref="AmpAsync"/>: the AMP page (the AMP view), the AMP boilerplate and runtime, the extension script of
/// every amp-* component it uses, its CSS inlined in <c>&lt;style amp-custom&gt;</c>, and checked by
/// <see cref="AmpChecker"/>.</item>
/// </list>
/// </summary>
public sealed class PageRenderer(IServiceProvider services, ILoggerFactory loggers, BlockRendererRegistry? registry = null)
{
    private const string Boilerplate =
        "<style amp-boilerplate>body{-webkit-animation:-amp-start 8s steps(1,end) 0s 1 normal both;-moz-animation:-amp-start 8s steps(1,end) 0s 1 normal both;-ms-animation:-amp-start 8s steps(1,end) 0s 1 normal both;animation:-amp-start 8s steps(1,end) 0s 1 normal both}@-webkit-keyframes -amp-start{from{visibility:hidden}to{visibility:visible}}@-moz-keyframes -amp-start{from{visibility:hidden}to{visibility:visible}}@-ms-keyframes -amp-start{from{visibility:hidden}to{visibility:visible}}@-o-keyframes -amp-start{from{visibility:hidden}to{visibility:visible}}@keyframes -amp-start{from{visibility:hidden}to{visibility:visible}}</style><noscript><style amp-boilerplate>body{-webkit-animation:none;-moz-animation:none;-ms-animation:none;animation:none}</style></noscript>";

    private BlockRendererRegistry Registry => registry ?? BlockRendererRegistry.Default;

    /// <summary>The body of a page model (the <c>&lt;article data-mb-page&gt;</c> element).</summary>
    public async Task<string> RenderBodyAsync(PageModel page, RenderSettings settings)
    {
        await using var renderer = new HtmlRenderer(services, loggers);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<DesignBody>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DesignBody.Page)] = page,
                [nameof(DesignBody.Settings)] = settings,
                [nameof(DesignBody.Registry)] = Registry
            }));
            return output.ToHtmlString();
        });
    }

    public async Task<RenderedPage> CanonicalAsync(RenderDocument document, RenderDesign design, RenderSettings settings)
    {
        var body = await RenderBodyAsync(ResponsiveMerge.Merge(document, design), settings);
        var html = new StringBuilder();
        Head(html, document, amp: false);
        foreach (var stylesheet in settings.Stylesheets) html.Append("<link rel=\"stylesheet\" href=\"").Append(Attr(stylesheet)).Append("\">");
        if (document.AmpUrl is { } amp) html.Append("<link rel=\"amphtml\" href=\"").Append(Attr(amp)).Append("\">");
        html.Append("</head><body>").Append(body).Append("</body></html>");
        return new RenderedPage(html.ToString(), false, 0, []);
    }

    public async Task<RenderedPage> AmpAsync(RenderDocument document, RenderDesign design, RenderSettings settings)
    {
        var body = await RenderBodyAsync(ResponsiveMerge.Amp(document, design), settings);
        var html = new StringBuilder();
        Head(html, document, amp: true);
        html.Append("<script async src=\"").Append(AmpChecker.Runtime).Append("\"></script>");
        foreach (var component in AmpChecker.Extensions.Keys.Where(c => body.Contains("<" + c, StringComparison.Ordinal)))
            html.Append(AmpChecker.ExtensionScript(component));
        html.Append(Boilerplate);
        html.Append("<style amp-custom>").Append(AmpStyles.Css).Append("</style>");
        html.Append("</head><body>").Append(body).Append("</body></html>");
        var text = html.ToString();
        return new RenderedPage(text, true, AmpChecker.CustomCssBytes(text), AmpChecker.Check(text));
    }

    private static void Head(StringBuilder html, RenderDocument document, bool amp)
    {
        html.Append("<!doctype html><html ").Append(amp ? "amp " : "")
            .Append("lang=\"").Append(Attr(document.Culture)).Append("\" dir=\"").Append(Attr(document.Direction)).Append("\">");
        html.Append("<head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>").Append(WebUtility.HtmlEncode(document.Title)).Append("</title>");
        if (document.Description is { Length: > 0 } description)
            html.Append("<meta name=\"description\" content=\"").Append(Attr(description)).Append("\">");
        if (document.CanonicalUrl is { } canonical) html.Append("<link rel=\"canonical\" href=\"").Append(Attr(canonical)).Append("\">");
        if (document.JsonLd is { Length: > 0 } jsonLd)
            html.Append("<script type=\"application/ld+json\">").Append(SafeJson(jsonLd)).Append("</script>");
    }

    private static string Attr(string value) => WebUtility.HtmlEncode(value);

    /// <summary>JSON inside a script element: no "&lt;" (so no "&lt;/script&gt;"), no line separators.</summary>
    internal static string SafeJson(string json)
    {
        var safe = new StringBuilder(json.Length);
        foreach (var c in json)
        {
            if (c is '<' or '>' or '&' || c == (char)0x2028 || c == (char)0x2029) safe.Append("\\u").Append(((int)c).ToString("x4"));
            else safe.Append(c);
        }
        return safe.ToString();
    }
}
