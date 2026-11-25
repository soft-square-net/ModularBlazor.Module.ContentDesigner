using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// The studio's design and the workspace's document in ContentDesigner.Rendering's terms, so the canvas renders the same
/// Razor components as the server's pages, live, with no round trip.
/// </summary>
public static class RenderMapping
{
    /// <summary>The four views with their inheritance resolved and their block templates attached.</summary>
    public static RenderDesign Design(DViews views, IReadOnlyDictionary<int, RenderBlockTemplate>? blockTemplates = null)
    {
        RenderView View(string view)
        {
            var design = DesignEditor.Resolve(views, view);
            return new RenderView(
                [.. design.AllRegions.Select(r => new RenderRegion(r.Id, r.Kind, r.ParentId, r.Order, r.Hidden, r.Span, r.Tokens))],
                [.. design.AllPlacements.Select(p => new RenderPlacement(p.Part, p.RegionId, p.Order, p.Hidden, p.Span, p.Tokens,
                    p.BlockTemplateId is { } id && blockTemplates?.TryGetValue(id, out var t) == true ? t : null))]);
        }
        return new RenderDesign(View(Views.Mobile), View(Views.Tablet), View(Views.Desktop), View(Views.Amp));
    }

    /// <summary>
    /// The page the canvas shows for a view: the web views as one responsive page (the device frame's width picks the
    /// breakpoint through the editorial pack's container queries), AMP as its own arrangement with the web renderers.
    /// </summary>
    public static PageModel Page(RenderDocument document, RenderDesign design, string view) =>
        Decorate(Views.IsWeb(view) ? ResponsiveMerge.Merge(document, design) : ResponsiveMerge.Single(document, design.Amp));

    /// <summary>
    /// Adds MudCanvas's attributes to every node, so a click selects it: <c>data-block-id</c> (the selection id:
    /// <c>region:{id}</c>, <c>prop:{key}</c>, <c>content:{id}</c>), <c>data-block-type</c> and <c>data-block-label</c> (the chip),
    /// <c>data-container</c> on regions. Blocks that come through <c>items</c> name their content too.
    /// </summary>
    public static PageModel Decorate(PageModel page)
    {
        PageNode Node(PageNode node) => node switch
        {
            RegionNode region => region with
            {
                Attributes = With(region.Attributes, DesignEditor.RegionPrefix + region.Id, region.Kind.ToLowerInvariant(),
                    $"{region.Kind} · {region.Id}", container: true, empty: region.Children.Count == 0),
                Children = [.. region.Children.Select(Node)]
            },
            PropNode prop => prop with { Attributes = With(prop.Attributes, "prop:" + prop.Key, "value", prop.Key) },
            BlockNode block => block with
            {
                Attributes = With(block.Attributes, "content:" + block.Block.Id, block.Block.FieldKey, Label(block.Block.FieldKey))
            },
            _ => node
        };
        return page with { Children = [.. page.Children.Select(Node)] };
    }

    private static Dictionary<string, object> With(IReadOnlyDictionary<string, object> attributes, string id, string type, string label,
        bool container = false, bool empty = false)
    {
        var result = new Dictionary<string, object>(attributes, StringComparer.Ordinal)
        {
            ["data-block-id"] = id,
            ["data-block-type"] = type,
            ["data-block-label"] = label
        };
        if (container) result["data-container"] = "true";
        if (empty) result["data-empty"] = "true";
        return result;
    }

    /// <summary>"block.video" → "Video".</summary>
    public static string Label(string fieldKey)
    {
        var name = fieldKey[(fieldKey.LastIndexOf('.') + 1)..].Replace('-', ' ').Replace('_', ' ');
        return name.Length == 0 ? fieldKey : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
