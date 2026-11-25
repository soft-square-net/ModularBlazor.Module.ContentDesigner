using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;

/// <summary>
/// A block template: how one block field (<c>block.video</c>, ...) looks in one view. The order of its inner parts (the
/// field's value keys: caption before credit, ...), the ones it hides, its style tokens, and optionally a content (a
/// DynamicFields node as JSON: <c>{ field, fieldVersion, content, style }</c>) for a block that brings its values along.
/// </summary>
public sealed class BlockTemplate : AggregateRoot<int>, ITenantOwned
{
    public const int NameMaxLength = 200;
    public const int FieldKeyMaxLength = 100;
    public const int MaxParts = 50;
    public const int ContentMaxLength = 64 * 1024;

    private BlockTemplate() { } // EF Core

    public string TenantId { get; set; } = "";
    public string Name { get; private set; } = "";
    public string FieldKey { get; private set; } = "";
    public ViewKind View { get; private set; }
    public IReadOnlyList<string> PartOrder { get; private set; } = [];
    public IReadOnlyList<string> HiddenParts { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Tokens { get; private set; } = StyleTokens.Empty;

    /// <summary>The block's content as a JSON node, or null.</summary>
    public string? ContentJson { get; private set; }

    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public static BlockTemplate Create(string name, string fieldKey, ViewKind view, IReadOnlyList<string>? partOrder,
        IReadOnlyList<string>? hiddenParts, IReadOnlyDictionary<string, string>? tokens, string? contentJson, string by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(by)) throw new DomainException("A template needs its author.");
        if (string.IsNullOrWhiteSpace(fieldKey) || fieldKey.Length > FieldKeyMaxLength) throw new DomainException("A block template is for a block field (its key).");
        var template = new BlockTemplate { FieldKey = fieldKey, View = view, CreatedBy = by, CreatedAt = now, UpdatedAt = now };
        template.Set(name, partOrder, hiddenParts, tokens, contentJson);
        return template;
    }

    public void Edit(string name, IReadOnlyList<string>? partOrder, IReadOnlyList<string>? hiddenParts,
        IReadOnlyDictionary<string, string>? tokens, string? contentJson, DateTimeOffset now)
    {
        Set(name, partOrder, hiddenParts, tokens, contentJson);
        UpdatedAt = now;
        Version++;
    }

    private void Set(string name, IReadOnlyList<string>? partOrder, IReadOnlyList<string>? hiddenParts,
        IReadOnlyDictionary<string, string>? tokens, string? contentJson)
    {
        Name = Names.Checked(name, NameMaxLength, "A block template");
        PartOrder = Keys(partOrder, "The part order");
        HiddenParts = Keys(hiddenParts, "The hidden parts");
        Tokens = StyleTokens.Checked(tokens, "A block template");
        if (contentJson is { Length: > ContentMaxLength }) throw new DomainException($"A block template's content is at most {ContentMaxLength / 1024} KB.");
        if (contentJson is not null)
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(contentJson);
                if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                    !json.RootElement.TryGetProperty("field", out var field) || field.GetString() != FieldKey)
                    throw new DomainException($"A block template's content is a node of '{FieldKey}'.");
            }
            catch (System.Text.Json.JsonException) { throw new DomainException("A block template's content is JSON."); }
        }
        ContentJson = contentJson;
    }

    private static IReadOnlyList<string> Keys(IReadOnlyList<string>? keys, string what)
    {
        if (keys is null || keys.Count == 0) return [];
        if (keys.Count > MaxParts) throw new DomainException($"{what} has at most {MaxParts} keys.");
        if (keys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > PartRef.MaxKeyLength)) throw new DomainException($"{what} holds value keys.");
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) throw new DomainException($"{what} names a key twice.");
        return [.. keys];
    }
}
