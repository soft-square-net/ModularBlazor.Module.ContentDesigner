using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>A node of the page to render: a region, a root value or a block, with its token attributes.</summary>
public abstract record PageNode(IReadOnlyDictionary<string, object> Attributes);

public sealed record RegionNode(string Id, string Kind, IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<PageNode> Children)
    : PageNode(Attributes);

public sealed record PropNode(string Key, IReadOnlyDictionary<string, object> Attributes) : PageNode(Attributes);

/// <summary>
/// A block. <see cref="PartOrder"/> and <see cref="PartAttributes"/> come from the block templates of the views: the
/// order of its inner parts and, per part, the <c>data-mb-hidden</c> attributes of the views that hide it.
/// </summary>
public sealed record BlockNode(
    RenderBlock Block,
    IReadOnlyDictionary<string, object> Attributes,
    IReadOnlyList<string> PartOrder,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> PartAttributes,
    IReadOnlySet<string> HiddenEverywhere) : PageNode(Attributes);

/// <summary>The page: the document and its tree of regions.</summary>
public sealed record PageModel(RenderDocument Document, IReadOnlyList<PageNode> Children, bool Amp);
