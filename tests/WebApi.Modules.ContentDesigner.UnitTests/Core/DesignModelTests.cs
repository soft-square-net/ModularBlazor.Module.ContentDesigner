using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.Core;

/// <summary>The structural rules every design keeps (a broken one throws), and the aggregates' own rules.</summary>
public class DesignModelTests
{
    private static readonly DateTimeOffset Now = TestClock.Start;

    [Theory]
    [InlineData("prop:headline", PartKind.Prop, "headline")]
    [InlineData("content:abc", PartKind.Content, "abc")]
    [InlineData("items", PartKind.Items, "")]
    public void PartsParseAndPrint(string text, PartKind kind, string key)
    {
        PartRef.TryParse(text, out var part).ShouldBeTrue();
        part.ShouldBe(new PartRef(kind, key));
        part.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("headline")]
    [InlineData("prop:")]
    [InlineData("block:x")]
    [InlineData("prop: headline")]
    public void BadPartsDoNotParse(string text) => PartRef.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void TheSampleDesignIsWellFormed() => Should.NotThrow(() => Samples.Design().Checked());

    [Fact]
    public void ARegionIdIsUsedOnce() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Section), new("a", RegionKind.Row)], []).Checked("v"))
            .Message.ShouldContain("used twice");

    [Fact]
    public void ARegionsParentExists() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Row, "missing")], []).Checked("v"))
            .Message.ShouldContain("does not exist");

    [Fact]
    public void RegionsDoNotNestInALoop() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Row, "b"), new("b", RegionKind.Row, "a")], []).Checked("v"));

    [Fact]
    public void APartIsPlacedOnce() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Section)], [new("items", "a"), new("items", "a", 1)]).Checked("v"))
            .Message.ShouldContain("placed twice");

    [Theory]
    [InlineData("width", "huge")]
    [InlineData("colour", "red")]
    public void TokensAreTheEditorialPacks(string token, string value) =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Section)], [new("items", "a", Tokens: Samples.Tokens((token, value)))]).Checked("v"));

    [Fact]
    public void OnlyAContentTakesABlockTemplate() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Section)], [new("prop:headline", "a", BlockTemplateId: 3)]).Checked("v"));

    [Fact]
    public void AnInheritingViewHasNothingOfItsOwn() =>
        Should.Throw<DomainException>(() => new ViewDesign([new("a", RegionKind.Section)], [], ViewKind.Mobile).Checked("v"));

    [Fact]
    public void InheritanceNeverLoops()
    {
        var views = DesignViews.Starter with { Mobile = ViewDesign.Inherit(ViewKind.Tablet) };
        Should.Throw<DomainException>(() => views.Checked()).Message.ShouldContain("loop");
    }

    [Fact]
    public void AnInheritingViewShowsTheOneItInherits()
    {
        var views = Samples.Design();
        views.Resolve(ViewKind.Amp).ShouldBeSameAs(views.Mobile);
        views.Resolve(ViewKind.Desktop).ShouldBeSameAs(views.Desktop);
    }

    [Fact]
    public void ADesignIsMadeForAPostAndCountsItsChanges()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, DesignViews.Starter, null, "u1", Now);
        design.Arrange(ViewKind.Desktop, Samples.Design().Desktop, "u2", Now);
        design.Version.ShouldBe(1);
        design.UpdatedBy.ShouldBe("u2");
        design.Draft.Desktop.ShouldMatch(Samples.Design().Desktop);
    }

    [Fact]
    public void ATemplateOfAnotherKindDoesNotApply()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, DesignViews.Starter, null, "u1", Now);
        Should.Throw<DomainException>(() => design.ApplyTemplate(1, "post.blog", Samples.Design(), null, "u1", Now));
    }

    [Fact]
    public void ADesignFollowsItsPostsKind()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 1, DesignViews.Starter, null, "u1", Now);
        var version = design.Version;

        design.Follow("Article"); // an old enum name is read as its key
        design.Kind.ShouldBe("post.article");
        design.Version.ShouldBe(version); // saved with the change that follows
        design.ApplyTemplate(2, "post.article", Samples.Design(), null, "u1", Now);

        design.Follow("");
        design.Kind.ShouldBe("post.article");
    }

    [Fact]
    public void ApplyingSomeViewsKeepsTheOthers()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, DesignViews.Starter, null, "u1", Now);
        design.ApplyTemplate(4, "post.news", Samples.Design(), [ViewKind.Desktop], "u1", Now);
        design.Draft.Desktop.ShouldMatch(Samples.Design().Desktop);
        design.Draft.Mobile.ShouldMatch(DesignViews.Starter.Mobile);
        design.BasedOnTemplateId.ShouldBe(4);
    }

    [Fact]
    public void PublishingFreezesTheDraftAndPinsTheRevisions()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "u1", Now);
        design.Publish(new Dictionary<string, int> { [Samples.Image] = 3 }, new Dictionary<string, string> { [Samples.Image] = "n-image" }, "desk", Now);
        design.Arrange(ViewKind.Mobile, ViewDesign.Starter, "u1", Now);

        design.Published!.DesignVersion.ShouldBe(1);
        design.Published.Views.Mobile.ShouldMatch(Samples.Design().Mobile);
        design.Published.Pins[Samples.Image].ShouldBe(3);
        design.Draft.Mobile.ShouldMatch(ViewDesign.Starter);
    }

    [Fact]
    public void ABlockTemplatesContentIsANodeOfItsField()
    {
        Should.Throw<DomainException>(() => BlockTemplate.Create("Hero", "block.image", ViewKind.Mobile, null, null, null, """{ "field": "block.video" }""", "u1", Now));
        Should.NotThrow(() => BlockTemplate.Create("Hero", "block.image", ViewKind.Mobile, ["caption", "credit"], ["credit"],
            Samples.Tokens(("width", "full")), """{ "field": "block.image", "fieldVersion": 1, "content": {} }""", "u1", Now));
    }

    [Fact]
    public void ATemplateHasAName() =>
        Should.Throw<DomainException>(() => DocumentTemplate.Create(" ", null, "post.news", 2, DesignViews.Starter, "u1", Now));
}
