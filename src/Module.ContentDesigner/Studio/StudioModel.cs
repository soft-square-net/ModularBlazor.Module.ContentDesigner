using System.Text.Json;
using System.Text.Json.Serialization;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Studio;

// The studio's own copy of a design, immutable, in the API's JSON shape (Core's ViewDesign): edits make new values, so
// undo keeps snapshots and the canvas compares by reference. It converts to and from the generated client's types by JSON.

/// <summary>A region of a view: a section, a row or a column.</summary>
public sealed record DRegion(
    string Id,
    string Kind,
    string? ParentId = null,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null);

/// <summary>Where a part (<c>prop:{key}</c>, <c>content:{id}</c>, <c>items</c>) shows in a view.</summary>
public sealed record DPlacement(
    string Part,
    string RegionId,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null,
    int? BlockTemplateId = null);

/// <summary>One view: its regions and placements, or the view it inherits (then it has neither).</summary>
public sealed record DView(IReadOnlyList<DRegion>? Regions = null, IReadOnlyList<DPlacement>? Placements = null, string? InheritsFrom = null)
{
    [JsonIgnore] public IReadOnlyList<DRegion> AllRegions => Regions ?? [];
    [JsonIgnore] public IReadOnlyList<DPlacement> AllPlacements => Placements ?? [];
    [JsonIgnore] public bool Inherits => InheritsFrom is not null;

    public static DView Inherit(string from) => new(InheritsFrom: from);
}

/// <summary>The four views.</summary>
public sealed record DViews(DView Mobile, DView Tablet, DView Desktop, DView Amp)
{
    public DView this[string view] => view switch
    {
        Views.Mobile => Mobile,
        Views.Tablet => Tablet,
        Views.Desktop => Desktop,
        Views.Amp => Amp,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Not a view.")
    };

    public DViews With(string view, DView design) => view switch
    {
        Views.Mobile => this with { Mobile = design },
        Views.Tablet => this with { Tablet = design },
        Views.Desktop => this with { Desktop = design },
        Views.Amp => this with { Amp = design },
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Not a view.")
    };
}

/// <summary>The view keys, as the API's JSON names them ("Mobile") and as the routes do ("mobile").</summary>
public static class Views
{
    public const string Mobile = "Mobile";
    public const string Tablet = "Tablet";
    public const string Desktop = "Desktop";
    public const string Amp = "Amp";

    public static IReadOnlyList<string> All { get; } = [Mobile, Tablet, Desktop, Amp];

    /// <summary>The views that merge into one responsive page and share one region tree.</summary>
    public static IReadOnlyList<string> Web { get; } = [Mobile, Tablet, Desktop];

    public static bool IsWeb(string view) => view != Amp;

    public static string Label(string view) => view == Amp ? "AMP" : view;

    /// <summary>MudCanvas's device key.</summary>
    public static string Device(string view) => view.ToLowerInvariant();

    public static ViewKind Kind(string view) => Enum.Parse<ViewKind>(view);
}

/// <summary>The style tokens a design may set: UI.MudTail's editorial pack (the server checks the same list).</summary>
public static class Tokens
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Values { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["width"] = ["content", "wide", "full", "half", "third"],
        ["align"] = ["start", "center", "end"],
        ["spacing"] = ["none", "sm", "md", "lg", "xl"],
        ["tone"] = ["default", "muted", "accent", "inverse"],
        ["size"] = ["sm", "md", "lg", "xl"],
        ["level"] = ["h1", "h2", "h3", "h4", "h5", "h6"],
        ["aspect"] = ["auto", "1-1", "4-3", "3-2", "16-9", "21-9", "3-4", "9-16"],
        ["columns"] = ["1", "2", "3", "4", "5", "6", "12"],
        ["layout"] = ["standard", "wide", "full"]
    };

    /// <summary>The tokens offered for a region: its layout ones.</summary>
    public static IReadOnlyList<string> ForRegion { get; } = ["layout", "columns", "width", "spacing", "align", "tone"];

    public static IReadOnlyList<string> Spans { get; } = ["full", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"];

    public static bool IsKnown(string token, string value) => Values.TryGetValue(token, out var values) && values.Contains(value);

    public static bool IsSpan(string? span) =>
        span is null || span == "full" || (int.TryParse(span, out var n) && n is >= 1 and <= 12 && n.ToString() == span);
}

/// <summary>JSON between the generated client's types and the studio's.</summary>
public static class StudioJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TTo Convert<TTo>(object value) =>
        JsonSerializer.Deserialize<TTo>(JsonSerializer.Serialize(value, value.GetType(), Options), Options)
        ?? throw new JsonException($"Could not read a {typeof(TTo).Name}.");

    public static DViews From(DesignViews views) => Convert<DViews>(views);

    public static DesignViews ToApi(DViews views) => Convert<DesignViews>(views);
}
