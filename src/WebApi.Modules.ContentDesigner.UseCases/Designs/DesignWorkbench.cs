using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>The field versions a post uses: its kind's and each content's.</summary>
public sealed record PostFields(FieldVersionInfo? Kind, IReadOnlyDictionary<string, FieldVersionInfo> Blocks)
{
    public static string Key(string field, int version) => $"{field}@{version}";

    public FieldVersionInfo? Block(string field, int version) => Blocks.GetValueOrDefault(Key(field, version));
}

/// <summary>
/// What the design use cases share: gathering the field versions a post uses (DynamicFields), the checks (the design
/// rules, DynamicFields validation, JSON-LD essentials, the AMP page), and rendering a design to HTML or AMP.
/// </summary>
public sealed class DesignWorkbench(
    IFieldsApi fields,
    IReadRepository<BlockTemplate> blockTemplates,
    IOptions<ContentDesignerOptions> options,
    PageRenderer renderer)
{
    public static class Codes
    {
        public const string Content = "content";
        public const string JsonLd = "jsonld";
        public const string Amp = "amp";
    }

    /// <summary>The values JSON-LD needs at its top (plan section 4.5).</summary>
    public static IReadOnlyList<string> JsonLdEssentials { get; } = ["headline", "image", "datePublished", "author"];

    public BlockRendererRegistry Registry { get; } = BlockRendererRegistry.Default;

    public RenderSettings Settings => new()
    {
        AssetUrlTemplate = options.Value.AssetUrlTemplate,
        Stylesheets = [.. options.Value.Stylesheets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
    };

    public async Task<PostFields> FieldsAsync(string kind, int kindVersion, IEnumerable<(string Field, int Version)> blocks, CancellationToken ct)
    {
        var kindField = await fields.GetFieldVersionAsync(kind, kindVersion, ct);
        var result = new Dictionary<string, FieldVersionInfo>(StringComparer.Ordinal);
        foreach (var (field, version) in blocks.Distinct())
            if (await fields.GetFieldVersionAsync(field, version, ct) is { } info)
                result[PostFields.Key(field, version)] = info;
        return new PostFields(kindField, result);
    }

    public static IEnumerable<(string, int)> BlockFields(JsonElement document)
    {
        foreach (var node in PostDocuments.Children(document))
        {
            yield return (PostDocuments.Str(node, "schemaId") ?? "", PostDocuments.Int(node, "schemaVersion"));
            foreach (var child in BlockFields(node)) yield return child;
        }
    }

    public async Task<DesignContext> ContextAsync(DesignViews views, ContentPost post, PostFields postFields, CancellationToken ct)
    {
        var templateIds = ViewKinds.All.SelectMany(v => views[v].AllPlacements).Select(p => p.BlockTemplateId).OfType<int>().Distinct().ToList();
        var templates = templateIds.Count == 0 ? [] : await blockTemplates.ListAsync(new BlockTemplatesByIdsSpec(templateIds), ct);

        var styles = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
        foreach (var content in post.Contents)
            if (postFields.Block(content.FieldKey, content.FieldVersion) is { } field)
                styles.TryAdd(content.FieldKey, field.Styles.ToDictionary(s => s.Key, s => s.Values, StringComparer.Ordinal));

        return new DesignContext(
            post.Kind,
            post.KindVersion,
            [.. (postFields.Kind?.Values ?? []).Select(v => new KindValue(v.Key, v.Required, v.SchemaProperty))],
            PostDocuments.Contents(post),
            styles,
            templates.ToDictionary(t => t.Id, t => new BlockTemplateRef(t.FieldKey, t.View)),
            key => Registry.Support(key) is var (has, amp) ? new RendererSupport(has, amp) : new RendererSupport(false, true));
    }

    /// <summary>
    /// Every check of the plan (section 4) for the views asked (all by default): the design rules, then DynamicFields'
    /// validation of the post's document, the JSON-LD essentials and, with the AMP view, the AMP page's own rules.
    /// </summary>
    public async Task<IReadOnlyList<DesignProblem>> CheckAsync(DesignViews views, int designKindVersion, ContentPost post,
        IReadOnlyCollection<ViewKind>? only, string? culture, CancellationToken ct)
        => (await CheckWithJsonLdAsync(views, designKindVersion, post, only, culture, ct)).Problems;

    /// <summary><see cref="CheckAsync"/>, with the JSON-LD DynamicFields built (null when it built none): the studio's JSON-LD panel.</summary>
    public async Task<(IReadOnlyList<DesignProblem> Problems, string? JsonLd)> CheckWithJsonLdAsync(DesignViews views, int designKindVersion,
        ContentPost post, IReadOnlyCollection<ViewKind>? only, string? culture, CancellationToken ct)
    {
        var postFields = await FieldsAsync(post.Kind, post.KindVersion, BlockFields(post.Document), ct);
        var context = await ContextAsync(views, post, postFields, ct);
        var problems = DesignRules.Check(views, designKindVersion, context, only).ToList();

        var tree = PostDocuments.ToFieldsTree(post.Document);
        foreach (var issue in await fields.ValidateAsync(tree, ct))
            problems.Add(new(null, ProblemSeverity.Error, Codes.Content, issue.Path, issue.Message));

        var jsonLd = await fields.BuildJsonLdAsync(tree, JsonLdUrl(post.Permalink), culture, Assets(post.Document, postFields), ct);
        if (jsonLd is null)
            problems.Add(new(null, ProblemSeverity.Warning, Codes.JsonLd, "", "DynamicFields built no JSON-LD for the post."));
        else
            foreach (var missing in MissingEssentials(jsonLd.JsonLd))
                problems.Add(new(null, ProblemSeverity.Error, Codes.JsonLd, "/" + missing, $"The JSON-LD has no '{missing}'; search engines need it."));

        if (only is not { Count: > 0 } || only.Contains(ViewKind.Amp))
        {
            var amp = await RenderAsync(views, Document(post, postFields, culture, null), amp: true, ct);
            foreach (var issue in amp.AmpIssues)
                problems.Add(new(ViewKind.Amp, ProblemSeverity.Error, Codes.Amp, "", $"AMP ({issue.Rule}): {issue.Message}"));
        }
        return (problems, jsonLd?.JsonLd);
    }

    public static IEnumerable<string> MissingEssentials(string jsonLd)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(jsonLd); }
        catch (JsonException) { return JsonLdEssentials; }
        if (node is JsonObject graph && graph["@graph"] is JsonArray items) node = items.FirstOrDefault();
        if (node is JsonArray array) node = array.FirstOrDefault();
        var root = node as JsonObject;
        return JsonLdEssentials.Where(key => root?[key] is null || root[key] is JsonValue v && v.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s));
    }

    public string? CanonicalUrl(string? permalink) =>
        permalink is null ? null
        : Uri.TryCreate(permalink, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? permalink // on Unix "/x" is an absolute file URI
        : options.Value.SiteBaseUrl.TrimEnd('/') + "/" + permalink.TrimStart('/');

    /// <summary>
    /// The canonical URL for JSON-LD: only an absolute http(s) address, which DynamicFields requires. Without
    /// <c>SiteBaseUrl</c> a permalink stays relative, and the JSON-LD then has no url rather than failing.
    /// </summary>
    public string? JsonLdUrl(string? permalink) =>
        CanonicalUrl(permalink) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;

    public string AmpUrl(string publicId) => options.Value.AmpUrlTemplate.Replace("{publicId}", Uri.EscapeDataString(publicId), StringComparison.Ordinal);

    /// <summary>The asset values of the document (root and blocks), with their URLs, for JSON-LD.</summary>
    public IReadOnlyDictionary<string, RenderAsset> Assets(JsonElement document, PostFields postFields)
    {
        var settings = Settings;
        var ids = new List<string>();
        if (postFields.Kind is { } kind)
            ids.AddRange(PostDocuments.AssetIds(PostDocuments.Root(document), kind.Values.Where(v => v.Kind == "Asset").Select(v => v.Key)));

        void Walk(JsonElement node)
        {
            foreach (var child in PostDocuments.Children(node))
            {
                if (postFields.Block(PostDocuments.Str(child, "schemaId") ?? "", PostDocuments.Int(child, "schemaVersion")) is { } field &&
                    child.TryGetProperty("content", out var content))
                    ids.AddRange(PostDocuments.AssetIds(content, field.Values.Where(v => v.Kind == "Asset").Select(v => v.Key)));
                Walk(child);
            }
        }
        Walk(document);
        return ids.Distinct(StringComparer.Ordinal).ToDictionary(id => id, id =>
        {
            var url = settings.Asset(id, null).Url;
            return new RenderAsset(CanonicalUrl(url) ?? url);
        }, StringComparer.Ordinal);
    }

    /// <summary>The renderer's document of a working post (the draft preview and the checks).</summary>
    public RenderDocument Document(ContentPost post, PostFields postFields, string? culture, string? jsonLd)
    {
        var language = culture ?? "en";
        var byNode = post.Contents.GroupBy(c => c.NodeId).ToDictionary(g => g.Key, g => g.First().ContentId, StringComparer.Ordinal);
        return new RenderDocument(post.Kind, language, post.Title, PostDocuments.Root(post.Document).Clone(),
            PostDocuments.Blocks(post.Document, byNode), RenderDocument.DirectionOf(language),
            Description(post.Document), CanonicalUrl(post.Permalink), AmpUrl(post.PublicId), jsonLd, Values(postFields));
    }

    /// <summary>The renderer's document of a published post; <paramref name="nodeIds"/> maps the design's content ids to node ids.</summary>
    public RenderDocument Document(PublishedPost post, IReadOnlyDictionary<string, string> nodeIds, PostFields? postFields, string culture, string? jsonLd)
    {
        var byNode = nodeIds.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        return new RenderDocument(post.Kind, culture, post.Title, PostDocuments.Root(post.Document).Clone(),
            PostDocuments.Blocks(post.Document, byNode), RenderDocument.DirectionOf(culture),
            Description(post.Document), CanonicalUrl(post.Permalink), AmpUrl(post.PublicId), jsonLd,
            postFields is null ? null : Values(postFields));
    }

    private static string? Description(JsonElement document) =>
        PostDocuments.Root(document) is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

    private static IReadOnlyDictionary<string, RenderValue>? Values(PostFields postFields) =>
        postFields.Kind?.Values.ToDictionary(v => v.Key, v => new RenderValue(v.Kind, v.Label, v.Multiple), StringComparer.Ordinal);

    /// <summary>The design in the renderer's terms: each view resolved, block templates loaded.</summary>
    public async Task<RenderDesign> RenderDesignAsync(DesignViews views, CancellationToken ct)
    {
        var ids = ViewKinds.All.SelectMany(v => views.Resolve(v).AllPlacements).Select(p => p.BlockTemplateId).OfType<int>().Distinct().ToList();
        var templates = ids.Count == 0
            ? new Dictionary<int, RenderBlockTemplate>()
            : (await blockTemplates.ListAsync(new BlockTemplatesByIdsSpec(ids), ct))
                .ToDictionary(t => t.Id, t => new RenderBlockTemplate(t.PartOrder, t.HiddenParts, t.Tokens));

        RenderView View(ViewKind kind)
        {
            var view = views.Resolve(kind);
            return new RenderView(
                [.. view.AllRegions.Select(r => new RenderRegion(r.Id, r.Kind.ToString(), r.ParentId, r.Order, r.Hidden, r.Span, r.Tokens))],
                [.. view.AllPlacements.Select(p => new RenderPlacement(p.Part, p.RegionId, p.Order, p.Hidden, p.Span, p.Tokens,
                    p.BlockTemplateId is { } id ? templates.GetValueOrDefault(id) : null))]);
        }
        return new RenderDesign(View(ViewKind.Mobile), View(ViewKind.Tablet), View(ViewKind.Desktop), View(ViewKind.Amp));
    }

    public async Task<RenderedPage> RenderAsync(DesignViews views, RenderDocument document, bool amp, CancellationToken ct)
    {
        var design = await RenderDesignAsync(views, ct);
        return amp ? await renderer.AmpAsync(document, design, Settings) : await renderer.CanonicalAsync(document, design, Settings);
    }

    /// <summary>The draft as a page, with the post's working document and its JSON-LD.</summary>
    public async Task<RenderedPage> RenderDraftAsync(DesignViews views, ContentPost post, bool amp, string? culture, CancellationToken ct)
    {
        var postFields = await FieldsAsync(post.Kind, post.KindVersion, BlockFields(post.Document), ct);
        var jsonLd = await fields.BuildJsonLdAsync(PostDocuments.ToFieldsTree(post.Document), JsonLdUrl(post.Permalink), culture,
            Assets(post.Document, postFields), ct);
        return await RenderAsync(views, Document(post, postFields, culture, jsonLd?.JsonLd), amp, ct);
    }

    /// <summary>
    /// A published design over the published post. DynamicFields may refuse an anonymous caller (no service client
    /// configured): the page then renders without JSON-LD and types root values by their shape.
    /// </summary>
    public async Task<RenderedPage> RenderPublishedAsync(PublishedDesign design, PublishedPost post, bool amp, string culture, CancellationToken ct)
    {
        PostFields? postFields = null;
        JsonLdResult? jsonLd = null;
        try
        {
            postFields = await FieldsAsync(post.Kind, post.KindVersion, BlockFields(post.Document), ct);
            jsonLd = await fields.BuildJsonLdAsync(PostDocuments.ToFieldsTree(post.Document), JsonLdUrl(post.Permalink), culture,
                Assets(post.Document, postFields), ct);
        }
        catch (ExternalApiException) { }
        return await RenderAsync(design.Views, Document(post, design.NodeIds, postFields, culture, jsonLd?.JsonLd), amp, ct);
    }
}
