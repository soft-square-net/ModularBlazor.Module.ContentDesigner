namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>Studio v2's drag and drop: the editor's positional edits, the palette keys and the drop planner.</summary>
public class DropPlannerTests
{
    private const string Head = "head";
    private const string Body = "body";
    private const string Aside = "aside";
    private const string Heading = "content:h1";
    private const string Paragraph = "content:p1";
    private const string Image = "content:i1";

    /// <summary>
    /// Mobile: section "head" (headline), section "body" (heading, paragraph, items), section "aside" (with a row "list");
    /// Tablet inherits Mobile; Desktop has its own copy; AMP its own copy.
    /// </summary>
    private static DViews Sample()
    {
        var mobile = new DView(
            [new DRegion(Head, "Section", Order: 0), new DRegion(Body, "Section", Order: 1), new DRegion(Aside, "Section", Order: 2),
             new DRegion("list", "Row", Aside, 0)],
            [new DPlacement("prop:headline", Head, 0), new DPlacement(Heading, Body, 0), new DPlacement(Paragraph, Body, 1), new DPlacement("items", Body, 2)]);
        return new DViews(mobile, DView.Inherit(Views.Mobile), mobile, mobile);
    }

    private static IReadOnlyList<string> Children(DViews views, string view, string? region) =>
        DesignEditor.Children(DesignEditor.Resolve(views, view), region);

    private static string? Field(string contentId) => contentId switch { "h1" => "block.heading", "p1" => "block.paragraph", "i1" => "block.image", _ => null };

    private static TemplateInfo? Template(int id) => id switch
    {
        7 => new TemplateInfo(7, "block.image", Views.Mobile),
        8 => new TemplateInfo(8, "block.heading", Views.Tablet),
        _ => null
    };

    private static (DViews Views, string? Select) Run(DViews views, string view, CanvasDrop drop) =>
        DropPlanner.Plan(views, view, drop, Field, Template).Edit!(views);

    // ───────────── the editor ─────────────

    [Fact]
    public void Moving_within_a_region_reorders_this_view_only()
    {
        var views = DesignEditor.MoveTo(Sample(), Views.Desktop, "items", Body, 0);

        Children(views, Views.Desktop, Body).ShouldBe(["items", Heading, Paragraph]);
        Children(views, Views.Mobile, Body).ShouldBe([Heading, Paragraph, "items"]);
    }

    [Fact]
    public void Moving_to_another_region_changes_the_one_web_tree_and_orders_this_view()
    {
        var views = DesignEditor.MoveTo(Sample(), Views.Mobile, Paragraph, Head, 0);

        Children(views, Views.Mobile, Head).ShouldBe([Paragraph, "prop:headline"]);
        Children(views, Views.Desktop, Head).ShouldBe(["prop:headline", Paragraph]); // the region changed; its own order did not
        views.Tablet.InheritsFrom.ShouldBe(Views.Mobile);
        Children(views, Views.Amp, Body).ShouldContain(Paragraph); // AMP has its own tree
    }

    [Fact]
    public void Moving_in_AMP_changes_AMP_alone()
    {
        var views = DesignEditor.MoveTo(Sample(), Views.Amp, Heading, Aside, 0);

        DesignEditor.ParentOf(views.Amp, Heading).ShouldBe(Aside);
        DesignEditor.ParentOf(views.Mobile, Heading).ShouldBe(Body);
    }

    [Fact]
    public void A_move_in_an_inheriting_view_detaches_it()
    {
        var views = DesignEditor.MoveTo(Sample(), Views.Tablet, Paragraph, Body, 0);

        views.Tablet.Inherits.ShouldBeFalse();
        Children(views, Views.Tablet, Body).ShouldBe([Paragraph, Heading, "items"]);
        Children(views, Views.Mobile, Body).ShouldBe([Heading, Paragraph, "items"]);
    }

    [Fact]
    public void Regions_move_to_the_top_and_into_other_regions()
    {
        var views = DesignEditor.MoveTo(Sample(), Views.Mobile, "region:list", null, 0);
        Children(views, Views.Mobile, null).ShouldBe(["region:list", "region:" + Head, "region:" + Body, "region:" + Aside]);

        views = DesignEditor.MoveTo(views, Views.Mobile, "region:" + Aside, Body, 1);
        Children(views, Views.Mobile, Body).ShouldBe([Heading, "region:" + Aside, Paragraph, "items"]);
        DesignEditor.FindRegion(views.Desktop, Aside)!.ParentId.ShouldBe(Body);
    }

    [Fact]
    public void A_region_does_not_go_inside_itself_or_too_deep()
    {
        Should.Throw<EditException>(() => DesignEditor.MoveTo(Sample(), Views.Mobile, "region:" + Aside, "list", 0)).Message.ShouldContain("inside itself");

        var deep = Sample();
        (deep, var a) = DesignEditor.AddRegion(deep, Views.Mobile, "Column", "list");   // aside > list > a
        (deep, var b) = DesignEditor.AddRegion(deep, Views.Mobile, "Row", a);           // aside > list > a > b (4)
        Should.Throw<EditException>(() => DesignEditor.MoveTo(deep, Views.Mobile, "region:" + Aside, Body, 0)).Message.ShouldContain("deep");
        b.ShouldNotBeNull();
    }

    [Fact]
    public void A_part_does_not_go_to_the_top()
    {
        Should.Throw<EditException>(() => DesignEditor.MoveTo(Sample(), Views.Mobile, Heading, null, 0));
    }

    [Fact]
    public void Placing_and_adding_at_a_position()
    {
        var views = DesignEditor.PlaceAt(Sample(), Views.Mobile, Image, Body, 1);
        Children(views, Views.Mobile, Body).ShouldBe([Heading, Image, Paragraph, "items"]);

        var (added, id) = DesignEditor.AddRegionAt(views, Views.Mobile, "Row", Body, 0);
        Children(added, Views.Mobile, Body)[0].ShouldBe("region:" + id);
        DesignEditor.FindRegion(added.Desktop, id).ShouldNotBeNull(); // structure: every web view with a design
    }

    [Fact]
    public void A_block_template_goes_on_a_content_and_detaching_keeps_its_look()
    {
        var views = DesignEditor.SetToken(Sample(), Views.Mobile, Heading, "align", "center");
        views = DesignEditor.SetBlockTemplate(views, Views.Mobile, Heading, 4);
        DesignEditor.FindPlacement(views.Mobile, Heading)!.BlockTemplateId.ShouldBe(4);
        Should.Throw<EditException>(() => DesignEditor.SetBlockTemplate(views, Views.Mobile, "prop:headline", 4));

        var detached = DesignEditor.DetachBlockTemplate(views, Views.Mobile, Heading,
            new Dictionary<string, string> { ["align"] = "end", ["size"] = "lg" });
        var placement = DesignEditor.FindPlacement(detached.Mobile, Heading)!;
        placement.BlockTemplateId.ShouldBeNull();
        placement.Tokens!["size"].ShouldBe("lg");
        placement.Tokens["align"].ShouldBe("center"); // the placement's own token wins
    }

    // ───────────── palette keys ─────────────

    [Theory]
    [InlineData("part:content:x", PaletteKind.Part, "content:x")]
    [InlineData("part:items", PaletteKind.Part, "items")]
    [InlineData("new-region:Row", PaletteKind.Region, "Row")]
    [InlineData("template:12", PaletteKind.Template, "12")]
    [InlineData("library:abc", PaletteKind.Library, "abc")]
    public void Palette_keys_round_trip(string key, PaletteKind kind, string value)
    {
        Palette.Parse(key).ShouldBe(new PaletteItem(kind, value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("part:nothing")]
    [InlineData("new-region:Grid")]
    [InlineData("template:x")]
    [InlineData("library:")]
    [InlineData("content:x")]
    public void Other_keys_are_not_palette_items(string? key) => Palette.Parse(key).ShouldBeNull();

    [Fact]
    public void Palette_keys_are_built_as_they_are_read()
    {
        Palette.Parse(Palette.Part(Image))!.Value.ShouldBe(Image);
        Palette.Parse(Palette.Template(3))!.TemplateId.ShouldBe(3);
        Palette.Attributes("x")["data-block-new"].ShouldBe("x");
    }

    // ───────────── the planner ─────────────

    [Fact]
    public void A_block_dropped_before_another_lands_before_it()
    {
        var (views, select) = Run(Sample(), Views.Mobile, new CanvasDrop("move", Paragraph, null, Heading, "before"));
        Children(views, Views.Mobile, Body).ShouldBe([Paragraph, Heading, "items"]);
        select.ShouldBe(Paragraph);
    }

    [Fact]
    public void A_block_dropped_after_one_in_another_region_moves_there()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("move", Heading, null, "prop:headline", "after"));
        Children(views, Views.Mobile, Head).ShouldBe(["prop:headline", Heading]);
    }

    [Fact]
    public void A_block_dropped_inside_a_region_goes_to_its_end()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("move", Heading, null, "region:list", "inside"));
        Children(views, Views.Mobile, "list").ShouldBe([Heading]);
    }

    [Fact]
    public void A_section_dropped_before_the_first_goes_first()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("move", "region:" + Aside, null, "region:" + Head, "before"));
        Children(views, Views.Mobile, null).ShouldBe(["region:" + Aside, "region:" + Head, "region:" + Body]);
    }

    [Fact]
    public void A_region_dropped_on_the_page_goes_to_the_end_of_the_top()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("move", "region:list", null, DropPlanner.Page, "inside"));
        Children(views, Views.Mobile, null).Last().ShouldBe("region:list");
    }

    [Fact]
    public void A_block_shown_through_items_is_placed_when_dragged()
    {
        var (views, select) = Run(Sample(), Views.Mobile, new CanvasDrop("move", Image, null, Heading, "before"));
        Children(views, Views.Mobile, Body).ShouldBe([Image, Heading, Paragraph, "items"]);
        select.ShouldBe(Image);
    }

    [Fact]
    public void A_drop_next_to_a_block_shown_through_items_lands_next_to_items()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("move", Heading, null, Image, "after"));
        Children(views, Views.Mobile, Body).ShouldBe([Paragraph, "items", Heading]);
    }

    [Fact]
    public void Parts_and_regions_come_from_the_palette()
    {
        var (placed, _) = Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Part(Image), Paragraph, "before"));
        Children(placed, Views.Mobile, Body).ShouldBe([Heading, Image, Paragraph, "items"]);

        var (added, select) = Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Region("Row"), "region:" + Body, "inside"));
        select.ShouldNotBeNull().ShouldStartWith("region:row-");
        Children(added, Views.Mobile, Body).Last().ShouldBe(select);
    }

    [Fact]
    public void A_part_is_not_dropped_at_the_top()
    {
        Should.Throw<EditException>(() => Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Part(Image), DropPlanner.Page, "inside")));
    }

    [Fact]
    public void A_template_goes_on_a_block_of_its_field_in_its_view()
    {
        var (views, _) = Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Template(7), Image, "inside"));
        DesignEditor.FindPlacement(views.Mobile, Image)!.BlockTemplateId.ShouldBe(7); // placed where items is, then styled
        Children(views, Views.Mobile, Body).ShouldBe([Heading, Paragraph, Image, "items"]);

        Should.Throw<EditException>(() => Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Template(7), Heading, "inside")))
            .Message.ShouldContain("image block");
        Should.Throw<EditException>(() => Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Template(8), Heading, "inside")))
            .Message.ShouldContain("Tablet");
        Should.Throw<EditException>(() => Run(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Template(9), Heading, "inside")));
    }

    [Fact]
    public void A_library_content_is_attached_then_placed_where_it_was_dropped()
    {
        var plan = DropPlanner.Plan(Sample(), Views.Mobile, new CanvasDrop("new", null, Palette.Library("abc"), Paragraph, "after"), Field, Template);
        plan.Edit.ShouldBeNull();
        plan.Attach.ShouldBe(new LibraryDrop("abc", Body, 2));
    }

    [Fact]
    public void Unknown_drops_are_refused()
    {
        Should.Throw<EditException>(() => DropPlanner.Plan(Sample(), Views.Mobile, new CanvasDrop("new", null, "bogus", Heading, "before"), Field, Template));
        Should.Throw<EditException>(() => DropPlanner.Plan(Sample(), Views.Mobile, new CanvasDrop("move", DropPlanner.Page, null, Heading, "before"), Field, Template));
        Should.Throw<EditException>(() => Run(Sample(), Views.Mobile, new CanvasDrop("move", Heading, null, "prop:nowhere", "before")));
    }
}
