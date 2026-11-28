using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests;

/// <summary>The design workbench over substituted ports: DynamicFields answers with the sample fields.</summary>
public static class Bench
{
    /// <summary>JSON-LD with a "&lt;/script&gt;" in it, to see it escaped in the page.</summary>
    public const string JsonLd = """
    {"@context":"https://schema.org","@type":"NewsArticle","headline":"Final </script> day","image":"https://news.example/a.jpg",
     "datePublished":"2026-11-20T09:00:00Z","author":{"@type":"Person","name":"Mona"}}
    """;

    public static ContentDesignerOptions Options { get; } = new() { SiteBaseUrl = "https://news.example" };

    public static IFieldsApi Fields(string? jsonLd = JsonLd, IReadOnlyList<ContentIssue>? issues = null)
    {
        var fields = Substitute.For<IFieldsApi>();
        fields.GetFieldVersionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<FieldVersionInfo?>(call.ArgAt<string>(0).StartsWith("post.", StringComparison.Ordinal)
                ? Samples.Kind
                : Samples.Block(call.ArgAt<string>(0))));
        fields.ValidateAsync(Arg.Any<JsonNode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(issues ?? (IReadOnlyList<ContentIssue>)[]));
        fields.BuildJsonLdAsync(Arg.Any<JsonNode>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, RenderAsset>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(jsonLd is null ? null : new JsonLdResult(jsonLd, [])));
        return fields;
    }

    /// <summary>The rights over a caller, with the module's default settings.</summary>
    public static DesignRights Rights(IAccessScopes access, IContentApi content) =>
        new(access, content, Microsoft.Extensions.Options.Options.Create(Options));

    /// <summary>A caller who holds every permission everywhere.</summary>
    public static IAccessScopes AllowEverything(IAccessScopes access)
    {
        access.HasPermissionAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(true));
        return access;
    }

    /// <summary>A caller with exactly these grants: a permission on a scope.</summary>
    public static IAccessScopes Grants(params (string Permission, string Scope)[] grants)
    {
        var access = Substitute.For<IAccessScopes>();
        access.UserId.Returns("editor-1");
        access.HasPermissionAsync(default!, default!, default).ReturnsForAnyArgs(call => Task.FromResult(grants.Any(g =>
            g.Permission == call.ArgAt<string>(0) && ((IEnumerable<string>)call.Args()[1]).Contains(g.Scope, StringComparer.OrdinalIgnoreCase))));
        return access;
    }

    public static IOrganizationApi Organization(IReadOnlyList<UnitName>? units = null, IReadOnlyList<UnitMemberName>? members = null)
    {
        var organization = Substitute.For<IOrganizationApi>();
        organization.ListUnitsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(units ?? []));
        organization.MembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(members ?? []));
        return organization;
    }

    public static PageRenderer Renderer() => new(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);

    public static DesignWorkbench Workbench(IFieldsApi? fields = null, IReadRepository<BlockTemplate>? blockTemplates = null)
    {
        if (blockTemplates is null)
        {
            blockTemplates = Substitute.For<IReadRepository<BlockTemplate>>();
            blockTemplates.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<BlockTemplate>>(), Arg.Any<CancellationToken>()).Returns([]);
        }
        return new(fields ?? Fields(), blockTemplates, Microsoft.Extensions.Options.Options.Create(Options), Renderer());
    }
}
