using Mediator;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>Plain text edited in place in the studio, saved through Content (phase E1).</summary>
public class EditTextHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Heading = "11111111-1111-1111-1111-111111111111";
    private const string Paragraph = "22222222-2222-2222-2222-222222222222";

    private readonly IContentApi _content = Substitute.For<IContentApi>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly WorkspaceDto _workspace = new(new WorkspacePost(7, "pub-7", "T", "post.news", 2, 4, null), [], null!, [],
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), null);

    public EditTextHandlerTests()
    {
        var document = JsonSerializer.SerializeToElement(new
        {
            schemaId = "post.news", schemaVersion = 2,
            content = new { headline = "Old headline", author = "Mona" },
            style = new { align = "start" },
            children = Array.Empty<object>()
        });
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(new ContentPost(7, "pub-7", null, "T", "post.news", 2, 4, document,
        [
            new PostContentInfo(Heading, "n-h", "block.heading", 1, 0, false, 3, null, null, "draft"),
            new PostContentInfo(Paragraph, "n-p", "block.paragraph", 1, 1, false, 1, null, null, "draft")
        ]));
        _content.GetContentAsync(Heading, Arg.Any<CancellationToken>()).Returns(Detail(Heading, "n-h", "block.heading", "Old heading"));
        _content.GetContentAsync(Paragraph, Arg.Any<CancellationToken>()).Returns(Detail(Paragraph, "n-p", "block.paragraph", "Old words"));
        _sender.Send(Arg.Any<GetWorkspaceQuery>(), Arg.Any<CancellationToken>()).Returns(Result<WorkspaceDto>.Success(_workspace));
    }

    private static LibraryContentDetail Detail(string id, string nodeId, string field, string text) => new(
        new LibraryContent(id, nodeId, field, 1, "draft", 3, null, null, null, DateTimeOffset.UnixEpoch),
        JsonSerializer.SerializeToElement(new
        {
            nodeId, postKind = "post.news", schemaId = field, schemaVersion = 1,
            content = new { text, note = "kept" }, style = new { level = "h3" }, children = Array.Empty<object>()
        }),
        Version: 9);

    private EditTextHandler Handler() => new(_content, Bench.Workbench(), _sender);

    [Fact]
    public async Task AContentsTextIsSavedAsANewRevisionWithItsOtherValuesKept()
    {
        var result = await Handler().Handle(new EditTextCommand(7, "content:" + Heading, "text", "New\nheading ", "Old heading"), Ct);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Value.ShouldBe(_workspace);
        await _content.Received().EditContentAsync(Heading, 9, Arg.Is<JsonElement>(v =>
            v.GetProperty("content").GetProperty("text").GetString() == "New heading"
            && v.GetProperty("content").GetProperty("note").GetString() == "kept"
            && v.GetProperty("style").GetProperty("level").GetString() == "h3"
            && v.GetProperty("nodeId").GetString() == "n-h"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARootValueIsSavedWithTheRootsStyleKept()
    {
        var result = await Handler().Handle(new EditTextCommand(7, "prop:headline", null, "New headline", "Old headline"), Ct);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        await _content.Received().SetPostRootAsync(7, 4, 2,
            Arg.Is<JsonElement>(c => c.GetProperty("headline").GetString() == "New headline" && c.GetProperty("author").GetString() == "Mona"),
            Arg.Is<JsonElement>(s => s.GetProperty("align").GetString() == "start"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATextChangedMeanwhileIsAConflictAndNothingIsSaved()
    {
        var result = await Handler().Handle(new EditTextCommand(7, "content:" + Heading, "text", "Mine", "What I saw"), Ct);

        result.Status.ShouldBe(ResultStatus.Conflict);
        await _content.DidNotReceiveWithAnyArgs().EditContentAsync(default!, default, default, Ct);
    }

    [Theory]
    [InlineData("content:" + Paragraph, "text")]   // rich text: E1b
    [InlineData("prop:image", null)]               // an asset
    [InlineData("prop:datePublished", null)]       // a date
    [InlineData("content:" + Heading, "level")]    // not a value of the field
    [InlineData("region:body", null)]
    [InlineData("content:33333333-3333-3333-3333-333333333333", "text")] // not in the post
    public async Task OnlyPlainTextValuesOfThePostAreEditedHere(string part, string? key)
    {
        var result = await Handler().Handle(new EditTextCommand(7, part, key, "x", null), Ct);

        result.Status.ShouldBe(ResultStatus.Invalid);
        await _content.DidNotReceiveWithAnyArgs().EditContentAsync(default!, default, default, Ct);
        await _content.DidNotReceiveWithAnyArgs().SetPostRootAsync(default, default, default, default, default, Ct);
    }

    [Fact]
    public async Task ContentsRefusalComesBackAsItsStatus()
    {
        _content.EditContentAsync(Heading, 9, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ExternalApiException("Content", 403, "You may not edit this content in stage 'review'."));

        var result = await Handler().Handle(new EditTextCommand(7, "content:" + Heading, "text", "Mine", "Old heading"), Ct);

        result.Status.ShouldBe(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task ATooLongTextIsRefused()
    {
        var result = await Handler().Handle(new EditTextCommand(7, "prop:headline", null, new string('a', EditTextHandler.MaxLength + 1), null), Ct);
        result.Status.ShouldBe(ResultStatus.Invalid);
    }
}
