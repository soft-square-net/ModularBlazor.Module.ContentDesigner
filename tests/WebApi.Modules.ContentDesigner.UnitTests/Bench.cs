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
