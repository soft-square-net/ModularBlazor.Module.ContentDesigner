using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.Core;

/// <summary>The design checks of plan section 4.2 to 4.4, on the sample news post.</summary>
public class DesignRulesTests
{
    private static DesignContext Context(Func<string, RendererSupport>? renderers = null, int kindVersion = 2,
        IReadOnlyDictionary<int, BlockTemplateRef>? blockTemplates = null)
    {
        var post = Samples.Post(kindVersion);
        return new DesignContext("post.news", kindVersion,
            [.. Samples.Kind.Values.Select(v => new KindValue(v.Key, v.Required, v.SchemaProperty))],
            PostDocuments.Contents(post),
            post.Contents.Select(c => Samples.Block(c.FieldKey)).DistinctBy(f => f.Key)
                .ToDictionary(f => f.Key, f => (IReadOnlyDictionary<string, IReadOnlyList<string>>)f.Styles.ToDictionary(s => s.Key, s => s.Values)),
            blockTemplates ?? new Dictionary<int, BlockTemplateRef>(),
            renderers ?? (_ => new RendererSupport(true, true)));
    }

    private static IReadOnlyList<DesignProblem> Check(DesignViews views, DesignContext? context = null) =>
        DesignRules.Check(views.Checked(), 2, context ?? Context());

    [Fact]
    public void TheSampleDesignHasNoErrors() => Check(Samples.Design()).HasErrors().ShouldBeFalse();

    [Fact]
    public void ADesignOnAnOlderKindVersionIsFlagged() =>
        DesignRules.Check(Samples.Design(), 2, Context(kindVersion: 3)).ShouldContain(p => p.Code == DesignRules.Codes.KindDrift && p.Severity == ProblemSeverity.Warning);

    [Fact]
    public void APlacedPartExists()
    {
        var views = Samples.Design();
        var mobile = views.Mobile with { Placements = [.. views.Mobile.AllPlacements, new Placement("prop:subtitle", "head", 9), new Placement("content:gone", "body", 9)] };
        var problems = Check(views with { Mobile = mobile, Desktop = ViewDesign.Inherit(ViewKind.Mobile) });
        problems.Count(p => p.Code == DesignRules.Codes.UnknownPart).ShouldBe(2);
    }

    [Fact]
    public void ARequiredValueShowsInEveryView()
    {
        var views = Samples.Design();
        var desktop = views.Desktop with { Placements = [.. views.Desktop.AllPlacements.Select(p => p.Part == "prop:author" ? p with { Hidden = true } : p)] };
        var problems = Check(views with { Desktop = desktop });
        problems.ShouldContain(p => p.Code == DesignRules.Codes.RequiredHidden && p.View == ViewKind.Desktop && p.Message.Contains("'author'"));
        problems.ShouldNotContain(p => p.Code == DesignRules.Codes.RequiredHidden && p.View == ViewKind.Mobile);
    }

    [Fact]
    public void ARequiredContentShowsInEveryView()
    {
        var views = Samples.Design();
        // Hiding the items hides the required paragraph, which no placement names.
        var desktop = views.Desktop with { Placements = [.. views.Desktop.AllPlacements.Select(p => p.Part == "items" ? p with { Hidden = true } : p)] };
        var problems = Check(views with { Desktop = desktop });
        problems.ShouldContain(p => p.Code == DesignRules.Codes.RequiredHidden && p.Message.Contains(Samples.Paragraph));
        problems.ShouldContain(p => p.Code == DesignRules.Codes.Unplaced && p.Severity == ProblemSeverity.Warning && p.Message.Contains(Samples.Gallery));
    }

    [Fact]
    public void AHiddenRegionHidesWhatIsInIt()
    {
        var views = Samples.Design();
        var desktop = views.Desktop with { Regions = [.. views.Desktop.AllRegions.Select(r => r.Id == "head" ? r with { Hidden = true } : r)] };
        Check(views with { Desktop = desktop }).Count(p => p.Code == DesignRules.Codes.RequiredHidden && p.View == ViewKind.Desktop).ShouldBe(5);
    }

    [Fact]
    public void AContentsTokensAreItsFields()
    {
        var views = Samples.Design();
        var mobile = views.Mobile with { Placements = [.. views.Mobile.AllPlacements.Select(p => p.Part == "content:" + Samples.Image ? p with { Tokens = Samples.Tokens(("tone", "accent"), ("aspect", "9-16")) } : p)] };
        var problems = Check(views with { Mobile = mobile, Desktop = ViewDesign.Inherit(ViewKind.Mobile) });
        problems.Count(p => p.Code == DesignRules.Codes.Token).ShouldBe(2);
    }

    [Fact]
    public void ABlockTemplateIsForTheContentsField()
    {
        var views = Samples.Design();
        var mobile = views.Mobile with { Placements = [.. views.Mobile.AllPlacements.Select(p => p.Part == "content:" + Samples.Image ? p with { BlockTemplateId = 5 } : p)] };
        var context = Context(blockTemplates: new Dictionary<int, BlockTemplateRef> { [5] = new("block.video", ViewKind.Mobile) });
        Check(views with { Mobile = mobile, Desktop = ViewDesign.Inherit(ViewKind.Mobile) }, context)
            .ShouldContain(p => p.Code == DesignRules.Codes.BlockTemplate && p.Severity == ProblemSeverity.Error);
    }

    [Fact]
    public void WebViewsNeverReparent()
    {
        var views = Samples.Design();
        var desktop = views.Desktop with { Placements = [.. views.Desktop.AllPlacements.Select(p => p.Part == "prop:author" ? p with { RegionId = "body" } : p)] };
        Check(views with { Desktop = desktop }).ShouldContain(p => p.Code == DesignRules.Codes.Reparented && p.View == ViewKind.Desktop);
    }

    [Fact]
    public void AVisualOrderOtherThanTheReadingOrderIsAWarning()
    {
        // The sample shows the image (third in the post) before the heading and the paragraph.
        Check(Samples.Design()).ShouldContain(p => p.Code == DesignRules.Codes.ReadingOrder && p.View == ViewKind.Desktop && p.Severity == ProblemSeverity.Warning);
        // The starter places everything through items, in the post's order.
        Check(DesignViews.Starter).ShouldNotContain(p => p.Code == DesignRules.Codes.ReadingOrder);
    }

    [Fact]
    public void HeadingLevelsDoNotSkip()
    {
        var views = Samples.Design();
        var mobile = views.Mobile with
        {
            Placements = [.. views.Mobile.AllPlacements, new Placement("content:" + Samples.Heading, "body", -1, Tokens: Samples.Tokens(("level", "h4")))]
        };
        Check(views with { Mobile = mobile, Desktop = ViewDesign.Inherit(ViewKind.Mobile) })
            .ShouldContain(p => p.Code == DesignRules.Codes.HeadingLevel && p.Message.Contains("level 4 follows one of level 1"));
    }

    [Fact]
    public void TheAmpViewUsesAmpRenderersOnly()
    {
        var context = Context(key => key == "block.video" ? new RendererSupport(true, false) : new RendererSupport(true, true));
        var problems = DesignRules.Check(Samples.Design(), 2, context);
        problems.ShouldContain(p => p.Code == DesignRules.Codes.AmpRenderer && p.View == ViewKind.Amp && p.Message.Contains("block.video"));
        problems.ShouldNotContain(p => p.Code == DesignRules.Codes.AmpRenderer && p.View != ViewKind.Amp);
    }

    [Fact]
    public void AFieldWithoutARendererFallsBack() =>
        DesignRules.Check(Samples.Design(), 2, Context(key => key == "block.gallery" ? new RendererSupport(false, true) : new RendererSupport(true, true)))
            .ShouldContain(p => p.Code == DesignRules.Codes.Fallback && p.Severity == ProblemSeverity.Warning);

    [Fact]
    public void TheChecksCanBeLimitedToViews() =>
        DesignRules.Check(Samples.Design(), 2, Context(), [ViewKind.Mobile]).ShouldAllBe(p => p.View == null || p.View == ViewKind.Mobile);
}
