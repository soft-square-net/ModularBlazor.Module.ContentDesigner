namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// MudCanvas's <c>drop</c> gesture: a block moved (<see cref="Id"/>) or a palette item dropped (<see cref="Key"/>), before,
/// after or inside <see cref="TargetId"/>.
/// </summary>
public sealed record CanvasDrop(string Kind, string? Id, string? Key, string? TargetId, string? Position);

/// <summary>A block template as the planner needs it: its field and view.</summary>
public sealed record TemplateInfo(int Id, string FieldKey, string View, IReadOnlyDictionary<string, string>? Tokens = null);

/// <summary>A library content to add to the post, then place at <see cref="RegionId"/> / <see cref="Index"/>.</summary>
public sealed record LibraryDrop(string ContentId, string RegionId, int Index);

/// <summary>
/// What a drop does: an edit of the views (with what to select afterwards), or a library content to attach first.
/// </summary>
public sealed record DropPlan(Func<DViews, (DViews Views, string? Select)>? Edit = null, LibraryDrop? Attach = null);

/// <summary>
/// Turns a canvas drop into one edit (pure, so the rules are tested without a browser). The canvas sends where the
/// pointer was (before, after or inside a block); the planner finds the region and the position among the region's
/// children in the design. Blocks shown through <c>items</c> are not placed themselves: a drop next to one lands next to
/// <c>items</c>, and dragging one places it. A drop the rules refuse throws <see cref="EditException"/> with the reason.
/// </summary>
public static class DropPlanner
{
    /// <summary>The selection id of the page element: the canvas's root block, the top of the region tree.</summary>
    public const string Page = "page";

    public static DropPlan Plan(DViews views, string view, CanvasDrop drop, Func<string, string?> fieldOfContent, Func<int, TemplateInfo?> template)
    {
        var design = DesignEditor.Resolve(views, view);
        if (drop.Kind == "move")
        {
            var id = drop.Id ?? throw new EditException("Nothing was dragged.");
            if (id == Page) throw new EditException("The page itself does not move.");
            var (parent, index) = Where(design, drop, id);
            if (DesignEditor.Exists(design, id)) return Edit(v => DesignEditor.MoveTo(v, view, id, parent, index), id);
            if (DesignEditor.IsContent(id)) return Edit(v => DesignEditor.PlaceAt(v, view, id, Region(parent), index), id); // out of "items"
            throw new EditException($"The {Views.Label(view)} view has no '{id}'.");
        }

        var item = Palette.Parse(drop.Key) ?? throw new EditException("That cannot be dropped on the canvas.");
        switch (item.Kind)
        {
            case PaletteKind.Part:
            {
                var (parent, index) = Where(design, drop, item.Value);
                return DesignEditor.Exists(design, item.Value)
                    ? Edit(v => DesignEditor.MoveTo(v, view, item.Value, parent, index), item.Value)
                    : Edit(v => DesignEditor.PlaceAt(v, view, item.Value, Region(parent), index), item.Value);
            }
            case PaletteKind.Region:
            {
                var (parent, index) = Where(design, drop, null);
                return new DropPlan(v =>
                {
                    var (result, regionId) = DesignEditor.AddRegionAt(v, view, item.Value, parent, index);
                    return (result, DesignEditor.RegionPrefix + regionId);
                });
            }
            case PaletteKind.Template:
            {
                var info = template(item.TemplateId!.Value) ?? throw new EditException("That block template does not exist any more.");
                var part = drop.TargetId;
                if (part is null || !DesignEditor.IsContent(part))
                    throw new EditException($"Drop a {Label(info.FieldKey)} template on a {Label(info.FieldKey).ToLowerInvariant()} block.");
                var field = fieldOfContent(part[8..]);
                if (field != info.FieldKey)
                    throw new EditException($"A {Label(info.FieldKey)} template goes on a {Label(info.FieldKey).ToLowerInvariant()} block, not on {(field is null ? "this block" : "a " + Label(field).ToLowerInvariant())}.");
                if (info.View != view) throw new EditException($"This template is for the {Views.Label(info.View)} view.");
                if (DesignEditor.Exists(design, part)) return Edit(v => DesignEditor.SetBlockTemplate(v, view, part, info.Id), part);
                // Shown through "items": place it where items is, then apply the template.
                var items = DesignEditor.FindPlacement(design, "items") ?? throw new EditException($"'{part}' is not shown in the {Views.Label(view)} view.");
                var at = DesignEditor.Children(design, items.RegionId).ToList().IndexOf("items");
                return Edit(v => DesignEditor.SetBlockTemplate(DesignEditor.PlaceAt(v, view, part, items.RegionId, at), view, part, info.Id), part);
            }
            case PaletteKind.Library:
            {
                var (parent, index) = Where(design, drop, null);
                return new DropPlan(Attach: new LibraryDrop(item.Value, Region(parent), index));
            }
            default:
                throw new EditException("That cannot be dropped on the canvas.");
        }
    }

    /// <summary>The region (null: the top) and the position among its children where the drop lands.</summary>
    public static (string? Parent, int Index) Where(DView design, CanvasDrop drop, string? moving)
    {
        var target = drop.TargetId ?? Page;
        if (drop.Position == "inside")
        {
            if (target == Page) return (null, int.MaxValue);
            if (DesignEditor.IsRegion(target) && DesignEditor.FindRegion(design, DesignEditor.RegionId(target)) is not null)
                return (DesignEditor.RegionId(target), int.MaxValue);
            throw new EditException("Drop it in a region.");
        }
        var anchor = Anchor(design, target);
        var parent = DesignEditor.ParentOf(design, anchor);
        var siblings = DesignEditor.Children(design, parent).Where(x => x != moving).ToList();
        var at = siblings.IndexOf(anchor);
        if (at < 0) return (parent, siblings.Count); // dropped next to itself
        return (parent, drop.Position == "after" ? at + 1 : at);
    }

    /// <summary>The design child the target stands for: itself, or "items" for a block shown through it.</summary>
    private static string Anchor(DView design, string target)
    {
        if (target == Page) throw new EditException("Drop it in a region.");
        if (DesignEditor.Exists(design, target)) return target;
        if (DesignEditor.IsContent(target) && DesignEditor.FindPlacement(design, "items") is not null) return "items";
        throw new EditException($"'{target}' is not in this view.");
    }

    private static string Region(string? parent) => parent ?? throw new EditException("A part goes in a region, not at the top of the page.");

    private static DropPlan Edit(Func<DViews, DViews> edit, string select) => new(v => (edit(v), select));

    private static string Label(string fieldKey) => RenderMapping.Label(fieldKey);
}
