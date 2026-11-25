namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>What a palette item brings to the canvas.</summary>
public enum PaletteKind { Part, Region, Template, Library }

/// <summary>A palette item: a part to place, a region kind to add, a block template to apply, a library content to add.</summary>
public sealed record PaletteItem(PaletteKind Kind, string Value)
{
    public int? TemplateId => Kind == PaletteKind.Template && int.TryParse(Value, out var id) ? id : null;
}

/// <summary>
/// The keys of palette items. MudCanvas takes any element on the page with <c>data-block-new="{key}"</c> and
/// <c>draggable="true"</c> as a palette item and reports its key with the drop, so the Toolbox, the Contents tray and the
/// library picker (other dock panes, the same document) can all drop onto the canvas.
/// </summary>
public static class Palette
{
    public const string PartPrefix = "part:";
    public const string RegionPrefix = "new-region:";
    public const string TemplatePrefix = "template:";
    public const string LibraryPrefix = "library:";

    public static string Part(string part) => PartPrefix + part;

    public static string Region(string kind) => RegionPrefix + kind;

    public static string Template(int id) => TemplatePrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Library(string contentId) => LibraryPrefix + contentId;

    public static PaletteItem? Parse(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.StartsWith(PartPrefix, StringComparison.Ordinal) && DesignEditor.IsPart(key[PartPrefix.Length..]))
            return new(PaletteKind.Part, key[PartPrefix.Length..]);
        if (key.StartsWith(RegionPrefix, StringComparison.Ordinal) && key[RegionPrefix.Length..] is "Section" or "Row" or "Column")
            return new(PaletteKind.Region, key[RegionPrefix.Length..]);
        if (key.StartsWith(TemplatePrefix, StringComparison.Ordinal) && int.TryParse(key[TemplatePrefix.Length..], out var id) && id > 0)
            return new(PaletteKind.Template, key[TemplatePrefix.Length..]);
        if (key.StartsWith(LibraryPrefix, StringComparison.Ordinal) && key.Length > LibraryPrefix.Length)
            return new(PaletteKind.Library, key[LibraryPrefix.Length..]);
        return null;
    }

    /// <summary>The attributes that make an element a palette item.</summary>
    public static IReadOnlyDictionary<string, object> Attributes(string key) =>
        new Dictionary<string, object> { ["data-block-new"] = key, ["draggable"] = "true" };
}
