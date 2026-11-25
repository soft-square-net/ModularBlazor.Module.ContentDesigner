using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UnitTests;

/// <summary>A news post of the DynamicFields starter kind, as Content and DynamicFields would describe it.</summary>
public static class Samples
{
    public const string Heading = "11111111-1111-1111-1111-111111111111";
    public const string Paragraph = "22222222-2222-2222-2222-222222222222";
    public const string Image = "33333333-3333-3333-3333-333333333333";
    public const string Gallery = "44444444-4444-4444-4444-444444444444";
    public const string Video = "55555555-5555-5555-5555-555555555555";

    public const string DocumentJson = """
    {
      "schemaId": "post.news", "schemaVersion": 2,
      "content": { "headline": "Final day at the venue", "description": "The last day, in pictures.", "image": "asset-hero",
                   "author": "Mona", "datePublished": "2026-11-20T09:00:00Z" },
      "style": {},
      "children": [
        { "nodeId": "n-heading", "schemaId": "block.heading", "schemaVersion": 1, "content": { "text": "The morning" }, "style": { "level": "h2" }, "children": [] },
        { "nodeId": "n-paragraph", "schemaId": "block.paragraph", "schemaVersion": 1, "content": { "text": "Doors opened at nine.\n\nThe crowd <b>was</b> large." }, "style": {}, "children": [] },
        { "nodeId": "n-image", "schemaId": "block.image", "schemaVersion": 1,
          "content": { "image": "asset-1", "alt": "The stage", "caption": "The main stage", "credit": "Photo: Ali" },
          "style": { "aspect": "16-9", "width": { "base": "full", "md": "wide" } }, "children": [] },
        { "nodeId": "n-gallery", "schemaId": "block.gallery", "schemaVersion": 1, "content": { "title": "More" }, "style": { "columns": { "base": "2", "lg": "3" } },
          "children": [
            { "nodeId": "g1", "schemaId": "block.image", "schemaVersion": 1, "content": { "image": "asset-2", "alt": "One" }, "style": {}, "children": [] },
            { "nodeId": "g2", "schemaId": "block.image", "schemaVersion": 1, "content": { "image": "asset-3", "alt": "Two" }, "style": {}, "children": [] } ] },
        { "nodeId": "n-video", "schemaId": "block.video", "schemaVersion": 1,
          "content": { "video": "https://www.youtube.com/watch?v=abc123", "title": "Highlights", "description": "Two minutes", "thumbnail": "asset-4", "uploadDate": "2026-11-20" },
          "style": { "aspect": "16-9" }, "children": [] }
      ]
    }
    """;

    public static JsonElement Document => JsonDocument.Parse(DocumentJson).RootElement.Clone();

    public static ContentPost Post(int kindVersion = 2) => new(7, "pub-7", "/news/final-day", "Final day at the venue", "post.news", kindVersion, 3, Document,
    [
        new(Heading, "n-heading", "block.heading", 1, 0, false, 1, 1, null, "accepted"),
        new(Paragraph, "n-paragraph", "block.paragraph", 1, 1, true, 2, 2, null, "accepted"),
        new(Image, "n-image", "block.image", 1, 2, false, 1, 1, null, "accepted"),
        new(Gallery, "n-gallery", "block.gallery", 1, 3, false, 1, 1, null, "accepted"),
        new(Video, "n-video", "block.video", 1, 4, false, 1, 1, null, "accepted")
    ]);

    public static FieldVersionInfo Kind { get; } = new("post.news", 2, "NewsArticle",
    [
        new("headline", "Headline", "Text", true, false, "headline"),
        new("description", "Summary", "Text", true, false, "description"),
        new("image", "Main image", "Asset", true, false, "image"),
        new("author", "Author", "Text", true, false, "author"),
        new("datePublished", "Published", "DateTime", true, false, "datePublished")
    ], []);

    public static FieldVersionInfo Block(string key) => key switch
    {
        "block.heading" => new(key, 1, "WebPageElement", [new("text", "Heading", "Text", true, false, "headline")],
            [new("level", ["h2", "h3", "h4", "h5", "h6"], false), new("align", ["start", "center", "end"], false)]),
        "block.image" => new(key, 1, "ImageObject", [new("image", "Image", "Asset", true, false, "contentUrl"), new("alt", "Alt", "Text", true, false, "description")],
            [new("width", ["content", "wide", "full", "half", "third"], true), new("aspect", ["auto", "1-1", "4-3", "3-2", "16-9", "21-9"], true), new("align", ["start", "center", "end"], false)]),
        "block.gallery" => new(key, 1, "ImageGallery", [new("title", "Title", "Text", false, false, "name")],
            [new("columns", ["1", "2", "3", "4", "6"], true), new("spacing", ["none", "sm", "md", "lg", "xl"], true)]),
        "block.video" => new(key, 1, "VideoObject", [new("video", "Video", "Asset", true, false, "contentUrl"), new("thumbnail", "Thumbnail", "Asset", true, false, "thumbnailUrl")],
            [new("width", ["content", "wide", "full", "half", "third"], true), new("aspect", ["auto", "1-1", "4-3", "3-2", "16-9", "21-9"], true)]),
        _ => new(key, 1, "WebPageElement", [new("text", "Text", "RichText", true, false, "text")],
            [new("align", ["start", "center", "end"], false), new("size", ["sm", "md", "lg", "xl"], true), new("tone", ["default", "muted", "accent", "inverse"], false),
             new("spacing", ["none", "sm", "md", "lg", "xl"], true)])
    };

    /// <summary>Designs hold lists, which records compare by reference; tests compare them as JSON.</summary>
    public static void ShouldMatch(this ViewDesign actual, ViewDesign expected) =>
        JsonSerializer.Serialize(actual.Checked("actual")).ShouldBe(JsonSerializer.Serialize(expected.Checked("expected")));

    public static Dictionary<string, string> Tokens(params (string Key, string Value)[] tokens) =>
        tokens.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);

    /// <summary>A three-region design that shows every required value and content: a header, the body, an aside.</summary>
    public static DesignViews Design()
    {
        IReadOnlyList<Region> regions =
        [
            new("page", RegionKind.Section, Tokens: Tokens(("layout", "standard"))),
            new("head", RegionKind.Row, "page", 0),
            new("body", RegionKind.Row, "page", 1, Tokens: Tokens(("columns", "1"))),
        ];
        IReadOnlyList<Placement> mobile =
        [
            new("prop:headline", "head", 0),
            new("prop:description", "head", 1),
            new("prop:image", "head", 2),
            new("prop:author", "head", 3),
            new("prop:datePublished", "head", 4),
            new("content:" + Image, "body", 0),
            new("items", "body", 1)
        ];
        var desktop = new ViewDesign(
            [.. regions.Select(r => r.Id == "body" ? r with { Tokens = Tokens(("columns", "3")) } : r)],
            [.. mobile.Select(p => p.Part == "content:" + Image ? p with { Order = 2, Span = "2" } : p.Part == "items" ? p with { Order = 0 } : p)]);
        return new DesignViews(new ViewDesign(regions, mobile), ViewDesign.Inherit(ViewKind.Mobile), desktop, ViewDesign.Inherit(ViewKind.Mobile));
    }
}
