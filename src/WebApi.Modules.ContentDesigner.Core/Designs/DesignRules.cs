using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

[JsonConverter(typeof(JsonStringEnumConverter<ProblemSeverity>))]
public enum ProblemSeverity
{
    Warning,
    Error
}

/// <summary>
/// One finding of the checks, for the studio's Problems panel. <see cref="View"/> is null for the whole design;
/// <see cref="Path"/> is a JSON Pointer into the design (<c>/mobile/placements/3</c>) or the document.
/// An error blocks publication; a warning does not.
/// </summary>
public sealed record DesignProblem(ViewKind? View, ProblemSeverity Severity, string Code, string Path, string Message, string? NodeId = null);

/// <summary>A root value of the post's kind (a DynamicFields property of the root field version).</summary>
public sealed record KindValue(string Key, bool Required, string? SchemaProperty = null);

/// <summary>A content attached to the post: its id, node id, field, reading order and whether the post requires it.</summary>
public sealed record DesignContent(string Id, string NodeId, string FieldKey, int FieldVersion, int Order, bool Required,
    IReadOnlyDictionary<string, string>? Style = null);

/// <summary>A block template a placement names: its field and view.</summary>
public sealed record BlockTemplateRef(string FieldKey, ViewKind View);

/// <summary>How a field renders: a renderer of its own (else the fallback) and an AMP variant.</summary>
public sealed record RendererSupport(bool HasRenderer, bool HasAmp);

/// <summary>
/// What the checks need to know about the post, gathered by the caller (Content and DynamicFields, over HTTP).
/// <see cref="FieldStyles"/> holds the style tokens each content's field version allows (token → values); a field
/// missing from it is not checked for tokens.
/// </summary>
public sealed record DesignContext(
    string Kind,
    int KindVersion,
    IReadOnlyList<KindValue> KindValues,
    IReadOnlyList<DesignContent> Contents,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> FieldStyles,
    IReadOnlyDictionary<int, BlockTemplateRef> BlockTemplates,
    Func<string, RendererSupport> Renderers);

/// <summary>
/// The design checks (plan section 4.2 to 4.4), pure: placed parts exist; required values and contents are visible in
/// every view; tokens fit the field; version drift; web views never re-parent a part; visual against reading order and
/// heading levels; AMP renderers only. Content validation, JSON-LD essentials and the AMP page itself are checked by the
/// caller, which calls DynamicFields and renders the page.
/// </summary>
public static class DesignRules
{
    public static class Codes
    {
        public const string UnknownPart = "unknown-part";
        public const string RequiredHidden = "required-hidden";
        public const string Unplaced = "unplaced";
        public const string Token = "token";
        public const string KindDrift = "kind-drift";
        public const string BlockTemplate = "block-template";
        public const string Reparented = "reparented";
        public const string ReadingOrder = "reading-order";
        public const string HeadingLevel = "heading-level";
        public const string AmpRenderer = "amp-renderer";
        public const string Fallback = "fallback";
    }

    public static IReadOnlyList<DesignProblem> Check(DesignViews views, int designKindVersion, DesignContext context, IReadOnlyCollection<ViewKind>? only = null)
    {
        var problems = new List<DesignProblem>();
        if (designKindVersion != context.KindVersion)
            problems.Add(new(null, ProblemSeverity.Warning, Codes.KindDrift, "",
                $"The design was made on version {designKindVersion} of '{context.Kind}'; the post is on version {context.KindVersion}. Check the views."));

        var contents = context.Contents.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var values = context.KindValues.ToDictionary(v => v.Key, StringComparer.Ordinal);

        foreach (var view in only is { Count: > 0 } ? only : ViewKinds.All)
        {
            var own = views[view].InheritsFrom is null;
            var design = views.Resolve(view);
            var path = "/" + view.Key();
            if (own)
            {
                Parts(view, design, path, contents, values, context, problems);
                Visibility(view, design, contents, context, problems);
                Accessibility(view, design, path, contents, problems);
            }
            if (view == ViewKind.Amp) Amp(design, path, contents, context, problems);
            else if (own) Fallbacks(view, design, contents, context, problems);
        }
        if (only is not { Count: > 0 } || only.Any(v => v != ViewKind.Amp)) Reparenting(views, problems);
        return problems;
    }

    public static bool HasErrors(this IEnumerable<DesignProblem> problems) => problems.Any(p => p.Severity == ProblemSeverity.Error);

    private static void Parts(ViewKind view, ViewDesign design, string path, Dictionary<string, DesignContent> contents,
        Dictionary<string, KindValue> values, DesignContext context, List<DesignProblem> problems)
    {
        for (var i = 0; i < design.AllPlacements.Count; i++)
        {
            var placement = design.AllPlacements[i];
            var at = $"{path}/placements/{i}";
            var part = PartRef.Parse(placement.Part);
            switch (part.Kind)
            {
                case PartKind.Prop when !values.ContainsKey(part.Key):
                    problems.Add(new(view, ProblemSeverity.Error, Codes.UnknownPart, at, $"'{context.Kind}' has no value '{part.Key}'."));
                    break;
                case PartKind.Content when !contents.TryGetValue(part.Key, out _):
                    problems.Add(new(view, ProblemSeverity.Error, Codes.UnknownPart, at, $"Content {part.Key} is not attached to the post any more."));
                    break;
                case PartKind.Content:
                    var content = contents[part.Key];
                    if (context.FieldStyles.TryGetValue(content.FieldKey, out var styles))
                        foreach (var (token, value) in placement.Tokens ?? StyleTokens.Empty)
                            if (!styles.TryGetValue(token, out var allowed) || !allowed.Contains(value))
                                problems.Add(new(view, ProblemSeverity.Error, Codes.Token, $"{at}/tokens/{token}",
                                    styles.ContainsKey(token)
                                        ? $"'{content.FieldKey}' allows {token} {string.Join(", ", styles[token])}, not '{value}'."
                                        : $"'{content.FieldKey}' has no style '{token}'."));
                    if (placement.BlockTemplateId is { } templateId)
                    {
                        if (!context.BlockTemplates.TryGetValue(templateId, out var template))
                            problems.Add(new(view, ProblemSeverity.Error, Codes.BlockTemplate, $"{at}/blockTemplateId", $"Block template {templateId} does not exist."));
                        else if (template.FieldKey != content.FieldKey)
                            problems.Add(new(view, ProblemSeverity.Error, Codes.BlockTemplate, $"{at}/blockTemplateId",
                                $"Block template {templateId} is for '{template.FieldKey}', the content is a '{content.FieldKey}'."));
                        else if (template.View != view)
                            problems.Add(new(view, ProblemSeverity.Warning, Codes.BlockTemplate, $"{at}/blockTemplateId",
                                $"Block template {templateId} was made for the {template.View.Key()} view."));
                    }
                    break;
            }
        }
    }

    private static void Visibility(ViewKind view, ViewDesign design, Dictionary<string, DesignContent> contents, DesignContext context, List<DesignProblem> problems)
    {
        var path = "/" + view.Key();
        foreach (var value in context.KindValues.Where(v => v.Required))
        {
            var placement = design.PlacementOf(PartRef.Prop(value.Key));
            if (placement is null || !design.Shows(placement))
                problems.Add(new(view, ProblemSeverity.Error, Codes.RequiredHidden, path,
                    $"The required value '{value.Key}'{(value.SchemaProperty is { } p ? $" (schema.org {p})" : "")} is {(placement is null ? "not placed" : "hidden")} in the {view.Key()} view."));
        }

        var items = design.PlacementOf(PartRef.Items);
        foreach (var content in context.Contents)
        {
            var placement = design.PlacementOf(PartRef.Content(content.Id)) ?? items;
            if (placement is not null && design.Shows(placement)) continue;
            var how = placement is null ? "not placed (and the view has no items)" : "hidden";
            problems.Add(content.Required
                ? new(view, ProblemSeverity.Error, Codes.RequiredHidden, path, $"The required content {content.Id} ({content.FieldKey}) is {how} in the {view.Key()} view.")
                : new(view, ProblemSeverity.Warning, Codes.Unplaced, path, $"Content {content.Id} ({content.FieldKey}) is {how} in the {view.Key()} view."));
        }
    }

    /// <summary>The visible parts of a view in visual order: regions by order, depth first; items expanded.</summary>
    public static IReadOnlyList<PartRef> VisualOrder(ViewDesign design, IReadOnlyList<DesignContent> contents)
    {
        var explicitContents = design.AllPlacements.Select(p => PartRef.Parse(p.Part)).Where(p => p.Kind == PartKind.Content)
            .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var result = new List<PartRef>();

        void Walk(string? regionId)
        {
            var children = design.AllRegions.Where(r => r.ParentId == regionId && !r.Hidden).Select(r => (r.Order, 0, (object)r))
                .Concat(regionId is null ? [] : design.AllPlacements.Where(p => p.RegionId == regionId && !p.Hidden).Select(p => (p.Order, 1, (object)p)))
                .OrderBy(c => c.Item1).ThenBy(c => c.Item2);
            foreach (var (_, _, child) in children)
            {
                if (child is Region region) { Walk(region.Id); continue; }
                var part = PartRef.Parse(((Placement)child).Part);
                if (part.Kind == PartKind.Items)
                    result.AddRange(contents.OrderBy(c => c.Order).Where(c => !explicitContents.Contains(c.Id)).Select(c => PartRef.Content(c.Id)));
                else result.Add(part);
            }
        }

        Walk(null);
        return result;
    }

    private static void Accessibility(ViewKind view, ViewDesign design, string path, Dictionary<string, DesignContent> contents, List<DesignProblem> problems)
    {
        var visual = VisualOrder(design, [.. contents.Values]);
        var shown = visual.Where(p => p.Kind == PartKind.Content && contents.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        var reading = contents.Values.Where(c => shown.Contains(c.Id)).OrderBy(c => c.Order).Select(c => c.Id).ToList();
        if (!shown.SequenceEqual(reading))
            problems.Add(new(view, ProblemSeverity.Warning, Codes.ReadingOrder, path,
                $"In the {view.Key()} view the contents show in another order than the post reads; screen readers and AMP follow the post's order."));

        var last = 0;
        foreach (var part in visual)
        {
            var level = part switch
            {
                { Kind: PartKind.Prop, Key: "headline" } => 1,
                { Kind: PartKind.Content } when contents.TryGetValue(part.Key, out var c) && c.FieldKey == "block.heading" =>
                    Level(design.PlacementOf(part)?.Tokens, c.Style),
                _ => 0
            };
            if (level == 0) continue;
            if (last > 0 && level > last + 1)
                problems.Add(new(view, ProblemSeverity.Warning, Codes.HeadingLevel, path,
                    $"In the {view.Key()} view a heading of level {level} follows one of level {last}; levels should not skip."));
            last = level;
        }
    }

    private static int Level(IReadOnlyDictionary<string, string>? tokens, IReadOnlyDictionary<string, string>? style)
    {
        var text = tokens?.GetValueOrDefault("level") ?? style?.GetValueOrDefault("level") ?? "h2";
        return text is ['h', >= '1' and <= '6'] ? text[1] - '0' : 2;
    }

    private static IEnumerable<DesignContent> ShownContents(ViewDesign design, Dictionary<string, DesignContent> contents) =>
        VisualOrder(design, [.. contents.Values]).Where(p => p.Kind == PartKind.Content && contents.ContainsKey(p.Key)).Select(p => contents[p.Key]);

    private static void Amp(ViewDesign design, string path, Dictionary<string, DesignContent> contents, DesignContext context, List<DesignProblem> problems)
    {
        foreach (var content in ShownContents(design, contents).DistinctBy(c => c.FieldKey))
        {
            var support = context.Renderers(content.FieldKey);
            if (!support.HasAmp)
                problems.Add(new(ViewKind.Amp, ProblemSeverity.Error, Codes.AmpRenderer, path, $"'{content.FieldKey}' has no AMP renderer; hide it in the AMP view."));
            else if (!support.HasRenderer)
                problems.Add(new(ViewKind.Amp, ProblemSeverity.Warning, Codes.Fallback, path, $"'{content.FieldKey}' shows with the fallback renderer in AMP (its values as text)."));
        }
    }

    private static void Fallbacks(ViewKind view, ViewDesign design, Dictionary<string, DesignContent> contents, DesignContext context, List<DesignProblem> problems)
    {
        foreach (var content in ShownContents(design, contents).DistinctBy(c => c.FieldKey))
            if (!context.Renderers(content.FieldKey).HasRenderer)
                problems.Add(new(view, ProblemSeverity.Warning, Codes.Fallback, "/" + view.Key(), $"'{content.FieldKey}' shows with the fallback renderer (its values as text)."));
    }

    /// <summary>
    /// The web views merge into one page, so they share one tree: the same regions in the same parents, and every part
    /// in the same region. Only order, hidden, span and tokens differ per view.
    /// </summary>
    private static void Reparenting(DesignViews views, List<DesignProblem> problems)
    {
        var mobile = views.Resolve(ViewKind.Mobile);
        var regions = mobile.AllRegions.ToDictionary(r => r.Id, r => r.ParentId, StringComparer.Ordinal);
        var parts = mobile.AllPlacements.ToDictionary(p => p.Part, p => p.RegionId, StringComparer.Ordinal);
        foreach (var view in new[] { ViewKind.Tablet, ViewKind.Desktop })
        {
            if (views[view].InheritsFrom is not null && ReferenceEquals(views.Resolve(view), mobile)) continue;
            var design = views.Resolve(view);
            var other = design.AllRegions.ToDictionary(r => r.Id, r => r.ParentId, StringComparer.Ordinal);
            if (other.Count != regions.Count || other.Any(r => !regions.TryGetValue(r.Key, out var parent) || parent != r.Value))
                problems.Add(new(view, ProblemSeverity.Error, Codes.Reparented, "/" + view.Key() + "/regions",
                    $"The {view.Key()} view has other regions than the mobile view; web views share one tree (hide, order or span instead)."));
            foreach (var placement in design.AllPlacements)
                if (parts.TryGetValue(placement.Part, out var region) ? region != placement.RegionId : true)
                    problems.Add(new(view, ProblemSeverity.Error, Codes.Reparented, "/" + view.Key() + "/placements",
                        $"'{placement.Part}' is {(parts.ContainsKey(placement.Part) ? "in another region than" : "not placed in")} the mobile view; web views share one tree."));
            foreach (var part in parts.Keys.Where(p => design.PlacementOf(PartRef.Parse(p)) is null))
                problems.Add(new(view, ProblemSeverity.Error, Codes.Reparented, "/" + view.Key() + "/placements",
                    $"'{part}' is placed in the mobile view but not in the {view.Key()} view; hide it there instead."));
        }
    }
}
