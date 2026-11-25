using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>
/// Builds the page to render from a document and its design.
/// <list type="bullet">
/// <item><see cref="Merge"/>: Mobile, Tablet and Desktop into one responsive tree. The tree (regions, and the region
/// of every part) and the DOM order are Mobile's. Each element gets its Mobile values as base attributes and, where
/// Tablet or Desktop differ, <c>-md</c> / <c>-lg</c> ones: tokens, hidden, span. A region whose children Tablet or
/// Desktop order differently gets <c>data-mb-order-md</c> / <c>-lg</c> on every child.</item>
/// <item><see cref="Amp"/>: the AMP view alone, base values only (the AMP page has no container queries).</item>
/// </list>
/// A content's own style (from DynamicFields) is the start; block template tokens, then placement tokens, override
/// it in their view. <c>items</c> expands into the contents no placement names, in reading order.
/// </summary>
public static class ResponsiveMerge
{
    private const int MaxOrder = 24;

    public static PageModel Merge(RenderDocument document, RenderDesign design) =>
        Build(document, [design.Mobile, design.Tablet, design.Desktop], [TokenMap.Base, TokenMap.Md, TokenMap.Lg], amp: false);

    public static PageModel Amp(RenderDocument document, RenderDesign design) =>
        Build(document, [design.Amp], [TokenMap.Base], amp: true);

    /// <summary>
    /// One view alone as a web page, base values only, with the web renderers: the studio's AMP tab, which shows the AMP
    /// view's arrangement in a browser that runs no AMP runtime.
    /// </summary>
    public static PageModel Single(RenderDocument document, RenderView view) =>
        Build(document, [view], [TokenMap.Base], amp: false);

    private sealed record Child(int Order, int Type, int Index, RenderRegion? Region, RenderPlacement? Placement)
    {
        public string Key => Region is not null ? "region:" + Region.Id : Placement!.Part;
    }

    private static PageModel Build(RenderDocument document, RenderView[] views, int[] breakpoints, bool amp)
    {
        var structure = views[0];
        var blocks = new Dictionary<string, RenderBlock>(StringComparer.Ordinal);
        foreach (var block in document.Contents) { blocks.TryAdd(block.Id, block); blocks.TryAdd(block.NodeId, block); }

        var named = structure.Placements.Where(p => p.Part.StartsWith("content:", StringComparison.Ordinal))
            .Select(p => p.Part["content:".Length..]).Where(blocks.ContainsKey).Select(id => blocks[id].Id).ToHashSet(StringComparer.Ordinal);
        var rest = document.Contents.Where(c => !named.Contains(c.Id)).ToList();

        var regionsByView = views.Select(v => v.Regions.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)).ToArray();
        var placementsByView = views.Select(v => v.Placements.GroupBy(p => p.Part).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)).ToArray();

        List<PageNode> Walk(string? parentId, int depth)
        {
            var children = structure.Regions.Where(r => r.ParentId == parentId).Select((r, i) => new Child(r.Order, 0, i, r, null))
                .Concat(parentId is null ? [] : structure.Placements.Where(p => p.RegionId == parentId).Select((p, i) => new Child(p.Order, 1, i, null, p)))
                .OrderBy(c => c.Order).ThenBy(c => c.Type).ThenBy(c => c.Index).ToList();
            if (children.Count == 0 || depth > 8) return [];

            // The rank of each child per view; a view that orders them otherwise than the one below it writes order attributes.
            var ranks = new Dictionary<string, int>[views.Length];
            var needOrder = new bool[views.Length];
            for (var v = 0; v < views.Length; v++)
            {
                var view = v;
                ranks[v] = children
                    .Select(c => (c, order: c.Region is not null
                        ? regionsByView[view].GetValueOrDefault(c.Region.Id)?.Order ?? c.Order
                        : placementsByView[view].GetValueOrDefault(c.Placement!.Part)?.Order ?? c.Order))
                    .OrderBy(x => x.order).ThenBy(x => x.c.Type).ThenBy(x => x.c.Index)
                    .Select((x, rank) => (x.c.Key, rank)).ToDictionary(x => x.Key, x => Math.Min(x.rank, MaxOrder), StringComparer.Ordinal);
                needOrder[v] = v > 0 && children.Any(c => ranks[view][c.Key] != Effective(ranks, needOrder, view - 1, c.Key, children));
            }

            var nodes = new List<PageNode>();
            foreach (var child in children)
            {
                if (child.Region is { } region)
                {
                    var map = new TokenMap();
                    for (var v = 0; v < views.Length; v++)
                    {
                        var r = regionsByView[v].GetValueOrDefault(region.Id) ?? region;
                        map.SetAll(r.Tokens, breakpoints[v]);
                        map.Set("hidden", breakpoints[v], r.Hidden ? "true" : "false");
                        map.Set("span", breakpoints[v], r.Span);
                        if (needOrder[v]) map.Set("order", breakpoints[v], ranks[v][child.Key].ToString());
                    }
                    var attributes = map.Attributes();
                    attributes["data-mb-region"] = region.Kind.ToLowerInvariant();
                    attributes["data-mb-region-id"] = region.Id;
                    nodes.Add(new RegionNode(region.Id, region.Kind, attributes, Walk(region.Id, depth + 1)));
                    continue;
                }

                var placement = child.Placement!;
                var perView = Enumerable.Range(0, views.Length).Select(v => placementsByView[v].GetValueOrDefault(placement.Part) ?? placement).ToArray();
                TokenMap Placed(RenderBlock? block)
                {
                    var map = new TokenMap();
                    if (block is not null) map.SetStyle(block.Style, baseOnly: amp);
                    for (var v = 0; v < views.Length; v++)
                    {
                        map.SetAll(perView[v].BlockTemplate?.Tokens, breakpoints[v]);
                        map.SetAll(perView[v].Tokens, breakpoints[v]);
                        map.Set("hidden", breakpoints[v], perView[v].Hidden ? "true" : "false");
                        map.Set("span", breakpoints[v], perView[v].Span);
                        if (needOrder[v]) map.Set("order", breakpoints[v], ranks[v][child.Key].ToString());
                    }
                    return map;
                }

                if (placement.Part == "items")
                {
                    foreach (var block in rest) nodes.Add(Block(block, Placed(block), perView, breakpoints));
                }
                else if (placement.Part.StartsWith("prop:", StringComparison.Ordinal))
                {
                    var key = placement.Part["prop:".Length..];
                    var attributes = Placed(null).Attributes();
                    attributes["data-mb-prop"] = key;
                    nodes.Add(new PropNode(key, attributes));
                }
                else if (placement.Part.StartsWith("content:", StringComparison.Ordinal) && blocks.TryGetValue(placement.Part["content:".Length..], out var block))
                {
                    nodes.Add(Block(block, Placed(block), perView, breakpoints));
                }
            }
            return nodes;
        }

        return new PageModel(document, Walk(null, 0), amp);
    }

    private static int Effective(Dictionary<string, int>[] ranks, bool[] needOrder, int view, string key, List<Child> children)
    {
        for (var v = view; v > 0; v--) if (needOrder[v]) return ranks[v][key];
        return children.FindIndex(c => c.Key == key) is var i and >= 0 ? Math.Min(i, MaxOrder) : 0;
    }

    private static BlockNode Block(RenderBlock block, TokenMap map, RenderPlacement[] perView, int[] breakpoints)
    {
        var attributes = map.Attributes();
        attributes["data-mb-block"] = block.FieldKey;

        var hiddenKeys = perView.SelectMany(p => p.BlockTemplate?.HiddenParts ?? []).Distinct(StringComparer.Ordinal).ToList();
        var parts = new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal);
        var everywhere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in hiddenKeys)
        {
            var partMap = new TokenMap();
            var all = true;
            for (var v = 0; v < perView.Length; v++)
            {
                var hidden = perView[v].BlockTemplate?.HiddenParts?.Contains(key) == true;
                all &= hidden;
                partMap.Set("hidden", breakpoints[v], hidden ? "true" : "false");
            }
            if (all) everywhere.Add(key);
            else parts[key] = partMap.Attributes();
        }
        var order = perView.Select(p => p.BlockTemplate?.PartOrder).FirstOrDefault(o => o is { Count: > 0 }) ?? [];
        return new BlockNode(block, attributes, order, parts, everywhere);
    }
}
