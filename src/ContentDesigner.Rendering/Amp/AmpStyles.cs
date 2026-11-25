using System.Text;

namespace soft.square.net.ContentDesigner.Rendering.Amp;

/// <summary>
/// The AMP page's <c>&lt;style amp-custom&gt;</c>: the editorial pack's token rules for the base breakpoint only (the
/// AMP page is one view, with no container queries), plain colours instead of the theme's palette variables, no
/// <c>!important</c>. Kept well under AMP's 75 KB limit.
/// </summary>
public static class AmpStyles
{
    public const int MaxBytes = 75_000;

    private static readonly string[] Spacing = ["none", "sm", "md", "lg", "xl"];
    private static readonly Dictionary<string, string> Space = new() { ["none"] = "0", ["sm"] = ".5rem", ["md"] = "1rem", ["lg"] = "2rem", ["xl"] = "3rem" };
    private static readonly Dictionary<string, string> TextSize = new() { ["sm"] = ".875rem", ["md"] = "1rem", ["lg"] = "1.25rem", ["xl"] = "1.5rem" };
    private static readonly Dictionary<string, string> Heading = new() { ["h1"] = "2.5rem", ["h2"] = "2rem", ["h3"] = "1.5rem", ["h4"] = "1.25rem", ["h5"] = "1.125rem", ["h6"] = "1rem" };
    private static readonly Dictionary<string, string> Aspect = new() { ["1-1"] = "1/1", ["4-3"] = "4/3", ["3-2"] = "3/2", ["16-9"] = "16/9", ["21-9"] = "21/9", ["3-4"] = "3/4", ["9-16"] = "9/16" };
    private static readonly string[] Columns = ["1", "2", "3", "4", "5", "6", "12"];
    private static readonly Dictionary<string, (string Bg, string Fg)> Tone = new()
    {
        ["default"] = ("transparent", "inherit"),
        ["muted"] = ("#f1f1f4", "#5f6368"),
        ["accent"] = ("#594ae2", "#ffffff"),
        ["inverse"] = ("#1f1f24", "#ffffff")
    };

    public static string Css { get; } = Build();

    private static string Build()
    {
        var css = new StringBuilder();
        css.Append("body{margin:0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Noto Sans Arabic',sans-serif;line-height:1.6;color:#1f1f24;background:#fff}");
        css.Append("[data-mb-page]{display:block;padding:0 1rem}");
        css.Append("figure{margin:0 0 1rem}img,video{max-width:100%;height:auto}figcaption{font-size:.875rem;color:#5f6368}");
        css.Append("[data-mb-region]{display:block}");
        css.Append("[data-mb-width=content]{max-width:42rem;margin-left:auto;margin-right:auto}");
        css.Append("[data-mb-width=wide]{max-width:64rem;margin-left:auto;margin-right:auto}");
        css.Append("[data-mb-width=full]{max-width:none}");
        css.Append("[data-mb-width=half],[data-mb-width=third]{max-width:none}");
        css.Append("[data-mb-align=start]{text-align:start}[data-mb-align=center]{text-align:center}[data-mb-align=end]{text-align:end}");
        foreach (var v in Spacing) css.Append($"[data-mb-spacing={v}]{{padding-top:{Space[v]};padding-bottom:{Space[v]}}}");
        foreach (var (v, (bg, fg)) in Tone) css.Append($"[data-mb-tone={v}]{{background-color:{bg};color:{fg}}}");
        foreach (var (v, size) in TextSize) css.Append($"[data-mb-size={v}]{{font-size:{size}}}");
        foreach (var (v, size) in Heading) css.Append($"[data-mb-level={v}]{{font-size:{size};line-height:1.25}}");
        css.Append("h1{font-size:2rem;line-height:1.25}");
        css.Append("[data-mb-aspect]{position:relative}[data-mb-aspect=auto]{aspect-ratio:auto}");
        foreach (var (v, ratio) in Aspect) css.Append($"[data-mb-aspect=\"{v}\"]{{aspect-ratio:{ratio};overflow:hidden}}");
        foreach (var n in Columns) css.Append($"[data-mb-columns=\"{n}\"]{{display:grid;grid-template-columns:repeat({n},minmax(0,1fr));gap:1rem}}");
        css.Append("[data-mb-layout=standard]{max-width:72rem;margin-left:auto;margin-right:auto}");
        css.Append("[data-mb-layout=wide]{max-width:90rem;margin-left:auto;margin-right:auto}");
        css.Append("[data-mb-layout=full]{max-width:none}");
        for (var n = 1; n <= 12; n++) css.Append($"[data-mb-span=\"{n}\"]{{grid-column:span {n}/span {n}}}");
        css.Append("[data-mb-span=full]{grid-column:1/-1}");
        css.Append("[data-mb-hidden=true]{display:none}");
        return css.ToString();
    }
}
