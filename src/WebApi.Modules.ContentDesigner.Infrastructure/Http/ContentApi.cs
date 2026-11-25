using System.Text.Json;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;

/// <summary>
/// <see cref="IContentApi"/> over Content's HTTP API (<c>/api/content</c>). The wire types hold only the fields this
/// module reads, so Content can grow its responses freely; no package of Content is referenced.
/// </summary>
public sealed class ContentApi(Gateway gateway) : IContentApi
{
    private const string Service = "Content";

    private sealed record PostContentWire(Guid ContentId, int Order, bool Required, int? PinnedRevision, string NodeId, string FieldKey,
        int FieldVersion, string Stage, int Revision, int? AcceptedRevision, string? SectionId = null, string? AssigneeId = null,
        string? ReviewNote = null, bool Drift = false);

    private sealed record PostWire(int Id, string Title, int Version, string PublicId, string? Permalink, string SchemaId, int SchemaVersion,
        JsonElement Document, List<PostContentWire>? Contents);

    private sealed record PublishedWire(int Id, string PublicId, string? Permalink, string Title, DateTimeOffset PublishedAt,
        string SchemaId, int SchemaVersion, JsonElement Document, List<string>? ServedLanguages);

    private sealed record FeedEventWire(long Id, string Type, int? PostId, Guid? ContentId, int Version, int? Revision, string? Stage, string By, DateTimeOffset At);

    private sealed record FeedPageWire(List<FeedEventWire>? Events, long Last, bool More);

    public async Task<ContentPost?> GetPostAsync(int postId, CancellationToken ct)
    {
        var post = await Gateway.ReadAsync<PostWire>(Service, await gateway.SendAsync(Service, HttpMethod.Get, $"/api/content/posts/{postId}", null, ct), ct);
        if (post is null) return null;
        var kind = DocumentKind(post.Document, post.SchemaId, post.SchemaVersion);
        return new ContentPost(post.Id, post.PublicId, post.Permalink, post.Title, kind.Key, kind.Version, post.Version, post.Document,
            [.. (post.Contents ?? []).OrderBy(c => c.Order).Select(c => new PostContentInfo(c.ContentId.ToString(), c.NodeId, c.FieldKey,
                c.FieldVersion, c.Order, c.Required, c.Revision, c.AcceptedRevision, c.PinnedRevision, c.Stage, c.SectionId, c.AssigneeId,
                c.ReviewNote, c.Drift))]);
    }

    public async Task<PublishedPost?> GetPublishedAsync(string publicId, string? culture, CancellationToken ct)
    {
        var path = $"/api/content/public/{Uri.EscapeDataString(publicId)}" + (culture is { Length: > 0 } ? $"?culture={Uri.EscapeDataString(culture)}" : "");
        var post = await Gateway.ReadAsync<PublishedWire>(Service, await gateway.SendAsync(Service, HttpMethod.Get, path, null, ct), ct);
        if (post is null) return null;
        var kind = DocumentKind(post.Document, post.SchemaId, post.SchemaVersion);
        return new PublishedPost(post.Id, post.PublicId, post.Permalink, post.Title, post.PublishedAt, kind.Key, kind.Version, post.Document,
            post.ServedLanguages ?? []);
    }

    public async Task<FeedPage> GetPostEventsAsync(int postId, long after, CancellationToken ct)
    {
        var page = await Gateway.ReadAsync<FeedPageWire>(Service,
            await gateway.SendAsync(Service, HttpMethod.Get, $"/api/content/posts/{postId}/events?after={after}", null, ct), ct);
        return page is null
            ? new FeedPage([], after, false)
            : new FeedPage([.. (page.Events ?? []).Select(e => new FeedEvent(e.Id, e.Type, e.PostId, e.ContentId?.ToString(), e.Version, e.Revision, e.Stage, e.By, e.At))],
                page.Last, page.More);
    }

    /// <summary>The post's kind: its document's root field (the post's own schema id when the document has none).</summary>
    private static (string Key, int Version) DocumentKind(JsonElement document, string schemaId, int schemaVersion) =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty("schemaId", out var id) && id.GetString() is { Length: > 0 } key
            ? (key, document.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var n) ? n : schemaVersion)
            : (schemaId, schemaVersion);
}
