using Ardalis.Specification;
using Mediator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Amp;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Templates;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>Studio v2 on the server: the library and attach proxies, saving a block from unsaved views, AMP issues per part.</summary>
public class StudioV2HandlersTests
{
    private const string Shared = "66666666-6666-6666-6666-666666666666";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IReadRepository<PostDesign> _designs = Substitute.For<IReadRepository<PostDesign>>();
    private readonly IContentApi _content = Substitute.For<IContentApi>();

    public StudioV2HandlersTests() => _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Samples.Post());

    private PostDesign Existing()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "editor-1", TestClock.Start);
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);
        return design;
    }

    private static LibraryContent Row(string id, bool archived = false) =>
        new(id, "n-" + id[..4], "block.quote", 1, "accepted", 2, 2, "sports", null, TestClock.Start, archived);

    // ───────────── the library ─────────────

    [Fact]
    public async Task TheLibraryAsksContentForOneMoreRowToKnowWhetherMoreFollow()
    {
        _content.ListContentsAsync(Arg.Any<LibraryFilter>(), Arg.Any<CancellationToken>())
            .Returns([Row(Shared), Row(Samples.Heading, archived: true), Row(Samples.Image)]);

        var result = await new ListLibraryHandler(_content).Handle(new ListLibraryQuery("block.quote", "accepted", null, 0, 2), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(i => i.ContentId).ShouldBe([Shared, Samples.Image]); // the archived one is left out
        result.Value.More.ShouldBeTrue();
        await _content.Received().ListContentsAsync(new LibraryFilter("block.quote", "accepted", null, 0, 3), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task ALibraryPageOutOfRangeIsInvalid(int skip, int take)
    {
        var result = await new ListLibraryHandler(_content).Handle(new ListLibraryQuery(Skip: skip, Take: take), Ct);
        result.Status.ShouldBe(ResultStatus.Invalid);
    }

    [Fact]
    public async Task ContentBeingDownMakesTheLibraryUnavailable()
    {
        _content.ListContentsAsync(Arg.Any<LibraryFilter>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<LibraryContent>>(_ => throw new ExternalApiException("Content", null, "refused"));
        var result = await new ListLibraryHandler(_content).Handle(new ListLibraryQuery(), Ct);
        result.Status.ShouldBe(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task ALibraryItemComesWithItsBlockForThePreview()
    {
        var node = JsonDocument.Parse("""
            { "nodeId": "n-shared", "postKind": "News", "schemaId": "block.quote", "schemaVersion": 1,
              "content": { "text": "Never again." }, "style": { "align": "center" }, "children": [] }
            """).RootElement.Clone();
        _content.GetContentAsync(Shared, Arg.Any<CancellationToken>()).Returns(new LibraryContentDetail(Row(Shared), node));

        var result = await new GetLibraryItemHandler(_content).Handle(new GetLibraryItemQuery(Shared), Ct);

        var block = result.Value.Block;
        block.Id.ShouldBe(Shared);
        block.NodeId.ShouldBe("n-shared");
        block.FieldKey.ShouldBe("block.quote");
        block.Content.GetProperty("text").GetString().ShouldBe("Never again.");
    }

    [Fact]
    public async Task AnUnknownLibraryItemIsNotFound()
    {
        var result = await new GetLibraryItemHandler(_content).Handle(new GetLibraryItemQuery(Shared), Ct);
        result.Status.ShouldBe(ResultStatus.NotFound);
    }

    // ───────────── attach ─────────────

    [Fact]
    public async Task AttachingAContentCallsContentAtThePostsVersionThenReturnsTheWorkspace()
    {
        var workspace = Substitute.For<ISender>();
        var dto = new WorkspaceDto(new WorkspacePost(7, "pub-7", "T", "post.news", 2, 4, null), [], null!, [], new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), null);
        workspace.Send(Arg.Any<GetWorkspaceQuery>(), Arg.Any<CancellationToken>()).Returns(Result<WorkspaceDto>.Success(dto));

        var result = await new AttachContentHandler(_content, workspace).Handle(new AttachContentCommand(7, 3, Shared, 2), Ct);

        result.Value.ShouldBe(dto);
        await _content.Received().AttachContentAsync(7, 3, Shared, 2, false, Arg.Any<CancellationToken>()); // not required by default
        await workspace.Received().Send(new GetWorkspaceQuery(7, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttachingToAPostThatChangedIsAConflict()
    {
        _content.AttachContentAsync(7, 2, Shared, null, true, Arg.Any<CancellationToken>())
            .Returns(_ => throw new ExternalApiException("Content", 409, "Post 7 is at version 3, not 2."));

        var result = await new AttachContentHandler(_content, Substitute.For<ISender>()).Handle(new AttachContentCommand(7, 2, Shared, Required: true), Ct);

        result.Status.ShouldBe(ResultStatus.Conflict);
    }

    [Fact]
    public async Task AttachingNothingIsInvalid()
    {
        var result = await new AttachContentHandler(_content, Substitute.For<ISender>()).Handle(new AttachContentCommand(7, 3, " "), Ct);
        result.Status.ShouldBe(ResultStatus.Invalid);
        await _content.DidNotReceiveWithAnyArgs().AttachContentAsync(default, default, default!, default, default, Ct);
    }

    // ───────────── the workspace's block fields ─────────────

    [Fact]
    public async Task TheWorkspaceListsEachBlockFieldsValuesForTheInnerPartsEditor()
    {
        var result = await new GetWorkspaceHandler(_designs, _content, Bench.Workbench()).Handle(new GetWorkspaceQuery(7), Ct);
        var fields = result.Value.BlockFields.ShouldNotBeNull();
        fields["block.image"].ShouldBe([new WorkspaceBlockValue("image", "Image"), new WorkspaceBlockValue("alt", "Alt")]);
        fields.Keys.ShouldContain("block.video");
    }

    // ───────────── save block as template ─────────────

    [Fact]
    public async Task ABlockIsSavedAsTheUnsavedViewsShowIt()
    {
        Existing();
        var templates = Substitute.For<IRepository<BlockTemplate>>();
        templates.AddAsync(Arg.Any<BlockTemplate>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<BlockTemplate>());
        var access = Substitute.For<IAccessScopes>();
        access.UserId.Returns("designer-1");

        // The unsaved Mobile view gives the image a square aspect; the stored draft has no tokens on it.
        var unsaved = Samples.Design() with
        {
            Mobile = Samples.Design().Mobile with
            {
                Placements = [.. Samples.Design().Mobile.AllPlacements.Select(p => p.Part == "content:" + Samples.Image ? p with { Tokens = Samples.Tokens(("aspect", "1-1")) } : p)]
            }
        };
        var handler = new SaveBlockAsTemplateHandler(_designs, templates, _content, access, new TestClock());

        var fromDraft = await handler.Handle(new SaveBlockAsTemplateCommand(7, ViewKind.Mobile, "content:" + Samples.Image, "Square"), Ct);
        var fromUnsaved = await handler.Handle(new SaveBlockAsTemplateCommand(7, ViewKind.Mobile, "content:" + Samples.Image, "Square", Design: unsaved), Ct);

        fromDraft.Value.Tokens.ShouldBeEmpty();
        fromUnsaved.Value.Tokens["aspect"].ShouldBe("1-1");
        fromUnsaved.Value.FieldKey.ShouldBe("block.image");
        fromUnsaved.Value.View.ShouldBe(ViewKind.Mobile);
    }

    // ───────────── AMP ─────────────

    [Fact]
    public void AFragmentIsCheckedForTheRulesItsMarkupCanBreak()
    {
        var issues = AmpChecker.CheckFragment("""<div><img src="a.jpg"><p style="color:red" onclick="x()">Hi</p><amp-img src="b.jpg"></amp-img></div>""");
        issues.Select(i => i.Rule).ShouldBe(["tag", "style-attribute", "event-handler", "layout"], ignoreOrder: true);
        AmpChecker.CheckFragment("<p>Fine</p>").ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAmpIssueABlockCausesNamesItsPlacementOnce()
    {
        // A paragraph renderer whose AMP variant writes a plain <img>: the paragraph shows through "items" (placement 6).
        var registry = BlockRendererRegistry.Default.With(new BlockRenderer("block.paragraph", typeof(BadAmpBlock), typeof(BadAmpBlock)));
        var renderer = new PageRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, registry);
        var blockTemplates = Substitute.For<IReadRepository<BlockTemplate>>();
        blockTemplates.ListAsync(Arg.Any<ISpecification<BlockTemplate>>(), Arg.Any<CancellationToken>()).Returns([]);
        var workbench = new DesignWorkbench(Bench.Fields(), blockTemplates, Microsoft.Extensions.Options.Options.Create(Bench.Options), renderer);
        Existing();

        var report = (await new CheckDesignHandler(_designs, _content, workbench).Handle(new CheckDesignQuery(7), Ct)).Value;

        var amp = report.Problems.Where(p => p.Code == DesignWorkbench.Codes.Amp).ToList();
        var tag = amp.ShouldHaveSingleItem();
        tag.Path.ShouldBe("/amp/placements/6");
        tag.View.ShouldBe(ViewKind.Amp);
        tag.Message.ShouldContain("<img>");
        report.AmpCssBytes.ShouldNotBeNull().ShouldBeGreaterThan(0);
        report.AmpCssLimit.ShouldBe(AmpStyles.MaxBytes);
    }

    [Fact]
    public async Task ThePreviewNamesThePartOfEachAmpIssue()
    {
        var registry = BlockRendererRegistry.Default.With(new BlockRenderer("block.paragraph", typeof(BadAmpBlock), typeof(BadAmpBlock)));
        var renderer = new PageRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, registry);
        var blockTemplates = Substitute.For<IReadRepository<BlockTemplate>>();
        blockTemplates.ListAsync(Arg.Any<ISpecification<BlockTemplate>>(), Arg.Any<CancellationToken>()).Returns([]);
        var workbench = new DesignWorkbench(Bench.Fields(), blockTemplates, Microsoft.Extensions.Options.Options.Create(Bench.Options), renderer);
        Existing();

        var preview = (await new PreviewDesignHandler(_designs, _content, workbench).Handle(new PreviewDesignQuery(7, Amp: true), Ct)).Value;

        preview.AmpIssues.ShouldHaveSingleItem().Part.ShouldBe("items");
    }

    [Theory]
    [InlineData(59_000, false)]
    [InlineData(60_000, true)]
    [InlineData(75_000, true)]
    [InlineData(75_001, false)] // over the limit the AMP rule itself is the error
    public void TheCssBudgetWarnsFromEightyPercent(int bytes, bool warns)
    {
        var page = new RenderedPage("", true, bytes, []);
        var problems = DesignWorkbench.AmpProblems(Samples.Design(), page).ToList();
        problems.Any(p => p.Code == DesignWorkbench.Codes.AmpCssBudget && p.Severity == ProblemSeverity.Warning).ShouldBe(warns);
    }

    [Fact]
    public void APageIssueHasNoPath()
    {
        var page = new RenderedPage("", true, 100, [new AmpIssue("canonical", "No canonical."), new AmpIssue("tag", "<img>", "content:" + Samples.Image)]);
        var problems = DesignWorkbench.AmpProblems(Samples.Design(), page).ToList();
        problems[0].Path.ShouldBe("");
        problems[1].Path.ShouldBe("/amp/placements/5");
    }

    /// <summary>An AMP renderer that breaks the rules: a plain img.</summary>
    public sealed class BadAmpBlock : BlockComponent
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "img");
            builder.AddAttribute(1, "src", "a.jpg");
            builder.CloseElement();
        }
    }
}
