using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data;

namespace soft.square.net.WebApi.Modules.ContentDesigner.MigrationTests;

/// <summary>
/// One engine's migration set against a real database of that engine in Docker:
/// <list type="number">
/// <item>its model snapshot matches the model (no pending model changes; no database needed);</item>
/// <item>every migration applies to an empty database, in order;</item>
/// <item>the data step of <c>PostKindsAsFieldKeys</c> works both ways: down to the enum names, up to the field keys, in
/// the designs and the document templates, leaving a kind that never was an enum name alone;</item>
/// <item>every migration's Down runs (down to an empty schema), and every Up again.</item>
/// </list>
/// Without Docker the database tests are skipped.
/// </summary>
public abstract class MigrationHarness
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The migration that turns the kinds into field keys (its id ends with this name on every engine).</summary>
    private const string KindsMigration = "_PostKindsAsFieldKeys";

    protected abstract DatabaseProvider Provider { get; }

    /// <summary>The engine's container (not started).</summary>
    protected abstract IDatabaseContainer CreateContainer();

    /// <summary>A connection string that is never opened (the model check needs none).</summary>
    protected abstract string OfflineConnection { get; }

    /// <summary>A table of the module (<c>post_designs</c>), quoted, with its schema or prefix.</summary>
    protected abstract string Table(string name);

    /// <summary>A column or alias, quoted.</summary>
    protected abstract string Column(string name);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheModelSnapshotMatchesTheModel()
    {
        using var context = Context(OfflineConnection);

        context.Database.HasPendingModelChanges().ShouldBeFalse($"{Provider}: the model has changes no migration has: add one.");
    }

    [Fact]
    public async Task AppliesEveryMigrationAndMapsTheKindsBothWays()
    {
        await using var container = await StartAsync();
        var connection = container.GetConnectionString();

        // 1. Every migration, from an empty database.
        await using (var context = Context(connection))
        {
            var all = context.Database.GetMigrations().ToList();
            all.Count.ShouldBeGreaterThan(1);
            await context.Database.MigrateAsync(Ct);
            (await context.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(all);
            (await context.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        }

        // 2. A design, and templates of the six built-in kinds and of one an editor added since.
        string[] kinds = ["post.article", "post.news", "post.video", "post.reportage", "post.photo-story", "post.other", "post.opinion"];
        await using (var context = Context(connection))
        {
            context.PostDesigns.Add(PostDesign.Create(7, "pub-7", "post.photo-story", 1, DesignViews.Starter, null, "desk", Now));
            foreach (var kind in kinds)
                context.DocumentTemplates.Add(DocumentTemplate.Create("Template of " + kind, null, kind, 1, DesignViews.Starter, "desk", Now));
            await context.SaveChangesAsync(Ct);
        }

        // 3. Down to the enum names (the step before the kinds migration).
        await using (var context = Context(connection))
        {
            var all = context.Database.GetMigrations().ToList();
            var step = all.FindIndex(m => m.EndsWith(KindsMigration, StringComparison.Ordinal));
            step.ShouldBeGreaterThan(0, "the kinds migration exists");
            await context.GetService<IMigrator>().MigrateAsync(all[step - 1], Ct);
        }
        await using (var context = Context(connection))
        {
            (await Kinds(context, "post_designs")).ShouldBe(["PhotoStory"]);
            (await Kinds(context, "document_templates")).ShouldBe(
                ["Article", "News", "Video", "Reportage", "PhotoStory", "Other", "post.opinion"], ignoreOrder: true);
        }

        // 4. Up again: the enum names become field keys, and the rows read through EF.
        await using (var context = Context(connection))
        {
            await context.Database.MigrateAsync(Ct);
            (await Kinds(context, "post_designs")).ShouldBe(["post.photo-story"]);
            (await Kinds(context, "document_templates")).ShouldBe(kinds, ignoreOrder: true);
            (await context.PostDesigns.AsNoTracking().SingleAsync(Ct)).Kind.ShouldBe("post.photo-story");
            (await context.DocumentTemplates.AsNoTracking().CountAsync(Ct)).ShouldBe(kinds.Length);
        }

        // 5. Every Down, then every Up again.
        await using (var context = Context(connection))
        {
            await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, Ct);
            (await context.Database.GetAppliedMigrationsAsync(Ct)).ShouldBeEmpty();
            await context.Database.MigrateAsync(Ct);
            (await context.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        }
    }

    /// <summary>The kinds of every row of a table.</summary>
    private async Task<List<string>> Kinds(ContentDesignerDbContext context, string table)
    {
        var sql = $"SELECT {Column("Kind")} AS {Column("Value")} FROM {Table(table)}"; // names only, from this class
        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync(Ct);
    }

    private async Task<IDatabaseContainer> StartAsync()
    {
        IDatabaseContainer container;
        try
        {
            container = CreateContainer();
            await container.StartAsync(Ct);
        }
        catch (Exception e) when (e is DockerUnavailableException || e.InnerException is DockerUnavailableException)
        {
            Assert.Skip($"{Provider}: Docker is not available ({e.Message}).");
            throw; // not reached
        }
        return container;
    }

    /// <summary>A context on <paramref name="connection"/> with this engine's migration set, as the platform builds it.</summary>
    private ContentDesignerDbContext Context(string connection)
    {
        var services = new ServiceCollection();
        services.AddScoped<DomainEventDispatcher>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;

        var options = new DbContextOptionsBuilder<ContentDesignerDbContext>();
        DatabaseProviderAdapter.For(Provider).Use(options, connection, "contentdesigner_tests");
        options.ReplaceService<IMigrationsAssembly, ProviderMigrationsAssembly>();
        options.UseApplicationServiceProvider(provider);
        return new ContentDesignerDbContext(options.Options, new TestTenant("acme"));
    }

    private sealed class TestTenant(string id) : ITenantContext
    {
        public TenantInfo? Tenant { get; } = new(id, id, TenantStatus.Active, [], "", "contentdesigner_tests");
        public string? ResolvedBy => "test";
    }
}
