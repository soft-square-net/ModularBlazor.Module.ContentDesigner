using soft.square.net.WebApi.Modules.ContentDesigner.Client;
using RenderBlock = soft.square.net.ContentDesigner.Rendering.Model.RenderBlock;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>What a problem is about on the canvas: the view it shows in (null: any) and the selection id it marks.</summary>
public sealed record ProblemTarget(string? View, string Id);

/// <summary>
/// Where each problem points on the canvas, so the canvas can mark the part in the error or warning color and the
/// Problems panel can keep to the selected part. A problem in a content's node (its own, or a node inside it) is about
/// that content (<c>content:{id}</c>); otherwise its path names a placement or a region (<c>/{view}/placements/{n}</c>,
/// <c>/{view}/regions/{n}</c>).
/// </summary>
public static class ProblemTargets
{
    public static ProblemTarget? Of(StudioProblem problem, DViews? views, RenderDocument? document)
    {
        if (ContentOf(document, problem.NodeId) is { } content) return new(problem.View, "content:" + content.Id);
        if (views is null) return null;
        var segments = (problem.Path ?? "").Trim('/').Split('/');
        var view = problem.View
            ?? Views.All.FirstOrDefault(v => string.Equals(v, segments.ElementAtOrDefault(0), StringComparison.OrdinalIgnoreCase));
        if (view is null || segments.Length < 3 || !int.TryParse(segments[2], out var index) || index < 0) return null;
        var design = DesignEditor.Resolve(views, view);
        return segments[1] switch
        {
            "placements" when index < design.AllPlacements.Count => new(view, design.AllPlacements[index].Part),
            "regions" when index < design.AllRegions.Count => new(view, DesignEditor.RegionPrefix + design.AllRegions[index].Id),
            _ => null
        };
    }

    /// <summary>
    /// The marks for a view: each part with a problem there, and the worst severity it has (an error over a warning).
    /// A problem of no view marks its part in every view.
    /// </summary>
    public static IReadOnlyDictionary<string, ProblemSeverity> Marks(IEnumerable<StudioProblem> problems, string view, DViews? views,
        RenderDocument? document)
    {
        var marks = new Dictionary<string, ProblemSeverity>(StringComparer.Ordinal);
        foreach (var problem in problems)
        {
            if (Of(problem, views, document) is not { } target) continue;
            if (target.View is { } shownIn && !string.Equals(shownIn, view, StringComparison.OrdinalIgnoreCase)) continue;
            marks[target.Id] = marks.TryGetValue(target.Id, out var had) && had == ProblemSeverity.Error ? had : problem.Severity;
        }
        return marks;
    }

    /// <summary>Whether a problem is about the selected part (in any view).</summary>
    public static bool IsAbout(StudioProblem problem, string selected, DViews? views, RenderDocument? document) =>
        Of(problem, views, document) is { } target && target.Id == selected;

    /// <summary>The post's top-level content whose node tree holds a node; null when none does.</summary>
    public static RenderBlock? ContentOf(RenderDocument? document, string? nodeId)
    {
        if (document is null || nodeId is null) return null;
        static bool Holds(RenderBlock block, string id) => block.NodeId == id || (block.Children ?? []).Any(c => Holds(c, id));
        return document.Contents.FirstOrDefault(c => Holds(c, nodeId));
    }

    /// <summary>
    /// A stylesheet that outlines each marked part on the canvas (by its <c>data-block-id</c>), errors in the error color
    /// and warnings in the warning color. Empty when nothing is marked.
    /// </summary>
    public static string Css(IReadOnlyDictionary<string, ProblemSeverity> marks)
    {
        if (marks.Count == 0) return "";
        var css = new System.Text.StringBuilder();
        foreach (var group in marks.GroupBy(m => m.Value))
        {
            var color = group.Key == ProblemSeverity.Error ? "var(--mud-palette-error)" : "var(--mud-palette-warning)";
            css.Append(string.Join(",\n", group.Select(m => $".cd-canvas [data-block-id=\"{Escape(m.Key)}\"]")));
            css.Append($" {{ outline: 2px dashed {color}; outline-offset: -2px; }}\n");
        }
        return css.ToString();
    }

    /// <summary>A CSS string's text: quotes, backslashes and line ends escaped; '&lt;' too, so it cannot end the style element.</summary>
    internal static string Escape(string value) =>
        new System.Text.StringBuilder(value.Length)
            .AppendJoin("", value.Select(c => c switch
            {
                '"' or '\\' => "\\" + c,
                '<' => "\\3c ",
                '\n' or '\r' or '\f' => $"\\{(int)c:x} ",
                _ => c.ToString()
            }))
            .ToString();
}
