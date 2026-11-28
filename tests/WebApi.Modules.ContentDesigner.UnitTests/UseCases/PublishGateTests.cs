using Ardalis.Specification;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>The newsroom workflow's say on publishing: only a project InProduction or ReadyForDesk lets a design out.</summary>
public class PublishGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRepository<PostDesign> _designs = Substitute.For<IRepository<PostDesign>>();
    private readonly IContentApi _content = Substitute.For<IContentApi>();
    private readonly IAccessScopes _access = Substitute.For<IAccessScopes>();
    private readonly TestClock _clock = new();

    public PublishGateTests()
    {
        _access.UserId.Returns("editor-1");
        Bench.AllowEverything(_access);
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Samples.Post());
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>())
            .Returns(PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "editor-1", TestClock.Start));
    }

    private static PostProjects Project(params string[] states) =>
        PostProjects.Known([.. states.Select(s => new NewsRoomProject(Guid.NewGuid(), s))]);

    private async Task<Ardalis.Result.Result<PostDesignDto>> PublishAsync(PostProjects newsRoom) =>
        await new PublishDesignHandler(_designs, _content, Bench.Workbench(), _access, Bench.Rights(_access, _content), Bench.Gate(newsRoom), _clock)
            .Handle(new PublishDesignCommand(7, 0), Ct);

    [Theory]
    [InlineData("InProduction")]
    [InlineData("ReadyForDesk")]
    public async Task AProjectInProductionOrReadyForTheDeskLetsTheDesignOut(string state)
    {
        var result = await PublishAsync(Project(state));

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Value.Published.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("NeedsModeration")]
    [InlineData("Scheduled")]
    [InlineData("Published")]
    [InlineData("Killed")]
    public async Task AProjectInAnyOtherStateDoesNot(string state)
    {
        var result = await PublishAsync(Project(state));

        result.Status.ShouldBe(ResultStatus.Conflict);
        result.Errors.Single().ShouldContain(state);
        await _designs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, Ct);
    }

    [Fact]
    public async Task APostWithoutAProjectPublishesAsBefore() =>
        (await PublishAsync(PostProjects.Known())).IsSuccess.ShouldBeTrue();

    [Fact]
    public async Task ADeploymentWithoutNewsRoomPublishesAsBefore() =>
        (await PublishAsync(PostProjects.Of(NewsRoomAnswer.NotInstalled))).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(NewsRoomAnswer.Unreachable)]
    [InlineData(NewsRoomAnswer.CannotSee)]
    public async Task WhenItCannotBeToldPublishingWaits(NewsRoomAnswer answer)
    {
        var result = await PublishAsync(PostProjects.Of(answer));

        result.Status.ShouldBe(ResultStatus.Unavailable);
        await _designs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, Ct);
    }

    [Fact]
    public async Task TakingADesignDownIsNeverGated()
    {
        var design = PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "editor-1", TestClock.Start);
        design.Publish(new Dictionary<string, int>(), new Dictionary<string, string>(), "editor-1", TestClock.Start);
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(design);

        // The handler takes no gate at all.
        var result = await new UnpublishDesignHandler(_designs, _access, Bench.Rights(_access, _content), _clock)
            .Handle(new UnpublishDesignCommand(7, design.Version), Ct);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Value.Published.ShouldBeNull();
    }

    [Fact]
    public async Task TheRightsCarryTheWorkflowsSayForThoseWhoMayPublish()
    {
        var project = new NewsRoomProject(Guid.NewGuid(), "NeedsModeration");

        var rights = (await new GetPostRightsHandler(_content, Bench.Rights(_access, _content), Bench.Gate(PostProjects.Known(project)))
            .Handle(new GetPostRightsQuery(7), Ct)).Value;

        rights.CanPublish.ShouldBeTrue(); // the right and the workflow's say are apart: taking down needs only the right
        var publishing = rights.Publishing.ShouldNotBeNull();
        publishing.Allows.ShouldBeFalse();
        publishing.Status.ShouldBe(PublishGateStatuses.Closed);
        publishing.ProjectId.ShouldBe(project.Id);
        publishing.ProjectState.ShouldBe("NeedsModeration");
    }

    [Fact]
    public async Task NewsRoomIsNotAskedForACallerWhoMayNotPublish()
    {
        var newsRoom = Substitute.For<INewsRoomApi>();
        var access = Bench.Grants();

        var rights = (await new GetPostRightsHandler(_content, Bench.Rights(access, _content), new PublishGate(newsRoom))
            .Handle(new GetPostRightsQuery(7), Ct)).Value;

        rights.Publishing.ShouldBeNull();
        await newsRoom.DidNotReceiveWithAnyArgs().GetPostProjectsAsync(default, Ct);
    }
}
