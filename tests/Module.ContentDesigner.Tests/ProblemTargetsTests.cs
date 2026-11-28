using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;
using RenderBlock = soft.square.net.ContentDesigner.Rendering.Model.RenderBlock;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;

namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>Where a problem points on the canvas: the marks in the error and warning colors, and the Problems panel's filter.</summary>
public class ProblemTargetsTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    private static DViews Views2()
    {
        var mobile = new DView(
            [new DRegion("body", "Section")],
            [new DPlacement("prop:headline", "body", 0), new DPlacement("content:c-1", "body", 1)]);
        return new DViews(mobile, DView.Inherit(Views.Mobile), DView.Inherit(Views.Tablet), mobile);
    }

    /// <summary>One content (c-1, node n-1) holding a child node n-2.</summary>
    private static RenderDocument Document() => new("article", "en", "Post", Empty,
        [new RenderBlock("c-1", "n-1", "block.image", 1, Empty, Empty, [new RenderBlock("n-2", "n-2", "block.caption", 1, Empty, Empty)])]);

    private static StudioProblem Problem(ProblemSeverity severity, string? nodeId = null, string path = "", View? view = null) =>
        new(new DesignProblem { Severity = severity, Code = "content", Path = path, Message = "m", NodeId = nodeId, View = view }, null, nodeId, "m");

    [Fact]
    public void A_problem_in_a_node_inside_a_content_marks_that_content()
    {
        ProblemTargets.Of(Problem(ProblemSeverity.Error, "n-2"), Views2(), Document())!.Id.ShouldBe("content:c-1");
    }

    [Fact]
    public void A_path_names_a_placement_or_a_region()
    {
        ProblemTargets.Of(Problem(ProblemSeverity.Error, path: "/mobile/placements/0"), Views2(), Document())!.Id.ShouldBe("prop:headline");
        ProblemTargets.Of(Problem(ProblemSeverity.Error, path: "/mobile/regions/0"), Views2(), Document())!.Id.ShouldBe("region:body");
        ProblemTargets.Of(Problem(ProblemSeverity.Error, path: "/mobile/placements/9"), Views2(), Document()).ShouldBeNull();
    }

    [Fact]
    public void An_error_outranks_a_warning_on_the_same_part()
    {
        var marks = ProblemTargets.Marks(
            [Problem(ProblemSeverity.Warning, "n-1"), Problem(ProblemSeverity.Error, "n-2"), Problem(ProblemSeverity.Warning, path: "/mobile/regions/0")],
            Views.Mobile, Views2(), Document());

        marks["content:c-1"].ShouldBe(ProblemSeverity.Error);
        marks["region:body"].ShouldBe(ProblemSeverity.Warning);
    }

    [Fact]
    public void A_problem_of_another_view_does_not_mark_this_one()
    {
        ProblemTargets.Marks([Problem(ProblemSeverity.Error, "n-1", view: new View { Value = ViewKind.Amp })], Views.Mobile, Views2(), Document()).ShouldBeEmpty();
    }

    [Fact]
    public void The_marks_use_the_theme_colors_and_escape_the_ids()
    {
        var css = ProblemTargets.Css(new Dictionary<string, ProblemSeverity>
        {
            ["content:a\"b"] = ProblemSeverity.Error,
            ["region:x"] = ProblemSeverity.Warning
        });

        css.ShouldContain("[data-block-id=\"content:a\\\"b\"]");
        css.ShouldContain("var(--mud-palette-error)");
        css.ShouldContain("var(--mud-palette-warning)");
        ProblemTargets.Css(new Dictionary<string, ProblemSeverity>()).ShouldBeEmpty();
    }
}
