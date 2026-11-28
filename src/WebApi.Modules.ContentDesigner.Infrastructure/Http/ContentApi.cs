using System.Text.Json;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
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
        string? ReviewNote = null, bool Drift = false, Guid? UnitId = null);

    private sealed record PostWire(int Id, string Title, int Version, string PublicId, string? Permalink, string SchemaId, int SchemaVersion,
        JsonElement Document, List<PostContentWire>? Contents, string? PostKind = null, string? PressId = null, string? CategoryId = null,
        List<string>? Categories = null, Guid? UnitId = null, string? UnitPath = null, string? UrlPrefix = null);

    private sealed record PublishedWire(int Id, string PublicId, string? Permalink, string Title, DateTimeOffset PublishedAt,
        string SchemaId, int SchemaVersion, JsonElement Document, List<string>? ServedLanguages, string? PostKind = null);

    private sealed record FeedEventWire(long Id, string Type, int? PostId, Guid? ContentId, int Version, int? Revision, string? Stage, string By, DateTimeOffset At,
        string? Detail = null);

    private sealed record ContentSummaryWire(Guid Id, string NodeId, string FieldKey, int FieldVersion, string? SectionId, string? AssigneeId,
        string Stage, int Revision, int? AcceptedRevision, DateTimeOffset UpdatedAt, bool Archived, Guid? UnitId = null);

    private sealed record ContentDetailWire(Guid Id, string NodeId, string FieldKey, int FieldVersion, JsonElement Values, string? SectionId,
        string? AssigneeId, string Stage, int Revision, int? AcceptedRevision, DateTimeOffset UpdatedAt, DateTimeOffset? ArchivedAt,
        Guid? UnitId = null);

    private sealed record CategoryWire(string Id, string? Name, string? NameEn, string? NameAr);

    private sealed record FeedPageWire(List<FeedEventWire>? Events, long Last, bool More);

    public async Task<ContentPost?> GetPostAsync(int postId, CancellationToken ct)
    {
        var post = await Gateway.ReadAsync<PostWire>(Service, await gateway.SendAsync(Service, HttpMethod.Get, $"/api/content/posts/{postId}", null, ct), ct);
        if (post is null) return null;
        var schema = DocumentSchema(post.Document, post.SchemaId, post.SchemaVersion);
        return new ContentPost(post.Id, post.PublicId, post.Permalink, post.Title, Kind(post.PostKind, schema.Id), schema.Version, post.Version, post.Document,
            [.. (post.Contents ?? []).OrderBy(c => c.Order).Select(c => new PostContentInfo(c.ContentId.ToString(), c.NodeId, c.FieldKey,
                c.FieldVersion, c.Order, c.Required, c.Revision, c.AcceptedRevision, c.PinnedRevision, c.Stage, c.SectionId, c.AssigneeId,
                c.ReviewNote, c.Drift, c.UnitId))],
            post.PressId, Categories(post.CategoryId, post.Categories), post.UnitId, post.UnitPath, post.UrlPrefix, schema.Id);
    }

    public async Task<PublishedPost?> GetPublishedAsync(string publicId, string? culture, CancellationToken ct)
    {
        var path = $"/api/content/public/{Uri.EscapeDataString(publicId)}" + (culture is { Length: > 0 } ? $"?culture={Uri.EscapeDataString(culture)}" : "");
        var post = await Gateway.ReadAsync<PublishedWire>(Service, await gateway.SendAsync(Service, HttpMethod.Get, path, null, ct), ct);
        if (post is null) return null;
        var schema = DocumentSchema(post.Document, post.SchemaId, post.SchemaVersion);
        return new PublishedPost(post.Id, post.PublicId, post.Permalink, post.Title, post.PublishedAt, Kind(post.PostKind, schema.Id), schema.Version, post.Document,
            post.ServedLanguages ?? [], schema.Id);
    }

    public async Task<FeedPage> GetPostEventsAsync(int postId, long after, CancellationToken ct)
    {
        var page = await Gateway.ReadAsync<FeedPageWire>(Service,
            await gateway.SendAsync(Service, HttpMethod.Get, $"/api/content/posts/{postId}/events?after={after}", null, ct), ct);
        return page is null
            ? new FeedPage([], after, false)
            : new FeedPage([.. (page.Events ?? []).Select(e => new FeedEvent(e.Id, e.Type, e.PostId, e.ContentId?.ToString(), e.Version, e.Revision, e.Stage, e.By, e.At, e.Detail))],
                page.Last, page.More);
    }

    public async Task<IReadOnlyList<LibraryContent>> ListContentsAsync(LibraryFilter filter, CancellationToken ct)
    {
        var query = new List<string> { $"skip={Math.Max(0, filter.Skip)}", $"take={Math.Clamp(filter.Take, 1, 200)}" };
        if (filter.FieldKey is { Length: > 0 } field) query.Add("fieldKey=" + Uri.EscapeDataString(field));
        if (filter.Stage is { Length: > 0 } stage) query.Add("stage=" + Uri.EscapeDataString(stage));
        if (filter.SectionId is { Length: > 0 } section) query.Add("sectionId=" + Uri.EscapeDataString(section));
        if (filter.UnitId is { } unit) query.Add($"unitId={unit}");
        var items = await Gateway.ReadAsync<List<ContentSummaryWire>>(Service,
            await gateway.SendAsync(Service, HttpMethod.Get, "/api/content/contents?" + string.Join('&', query), null, ct), ct);
        return [.. (items ?? []).Select(c => new LibraryContent(c.Id.ToString(), c.NodeId, c.FieldKey, c.FieldVersion, c.Stage, c.Revision,
            c.AcceptedRevision, c.SectionId, c.AssigneeId, c.UpdatedAt, c.Archived, c.UnitId))];
    }

    public async Task<IReadOnlyList<CategoryName>> ListCategoriesAsync(string? pressId, CancellationToken ct)
    {
        var path = "/api/content/categories" + (pressId is { Length: > 0 } ? "?pressId=" + Uri.EscapeDataString(pressId) : "");
        var items = await Gateway.ReadAsync<List<CategoryWire>>(Service, await gateway.SendAsync(Service, HttpMethod.Get, path, null, ct), ct);
        return [.. (items ?? []).Select(c => new CategoryName(c.Id, c.NameEn ?? c.Name ?? c.Id, c.NameAr ?? c.Name ?? c.Id))];
    }

    public async Task<LibraryContentDetail?> GetContentAsync(string contentId, CancellationToken ct)
    {
        if (!Guid.TryParse(contentId, out var id)) return null;
        var c = await Gateway.ReadAsync<ContentDetailWire>(Service, await gateway.SendAsync(Service, HttpMethod.Get, $"/api/content/contents/{id}", null, ct), ct);
        return c is null ? null : new LibraryContentDetail(new LibraryContent(c.Id.ToString(), c.NodeId, c.FieldKey, c.FieldVersion, c.Stage,
            c.Revision, c.AcceptedRevision, c.SectionId, c.AssigneeId, c.UpdatedAt, c.ArchivedAt is not null, c.UnitId), c.Values);
    }

    public async Task AttachContentAsync(int postId, int postVersion, string contentId, int? index, bool required, CancellationToken ct)
    {
        if (!Guid.TryParse(contentId, out var id)) throw new ExternalApiException(Service, 404, $"'{contentId}' is not a content id.");
        using var response = await gateway.SendAsync(Service, HttpMethod.Post, $"/api/content/posts/{postId}/contents",
            new { version = postVersion, contentId = id, index, required }, ct);
        if (!response.IsSuccessStatusCode) throw await Gateway.FailureAsync(Service, response, ct);
    }

    /// <summary>The post's kind: Content's <c>postKind</c> (a root post field key), else the document's schema id.</summary>
    private static string Kind(string? postKind, string documentSchemaId) =>
        PostKindKeys.Normalize(string.IsNullOrWhiteSpace(postKind) ? documentSchemaId : postKind);

    /// <summary>The post's categories, the primary one first.</summary>
    private static List<string> Categories(string? primary, List<string>? all) =>
        [.. (string.IsNullOrEmpty(primary) ? [] : new[] { primary }).Concat(all ?? []).Distinct(StringComparer.Ordinal)];

    /// <summary>The document's root field and version (the post's own schema id when the document has none).</summary>
    private static (string Id, int Version) DocumentSchema(JsonElement document, string schemaId, int schemaVersion) =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty("schemaId", out var id) && id.GetString() is { Length: > 0 } key
            ? (key, document.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var n) ? n : schemaVersion)
            : (schemaId, schemaVersion);
}
