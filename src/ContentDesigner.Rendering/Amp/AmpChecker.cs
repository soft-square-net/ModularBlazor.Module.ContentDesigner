using System.Text;
using System.Text.RegularExpressions;

namespace soft.square.net.ContentDesigner.Rendering.Amp;

/// <summary>A broken AMP rule: what and where (the tag or attribute).</summary>
public sealed record AmpIssue(string Rule, string Message);

/// <summary>
/// The AMP rules a page can be checked against offline (the official validator needs Node; tests run it when
/// <c>CONTENTDESIGNER_AMP_VALIDATOR=1</c>). It checks the required markup (doctype, <c>&lt;html amp&gt;</c>, charset
/// first, viewport, canonical link, the runtime script, the boilerplate), the scripts (the runtime, the extension of every
/// amp-* component used and JSON-LD only), the forbidden tags (img, video, audio, iframe, ...), inline styles and event
/// handlers, the media layouts, and the custom CSS (one block, at most 75 KB, no <c>!important</c>).
/// </summary>
public static partial class AmpChecker
{
    public const string Runtime = "https://cdn.ampproject.org/v0.js";

    /// <summary>The components that need an extension script, and its version.</summary>
    public static IReadOnlyDictionary<string, string> Extensions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["amp-video"] = "0.1",
        ["amp-audio"] = "0.1",
        ["amp-carousel"] = "0.2",
        ["amp-iframe"] = "0.1"
    };

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "video", "audio", "iframe", "frame", "frameset", "embed", "object", "param", "applet", "base", "picture", "form", "input", "button", "select", "textarea"
    };

    private static readonly HashSet<string> NeedsLayout = new(StringComparer.Ordinal) { "amp-img", "amp-video", "amp-iframe", "amp-carousel" };

    public static string ExtensionScript(string component) =>
        $"<script async custom-element=\"{component}\" src=\"https://cdn.ampproject.org/v0/{component}-{Extensions[component]}.js\"></script>";

    public static IReadOnlyList<AmpIssue> Check(string html)
    {
        var issues = new List<AmpIssue>();
        if (!html.TrimStart().StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("doctype", "The page starts with <!doctype html>."));

        var tags = Tag().Matches(html).Select(m => (Name: m.Groups["name"].Value.ToLowerInvariant(), Attributes: Attributes(m.Groups["attrs"].Value), m.Index)).ToList();

        var htmlTag = tags.FirstOrDefault(t => t.Name == "html");
        if (htmlTag.Name is null || !(htmlTag.Attributes.ContainsKey("amp") || htmlTag.Attributes.ContainsKey("⚡")))
            issues.Add(new("html-amp", "The html element has the amp attribute."));

        var headIndex = tags.FindIndex(t => t.Name == "head");
        if (headIndex < 0 || headIndex + 1 >= tags.Count || tags[headIndex + 1] is not { Name: "meta" } first ||
            !first.Attributes.TryGetValue("charset", out var charset) || !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("charset", "<meta charset=\"utf-8\"> is the head's first child."));
        if (!tags.Any(t => t.Name == "meta" && t.Attributes.GetValueOrDefault("name") == "viewport"))
            issues.Add(new("viewport", "The head has a viewport meta tag."));
        if (!tags.Any(t => t.Name == "link" && t.Attributes.GetValueOrDefault("rel") == "canonical" && !string.IsNullOrWhiteSpace(t.Attributes.GetValueOrDefault("href"))))
            issues.Add(new("canonical", "The head links its canonical page (<link rel=\"canonical\">)."));
        if (!tags.Any(t => t.Name == "style" && t.Attributes.ContainsKey("amp-boilerplate")) || !html.Contains("<noscript><style amp-boilerplate>", StringComparison.Ordinal))
            issues.Add(new("boilerplate", "The head has the AMP boilerplate (style amp-boilerplate, and its noscript)."));

        var scripts = tags.Where(t => t.Name == "script").ToList();
        if (!scripts.Any(s => s.Attributes.GetValueOrDefault("src") == Runtime && s.Attributes.ContainsKey("async")))
            issues.Add(new("runtime", $"The head loads the AMP runtime ({Runtime}, async)."));
        foreach (var script in scripts)
        {
            var src = script.Attributes.GetValueOrDefault("src");
            var type = script.Attributes.GetValueOrDefault("type");
            if (src == Runtime || type == "application/ld+json") continue;
            if (script.Attributes.TryGetValue("custom-element", out var element) && Extensions.ContainsKey(element) &&
                src == $"https://cdn.ampproject.org/v0/{element}-{Extensions[element]}.js") continue;
            issues.Add(new("script", $"A script that is neither the AMP runtime, an AMP extension nor JSON-LD ({src ?? type ?? "inline"})."));
        }
        foreach (var component in Extensions.Keys.Where(c => tags.Any(t => t.Name == c)))
            if (!scripts.Any(s => s.Attributes.GetValueOrDefault("custom-element") == component))
                issues.Add(new("extension", $"<{component}> needs its extension script."));

        foreach (var tag in tags)
        {
            if (Forbidden.Contains(tag.Name) && !(tag.Name == "img" && InsideNoscript(html, tag.Index)))
                issues.Add(new("tag", $"<{tag.Name}> is not allowed in AMP; use its amp-* component."));
            if (tag.Name == "link" && tag.Attributes.GetValueOrDefault("rel") == "stylesheet")
                issues.Add(new("stylesheet", "A stylesheet link: AMP takes its CSS in <style amp-custom> only."));
            if (tag.Name == "style" && !tag.Attributes.ContainsKey("amp-custom") && !tag.Attributes.ContainsKey("amp-boilerplate"))
                issues.Add(new("style", "A <style> other than amp-custom and the boilerplate."));
            foreach (var name in tag.Attributes.Keys)
            {
                if (name == "style") issues.Add(new("style-attribute", $"<{tag.Name}> has a style attribute; use tokens."));
                if (name.StartsWith("on", StringComparison.Ordinal) && name != "on") issues.Add(new("event-handler", $"<{tag.Name}> has an event handler ({name})."));
            }
            if (NeedsLayout.Contains(tag.Name))
            {
                var layout = tag.Attributes.GetValueOrDefault("layout");
                var sized = tag.Attributes.ContainsKey("width") && tag.Attributes.ContainsKey("height");
                if (layout is not ("fill" or "nodisplay" or "flex-item" or "container") && !sized)
                    issues.Add(new("layout", $"<{tag.Name}> needs a width and height, or a fill layout."));
            }
            if (tag.Name is "amp-iframe" or "amp-video" or "amp-audio" && tag.Attributes.GetValueOrDefault("src") is { } source &&
                Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeHttps)
                issues.Add(new("https", $"<{tag.Name}> loads its source over https ({source})."));
        }

        var custom = CustomStyle().Matches(html);
        if (custom.Count > 1) issues.Add(new("amp-custom", "The page has one <style amp-custom> at most."));
        foreach (Match block in custom)
        {
            var css = block.Groups["css"].Value;
            var bytes = Encoding.UTF8.GetByteCount(css);
            if (bytes > AmpStyles.MaxBytes) issues.Add(new("css-size", $"The custom CSS is {bytes} bytes; AMP allows {AmpStyles.MaxBytes}."));
            if (css.Contains("!important", StringComparison.OrdinalIgnoreCase)) issues.Add(new("css-important", "The custom CSS uses !important."));
        }
        return issues;
    }

    /// <summary>The bytes of the page's custom CSS.</summary>
    public static int CustomCssBytes(string html) =>
        CustomStyle().Matches(html).Sum(m => Encoding.UTF8.GetByteCount(m.Groups["css"].Value));

    private static bool InsideNoscript(string html, int index)
    {
        var open = html.LastIndexOf("<noscript", index, StringComparison.OrdinalIgnoreCase);
        var close = html.LastIndexOf("</noscript", index, StringComparison.OrdinalIgnoreCase);
        return open > close;
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Attribute().Matches(text))
            result.TryAdd(m.Groups["name"].Value.ToLowerInvariant(),
                System.Net.WebUtility.HtmlDecode(m.Groups["dq"].Success ? m.Groups["dq"].Value : m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["bare"].Value));
        return result;
    }

    [GeneratedRegex(@"<(?<name>[a-zA-Z][a-zA-Z0-9-]*)(?<attrs>(?:\s+[^\s""'>/=]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+))?)*)\s*/?>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex(@"(?<name>[^\s""'>/=]+)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+)))?", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<style\s+amp-custom(?:=""[^""]*"")?\s*>(?<css>.*?)</style>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CustomStyle();
}
