using System.Text.Json;
using System.Text.Json.Nodes;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>
/// One plain text value edited in place on the studio's canvas: a root value of the post (<c>prop:{key}</c>) or a value
/// of one of its contents (<c>content:{id}</c> with <see cref="Key"/>). <see cref="Expected"/> is the text the studio
/// showed before the edit: when Content has another one now, someone changed it meanwhile and nothing is saved.
/// Returns the post's workspace again.
/// </summary>
public sealed record EditTextCommand(int PostId, string Part, string? Key, string Text, string? Expected, string? Culture = null)
    : ICommand<Result<WorkspaceDto>>;

/// <summary>
/// Saves the text through Content, which decides who may (the stage's editors for a content; the owner, the reviewer or
/// Desk for the post's own values) and keeps the revision history. Only DynamicFields Text values, single and in one line,
/// are edited here: rich text, lists and other kinds are refused (they are edited in Content). The post's working
/// document is always in its own language, so the studio's culture does not change what is saved.
/// </summary>
public sealed class EditTextHandler(IContentApi content, DesignWorkbench workbench, ISender sender) : ICommandHandler<EditTextCommand, Result<WorkspaceDto>>
{
    public const int MaxLength = 2_000;
    private const string PropPrefix = "prop:";
    private const string ContentPrefix = "content:";

    public async ValueTask<Result<WorkspaceDto>> Handle(EditTextCommand c, CancellationToken ct)
    {
        var text = OneLine(c.Text);
        if (text.Length > MaxLength) return Invalid(nameof(c.Text), $"The text is longer than {MaxLength} characters.");
        var isProp = c.Part.StartsWith(PropPrefix, StringComparison.Ordinal);
        var isContent = c.Part.StartsWith(ContentPrefix, StringComparison.Ordinal);
        var key = isProp ? c.Part[PropPrefix.Length..] : c.Key;
        if ((!isProp && !isContent) || string.IsNullOrWhiteSpace(key))
            return Invalid(nameof(c.Part), "Name a value: prop:{key}, or content:{id} with a key.");

        return await DesignResults.Calling<WorkspaceDto>(async () =>
        {
            if (await content.GetPostAsync(c.PostId, ct) is not { } post)
                return Result<WorkspaceDto>.NotFound(DesignResults.NoPost(c.PostId));

            if (isProp)
            {
                if (post.HasLegacyRoot) return Invalid(nameof(c.Part), "This post has no values of its kind yet: edit it in Content.");
                var (fieldVersion, root, style) = RootOf(post);
                var kind = (await workbench.FieldsAsync(post.DocumentField, fieldVersion, [], ct)).Kind;
                if (Refused(kind, key!) is { } why) return Invalid(nameof(c.Part), why);
                if (Changed(root, key!, c.Expected)) return Conflict(key!);
                await content.SetPostRootAsync(post.Id, post.Version, fieldVersion, With(root, key!, text), style, ct);
            }
            else
            {
                var contentId = c.Part[ContentPrefix.Length..];
                if (post.Contents.All(x => !string.Equals(x.ContentId, contentId, StringComparison.OrdinalIgnoreCase)))
                    return Invalid(nameof(c.Part), "That content is not in this post.");
                if (await content.GetContentAsync(contentId, ct) is not { } detail)
                    return Result<WorkspaceDto>.NotFound($"Content {contentId} does not exist, or you may not see it.");
                if (detail.Content.Archived) return Invalid(nameof(c.Part), "That content is archived.");
                var field = (await workbench.FieldsAsync(post.DocumentField, post.KindVersion,
                    [(detail.Content.FieldKey, detail.Content.FieldVersion)], ct)).Block(detail.Content.FieldKey, detail.Content.FieldVersion);
                if (Refused(field, key!) is { } why) return Invalid(nameof(c.Key), why);
                var values = detail.Node.ValueKind == JsonValueKind.Object && detail.Node.TryGetProperty("content", out var v) ? v : default;
                if (Changed(values, key!, c.Expected)) return Conflict(key!);
                var node = JsonNode.Parse(detail.Node.GetRawText())!.AsObject();
                node["content"] = JsonNode.Parse(With(values, key!, text).GetRawText());
                await content.EditContentAsync(contentId, detail.Version, JsonSerializer.SerializeToElement(node), ct);
            }
            return await sender.Send(new GetWorkspaceQuery(c.PostId, c.Culture), ct);
        });
    }

    /// <summary>Why a value may not be edited in place (null: it may): it must be one Text value of the field.</summary>
    internal static string? Refused(FieldVersionInfo? field, string key)
    {
        if (field is null) return "The field of this value is not known: edit it in Content.";
        if (field.Values.FirstOrDefault(v => v.Key == key) is not { } value) return $"'{key}' is not a value of '{field.Key}'.";
        if (!string.Equals(value.Kind, "Text", StringComparison.OrdinalIgnoreCase) || value.Multiple)
            return $"'{value.Label}' is not plain text: edit it in Content.";
        return null;
    }

    /// <summary>Whether Content has another text than the one the studio showed (no value counts as the empty text).</summary>
    internal static bool Changed(JsonElement values, string key, string? expected)
    {
        if (expected is null) return false;
        var now = values.ValueKind == JsonValueKind.Object && values.TryGetProperty(key, out var v) ? v : default;
        var current = now.ValueKind switch
        {
            JsonValueKind.String => now.GetString() ?? "",
            JsonValueKind.Undefined or JsonValueKind.Null => "",
            _ => null // not a text: never equal
        };
        return current is null || !string.Equals(OneLine(current), OneLine(expected), StringComparison.Ordinal);
    }

    /// <summary>The values with one text set (an empty text removes the value).</summary>
    internal static JsonElement With(JsonElement values, string key, string text)
    {
        var obj = values.ValueKind == JsonValueKind.Object ? JsonNode.Parse(values.GetRawText())!.AsObject() : [];
        if (text.Length == 0) obj.Remove(key);
        else obj[key] = text;
        return JsonSerializer.SerializeToElement(obj);
    }

    /// <summary>Line breaks become spaces and the ends are trimmed: these values are one line.</summary>
    internal static string OneLine(string? text) =>
        string.Join(' ', (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static (int FieldVersion, JsonElement Content, JsonElement Style) RootOf(ContentPost post)
    {
        var doc = post.Document;
        int fieldVersion = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("schemaVersion", out var sv) && sv.TryGetInt32(out var n) ? n : post.KindVersion;
        var style = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("style", out var s) && s.ValueKind == JsonValueKind.Object
            ? s.Clone()
            : JsonSerializer.SerializeToElement(new { });
        return (fieldVersion, PostDocuments.Root(doc), style);
    }

    private static Result<WorkspaceDto> Invalid(string name, string message) => Result<WorkspaceDto>.Invalid(new ValidationError(name, message));

    private static Result<WorkspaceDto> Conflict(string key) =>
        Result<WorkspaceDto>.Conflict($"'{key}' changed in Content since the studio showed it; the post is reloaded. Edit it again.");
}
