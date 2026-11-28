using Ardalis.Specification;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests.UseCases;

/// <summary>The newsroom's part: unit-scoped rights, field-key kinds, unit names and Content's refusals.</summary>
public class NewsroomHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Root = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Desk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private const string Arrange = ContentDesignerPermissions.Arrange;
    private const string Publish = ContentDesignerPermissions.Publish;

    private readonly IRepository<PostDesign> _designs = Substitute.For<IRepository<PostDesign>>();
    private readonly IContentApi _content = Substitute.For<IContentApi>();
    private readonly TestClock _clock = new();

    private static ContentPost Filed() => Samples.Post() with
    {
        PressId = "press-1", Categories = ["cat-sport", "cat-local"], UnitId = Desk, UnitPath = $"/{Root:N}/{Desk:N}/", UrlPrefix = "news"
    };

    private static PostDesign Design() => PostDesign.Create(7, "pub-7", "post.news", 2, Samples.Design(), null, "editor-1", TestClock.Start);

    private async Task<PostRightsDto> RightsOn(ContentPost? post, params (string, string)[] grants) =>
        await Bench.Rights(Bench.Grants(grants), _content).EvaluateAsync(7, post, Ct);

    [Fact]
    public void APostsScopesAreItsPressCategoriesAndUnitWithTheUnitsAbove() =>
        DesignRights.ScopesOf(Filed()).ShouldBe(["press:press-1", "category:cat-sport", "category:cat-local", $"unit:{Root:N}", $"unit:{Desk:N}"],
            ignoreOrder: true);

    [Theory]
    [InlineData("self")]
    [InlineData("above")]
    [InlineData("press")]
    [InlineData("category")]
    [InlineData("tenant")]
    public async Task AGrantOnThePostsUnitAnAncestorItsPressACategoryOrTheTenantLetsOneArrange(string where)
    {
        var scope = where switch
        {
            "self" => $"unit:{Desk:N}", "above" => $"unit:{Root:N}", "press" => "press:press-1", "category" => "category:cat-local", _ => "tenant"
        };
        var access = Bench.Grants((Arrange, scope));
        // The platform counts a tenant-wide grant for every scope; the substitute is told the same.
        if (where == "tenant") Bench.AllowEverything(access);

        var rights = await Bench.Rights(access, _content).EvaluateAsync(7, Filed(), Ct);

        rights.CanArrange.ShouldBeTrue();
        rights.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task AGrantOnAnotherUnitDoesNotCount()
    {
        var rights = await RightsOn(Filed(), (Arrange, $"unit:{Other:N}"), (Publish, $"unit:{Other:N}"));

        rights.CanArrange.ShouldBeFalse();
        rights.CanPublish.ShouldBeFalse();
        rights.Reason.ShouldBe(PostRightsReasons.NoRight);
        rights.UnitId.ShouldBe(Desk);
    }

    [Fact]
    public async Task ArrangeAndPublishAreCheckedApart()
    {
        var rights = await RightsOn(Filed(), (Arrange, $"unit:{Desk:N}"));

        rights.CanArrange.ShouldBeTrue();
        rights.CanPublish.ShouldBeFalse();
    }

    [Fact]
    public async Task APostInNoUnitIsNotTheShiftsToArrange()
    {
        var post = Samples.Post() with { PressId = "press-1" };

        var rights = await RightsOn(post, (Arrange, "press:press-1"), (Publish, "press:press-1"));

        rights.CanArrange.ShouldBeFalse();
        rights.CanPublish.ShouldBeFalse();
        rights.Reason.ShouldBe(PostRightsReasons.NoUnit);
    }

    [Theory]
    [InlineData("newsroom.moderate")]
    [InlineData("content.accept")]
    [InlineData("content.publish")]
    public async Task TheModeratorAndTheDeskMayArrangeAPostInNoUnit(string role)
    {
        var post = Samples.Post() with { PressId = "press-1" };

        var rights = await RightsOn(post, (Arrange, "press:press-1"), (role, "press:press-1"));

        rights.CanArrange.ShouldBeTrue();
        rights.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task APostContentDoesNotShowLeavesOnlyTenantWideRights()
    {
        var rights = await RightsOn(null, (Arrange, $"unit:{Desk:N}"));

        rights.CanArrange.ShouldBeFalse();
        rights.Reason.ShouldBe(PostRightsReasons.PostHidden);
    }

    [Fact]
    public async Task SavingOutsideTheRightsIsForbiddenBeforeTheVersionIsCompared()
    {
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Filed());
        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(Design());
        var access = Bench.Grants((Arrange, $"unit:{Other:N}"));

        var result = await new SaveDesignHandler(_designs, access, Bench.Rights(access, _content), _clock)
            .Handle(new SaveDesignCommand(7, 99, Samples.Design()), Ct);

        result.Status.ShouldBe(ResultStatus.Forbidden);
        await _designs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, Ct);
    }

    [Fact]
    public async Task OpeningDeletingAndPublishingAreGuardedToo()
    {
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Filed());
        var access = Bench.Grants();
        var rights = Bench.Rights(access, _content);
        var templates = Substitute.For<IReadRepository<DocumentTemplate>>();

        (await new OpenDesignHandler(_designs, templates, _content, access, rights, _clock).Handle(new OpenDesignCommand(7), Ct))
            .Status.ShouldBe(ResultStatus.Forbidden);

        _designs.FirstOrDefaultAsync(Arg.Any<ISpecification<PostDesign>>(), Arg.Any<CancellationToken>()).Returns(Design());
        (await new DeleteDesignHandler(_designs, rights).Handle(new DeleteDesignCommand(7, 0), Ct)).Status.ShouldBe(ResultStatus.Forbidden);
        (await new PublishDesignHandler(_designs, _content, Bench.Workbench(), access, rights, _clock).Handle(new PublishDesignCommand(7, 0), Ct))
            .Status.ShouldBe(ResultStatus.Forbidden);
        await _designs.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);
        await _designs.DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task TheWorkspaceNamesTheUnitsTheAssigneesAndTheCategories()
    {
        var filed = Filed();
        var post = filed with { Contents = [filed.Contents[0] with { UnitId = Desk, AssigneeId = "user-9", Stage = "in-progress" }, .. filed.Contents.Skip(1)] };
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(post);
        _content.ListCategoriesAsync("press-1", Arg.Any<CancellationToken>()).Returns([new CategoryName("cat-sport", "Sport", "رياضة")]);
        var organization = Bench.Organization([new UnitName(Desk, "Sports desk", "قسم الرياضة")], [new UnitMemberName("user-9", "Mona Adel")]);
        var access = Bench.Grants((Arrange, $"unit:{Desk:N}"));

        var workspace = (await new GetWorkspaceHandler(Substitute.For<IReadRepository<PostDesign>>(), _content, Bench.Workbench(), organization,
            Bench.Rights(access, _content)).Handle(new GetWorkspaceQuery(7), Ct)).Value;

        workspace.Post.UnitNameEn.ShouldBe("Sports desk");
        workspace.Post.UrlPrefix.ShouldBe("news");
        workspace.Post.Categories.ShouldNotBeNull().Select(c => c.NameEn).ShouldBe(["Sport", null]);
        workspace.Contents[0].UnitNameAr.ShouldBe("قسم الرياضة");
        workspace.Contents[0].AssigneeName.ShouldBe("Mona Adel");
        workspace.Contents[0].Stage.ShouldBe("in-progress");
        workspace.Contents[1].UnitId.ShouldBeNull();
        workspace.Rights.ShouldNotBeNull().CanArrange.ShouldBeTrue();
        workspace.Rights.CanPublish.ShouldBeFalse();
    }

    [Fact]
    public async Task CategoryNamesContentRefusesLeaveTheIds()
    {
        _content.GetPostAsync(7, Arg.Any<CancellationToken>()).Returns(Filed());
        _content.ListCategoriesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CategoryName>>(_ => throw new ExternalApiException("Content", 403, "No."));
        var access = Bench.AllowEverything(Substitute.For<IAccessScopes>());

        var workspace = (await new GetWorkspaceHandler(Substitute.For<IReadRepository<PostDesign>>(), _content, Bench.Workbench(), Bench.Organization(),
            Bench.Rights(access, _content)).Handle(new GetWorkspaceQuery(7), Ct)).Value;

        workspace.Post.Categories.ShouldNotBeNull().Select(c => c.Id).ShouldBe(["cat-sport", "cat-local"]);
        workspace.Post.UnitId.ShouldBe(Desk);
        workspace.Post.UnitNameEn.ShouldBeNull();
    }

    [Fact]
    public async Task AContentFindingNamesItsNode()
    {
        var workbench = Bench.Workbench(Bench.Fields(issues: [new("/children/3/children/1/content/alt", "Alt is required.")]));

        var report = await workbench.CheckWithJsonLdAsync(DesignViews.Starter, 2, Samples.Post(), null, null, Ct);

        report.Problems.Single(p => p.Message == "Alt is required.").NodeId.ShouldBe("g2");
    }

    [Fact]
    public void ContentsProblemsStayProblemsThroughAResult()
    {
        var refusal = new ExternalApiException("Content", 422, "Refused.",
            [new ContentValidationProblem("/content/text", "Too long.", "n-heading"), new ContentValidationProblem("/content/title", "Needed.", null)]);

        var result = refusal.ToResult<int>();

        result.Status.ShouldBe(ResultStatus.Invalid);
        ProblemErrors.Of(result.ValidationErrors).ShouldBe(refusal.Problems);
    }

    [Theory]
    [InlineData("Article", "post.article")]
    [InlineData("news", "post.news")]
    [InlineData("PhotoStory", "post.photo-story")]
    [InlineData("post.opinion", "post.opinion")]
    [InlineData(" post.news ", "post.news")]
    public void KindsAreFieldKeys(string given, string key)
    {
        PostDesign.Create(7, "pub-7", given, 1, Samples.Design(), null, "desk", TestClock.Start).Kind.ShouldBe(key);
        DocumentTemplate.Create("T", null, given, 1, Samples.Design(), "desk", TestClock.Start).Kind.ShouldBe(key);
    }
}
