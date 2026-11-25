using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

[JsonConverter(typeof(JsonStringEnumConverter<RegionKind>))]
public enum RegionKind
{
    Section,
    Row,
    Column
}

/// <summary>
/// A region of a view: a section, a row or a column. Regions are this module's own layout, never DynamicFields fields.
/// <see cref="Order"/> places it among its parent's children (regions and placements share one order).
/// </summary>
public sealed record Region(
    string Id,
    RegionKind Kind,
    string? ParentId = null,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null);

/// <summary>
/// Where one part shows in a view: its region, its order among the region's children, whether the view hides it, the
/// columns it spans, its style tokens, and optionally a block template (for a content).
/// </summary>
public sealed record Placement(
    string Part,
    string RegionId,
    int Order = 0,
    bool Hidden = false,
    string? Span = null,
    IReadOnlyDictionary<string, string>? Tokens = null,
    int? BlockTemplateId = null);

/// <summary>
/// One view's arrangement of a post: regions and the placements of its parts. A view that
/// <see cref="InheritsFrom"/> another has no regions or placements of its own and shows the other one's.
/// </summary>
public sealed record ViewDesign(
    IReadOnlyList<Region>? Regions = null,
    IReadOnlyList<Placement>? Placements = null,
    ViewKind? InheritsFrom = null)
{
    public const int MaxRegions = 200;
    public const int MaxPlacements = 1000;
    public const int MaxDepth = 4;
    public const int MaxIdLength = 64;

    [JsonIgnore] public IReadOnlyList<Region> AllRegions => Regions ?? [];
    [JsonIgnore] public IReadOnlyList<Placement> AllPlacements => Placements ?? [];

    /// <summary>One section with every content in reading order: what a post with no template starts with.</summary>
    public static ViewDesign Starter { get; } = new(
        [new Region("main", RegionKind.Section, Tokens: new Dictionary<string, string> { ["layout"] = "standard" })],
        [new Placement("prop:headline", "main", 0), new Placement(PartRef.ItemsText, "main", 1)]);

    public static ViewDesign Inherit(ViewKind from) => new(InheritsFrom: from);

    /// <summary>
    /// The structural rules (a broken one throws <see cref="DomainException"/>, so a design is always well formed):
    /// region ids unique, parents exist, no cycles, at most <see cref="MaxDepth"/> levels; each part placed at most
    /// once and in an existing region; tokens and spans the editorial pack knows. Whether the parts exist in the post
    /// and the view reads well are checks (<see cref="DesignRules"/>), reported as problems.
    /// </summary>
    public ViewDesign Checked(string where)
    {
        if (InheritsFrom is not null)
        {
            if (AllRegions.Count > 0 || AllPlacements.Count > 0)
                throw new DomainException($"{where} inherits from {InheritsFrom.Value.Key()}, so it has no regions or placements of its own.");
            return this;
        }
        if (AllRegions.Count > MaxRegions) throw new DomainException($"{where} has more than {MaxRegions} regions.");
        if (AllPlacements.Count > MaxPlacements) throw new DomainException($"{where} has more than {MaxPlacements} placements.");

        var regions = new Dictionary<string, Region>(StringComparer.Ordinal);
        var checkedRegions = new List<Region>();
        foreach (var region in AllRegions)
        {
            if (string.IsNullOrWhiteSpace(region.Id) || region.Id.Length > MaxIdLength || region.Id.Trim() != region.Id)
                throw new DomainException($"{where}: a region id is 1 to {MaxIdLength} characters, without spaces around.");
            if (!regions.TryAdd(region.Id, region)) throw new DomainException($"{where}: the region id '{region.Id}' is used twice.");
            if (!StyleTokens.IsSpan(region.Span)) throw new DomainException($"{where}: region '{region.Id}' spans 1 to {StyleTokens.MaxSpan} columns or 'full'.");
            checkedRegions.Add(region with { Tokens = StyleTokens.Checked(region.Tokens, $"{where}, region '{region.Id}'") });
        }
        foreach (var region in AllRegions)
        {
            var depth = 1;
            for (var parent = region.ParentId; parent is not null; parent = regions[parent].ParentId)
            {
                if (!regions.ContainsKey(parent)) throw new DomainException($"{where}: region '{region.Id}' is in '{parent}', which does not exist.");
                if (parent == region.Id || ++depth > MaxDepth)
                    throw new DomainException($"{where}: region '{region.Id}' is nested in itself or deeper than {MaxDepth} levels.");
            }
        }

        var parts = new HashSet<string>(StringComparer.Ordinal);
        var checkedPlacements = new List<Placement>();
        foreach (var placement in AllPlacements)
        {
            var part = PartRef.Parse(placement.Part);
            if (!parts.Add(part.ToString())) throw new DomainException($"{where}: '{part}' is placed twice.");
            if (!regions.ContainsKey(placement.RegionId)) throw new DomainException($"{where}: '{part}' is in region '{placement.RegionId}', which does not exist.");
            if (!StyleTokens.IsSpan(placement.Span)) throw new DomainException($"{where}: '{part}' spans 1 to {StyleTokens.MaxSpan} columns or 'full'.");
            if (placement.BlockTemplateId is not null && part.Kind != PartKind.Content)
                throw new DomainException($"{where}: only a content takes a block template, not '{part}'.");
            checkedPlacements.Add(placement with { Part = part.ToString(), Tokens = StyleTokens.Checked(placement.Tokens, $"{where}, '{part}'") });
        }
        return new ViewDesign(checkedRegions, checkedPlacements);
    }

    public Placement? PlacementOf(PartRef part)
    {
        var text = part.ToString();
        return AllPlacements.FirstOrDefault(p => p.Part == text);
    }

    /// <summary>Whether a region, or one it is in, is hidden.</summary>
    public bool RegionHidden(string regionId)
    {
        for (var region = AllRegions.FirstOrDefault(r => r.Id == regionId); region is not null;
             region = region.ParentId is null ? null : AllRegions.FirstOrDefault(r => r.Id == region.ParentId))
            if (region.Hidden) return true;
        return false;
    }

    /// <summary>A placement shows in this view: neither it nor a region it is in is hidden.</summary>
    public bool Shows(Placement placement) => !placement.Hidden && !RegionHidden(placement.RegionId);
}
