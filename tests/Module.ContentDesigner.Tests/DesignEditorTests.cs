namespace soft.square.net.Module.ContentDesigner.Tests;

public class DesignEditorTests
{
    private const string Body = "body";
    private const string Heading = "content:h1";
    private const string Paragraph = "content:p1";

    /// <summary>Mobile has its own design (a section with a heading and a paragraph); Tablet and Desktop inherit it; AMP has its own copy.</summary>
    private static DViews Sample()
    {
        var mobile = new DView(
            [new DRegion(Body, "Section")],
            [new DPlacement("prop:headline", Body, 0), new DPlacement(Heading, Body, 1), new DPlacement(Paragraph, Body, 2)]);
        return new DViews(mobile, DView.Inherit(Views.Mobile), DView.Inherit(Views.Tablet), mobile);
    }

    [Fact]
    public void A_view_shows_the_end_of_its_inheritance()
    {
        var views = Sample();

        DesignEditor.SourceOf(views, Views.Desktop).ShouldBe(Views.Mobile);
        DesignEditor.Resolve(views, Views.Desktop).ShouldBeSameAs(views.Mobile);
    }

    [Fact]
    public void A_per_view_edit_detaches_only_that_view()
    {
        var views = DesignEditor.SetHidden(Sample(), Views.Tablet, Paragraph, true);

        views.Tablet.Inherits.ShouldBeFalse();
        DesignEditor.FindPlacement(views.Tablet, Paragraph)!.Hidden.ShouldBeTrue();
        DesignEditor.FindPlacement(views.Mobile, Paragraph)!.Hidden.ShouldBeFalse();
        views.Desktop.InheritsFrom.ShouldBe(Views.Tablet); // it now shows Tablet's own design
        DesignEditor.FindPlacement(DesignEditor.Resolve(views, Views.Desktop), Paragraph)!.Hidden.ShouldBeTrue();
    }

    [Fact]
    public void Inheriting_keeps_what_the_views_that_inherited_it_showed()
    {
        var views = DesignEditor.SetSpan(Sample(), Views.Tablet, Heading, "6"); // Tablet has its own; Desktop inherits Tablet

        var result = DesignEditor.Inherit(views, Views.Tablet, Views.Mobile);

        result.Tablet.InheritsFrom.ShouldBe(Views.Mobile);
        result.Desktop.Inherits.ShouldBeFalse();
        DesignEditor.FindPlacement(result.Desktop, Heading)!.Span.ShouldBe("6");
    }

    [Fact]
    public void Inheritance_loops_are_refused()
    {
        var views = Sample(); // Desktop → Tablet → Mobile

        Should.Throw<EditException>(() => DesignEditor.Inherit(views, Views.Mobile, Views.Desktop));
        Should.Throw<EditException>(() => DesignEditor.Inherit(views, Views.Amp, Views.Amp));
    }

    [Fact]
    public void Move_reorders_siblings_in_one_view()
    {
        var views = DesignEditor.Move(Sample(), Views.Mobile, Paragraph, -1);

        DesignEditor.Children(views.Mobile, Body).ShouldBe(["prop:headline", Paragraph, Heading]);
        DesignEditor.Children(views.Amp, Body).ShouldBe(["prop:headline", Heading, Paragraph]);
    }

    [Fact]
    public void Moving_past_the_end_changes_nothing()
    {
        var views = Sample();

        DesignEditor.Move(views, Views.Mobile, Paragraph, 1).ShouldBeSameAs(views);
    }

    [Fact]
    public void Tokens_must_be_known_values()
    {
        var views = DesignEditor.SetToken(Sample(), Views.Mobile, Heading, "size", "lg");

        DesignEditor.FindPlacement(views.Mobile, Heading)!.Tokens!["size"].ShouldBe("lg");
        Should.Throw<EditException>(() => DesignEditor.SetToken(views, Views.Mobile, Heading, "size", "huge"));
        DesignEditor.FindPlacement(DesignEditor.SetToken(views, Views.Mobile, Heading, "size", null).Mobile, Heading)!.Tokens!
            .ContainsKey("size").ShouldBeFalse();
    }

    [Fact]
    public void A_region_added_in_a_web_view_joins_every_web_view_with_its_own_design_but_not_amp()
    {
        var detached = DesignEditor.SetHidden(Sample(), Views.Desktop, Heading, true); // Desktop own, Tablet inherits Mobile

        var (views, id) = DesignEditor.AddRegion(detached, Views.Mobile, "Row", Body);

        id.ShouldBe("row-1");
        DesignEditor.FindRegion(views.Mobile, id)!.ParentId.ShouldBe(Body);
        DesignEditor.FindRegion(views.Desktop, id).ShouldNotBeNull();
        views.Tablet.Inherits.ShouldBeTrue();
        DesignEditor.FindRegion(views.Amp, id).ShouldBeNull();
    }

    [Fact]
    public void Regions_nest_at_most_four_deep()
    {
        var views = Sample();
        var parent = Body;
        for (var i = 0; i < 3; i++) (views, parent) = DesignEditor.AddRegion(views, Views.Mobile, "Column", parent);

        Should.Throw<EditException>(() => DesignEditor.AddRegion(views, Views.Mobile, "Column", parent));
    }

    [Fact]
    public void Only_an_empty_region_is_removed()
    {
        var (views, id) = DesignEditor.AddRegion(Sample(), Views.Mobile, "Row", Body);

        Should.Throw<EditException>(() => DesignEditor.RemoveRegion(views, Views.Mobile, Body));
        DesignEditor.FindRegion(DesignEditor.RemoveRegion(views, Views.Mobile, id).Mobile, id).ShouldBeNull();
    }

    [Fact]
    public void Place_moves_a_part_to_the_end_of_another_region()
    {
        var (views, row) = DesignEditor.AddRegion(Sample(), Views.Mobile, "Row", Body);

        views = DesignEditor.Place(views, Views.Mobile, Heading, row);

        DesignEditor.ParentOf(views.Mobile, Heading).ShouldBe(row);
        DesignEditor.Children(views.Mobile, row).ShouldBe([Heading]);
        Should.Throw<EditException>(() => DesignEditor.Place(views, Views.Mobile, "nonsense", row));
    }

    [Fact]
    public void Unplace_takes_a_part_out_of_the_web_views()
    {
        var views = DesignEditor.Unplace(Sample(), Views.Desktop, Paragraph);

        DesignEditor.FindPlacement(views.Mobile, Paragraph).ShouldBeNull();
        DesignEditor.FindPlacement(views.Amp, Paragraph).ShouldNotBeNull();
    }

    [Fact]
    public void CopyTo_copies_the_settings_of_one_view_to_another()
    {
        var views = DesignEditor.SetSpan(Sample(), Views.Mobile, Heading, "8");
        views = DesignEditor.SetHidden(views, Views.Mobile, Heading, true);

        var copied = DesignEditor.CopyTo(views, Views.Mobile, Views.Amp, Heading);

        var amp = DesignEditor.FindPlacement(copied.Amp, Heading)!;
        amp.Span.ShouldBe("8");
        amp.Hidden.ShouldBeTrue();
    }

    [Theory]
    [InlineData("items", true)]
    [InlineData("prop:headline", true)]
    [InlineData("content:abc", true)]
    [InlineData("prop:", false)]
    [InlineData("region:body", false)]
    public void IsPart_knows_the_three_kinds_of_parts(string text, bool expected) => DesignEditor.IsPart(text).ShouldBe(expected);

    [Fact]
    public void The_views_round_trip_through_the_api_types()
    {
        var views = DesignEditor.SetToken(Sample(), Views.Tablet, Heading, "size", "xl");

        var back = StudioJson.From(StudioJson.ToApi(views));

        back.Tablet.Inherits.ShouldBeFalse();
        back.Desktop.InheritsFrom.ShouldBe(Views.Tablet);
        DesignEditor.FindPlacement(back.Tablet, Heading)!.Tokens!["size"].ShouldBe("xl");
        back.Amp.AllPlacements.Count.ShouldBe(3);
    }
}
