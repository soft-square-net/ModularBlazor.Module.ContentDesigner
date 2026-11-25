using Microsoft.EntityFrameworkCore;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;

namespace soft.square.net.WebApi.Modules.ContentDesigner.IntegrationTests.Data;

/// <summary>The designs and templates through the platform's repository, on a real database: JSON columns, tenants, versions.</summary>
public sealed class EfRepositoryDesigns : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly ContentDesignerDatabase _database = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private static DesignViews Views()
    {
        var mobile = new ViewDesign(
            [new("page", RegionKind.Section, Tokens: new Dictionary<string, string> { ["layout"] = "wide" }), new("body", RegionKind.Row, "page", 0, Span: "12")],
            [new("prop:headline", "body", 0), new("content:abc", "body", 1, Tokens: new Dictionary<string, string> { ["width"] = "full" }, BlockTemplateId: 4),
             new("items", "body", 2, Hidden: true)]);
        return new DesignViews(mobile, ViewDesign.Inherit(ViewKind.Mobile), ViewDesign.Inherit(ViewKind.Mobile), ViewDesign.Inherit(ViewKind.Mobile));
    }

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    private async Task<PostDesign> AddAsync(int postId, string tenant = "acme", string kind = "post.news")
    {
        await using var context = _database.For(tenant);
        return await new EfRepository<PostDesign>(context).AddAsync(PostDesign.Create(postId, $"pub-{postId}", kind, 2, Views(), null, "u1", Now), Ct);
    }

    [Fact]
    public async Task TheViewsRoundTripThroughTheirJsonColumn()
    {
        var saved = await AddAsync(7);
        await using (var context = _database.For("acme"))
        {
            var repository = new EfRepository<PostDesign>(context);
            var design = await repository.FirstOrDefaultAsync(new DesignByPostSpec(7), Ct);
            design!.Publish(new Dictionary<string, int> { ["abc"] = 3 }, new Dictionary<string, string> { ["abc"] = "n-1" }, "desk", Now);
            await repository.UpdateAsync(design, Ct);
        }

        await using var read = _database.For("acme");
        var loaded = await new EfRepository<PostDesign>(read).FirstOrDefaultAsync(new DesignByPublicIdSpec("pub-7"), Ct);
        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(saved.Id);
        Json(loaded.Draft).ShouldBe(Json(Views().Checked()));
        Json(loaded.Published!.Views).ShouldBe(Json(Views().Checked()));
        loaded.Published.Pins["abc"].ShouldBe(3);
        loaded.Published.NodeIds["abc"].ShouldBe("n-1");
        loaded.Version.ShouldBe(1);
    }

    [Fact]
    public async Task ADesignBelongsToItsTenant()
    {
        await AddAsync(7);
        await AddAsync(7, tenant: "globex");
        await using var globex = _database.For("globex");
        var designs = await new EfRepository<PostDesign>(globex).ListAsync(Ct);
        designs.ShouldHaveSingleItem().TenantId.ShouldBe("globex");
    }

    [Fact]
    public async Task APostHasOneDesignPerTenant()
    {
        await AddAsync(7);
        await Should.ThrowAsync<DbUpdateException>(() => AddAsync(7));
    }

    [Fact]
    public async Task ALostRaceIsAConcurrencyConflict()
    {
        await AddAsync(7);
        await using var first = _database.For("acme");
        await using var second = _database.For("acme");
        var a = await new EfRepository<PostDesign>(first).FirstOrDefaultAsync(new DesignByPostSpec(7), Ct);
        var b = await new EfRepository<PostDesign>(second).FirstOrDefaultAsync(new DesignByPostSpec(7), Ct);

        a!.Arrange(ViewKind.Desktop, ViewDesign.Starter, "u1", Now);
        await new EfRepository<PostDesign>(first).UpdateAsync(a, Ct);
        b!.Arrange(ViewKind.Tablet, ViewDesign.Starter, "u2", Now);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => new EfRepository<PostDesign>(second).UpdateAsync(b, Ct));
    }

    [Fact]
    public async Task DesignsPageByKind()
    {
        for (var i = 1; i <= 3; i++) await AddAsync(i);
        await AddAsync(9, kind: "post.blog");
        await using var context = _database.For("acme");
        var designs = new EfRepository<PostDesign>(context);
        var spec = new DesignsPageSpec(1, 2, "post.news");
        (await designs.ListAsync(spec, Ct)).Count.ShouldBe(2);
        (await designs.CountAsync(spec, Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task TemplatesKeepTheirViewsAndTheKindsDefault()
    {
        await using (var context = _database.For("acme"))
        {
            var templates = new EfRepository<DocumentTemplate>(context);
            var news = DocumentTemplate.Create("News", "Standard", "post.news", 2, Views(), "desk", Now);
            news.MakeDefault(true, Now);
            await templates.AddAsync(news, Ct);
            await templates.AddAsync(DocumentTemplate.Create("Feature", null, "post.news", 2, DesignViews.Starter, "desk", Now), Ct);
        }

        await using var read = _database.For("acme");
        var defaults = await new EfRepository<DocumentTemplate>(read).ListAsync(new DefaultTemplatesSpec("post.news"), Ct);
        var only = defaults.ShouldHaveSingleItem();
        only.Name.ShouldBe("News");
        Json(only.Views).ShouldBe(Json(Views().Checked()));
    }

    [Fact]
    public async Task BlockTemplatesKeepTheirPartsTokensAndContent()
    {
        int id;
        await using (var context = _database.For("acme"))
            id = (await new EfRepository<BlockTemplate>(context).AddAsync(BlockTemplate.Create("Hero", "block.image", ViewKind.Desktop,
                ["image", "caption"], ["credit"], new Dictionary<string, string> { ["width"] = "full", ["aspect"] = "21-9" },
                """{ "field": "block.image", "fieldVersion": 1, "content": { "alt": "x" } }""", "desk", Now), Ct)).Id;

        await using var read = _database.For("acme");
        var repository = new EfRepository<BlockTemplate>(read);
        var template = await repository.FirstOrDefaultAsync(new BlockTemplateByIdSpec(id), Ct);
        template.ShouldNotBeNull();
        template.View.ShouldBe(ViewKind.Desktop);
        template.PartOrder.ShouldBe(["image", "caption"]);
        template.HiddenParts.ShouldBe(["credit"]);
        template.Tokens["aspect"].ShouldBe("21-9");
        template.ContentJson.ShouldNotBeNull().ShouldContain("\"alt\"");
        (await repository.ListAsync(new BlockTemplatesSpec("block.image", ViewKind.Desktop), Ct)).ShouldHaveSingleItem();
        (await repository.ListAsync(new BlockTemplatesSpec("block.image", ViewKind.Mobile), Ct)).ShouldBeEmpty();
    }
}
