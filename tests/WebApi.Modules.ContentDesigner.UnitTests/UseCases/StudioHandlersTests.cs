using Ardalis.Specification;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>What the studio adds: the workspace, the change feed proxy, and checks of unsaved views with their JSON-LD.</summary>
public class StudioHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IReadRepository<PostDesign> _designs = Substitute.For<IReadRepository<PostDesign>>();
    private readonly IContentApi _content = Substitute.For<IContentApi>();

    private DesignRights Rights() => Bench.Rights(Bench.AllowEverything(Substitute.For<IAccessScopes>()), _content);

    private GetWorkspaceHandler Workspaces() => new(_designs, _content, Bench.Workbench(), Bench.Organization(), Rights(), Bench.Gate());

    public StudioHandlersTests() => _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Samples.Post());

    private PostDesign Existing()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "editor-1", TestClock.Start);
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);
        return design;
    }

    [Fact]
    public async Task TheWorkspaceHasThePostItsContentsTheDocumentAndTheDesign()
    {
        Existing();
        var result = await Workspaces().Handle(new GetWorkspaceQuery(7, "ar"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var workspace = result.Value;
        workspace.Post.Categories.ShouldNotBeNull().ShouldBeEmpty();
        (workspace.Post with { Categories = null }).ShouldBe(new WorkspacePost(7, "pub-7", "Final day at the venue", "post.news", 2, 3, "/news/final-day"));
        workspace.Contents.Select(c => c.ContentId).ShouldBe([Samples.Heading, Samples.Paragraph, Samples.Image, Samples.Gallery, Samples.Video]);
        workspace.Contents[4].FieldLabel.ShouldBe("Video clip"); // the field's title
        workspace.Contents[0].FieldLabel.ShouldBe("Heading"); // no title in the snapshot: a label made from the key
        workspace.Contents[0].Stage.ShouldBe("accepted");

        // The renderer's document names the top-level blocks by content id, in the post's language and direction.
        workspace.Document.Contents.Select(b => b.Id).ShouldBe([Samples.Heading, Samples.Paragraph, Samples.Image, Samples.Gallery, Samples.Video]);
        workspace.Document.Direction.ShouldBe("rtl");
        workspace.Document.JsonLd.ShouldBeNull();

        workspace.KindValues.Select(v => v.Key).ShouldContain("headline");
        workspace.FieldStyles["block.image"]["aspect"].ShouldContain("16-9");
        workspace.Design.ShouldNotBeNull().PostId.ShouldBe(7);
    }

    /// <summary>
    /// A post Content's UI made before posts got their kind's root: the check still blocks it, with one error that says what
    /// to do, instead of "legacy.basic is not published" and an unknown "headline".
    /// </summary>
    [Fact]
    public async Task ALegacyRootIsOneErrorThatSaysWhatToDo()
    {
        var fields = Bench.Fields(issues: [new ContentIssue("/field", "Field 'legacy.basic' version 1 is not published.")]);
        fields.GetFieldVersionAsync("legacy.basic", 1, Arg.Any<CancellationToken>()).Returns(Task.FromResult<FieldVersionInfo?>(null));
        var post = Samples.Post() with { KindVersion = 1, DocumentSchemaId = "legacy.basic" };

        var problems = await Bench.Workbench(fields).CheckAsync(Samples.Design(), 1, post, null, null, Ct);

        var error = problems.Where(p => p.Path == "/field").ShouldHaveSingleItem();
        error.Severity.ShouldBe(ProblemSeverity.Error);
        error.Message.ShouldContain("'post.news'");
        error.Message.ShouldContain("Contents Management");
        problems.ShouldNotContain(p => p.Message.Contains("legacy.basic"));
        problems.ShouldNotContain(p => p.Message.Contains("has no value 'headline'"));
        problems.HasErrors().ShouldBeTrue();
    }

    /// <summary>DynamicFields' warnings show as warnings: they never make the design invalid.</summary>
    [Fact]
    public async Task AFieldsWarningIsAWarning()
    {
        var fields = Bench.Fields(issues: [new ContentIssue("/content/headline", "Consider a shorter headline.", Warning: true)]);

        var problems = await Bench.Workbench(fields).CheckAsync(Samples.Design(), 1, Samples.Post(), null, null, Ct);

        var found = problems.Where(p => p.Message == "Consider a shorter headline.").ShouldHaveSingleItem();
        found.Severity.ShouldBe(ProblemSeverity.Warning);
    }

    /// <summary>
    /// The style tokens, written out. DynamicFields' StyleVocabularyTests hold the same lines; a change here needs one
    /// there and in MudTail's _style-tokens.scss.
    /// </summary>
    [Fact]
    public void TheStyleTokensAreTheSharedTable() =>
        string.Join("; ", StyleTokens.Values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={string.Join(",", v.Value)}"))
            .ShouldBe("align=start,center,end; aspect=auto,1-1,4-3,3-2,16-9,21-9,3-4,9-16; columns=1,2,3,4,5,6,12; "
                + "layout=standard,wide,full; level=h1,h2,h3,h4,h5,h6; size=sm,md,lg,xl; spacing=none,sm,md,lg,xl; "
                + "tone=default,muted,accent,inverse; width=content,wide,full,half,third");

    [Fact]
    public async Task APostWithoutADesignOpensWithNone()
    {
        var result = await Workspaces().Handle(new GetWorkspaceQuery(7), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Design.ShouldBeNull();
    }

    [Fact]
    public async Task AWorkspaceOfAPostContentDoesNotShowIsNotFound()
    {
        var result = await Workspaces().Handle(new GetWorkspaceQuery(8), Ct);
        result.Status.ShouldBe(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ContentBeingDownMakesTheWorkspaceUnavailable()
    {
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns<ContentPost?>(_ => throw new ExternalApiException("Content", null, "refused"));
        var result = await Workspaces().Handle(new GetWorkspaceQuery(7), Ct);
        result.Status.ShouldBe(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task TheEventsComeFromContentsFeed()
    {
        var page = new FeedPage([new FeedEvent(12, "content.updated", 7, Samples.Image, 4, 2, "submitted", "desk", TestClock.Start)], 12, false);
        _content.GetPostEventsAsync(7, 10, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetPostEventsHandler(_content).Handle(new GetPostEventsQuery(7, 10), Ct);

        result.Value.ShouldBe(page);
    }

    [Fact]
    public async Task ANegativeEventIdIsInvalid()
    {
        var result = await new GetPostEventsHandler(_content).Handle(new GetPostEventsQuery(7, -1), Ct);
        result.Status.ShouldBe(ResultStatus.Invalid);
    }

    [Fact]
    public async Task TheCheckReportCarriesTheJsonLd()
    {
        Existing();
        var result = await new CheckDesignHandler(_designs, _content, Bench.Workbench()).Handle(new CheckDesignQuery(7), Ct);
        result.Value.JsonLd.ShouldBe(Bench.JsonLd);
    }

    [Fact]
    public async Task WithoutASiteBaseUrlTheJsonLdGetsNoUrlRatherThanARelativeOne()
    {
        // DynamicFields refuses a relative url ("The post's url is an http or https address."), which made every check 503.
        Existing();
        var fields = Bench.Fields();
        var workbench = new DesignWorkbench(fields, Substitute.For<IReadRepository<soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.BlockTemplate>>(),
            Microsoft.Extensions.Options.Options.Create(new soft.square.net.WebApi.Modules.ContentDesigner.UseCases.ContentDesignerOptions { SiteBaseUrl = "" }), Bench.Renderer());

        var result = await new CheckDesignHandler(_designs, _content, workbench).Handle(new CheckDesignQuery(7), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fields.Received().BuildJsonLdAsync(Arg.Any<System.Text.Json.Nodes.JsonNode>(), Arg.Is<string?>(u => u == null), Arg.Any<string?>(),
            Arg.Any<IReadOnlyDictionary<string, soft.square.net.ContentDesigner.Rendering.Model.RenderAsset>?>(), Arg.Any<CancellationToken>());
        workbench.JsonLdUrl("https://news.example/a").ShouldBe("https://news.example/a");
    }

    [Fact]
    public async Task UnsavedViewsAreCheckedInsteadOfTheDraft()
    {
        Existing();
        // The unsaved Mobile view hides the required headline; the stored draft shows it.
        var unsaved = Samples.Design() with
        {
            Mobile = Samples.Design().Mobile with
            {
                Placements = [.. Samples.Design().Mobile.AllPlacements.Select(p => p.Part == "prop:headline" ? p with { Hidden = true } : p)]
            }
        };

        var draft = await new CheckDesignHandler(_designs, _content, Bench.Workbench()).Handle(new CheckDesignQuery(7), Ct);
        var edited = await new CheckDesignHandler(_designs, _content, Bench.Workbench()).Handle(new CheckDesignQuery(7, Design: unsaved), Ct);

        draft.Value.Problems.ShouldNotContain(p => p.Code == DesignRules.Codes.RequiredHidden);
        edited.Value.Problems.ShouldContain(p => p.Code == DesignRules.Codes.RequiredHidden && p.View == ViewKind.Mobile);
    }
}
