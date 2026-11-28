using System.Text.Json;
using System.Text.Json.Nodes;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// The parts of the canvas whose whole element is one plain text value, so MudCanvas can edit them in place
/// (<c>data-has-text</c>): the headline and the post's other Text values (<c>prop:{key}</c>), and the heading and text
/// blocks (<c>content:{id}</c>, value <c>text</c>). Rich text, lists, dates and the other kinds are edited in Content.
/// The server checks the same (DynamicFields' kind of the value), so this is only what the canvas offers.
/// </summary>
public static class TextParts
{
    /// <summary>Blocks whose element holds only their <c>text</c> value (HeadingBlock, TextBlock).</summary>
    public static readonly IReadOnlySet<string> TextBlocks = new HashSet<string>(StringComparer.Ordinal) { "block.heading", "block.text" };

    public const string BlockKey = "text";

    /// <summary>The key of the text value a part shows when it can be edited in place; null when it cannot.</summary>
    public static string? KeyOf(RenderDocument document, string part)
    {
        if (part.StartsWith("prop:", StringComparison.Ordinal))
        {
            var key = part[5..];
            var known = document.Values?.GetValueOrDefault(key);
            var text = known is null ? key == "headline" : string.Equals(known.Kind, "Text", StringComparison.OrdinalIgnoreCase) && !known.Multiple;
            return text && IsText(document.Root, key) ? key : null;
        }
        if (part.StartsWith("content:", StringComparison.Ordinal) && BlockOf(document, part) is { } block
            && TextBlocks.Contains(block.FieldKey) && IsText(block.Content, BlockKey))
            return BlockKey;
        return null;
    }

    /// <summary>The text a part shows now ("" when it has none).</summary>
    public static string TextOf(RenderDocument document, string part, string key)
    {
        var values = part.StartsWith("prop:", StringComparison.Ordinal) ? document.Root : BlockOf(document, part)?.Content ?? default;
        return values.ValueKind == JsonValueKind.Object && values.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
    }

    /// <summary>The document with one text changed, so the canvas shows the edit while it is being saved.</summary>
    public static RenderDocument With(RenderDocument document, string part, string key, string text)
    {
        if (part.StartsWith("prop:", StringComparison.Ordinal)) return document with { Root = Set(document.Root, key, text) };
        var id = part["content:".Length..];
        return document with
        {
            Contents = [.. document.Contents.Select(b => b.Id == id ? b with { Content = Set(b.Content, key, text) } : b)]
        };
    }

    /// <summary>One line, trimmed: the values edited in place are single-line (the server does the same).</summary>
    public static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static RenderBlock? BlockOf(RenderDocument document, string part) =>
        document.Contents.FirstOrDefault(b => b.Id == part["content:".Length..]);

    /// <summary>A value that is a single string (an absent one counts: it can be written).</summary>
    private static bool IsText(JsonElement values, string key) =>
        values.ValueKind == JsonValueKind.Object && (!values.TryGetProperty(key, out var v) || v.ValueKind is JsonValueKind.String or JsonValueKind.Null);

    private static JsonElement Set(JsonElement values, string key, string text)
    {
        var obj = values.ValueKind == JsonValueKind.Object ? JsonNode.Parse(values.GetRawText())!.AsObject() : [];
        if (text.Length == 0) obj.Remove(key);
        else obj[key] = text;
        return JsonSerializer.SerializeToElement(obj);
    }
}

/// <summary>The stylesheet that dims the parts whose text is being saved (by their <c>data-block-id</c>).</summary>
public static class TextSaving
{
    public static string Css(IEnumerable<string> parts)
    {
        var selectors = parts.Select(p => $".cd-canvas [data-block-id=\"{Escape(p)}\"]").ToList();
        return selectors.Count == 0 ? "" : string.Join(",\n", selectors) + " { opacity: .6; cursor: progress; }";
    }

    /// <summary>A CSS string's text: quotes, backslashes and line ends escaped, and '&lt;' so it cannot end the style element.</summary>
    internal static string Escape(string value) =>
        string.Concat(value.Select(c => c switch
        {
            '"' or '\\' => "\\" + c,
            '<' => "\\3c ",
            '\n' or '\r' or '\f' => $"\\{(int)c:x} ",
            _ => c.ToString()
        }));
}
