namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

/// <summary>
/// A post's kind is the key of a DynamicFields root post field (<c>post.news</c>). Content named kinds with an enum
/// before (<c>News</c>); this is the same mapping Content's own migration uses (wave 4 C1), applied to the stored
/// document templates and designs by the <c>PostKindsAsFieldKeys</c> migration and to input that still says the old name.
/// </summary>
public static class PostKindKeys
{
    /// <summary>The old enum names and the field keys that replaced them.</summary>
    public static IReadOnlyList<(string Name, string Key)> Legacy { get; } =
    [
        ("Article", "post.article"),
        ("News", "post.news"),
        ("Video", "post.video"),
        ("Reportage", "post.reportage"),
        ("PhotoStory", "post.photo-story"),
        ("Other", "post.other")
    ];

    /// <summary>The field key of an old enum name (any casing); any other text unchanged, trimmed.</summary>
    public static string Normalize(string? kind)
    {
        var text = kind?.Trim() ?? "";
        foreach (var (name, key) in Legacy)
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return key;
        return text;
    }
}
