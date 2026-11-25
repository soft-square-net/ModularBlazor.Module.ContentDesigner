using System.Text.Json;

namespace soft.square.net.ContentDesigner.Rendering.Model;

/// <summary>A region of a view (section, row or column), as the renderer gets it.</summary>
public sealed record RenderRegion(
    string Id,
    string Kind,
    string? ParentId = null,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null);

/// <summary>A block template a placement applies: inner part order, hidden parts, tokens.</summary>
public sealed record RenderBlockTemplate(
    IReadOnlyList<string>? PartOrder = null,
    IReadOnlyList<string>? HiddenParts = null,
    IReadOnlyDictionary<string, string>? Tokens = null);

/// <summary>Where one part (<c>prop:{key}</c>, <c>content:{id}</c>, <c>items</c>) shows in a view.</summary>
public sealed record RenderPlacement(
    string Part,
    string RegionId,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null,
    RenderBlockTemplate? BlockTemplate = null);

/// <summary>One view, inheritance already resolved.</summary>
public sealed record RenderView(IReadOnlyList<RenderRegion> Regions, IReadOnlyList<RenderPlacement> Placements)
{
    public static RenderView Empty { get; } = new([], []);
}

/// <summary>The four views of a design, resolved.</summary>
public sealed record RenderDesign(RenderView Mobile, RenderView Tablet, RenderView Desktop, RenderView Amp);

/// <summary>
/// One content of the post (a DynamicFields node): <see cref="Id"/> is the id the design names it by (Content's
/// content id; the node id when there is none), <see cref="Content"/> its values, <see cref="Style"/> its own style
/// (token → value, or token → { base, md, ... }).
/// </summary>
public sealed record RenderBlock(
    string Id,
    string NodeId,
    string FieldKey,
    int FieldVersion,
    JsonElement Content,
    JsonElement Style,
    IReadOnlyList<RenderBlock>? Children = null);

/// <summary>A file the post shows: its public URL and, when known, its size (AMP needs one) and type.</summary>
public sealed record RenderAsset(string Url, int? Width = null, int? Height = null, string? EncodingFormat = null);

/// <summary>What the renderer knows of a root value: its DynamicFields kind (Text, Asset, Date, ...) and label.</summary>
public sealed record RenderValue(string Kind, string? Label = null, bool Multiple = false);

/// <summary>
/// The post as the renderer gets it: its root values (<see cref="Root"/>, with <see cref="Values"/> describing them
/// when known), its contents in reading order, its culture and direction, and its URLs and JSON-LD.
/// </summary>
public sealed record RenderDocument(
    string Kind,
    string Culture,
    string Title,
    JsonElement Root,
    IReadOnlyList<RenderBlock> Contents,
    string Direction = "ltr",
    string? Description = null,
    string? CanonicalUrl = null,
    string? AmpUrl = null,
    string? JsonLd = null,
    IReadOnlyDictionary<string, RenderValue>? Values = null,
    IReadOnlyDictionary<string, RenderAsset>? Assets = null)
{
    /// <summary>Arabic, Hebrew, Persian, Urdu and the other right-to-left languages.</summary>
    public static string DirectionOf(string culture)
    {
        var language = culture.Split('-', 2)[0].ToLowerInvariant();
        return language is "ar" or "he" or "fa" or "ur" or "ps" or "sd" or "ug" or "yi" or "dv" or "ckb" ? "rtl" : "ltr";
    }
}

/// <summary>Site-wide render settings.</summary>
public sealed record RenderSettings
{
    /// <summary>The stylesheets of the canonical page: MudTail's core (palette) and its editorial pack.</summary>
    public IReadOnlyList<string> Stylesheets { get; init; } =
    [
        "/_content/soft.square.net.UI.MudTail/css/mudtail.min.css",
        "/_content/soft.square.net.UI.MudTail/css/packs/editorial.min.css"
    ];

    /// <summary>The URL of an asset the document does not describe; <c>{id}</c> is the asset id.</summary>
    public string AssetUrlTemplate { get; init; } = "/api/storage/items/{id}/content";

    public RenderAsset Asset(string id, IReadOnlyDictionary<string, RenderAsset>? known) =>
        known is not null && known.TryGetValue(id, out var asset)
            ? asset
            : new RenderAsset(AssetUrlTemplate.Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal));
}
