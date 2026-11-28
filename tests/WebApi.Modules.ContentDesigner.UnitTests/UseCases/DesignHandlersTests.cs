using Ardalis.Specification;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Rendering;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>The design use cases over substituted repositories and APIs.</summary>
public class DesignHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRepository<PostDesign> _designs = Substitute.For<IRepository<PostDesign>>();
    private readonly IReadRepository<PostDesign> _reads = Substitute.For<IReadRepository<PostDesign>>();
    private readonly IReadRepository<DocumentTemplate> _templates = Substitute.For<IReadRepository<DocumentTemplate>>();
    private readonly IContentApi _content = Substitute.For<IContentApi>();
    private readonly IAccessScopes _access = Substitute.For<IAccessScopes>();
    private readonly TestClock _clock = new();
    private DesignRights Rights => Bench.Rights(_access, _content);

    public DesignHandlersTests()
    {
        _access.UserId.Returns("editor-1");
        Bench.AllowEverything(_access);
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Samples.Post());
        _designs.AddAsync(Arg.Any<PostDesign>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<PostDesign>());
        _templates.ListAsync(Arg.Any<ISpecification<DocumentTemplate>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private void Existing(PostDesign design)
    {
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);
        _reads.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);
    }

    private static PostDesign Design(DesignViews? views = null) =>
        PostDesign.Create(7, "pub-7", "post.news", 2, views ?? Samples.Design(), null, "editor-1", TestClock.Start);

    [Fact]
    public async Task ANewDesignStartsFromTheKindsDefaultTemplate()
    {
        var template = DocumentTemplate.Create("News", null, "post.news", 2, Samples.Design(), "desk", TestClock.Start);
        _templates.ListAsync(Arg.Any<ISpecification<DocumentTemplate>>(), Arg.Any<CancellationToken>()).Returns([template]);

        var result = await new OpenDesignHandler(_designs, _templates, _content, _access, Rights, _clock).Handle(new OpenDesignCommand(7), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Draft.Desktop.ShouldMatch(Samples.Design().Desktop);
        result.Value.Kind.ShouldBe("post.news");
        result.Value.CreatedBy.ShouldBe("editor-1");
    }

    [Fact]
    public async Task WithoutADefaultANewDesignIsTheStarter()
    {
        var result = await new OpenDesignHandler(_designs, _templates, _content, _access, Rights, _clock).Handle(new OpenDesignCommand(7), Ct);
        result.Value.Draft.Mobile.ShouldMatch(ViewDesign.Starter);
    }

    [Fact]
    public async Task APostHasOneDesign()
    {
        _designs.AnyAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(true);
        var result = await new OpenDesignHandler(_designs, _templates, _content, _access, Rights, _clock).Handle(new OpenDesignCommand(7), Ct);
        result.Status.ShouldBe(ResultStatus.Conflict);
    }

    [Fact]
    public async Task ContentRefusingTheCallerIsForbidden()
    {
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns<ContentPost?>(_ => throw new ExternalApiException("Content", 403, "No."));
        var result = await new OpenDesignHandler(_designs, _templates, _content, _access, Rights, _clock).Handle(new OpenDesignCommand(7), Ct);
        result.Status.ShouldBe(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task AStaleVersionIsAConflict()
    {
        Existing(Design());
        var result = await new SaveDesignHandler(_designs, _access, Rights, _clock).Handle(new SaveDesignCommand(7, 5, Samples.Design()), Ct);
        result.Status.ShouldBe(ResultStatus.Conflict);
        await _designs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, Ct);
    }

    [Fact]
    public async Task PublishingPinsTheContentsRevisions()
    {
        Existing(Design());
        var result = await new PublishDesignHandler(_designs, _content, Bench.Workbench(), _access, Rights, _clock).Handle(new PublishDesignCommand(7, 0), Ct);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Value.Published.ShouldNotBeNull();
        result.Value.Published.Pins[Samples.Paragraph].ShouldBe(2);
        await _designs.Received(1).UpdateAsync(Arg.Any<PostDesign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADesignWithErrorsIsNotPublished()
    {
        // The starter design is fine, but DynamicFields finds the post's document invalid.
        Existing(Design(DesignViews.Starter));
        var workbench = Bench.Workbench(Bench.Fields(issues: [new("/content/headline", "The headline is required.")]));
        var result = await new PublishDesignHandler(_designs, _content, workbench, _access, Rights, _clock).Handle(new PublishDesignCommand(7, 0), Ct);

        result.Status.ShouldBe(ResultStatus.Conflict);
        result.Errors.ShouldContain("The headline is required.");
        await _designs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, Ct);
    }

    [Fact]
    public async Task JsonLdMissingAnEssentialIsAnError()
    {
        Existing(Design());
        var workbench = Bench.Workbench(Bench.Fields(jsonLd: """{"@type":"NewsArticle","headline":"x","image":"y","author":"z"}"""));
        var result = await new CheckDesignHandler(_reads, _content, workbench).Handle(new CheckDesignQuery(7), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Problems.ShouldContain(p => p.Code == DesignWorkbench.Codes.JsonLd && p.Path == "/datePublished" && p.Severity == ProblemSeverity.Error);
    }

    [Fact]
    public async Task APreviewOfUnsavedViewsIsChecked()
    {
        Existing(Design());
        var broken = Samples.Design() with { Mobile = new ViewDesign([new("a", RegionKind.Section)], [new("items", "missing")]) };
        // A malformed design is a DomainException, which the module's endpoint filter answers with 409.
        await Should.ThrowAsync<DomainException>(async () =>
            await new PreviewDesignHandler(_reads, _content, Bench.Workbench()).Handle(new PreviewDesignQuery(7, Views: broken), Ct));
    }

    public class Rendering
    {
        private readonly IReadRepository<PostDesign> _designs = Substitute.For<IReadRepository<PostDesign>>();
        private readonly IContentApi _content = Substitute.For<IContentApi>();
        private IFieldsApi _fields = Bench.Fields();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
        private DateTimeOffset _publishedAt = TestClock.Start;

        public Rendering()
        {
            var design = Design();
            var post = Samples.Post();
            design.Publish(post.Contents.ToDictionary(c => c.ContentId, c => c.Revision), post.Contents.ToDictionary(c => c.ContentId, c => c.NodeId),
                "desk", TestClock.Start);
            _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);
            _content.GetPublishedAsync("pub-7", Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => new PublishedPost(7, "pub-7", "/news/final-day", "Final day at the venue", _publishedAt, "post.news", 2, Samples.Document, ["en"]));
            _tenant.TenantId.Returns("acme");
        }

        private RenderPublishedHandler Handler() =>
            new(_designs, _content, Bench.Workbench(_fields), _cache, _tenant, Options.Create(Bench.Options));

        [Fact]
        public async Task ThePublishedPageRendersTheDesignedContents()
        {
            var result = await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Html.ShouldContain("data-mb-block=\"block.image\"");
            result.Value.Html.ShouldContain("<h1 data-mb-prop=\"headline\">Final day at the venue</h1>");
        }

        [Fact]
        public async Task APageIsCachedUntilThePostIsPublishedAgain()
        {
            await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            await _fields.ReceivedWithAnyArgs(1).BuildJsonLdAsync(default!, default, default, default, Ct);

            _publishedAt = _publishedAt.AddMinutes(5);
            await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            await _fields.ReceivedWithAnyArgs(2).BuildJsonLdAsync(default!, default, default, default, Ct);
        }

        [Fact]
        public async Task TheAmpPageIsCachedApart()
        {
            var web = await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            var amp = await Handler().Handle(new RenderPublishedQuery("pub-7", Amp: true), Ct);
            amp.Value.Html.ShouldStartWith("<!doctype html><html amp ");
            web.Value.Html.ShouldNotBe(amp.Value.Html);
        }

        [Fact]
        public async Task AnUnpublishedDesignIsNotFound()
        {
            _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(Design());
            (await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct)).Status.ShouldBe(ResultStatus.NotFound);
        }

        [Fact]
        public async Task WithoutDynamicFieldsThePageStillRenders()
        {
            _fields = Substitute.For<IFieldsApi>();
            _fields.GetFieldVersionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns<FieldVersionInfo?>(_ => throw new ExternalApiException("DynamicFields", 401, "Sign in."));
            var result = await Handler().Handle(new RenderPublishedQuery("pub-7"), Ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Html.ShouldNotContain("application/ld+json");
            result.Value.Html.ShouldContain("The morning");
        }
    }
}
