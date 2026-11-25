using System.Globalization;
using System.Text.Json;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>
/// What a block renderer gets: the block's values and children, its token attributes, the inner parts the views hide
/// and their order, the culture, and whether it renders for AMP. Renderers read values through it, never the JSON.
/// </summary>
public sealed class BlockContext
{
    private readonly BlockNode _node;

    public BlockContext(BlockNode node, RenderDocument document, RenderSettings settings, bool amp)
    {
        _node = node;
        Document = document;
        Settings = settings;
        Amp = amp;
    }

    public RenderBlock Block => _node.Block;
    public RenderDocument Document { get; }
    public RenderSettings Settings { get; }
    public bool Amp { get; }
    public CultureInfo Culture => CultureOf(Document.Culture);

    /// <summary>The block element's attributes: data-mb-* tokens and data-mb-block.</summary>
    public IReadOnlyDictionary<string, object> Attributes => _node.Attributes;

    /// <summary>The attributes split in two: those of the named tokens (for an inner element) and the rest.</summary>
    public (Dictionary<string, object> Inner, Dictionary<string, object> Outer) Split(params string[] tokens)
    {
        var inner = new Dictionary<string, object>(StringComparer.Ordinal);
        var outer = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in _node.Attributes)
            (tokens.Any(t => name == $"data-mb-{t}" || name.StartsWith($"data-mb-{t}-", StringComparison.Ordinal)) ? inner : outer)[name] = value;
        return (inner, outer);
    }

    /// <summary>The base value of a token on this block (<c>level</c>: h2).</summary>
    public string? Token(string token) => _node.Attributes.TryGetValue($"data-mb-{token}", out var value) ? value as string : null;

    /// <summary>Whether a value shows: it has one, and no view hides it everywhere.</summary>
    public bool Shows(string key) => !_node.HiddenEverywhere.Contains(key) && Texts(key).Count > 0;

    /// <summary>The attributes of an inner part (the views that hide it), or none.</summary>
    public IReadOnlyDictionary<string, object> Part(string key) =>
        _node.PartAttributes.TryGetValue(key, out var attributes) ? attributes : Empty;

    /// <summary>The keys in the block template's order (the ones it names first), the shown ones only.</summary>
    public IEnumerable<string> Ordered(params string[] keys) =>
        keys.Select((k, i) => (k, rank: _node.PartOrder.IndexOf(k) is var at and >= 0 ? at : _node.PartOrder.Count + i))
            .OrderBy(x => x.rank).Select(x => x.k).Where(Shows);

    public string? Text(string key) => Texts(key) is { Count: > 0 } texts ? texts[0] : null;

    public IReadOnlyList<string> Texts(string key) => ValueTexts(Block.Content, key);

    public RenderAsset? Asset(string key) => Text(key) is { } id ? Settings.Asset(id, Document.Assets) : null;

    /// <summary>Every value of the block as text, in the content's order (for the fallback).</summary>
    public IEnumerable<(string Key, string Text)> AllValues()
    {
        if (Block.Content.ValueKind != JsonValueKind.Object) yield break;
        foreach (var entry in Block.Content.EnumerateObject())
            if (!_node.HiddenEverywhere.Contains(entry.Name))
                foreach (var text in ValueTexts(Block.Content, entry.Name))
                    yield return (entry.Name, text);
    }

    /// <summary>The children (a gallery's images), each with its own style and no placement.</summary>
    public IEnumerable<BlockContext> Children() =>
        (Block.Children ?? []).Select(child =>
        {
            var map = new TokenMap();
            map.SetStyle(child.Style, baseOnly: Amp);
            var attributes = map.Attributes();
            attributes["data-mb-block"] = child.FieldKey;
            return new BlockContext(new BlockNode(child, attributes, [], new Dictionary<string, IReadOnlyDictionary<string, object>>(), new HashSet<string>()),
                Document, Settings, Amp);
        });

    internal static IReadOnlyList<string> ValueTexts(JsonElement content, string key)
    {
        if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty(key, out var value)) return [];
        return value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(Scalar).OfType<string>()]
            : Scalar(value) is { } text ? [text] : [];
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : null,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object when value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String => id.GetString(),
        _ => null
    };

    internal static CultureInfo CultureOf(string culture)
    {
        try { return CultureInfo.GetCultureInfo(culture); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();
}

internal static class ListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }
}
