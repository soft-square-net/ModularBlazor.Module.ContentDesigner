namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

public enum PartKind
{
    /// <summary>A root value of the post (<c>prop:headline</c>).</summary>
    Prop,
    /// <summary>One content attached to the post (<c>content:{id}</c>).</summary>
    Content,
    /// <summary>The post's other contents, the ones no placement names, in reading order (<c>items</c>).</summary>
    Items
}

/// <summary>What a placement shows: <c>prop:{key}</c>, <c>content:{id}</c> or <c>items</c>.</summary>
public readonly record struct PartRef(PartKind Kind, string Key)
{
    public const string ItemsText = "items";
    public const int MaxKeyLength = 100;

    public static PartRef Items { get; } = new(PartKind.Items, "");

    public static PartRef Prop(string key) => new(PartKind.Prop, key);

    public static PartRef Content(string id) => new(PartKind.Content, id);

    public static bool TryParse(string? text, out PartRef part)
    {
        part = default;
        if (text is null) return false;
        if (text == ItemsText) { part = Items; return true; }
        var colon = text.IndexOf(':');
        if (colon <= 0) return false;
        var key = text[(colon + 1)..];
        if (key.Length is 0 or > MaxKeyLength || key.Trim() != key) return false;
        switch (text[..colon])
        {
            case "prop": part = Prop(key); return true;
            case "content": part = Content(key); return true;
            default: return false;
        }
    }

    public static PartRef Parse(string text) =>
        TryParse(text, out var part) ? part : throw new DomainException($"'{text}' is not a part (prop:{{key}}, content:{{id}} or items).");

    public override string ToString() => Kind switch
    {
        PartKind.Items => ItemsText,
        PartKind.Prop => "prop:" + Key,
        _ => "content:" + Key
    };
}
