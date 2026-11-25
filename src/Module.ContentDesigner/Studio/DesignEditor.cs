namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>An edit the design rules do not allow; the message says why.</summary>
public sealed class EditException(string message) : Exception(message);

/// <summary>
/// The studio's edits, pure: each takes the views and returns new ones. Two kinds:
/// <list type="bullet">
/// <item><b>Structure</b> (regions, and which region a part is in): one tree for Mobile, Tablet and Desktop, so a
/// structural edit in a web view applies to every web view that has its own design. AMP has a tree of its own.</item>
/// <item><b>Per view</b> (order, hidden, span, tokens): only the view being edited. A view that inherits another is
/// detached first (it takes a copy of what it showed).</item>
/// </list>
/// Selection ids: a region is <c>region:{id}</c>, a part its own text (<c>prop:headline</c>, <c>content:{id}</c>, <c>items</c>).
/// </summary>
public static class DesignEditor
{
    public const string RegionPrefix = "region:";
    public const int MaxDepth = 4;
    public const int MaxOrder = 24;

    public static bool IsRegion(string id) => id.StartsWith(RegionPrefix, StringComparison.Ordinal);

    public static string RegionId(string id) => id[RegionPrefix.Length..];

    // ───────────── inheritance ─────────────

    /// <summary>The view whose own design <paramref name="view"/> shows (itself, or the end of its inheritance).</summary>
    public static string SourceOf(DViews views, string view)
    {
        var seen = new HashSet<string>();
        var current = view;
        while (views[current].InheritsFrom is { } from)
        {
            if (!seen.Add(current)) throw new EditException($"The {Views.Label(view)} view inherits in a loop.");
            current = from;
        }
        return current;
    }

    public static DView Resolve(DViews views, string view) => views[SourceOf(views, view)];

    /// <summary>The view gets a copy of what it showed and stops inheriting.</summary>
    public static DViews Detach(DViews views, string view)
    {
        var own = views[view];
        if (!own.Inherits) return views;
        var shown = Resolve(views, view);
        return views.With(view, shown with { InheritsFrom = null });
    }

    /// <summary>The view shows another one's design (its own is dropped).</summary>
    public static DViews Inherit(DViews views, string view, string from)
    {
        if (view == from) throw new EditException($"The {Views.Label(view)} view cannot inherit from itself.");
        for (var current = from; views[current].InheritsFrom is { } next; current = next)
            if (next == view) throw new EditException($"{Views.Label(from)} already shows {Views.Label(view)}: that would be a loop.");
        // Views that inherited this one now inherit what it will show, so nothing else changes under them.
        var result = views;
        foreach (var other in Views.All.Where(v => v != view && views[v].InheritsFrom == view))
            result = result.With(other, Resolve(views, view) with { InheritsFrom = null });
        return result.With(view, DView.Inherit(from));
    }

    // ───────────── reading ─────────────

    public static DRegion? FindRegion(DView view, string regionId) => view.AllRegions.FirstOrDefault(r => r.Id == regionId);

    public static DPlacement? FindPlacement(DView view, string part) => view.AllPlacements.FirstOrDefault(p => p.Part == part);

    /// <summary>Whether a selection id names something in the view.</summary>
    public static bool Exists(DView view, string id) =>
        IsRegion(id) ? FindRegion(view, RegionId(id)) is not null : FindPlacement(view, id) is not null;

    /// <summary>The region a selection is in (a region's parent; null at the top).</summary>
    public static string? ParentOf(DView view, string id) =>
        IsRegion(id) ? FindRegion(view, RegionId(id))?.ParentId : FindPlacement(view, id)?.RegionId;

    /// <summary>A region's children in display order: regions and placements share one order.</summary>
    public static IReadOnlyList<string> Children(DView view, string? regionId)
    {
        var regions = view.AllRegions.Select((r, i) => (Id: RegionPrefix + r.Id, r.Order, Type: 0, Index: i, Parent: r.ParentId));
        var parts = view.AllPlacements.Select((p, i) => (Id: p.Part, p.Order, Type: 1, Index: i, Parent: (string?)p.RegionId));
        return [.. regions.Concat(parts).Where(c => c.Parent == regionId).OrderBy(c => c.Order).ThenBy(c => c.Type).ThenBy(c => c.Index).Select(c => c.Id)];
    }

    public static int Depth(DView view, string regionId)
    {
        var depth = 0;
        for (var region = FindRegion(view, regionId); region is not null; region = region.ParentId is null ? null : FindRegion(view, region.ParentId))
            if (++depth > 16) break;
        return depth;
    }

    // ───────────── per view ─────────────

    /// <summary>Changes a placement in one view.</summary>
    public static DViews SetPlacement(DViews views, string view, string part, Func<DPlacement, DPlacement> change)
    {
        var detached = Detach(views, view);
        var design = detached[view];
        var placement = FindPlacement(design, part) ?? throw new EditException($"'{part}' is not placed in the {Views.Label(view)} view.");
        var changed = Checked(change(placement));
        return detached.With(view, design with { Placements = [.. design.AllPlacements.Select(p => p.Part == part ? changed : p)] });
    }

    /// <summary>Changes a region in one view.</summary>
    public static DViews SetRegion(DViews views, string view, string regionId, Func<DRegion, DRegion> change)
    {
        var detached = Detach(views, view);
        var design = detached[view];
        var region = FindRegion(design, regionId) ?? throw new EditException($"The {Views.Label(view)} view has no region '{regionId}'.");
        var changed = Checked(change(region));
        return detached.With(view, design with { Regions = [.. design.AllRegions.Select(r => r.Id == regionId ? changed : r)] });
    }

    /// <summary>Sets (or with null removes) a style token on a region or a placement, in one view.</summary>
    public static DViews SetToken(DViews views, string view, string id, string token, string? value)
    {
        if (value is not null && !Tokens.IsKnown(token, value)) throw new EditException($"'{value}' is not a value of the token '{token}'.");
        IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string>? tokens)
        {
            var result = new Dictionary<string, string>(tokens ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (value is null) result.Remove(token); else result[token] = value;
            return result;
        }
        return IsRegion(id)
            ? SetRegion(views, view, RegionId(id), r => r with { Tokens = With(r.Tokens) })
            : SetPlacement(views, view, id, p => p with { Tokens = With(p.Tokens) });
    }

    public static DViews SetHidden(DViews views, string view, string id, bool hidden) =>
        IsRegion(id) ? SetRegion(views, view, RegionId(id), r => r with { Hidden = hidden }) : SetPlacement(views, view, id, p => p with { Hidden = hidden });

    public static DViews SetSpan(DViews views, string view, string id, string? span) =>
        IsRegion(id) ? SetRegion(views, view, RegionId(id), r => r with { Span = span }) : SetPlacement(views, view, id, p => p with { Span = span });

    /// <summary>Moves a region or a part one place earlier (<paramref name="by"/> -1) or later (1) among its siblings, in one view.</summary>
    public static DViews Move(DViews views, string view, string id, int by)
    {
        var detached = Detach(views, view);
        var design = detached[view];
        if (!Exists(design, id)) throw new EditException($"The {Views.Label(view)} view has no '{id}'.");
        var siblings = Children(design, ParentOf(design, id)).ToList();
        var at = siblings.IndexOf(id);
        var to = Math.Clamp(at + by, 0, siblings.Count - 1);
        if (to == at) return views;
        siblings.RemoveAt(at);
        siblings.Insert(to, id);
        return detached.With(view, Renumber(design, siblings));
    }

    /// <summary>Copies a region's or a part's settings (order, hidden, span, tokens) from one view to another.</summary>
    public static DViews CopyTo(DViews views, string from, string to, string id)
    {
        if (from == to) return views;
        var source = Resolve(views, from);
        var detached = Detach(views, to);
        var target = detached[to];
        if (!Exists(source, id)) throw new EditException($"The {Views.Label(from)} view has no '{id}'.");
        if (!Exists(target, id)) throw new EditException($"The {Views.Label(to)} view has no '{id}' to copy to.");
        if (IsRegion(id))
        {
            var r = FindRegion(source, RegionId(id))!;
            return SetRegion(detached, to, r.Id, x => x with { Order = r.Order, Hidden = r.Hidden, Span = r.Span, Tokens = r.Tokens });
        }
        var p = FindPlacement(source, id)!;
        return SetPlacement(detached, to, id, x => x with { Order = p.Order, Hidden = p.Hidden, Span = p.Span, Tokens = p.Tokens });
    }

    // ───────────── structure ─────────────

    /// <summary>
    /// The views a structural edit in <paramref name="view"/> changes: AMP alone, or every web view with a design of its
    /// own (the edited one detached first). Web views that inherit follow their source.
    /// </summary>
    public static (DViews Views, IReadOnlyList<string> Targets) StructureTargets(DViews views, string view)
    {
        if (!Views.IsWeb(view)) return (Detach(views, view), [view]);
        var detached = Detach(views, view);
        return (detached, [.. Views.Web.Where(v => !detached[v].Inherits)]);
    }

    /// <summary>Adds a region (in <paramref name="parentId"/>, or at the top) at the end; returns its id.</summary>
    public static (DViews Views, string RegionId) AddRegion(DViews views, string view, string kind, string? parentId)
    {
        if (kind is not ("Section" or "Row" or "Column")) throw new EditException($"'{kind}' is not a region kind (Section, Row, Column).");
        var (result, targets) = StructureTargets(views, view);
        if (parentId is not null)
        {
            if (FindRegion(result[view], parentId) is null) throw new EditException($"There is no region '{parentId}'.");
            if (Depth(result[view], parentId) + 1 > MaxDepth) throw new EditException($"Regions nest at most {MaxDepth} deep.");
        }
        var taken = Views.All.SelectMany(v => result[v].AllRegions).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var stem = kind.ToLowerInvariant();
        var id = Enumerable.Range(1, 1000).Select(n => $"{stem}-{n}").First(candidate => !taken.Contains(candidate));
        foreach (var target in targets)
        {
            var design = result[target];
            var order = NextOrder(design, parentId);
            result = result.With(target, design with { Regions = [.. design.AllRegions, new DRegion(id, kind, parentId, order)] });
        }
        return (result, id);
    }

    /// <summary>Removes an empty region (no regions or parts in it in any view it is in).</summary>
    public static DViews RemoveRegion(DViews views, string view, string regionId)
    {
        var (result, targets) = StructureTargets(views, view);
        foreach (var target in targets)
        {
            var design = result[target];
            if (FindRegion(design, regionId) is null) continue;
            if (Children(design, regionId).Count > 0)
                throw new EditException($"Region '{regionId}' is not empty in the {Views.Label(target)} view: move or remove what is in it first.");
            result = result.With(target, design with { Regions = [.. design.AllRegions.Where(r => r.Id != regionId)] });
        }
        return result;
    }

    /// <summary>Places a part in a region (at the end), or moves it there when it is placed already.</summary>
    public static DViews Place(DViews views, string view, string part, string regionId)
    {
        if (!IsPart(part)) throw new EditException($"'{part}' is not a part (prop:{{key}}, content:{{id}} or items).");
        var (result, targets) = StructureTargets(views, view);
        foreach (var target in targets)
        {
            var design = result[target];
            if (FindRegion(design, regionId) is null) throw new EditException($"The {Views.Label(target)} view has no region '{regionId}'.");
            var existing = FindPlacement(design, part);
            if (existing?.RegionId == regionId) continue;
            var order = NextOrder(design, regionId);
            var placement = existing is null ? new DPlacement(part, regionId, order) : existing with { RegionId = regionId, Order = order };
            result = result.With(target, design with
            {
                Placements = [.. design.AllPlacements.Where(p => p.Part != part), placement]
            });
        }
        return result;
    }

    /// <summary>Takes a part out of the views (a content then shows through <c>items</c>, if that is placed).</summary>
    public static DViews Unplace(DViews views, string view, string part)
    {
        var (result, targets) = StructureTargets(views, view);
        foreach (var target in targets)
        {
            var design = result[target];
            result = result.With(target, design with { Placements = [.. design.AllPlacements.Where(p => p.Part != part)] });
        }
        return result;
    }

    public static bool IsPart(string text) =>
        text == "items" || (text.StartsWith("prop:", StringComparison.Ordinal) && text.Length > 5) || (text.StartsWith("content:", StringComparison.Ordinal) && text.Length > 8);

    // ───────────── helpers ─────────────

    private static int NextOrder(DView design, string? regionId)
    {
        var orders = design.AllRegions.Where(r => r.ParentId == regionId).Select(r => r.Order)
            .Concat(regionId is null ? [] : design.AllPlacements.Where(p => p.RegionId == regionId).Select(p => p.Order)).ToList();
        return Math.Min(orders.Count == 0 ? 0 : orders.Max() + 1, MaxOrder);
    }

    /// <summary>Gives the siblings the orders 0, 1, 2, ... in the list's order.</summary>
    private static DView Renumber(DView design, IReadOnlyList<string> siblings)
    {
        var rank = siblings.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => Math.Min(x.i, MaxOrder), StringComparer.Ordinal);
        return design with
        {
            Regions = [.. design.AllRegions.Select(r => rank.TryGetValue(RegionPrefix + r.Id, out var o) ? r with { Order = o } : r)],
            Placements = [.. design.AllPlacements.Select(p => rank.TryGetValue(p.Part, out var o) ? p with { Order = o } : p)]
        };
    }

    private static DPlacement Checked(DPlacement p)
    {
        if (!Tokens.IsSpan(p.Span)) throw new EditException("A span is 1 to 12 columns or 'full'.");
        return p;
    }

    private static DRegion Checked(DRegion r)
    {
        if (!Tokens.IsSpan(r.Span)) throw new EditException("A span is 1 to 12 columns or 'full'.");
        return r;
    }
}
