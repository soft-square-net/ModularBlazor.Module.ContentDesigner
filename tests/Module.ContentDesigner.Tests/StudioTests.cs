using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;
using PostDesignDto = soft.square.net.WebApi.Modules.ContentDesigner.Client.PostDesignDto;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;

namespace soft.square.net.Module.ContentDesigner.Tests;

public class RenderMappingTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RenderDocument Document() => new("post.news", "en", "A post", Json("""{ "headline": "A post" }"""),
    [
        new RenderBlock("h1", "n-h1", "block.heading", 1, Json("""{ "text": "Hello" }"""), Json("{}")),
        new RenderBlock("p1", "n-p1", "block.paragraph", 1, Json("""{ "text": "World" }"""), Json("{}"))
    ]);

    private static DViews Views2()
    {
        var web = new DView(
            [new DRegion("body", "Section"), new DRegion("empty", "Row", "body", 9)],
            [new DPlacement("prop:headline", "body", 0), new DPlacement("content:h1", "body", 1), new DPlacement("content:p1", "body", 2)]);
        return new DViews(web, DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile));
    }

    private static IEnumerable<PageNode> All(IEnumerable<PageNode> nodes) =>
        nodes.SelectMany(n => n is RegionNode r ? new[] { n }.Concat(All(r.Children)) : [n]);

    [Fact]
    public void Every_node_gets_the_canvas_attributes()
    {
        var page = RenderMapping.Page(Document(), RenderMapping.Design(Views2()), Views.Desktop);

        var nodes = All(page.Children).ToList();
        var body = nodes.OfType<RegionNode>().Single(r => r.Id == "body");
        body.Attributes["data-block-id"].ShouldBe("region:body");
        body.Attributes["data-container"].ShouldBe("true");
        nodes.OfType<RegionNode>().Single(r => r.Id == "empty").Attributes["data-empty"].ShouldBe("true");
        nodes.OfType<PropNode>().Single().Attributes["data-block-id"].ShouldBe("prop:headline");
        var heading = nodes.OfType<BlockNode>().Single(b => b.Block.Id == "h1");
        heading.Attributes["data-block-id"].ShouldBe("content:h1");
        heading.Attributes["data-block-type"].ShouldBe("block.heading");
        heading.Attributes["data-block-label"].ShouldBe("Heading");
    }

    [Fact]
    public void The_amp_view_is_its_own_arrangement()
    {
        var views = DesignEditor.Unplace(DesignEditor.Detach(Views2(), Views.Amp), Views.Amp, "content:p1");

        var amp = All(RenderMapping.Page(Document(), RenderMapping.Design(views), Views.Amp).Children).OfType<BlockNode>().ToList();
        var web = All(RenderMapping.Page(Document(), RenderMapping.Design(views), Views.Mobile).Children).OfType<BlockNode>().ToList();

        amp.Select(b => b.Block.Id).ShouldBe(["h1"]);
        web.Select(b => b.Block.Id).ShouldBe(["h1", "p1"]);
    }
}

public class StudioSessionTests
{
    private static StudioSession Open(DViews views)
    {
        var session = new StudioSession(7, () => throw new InvalidOperationException("No API in tests."), e => e.Message, (_, _) => ValueTask.CompletedTask);
        session.TakeDesign(new PostDesignDto { PostId = 7, Version = 1, Draft = StudioJson.ToApi(views) }, clearHistory: true);
        return session;
    }

    private static DViews Sample() => new(
        new DView([new DRegion("body", "Section")], [new DPlacement("content:a", "body", 0), new DPlacement("content:b", "body", 1)]),
        DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile));

    [Fact]
    public void Edits_undo_and_redo()
    {
        var session = Open(Sample());
        session.Dirty.ShouldBeFalse();

        session.Edit(v => DesignEditor.SetHidden(v, Views.Mobile, "content:a", true)).ShouldBeTrue();
        session.Dirty.ShouldBeTrue();
        session.CanUndo.ShouldBeTrue();

        session.Undo();
        session.Dirty.ShouldBeFalse();
        DesignEditor.FindPlacement(session.Views!.Mobile, "content:a")!.Hidden.ShouldBeFalse();

        session.Redo();
        DesignEditor.FindPlacement(session.Views!.Mobile, "content:a")!.Hidden.ShouldBeTrue();
    }

    [Fact]
    public void A_refused_edit_changes_nothing_and_says_why()
    {
        string? said = null;
        var session = new StudioSession(7, () => throw new InvalidOperationException(), e => e.Message, (message, _) => { said = message; return ValueTask.CompletedTask; });
        session.TakeDesign(new PostDesignDto { PostId = 7, Version = 1, Draft = StudioJson.ToApi(Sample()) }, clearHistory: true);

        session.Edit(v => DesignEditor.RemoveRegion(v, Views.Mobile, "body")).ShouldBeFalse();

        said.ShouldNotBeNull();
        session.Dirty.ShouldBeFalse();
        session.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Revert_goes_back_to_the_saved_design_and_can_be_undone()
    {
        var session = Open(Sample());
        session.Edit(v => DesignEditor.Move(v, Views.Mobile, "content:b", -1));

        session.Revert();

        session.Dirty.ShouldBeFalse();
        session.Undo();
        DesignEditor.Children(session.Views!.Mobile, "body").ShouldBe(["content:b", "content:a"]);
    }

    [Fact]
    public void The_current_view_follows_inheritance_and_a_selection_that_is_gone_is_cleared()
    {
        var session = Open(Sample());
        session.SelectView(Views.Tablet);
        session.InheritsFrom.ShouldBe(Views.Mobile);
        session.Current.ShouldBeSameAs(session.Views!.Mobile);

        session.Select("content:a");
        session.TakeDesign(new PostDesignDto { PostId = 7, Version = 2, Draft = StudioJson.ToApi(DesignEditor.Unplace(Sample(), Views.Mobile, "content:a")) }, clearHistory: false);

        session.Selected.ShouldBeNull();
        session.CanUndo.ShouldBeTrue();
    }
}

public class StudioReportTests
{
    /// <summary>Answers every call with a report of one warning, like POST .../design/check.</summary>
    private sealed class CheckHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "valid": true, "errors": 0, "warnings": 1, "problems": [] }""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static (StudioSession Session, CheckHandler Handler) Open()
    {
        var handler = new CheckHandler();
        var client = new WebApi.Modules.ContentDesigner.Client.ContentDesignerClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var session = new StudioSession(7, () => client, e => e.Message, (_, _) => ValueTask.CompletedTask);
        var views = new DViews(
            new DView([new DRegion("body", "Section")], [new DPlacement("content:a", "body", 0)]),
            DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile));
        session.TakeDesign(new WebApi.Modules.ContentDesigner.Client.PostDesignDto { PostId = 7, Version = 1, Draft = StudioJson.ToApi(views) }, clearHistory: true);
        return (session, handler);
    }

    [Fact]
    public async Task An_edit_after_a_check_marks_the_report_out_of_date()
    {
        var (session, handler) = Open();
        session.ReportStale.ShouldBeFalse(); // nothing checked yet

        (await session.CheckAsync()).ShouldBeTrue();
        handler.Calls.ShouldBe(1);
        session.Report.ShouldNotBeNull().Warnings.ShouldBe(1);
        session.ReportStale.ShouldBeFalse();

        session.Edit(v => DesignEditor.SetHidden(v, Views.Mobile, "content:a", true)).ShouldBeTrue();
        session.ReportStale.ShouldBeTrue();

        session.Undo(); // back to the views that were checked
        session.ReportStale.ShouldBeFalse();

        session.Redo();
        (await session.CheckAsync()).ShouldBeTrue();
        session.ReportStale.ShouldBeFalse();
    }
}

public class StudioIconsTests
{
    [Fact]
    public void A_lucide_name_becomes_svg_markup_for_the_dock()
    {
        var svg = StudioIcons.Svg("layers");
        svg.ShouldStartWith("<svg ");
        svg.ShouldContain("lucide-layers");
        svg.ShouldContain("<path ");
    }

    [Theory]
    [InlineData("layout-template")]
    [InlineData("code")]
    [InlineData("layers")]
    [InlineData("list-tree")]
    [InlineData("sliders-horizontal")]
    [InlineData("shapes")]
    [InlineData("folder-tree")]
    [InlineData("braces")]
    [InlineData("circle-alert")]
    [InlineData("activity")]
    public void Every_studio_panel_icon_is_carried(string name) => StudioIcons.Svg(name).ShouldNotBeEmpty(); // StudioPanels.cs

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-icon")]
    public void An_unknown_icon_draws_nothing(string? name) => StudioIcons.Svg(name).ShouldBe("");
}
