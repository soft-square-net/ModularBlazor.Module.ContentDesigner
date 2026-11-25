using System.Text.Json.Nodes;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>
/// Content's post document (<c>{ schemaId, schemaVersion, content, style, children: [{ nodeId, schemaId, ... }] }</c>)
/// in the shapes the other parties want: a DynamicFields content tree, the renderer's document, the checks' contents.
/// </summary>
public static class PostDocuments
{
    /// <summary>The document as a DynamicFields tree (<c>{ field, fieldVersion, content, style, children, nodeId }</c>).</summary>
    public static JsonNode ToFieldsTree(JsonElement document)
    {
        JsonObject Node(JsonElement node, bool root)
        {
            var result = new JsonObject
            {
                ["field"] = Str(node, "schemaId"),
                ["fieldVersion"] = Int(node, "schemaVersion"),
                ["content"] = Clone(node, "content") ?? new JsonObject(),
                ["style"] = Clone(node, "style") ?? new JsonObject()
            };
            if (!root && Str(node, "nodeId") is { Length: > 0 } nodeId) result["nodeId"] = nodeId;
            var children = new JsonArray();
            foreach (var child in Children(node)) children.Add(Node(child, false));
            result["children"] = children;
            return result;
        }
        return Node(document, true);
    }

    /// <summary>The top-level nodes of the document, in reading order.</summary>
    public static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray()
            : [];

    public static JsonElement Root(JsonElement document) =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty("content", out var content) ? content : default;

    /// <summary>The renderer's blocks: each top-level node, named by its content id when the map knows its node id.</summary>
    public static IReadOnlyList<RenderBlock> Blocks(JsonElement document, IReadOnlyDictionary<string, string> contentIdByNode)
    {
        RenderBlock Block(JsonElement node, bool top)
        {
            var nodeId = Str(node, "nodeId") ?? "";
            var id = top && contentIdByNode.TryGetValue(nodeId, out var contentId) ? contentId : nodeId;
            return new RenderBlock(id, nodeId, Str(node, "schemaId") ?? "", Int(node, "schemaVersion"),
                Get(node, "content"), Get(node, "style"), [.. Children(node).Select(c => Block(c, false))]);
        }
        return [.. Children(document).Select(c => Block(c, true))];
    }

    /// <summary>The post's contents for the checks, with each one's own style (base values).</summary>
    public static IReadOnlyList<DesignContent> Contents(ContentPost post)
    {
        var styles = Children(post.Document).Where(n => Str(n, "nodeId") is not null)
            .GroupBy(n => Str(n, "nodeId")!).ToDictionary(g => g.Key, g => BaseStyle(Get(g.First(), "style")), StringComparer.Ordinal);
        return [.. post.Contents.Select(c => new DesignContent(c.ContentId, c.NodeId, c.FieldKey, c.FieldVersion, c.Order, c.Required,
            styles.GetValueOrDefault(c.NodeId)))];
    }

    /// <summary>A node's style with each token's base value.</summary>
    public static IReadOnlyDictionary<string, string> BaseStyle(JsonElement style)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (style.ValueKind != JsonValueKind.Object) return result;
        foreach (var token in style.EnumerateObject())
        {
            if (token.Value.ValueKind == JsonValueKind.String) result[token.Name] = token.Value.GetString()!;
            else if (token.Value.ValueKind == JsonValueKind.Object && token.Value.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.String)
                result[token.Name] = b.GetString()!;
        }
        return result;
    }

    /// <summary>The ids of the asset values in a node's content, given its field version's asset keys.</summary>
    public static IEnumerable<string> AssetIds(JsonElement content, IEnumerable<string> assetKeys)
    {
        if (content.ValueKind != JsonValueKind.Object) yield break;
        foreach (var key in assetKeys)
        {
            if (!content.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } id) yield return id;
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } each) yield return each;
        }
    }

    public static string? Str(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int Int(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static JsonElement Get(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) ? v.Clone() : default;

    private static JsonNode? Clone(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? JsonNode.Parse(v.GetRawText()) : null;
}
