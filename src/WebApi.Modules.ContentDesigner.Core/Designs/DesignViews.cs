namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

/// <summary>
/// The four views of a design (a post's, or a document template's). A view may inherit another; inheritance never
/// loops. One content tree, four designs: views reorder, hide, span and restyle the same parts.
/// </summary>
public sealed record DesignViews(ViewDesign Mobile, ViewDesign Tablet, ViewDesign Desktop, ViewDesign Amp)
{
    /// <summary>The starter section on Mobile; Tablet and Desktop inherit it, and so does AMP.</summary>
    public static DesignViews Starter { get; } = new(
        ViewDesign.Starter,
        ViewDesign.Inherit(ViewKind.Mobile),
        ViewDesign.Inherit(ViewKind.Mobile),
        ViewDesign.Inherit(ViewKind.Mobile));

    public ViewDesign this[ViewKind view] => view switch
    {
        ViewKind.Mobile => Mobile,
        ViewKind.Tablet => Tablet,
        ViewKind.Desktop => Desktop,
        _ => Amp
    };

    public DesignViews With(ViewKind view, ViewDesign design) => view switch
    {
        ViewKind.Mobile => this with { Mobile = design },
        ViewKind.Tablet => this with { Tablet = design },
        ViewKind.Desktop => this with { Desktop = design },
        _ => this with { Amp = design }
    };

    /// <summary>The view as it shows: its own design, or the one it inherits (through any number of steps).</summary>
    public ViewDesign Resolve(ViewKind view)
    {
        var seen = new HashSet<ViewKind>();
        var current = view;
        while (this[current].InheritsFrom is { } from)
        {
            if (!seen.Add(current)) throw new DomainException($"The {view.Key()} view inherits in a loop.");
            current = from;
        }
        return this[current];
    }

    /// <summary>Every view's structural rules, and inheritance without loops.</summary>
    public DesignViews Checked()
    {
        var views = new DesignViews(
            Mobile.Checked("The mobile view"),
            Tablet.Checked("The tablet view"),
            Desktop.Checked("The desktop view"),
            Amp.Checked("The AMP view"));
        foreach (var view in ViewKinds.All)
        {
            if (views[view].InheritsFrom == view) throw new DomainException($"The {view.Key()} view cannot inherit from itself.");
            views.Resolve(view);
        }
        return views;
    }
}
