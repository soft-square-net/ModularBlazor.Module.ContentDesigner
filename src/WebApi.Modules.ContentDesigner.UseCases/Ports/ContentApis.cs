using System.Text.Json.Nodes;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

/// <summary>A content attached to a post, as Content lists it (<c>PostContentDto</c>).</summary>
public sealed record PostContentInfo(
    string ContentId,
    string NodeId,
    string FieldKey,
    int FieldVersion,
    int Order,
    bool Required,
    int Revision,
    int? AcceptedRevision,
    int? PinnedRevision,
    string Stage,
    string? SectionId = null,
    string? AssigneeId = null,
    string? ReviewNote = null,
    bool Drift = false);

/// <summary>
/// A post as the designer needs it (Content's <c>GET /posts/{id}</c>): its kind, its working document
/// (<c>{ schemaId, schemaVersion, content, style, children: [{ nodeId, schemaId, ... }] }</c>) and its contents.
/// </summary>
public sealed record ContentPost(
    int Id,
    string PublicId,
    string? Permalink,
    string Title,
    string Kind,
    int KindVersion,
    int Version,
    JsonElement Document,
    IReadOnlyList<PostContentInfo> Contents);

/// <summary>A published post (Content's anonymous <c>GET /public/{publicId}?culture=</c>).</summary>
public sealed record PublishedPost(
    int Id,
    string PublicId,
    string? Permalink,
    string Title,
    DateTimeOffset PublishedAt,
    string Kind,
    int KindVersion,
    JsonElement Document,
    IReadOnlyList<string> ServedLanguages);

public sealed record FeedEvent(long Id, string Type, int? PostId, string? ContentId, int Version, int? Revision, string? Stage, string By, DateTimeOffset At);

public sealed record FeedPage(IReadOnlyList<FeedEvent> Events, long Last, bool More);

/// <summary>A content of Content's shared library (<c>ContentSummary</c>), as the studio's "add existing content" lists it.</summary>
public sealed record LibraryContent(
    string ContentId,
    string NodeId,
    string FieldKey,
    int FieldVersion,
    string Stage,
    int Revision,
    int? AcceptedRevision,
    string? SectionId,
    string? AssigneeId,
    DateTimeOffset UpdatedAt,
    bool Archived = false);

/// <summary>One library content with its values: Content's <c>PostNode</c> (<c>{ nodeId, schemaId, schemaVersion, content, style, children }</c>).</summary>
public sealed record LibraryContentDetail(LibraryContent Content, JsonElement Node);

/// <summary>The library's filters (Content's <c>GET /contents</c>).</summary>
public sealed record LibraryFilter(string? FieldKey = null, string? Stage = null, string? SectionId = null, int Skip = 0, int Take = 20);

/// <summary>Content's API, through the gateway (with the caller's token and tenant).</summary>
public interface IContentApi
{
    /// <summary>The post, or null when it does not exist (or the caller may not see it).</summary>
    Task<ContentPost?> GetPostAsync(int postId, CancellationToken ct);

    /// <summary>The published post in a culture, or null when it is not published.</summary>
    Task<PublishedPost?> GetPublishedAsync(string publicId, string? culture, CancellationToken ct);

    /// <summary>The post's change feed after an event id (hot reload).</summary>
    Task<FeedPage> GetPostEventsAsync(int postId, long after, CancellationToken ct);

    /// <summary>The shared library: the contents the caller may see, newest change first (archived ones left out).</summary>
    Task<IReadOnlyList<LibraryContent>> ListContentsAsync(LibraryFilter filter, CancellationToken ct);

    /// <summary>One content with its values, or null when it does not exist (or the caller may not see it).</summary>
    Task<LibraryContentDetail?> GetContentAsync(string contentId, CancellationToken ct);

    /// <summary>Uses a library content in the post (Content's <c>POST /posts/{id}/contents</c>), at the post's version.</summary>
    Task AttachContentAsync(int postId, int postVersion, string contentId, int? index, bool required, CancellationToken ct);
}

public sealed record FieldValueInfo(string Key, string Label, string Kind, bool Required, bool Multiple, string? SchemaProperty);

public sealed record FieldStyleInfo(string Key, IReadOnlyList<string> Values, bool Responsive);

/// <summary>A published field version (DynamicFields' <c>GET /fields/{key}/versions/{n}</c>): its values and style tokens.</summary>
public sealed record FieldVersionInfo(string Key, int Version, string SchemaType, IReadOnlyList<FieldValueInfo> Values, IReadOnlyList<FieldStyleInfo> Styles);

public sealed record ContentIssue(string Path, string Message);

public sealed record JsonLdResult(string JsonLd, IReadOnlyList<ContentIssue> Skipped);

/// <summary>DynamicFields' API, through the gateway.</summary>
public interface IFieldsApi
{
    /// <summary>A published field version (immutable, so cached), or null.</summary>
    Task<FieldVersionInfo?> GetFieldVersionAsync(string key, int version, CancellationToken ct);

    /// <summary>The issues of a content tree (<c>POST /content/validate</c>); empty when valid.</summary>
    Task<IReadOnlyList<ContentIssue>> ValidateAsync(JsonNode root, CancellationToken ct);

    /// <summary>The tree's JSON-LD (<c>POST /content/jsonld</c>), or null when DynamicFields cannot be read.</summary>
    Task<JsonLdResult?> BuildJsonLdAsync(JsonNode root, string? url, string? language, IReadOnlyDictionary<string, RenderAsset>? assets, CancellationToken ct);
}

/// <summary>Another module's API answered with an error, or could not be reached (<see cref="Status"/> null).</summary>
public sealed class ExternalApiException(string service, int? status, string message)
    : Exception($"{service}: {(status is { } s ? $"HTTP {s}" : "unreachable")}: {message}")
{
    public string Service { get; } = service;
    public int? Status { get; } = status;
    public string Detail { get; } = message;

    public Result<T> ToResult<T>() => Status switch
    {
        401 => Result<T>.Unauthorized($"{Service} did not accept the caller ({Detail})."),
        403 => Result<T>.Forbidden($"{Service} refused: {Detail}"),
        404 => Result<T>.NotFound($"{Service}: {Detail}"),
        409 => Result<T>.Conflict($"{Service}: {Detail}"),
        400 or 422 => Result<T>.Invalid(new ValidationError(Service, Detail)),
        _ => Result<T>.Unavailable($"{Service} is not available: {Detail}")
    };
}
