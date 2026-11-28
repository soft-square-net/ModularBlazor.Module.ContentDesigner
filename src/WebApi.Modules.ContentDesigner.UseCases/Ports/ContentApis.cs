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
    bool Drift = false,
    Guid? UnitId = null);

/// <summary>
/// A post as the designer needs it (Content's <c>GET /posts/{id}</c>): its kind (a DynamicFields root post field key),
/// its working document (<c>{ schemaId, schemaVersion, content, style, children: [{ nodeId, schemaId, ... }] }</c>),
/// its contents, and how it is filed: its press, its categories (the primary one first), and the Organization unit the
/// workflow put it in, with the unit's path (null: no unit yet).
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
    IReadOnlyList<PostContentInfo> Contents,
    string? PressId = null,
    IReadOnlyList<string>? Categories = null,
    Guid? UnitId = null,
    string? UnitPath = null,
    string? UrlPrefix = null,
    string? DocumentSchemaId = null)
{
    /// <summary>
    /// The field whose version the document is written against: its root schema id. It is the kind itself, except for a
    /// legacy document (made from a title and a body), which has no DynamicFields field.
    /// </summary>
    public string DocumentField => DocumentSchemaId is { Length: > 0 } schema ? schema : Kind;

    /// <summary>Content's schema of a document made from a title and a body only: no values of the post's kind.</summary>
    public const string LegacyRoot = "legacy.basic";

    public bool HasLegacyRoot => DocumentField == LegacyRoot;
}

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
    IReadOnlyList<string> ServedLanguages,
    string? DocumentSchemaId = null)
{
    /// <inheritdoc cref="ContentPost.DocumentField"/>
    public string DocumentField => DocumentSchemaId is { Length: > 0 } schema ? schema : Kind;
}

/// <summary>One row of Content's change feed. <see cref="Detail"/> says more about some rows (a refused validation: its problems).</summary>
public sealed record FeedEvent(long Id, string Type, int? PostId, string? ContentId, int Version, int? Revision, string? Stage, string By, DateTimeOffset At,
    string? Detail = null);

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
    bool Archived = false,
    Guid? UnitId = null);

/// <summary>One library content with its values: Content's <c>PostNode</c> (<c>{ nodeId, schemaId, schemaVersion, content, style, children }</c>).</summary>
public sealed record LibraryContentDetail(LibraryContent Content, JsonElement Node);

/// <summary>The library's filters (Content's <c>GET /contents</c>).</summary>
public sealed record LibraryFilter(string? FieldKey = null, string? Stage = null, string? SectionId = null, int Skip = 0, int Take = 20,
    Guid? UnitId = null);

/// <summary>A Content category with its names (Content's <c>GET /categories</c>).</summary>
public sealed record CategoryName(string Id, string NameEn, string NameAr);

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

    /// <summary>The categories of a press, with their names.</summary>
    Task<IReadOnlyList<CategoryName>> ListCategoriesAsync(string? pressId, CancellationToken ct);

    /// <summary>One content with its values, or null when it does not exist (or the caller may not see it).</summary>
    Task<LibraryContentDetail?> GetContentAsync(string contentId, CancellationToken ct);

    /// <summary>Uses a library content in the post (Content's <c>POST /posts/{id}/contents</c>), at the post's version.</summary>
    Task AttachContentAsync(int postId, int postVersion, string contentId, int? index, bool required, CancellationToken ct);
}

public sealed record FieldValueInfo(string Key, string Label, string Kind, bool Required, bool Multiple, string? SchemaProperty);

public sealed record FieldStyleInfo(string Key, IReadOnlyList<string> Values, bool Responsive);

/// <summary>
/// A published field version (DynamicFields' <c>GET /fields/{key}/versions/{n}</c>): its values and style tokens, and
/// its title ("Video"), when the snapshot has one.
/// </summary>
public sealed record FieldVersionInfo(string Key, int Version, string SchemaType, IReadOnlyList<FieldValueInfo> Values, IReadOnlyList<FieldStyleInfo> Styles,
    string? Title = null);

public sealed record ContentIssue(string Path, string Message, bool Warning = false);

public sealed record JsonLdResult(string JsonLd, IReadOnlyList<ContentIssue> Skipped);

/// <summary>A published root post field (DynamicFields' <c>GET /catalog/root-post-fields</c>): a post kind and its static URL prefix.</summary>
public sealed record PostKindInfo(string Key, string Title, string? Description, string SchemaType, string? UrlPrefix, int Version);

/// <summary>DynamicFields' API, through the gateway.</summary>
public interface IFieldsApi
{
    /// <summary>A published field version (immutable, so cached), or null.</summary>
    Task<FieldVersionInfo?> GetFieldVersionAsync(string key, int version, CancellationToken ct);

    /// <summary>The post kinds: the published root post fields, with their URL prefixes.</summary>
    Task<IReadOnlyList<PostKindInfo>> ListPostKindsAsync(CancellationToken ct);

    /// <summary>The issues of a content tree (<c>POST /content/validate</c>); empty when valid.</summary>
    Task<IReadOnlyList<ContentIssue>> ValidateAsync(JsonNode root, CancellationToken ct);

    /// <summary>The tree's JSON-LD (<c>POST /content/jsonld</c>), or null when DynamicFields cannot be read.</summary>
    Task<JsonLdResult?> BuildJsonLdAsync(JsonNode root, string? url, string? language, IReadOnlyDictionary<string, RenderAsset>? assets, CancellationToken ct);
}

/// <summary>An Organization unit's names.</summary>
public sealed record UnitName(Guid Id, string NameEn, string NameAr);

/// <summary>A member of a unit: the token subject (Content's assignee ids) and the name to show.</summary>
public sealed record UnitMemberName(string UserId, string DisplayName);

/// <summary>
/// Organization's local directory, through the gateway. Names only, and Organization is optional: both answer empty
/// when it is not installed, cannot be reached or refuses the caller, and the studio then shows ids.
/// </summary>
public interface IOrganizationApi
{
    Task<IReadOnlyList<UnitName>> ListUnitsAsync(CancellationToken ct);

    Task<IReadOnlyList<UnitMemberName>> MembersAsync(Guid unitId, CancellationToken ct);
}

/// <summary>One problem Content (DynamicFields) found: a JSON Pointer into what it checked, the message, and its node.</summary>
public sealed record ContentValidationProblem(string Path, string Message, string? NodeId);

/// <summary>
/// DynamicFields' problems that made Content refuse a step (422), carried through this module as an Ardalis
/// <c>Invalid</c> result whose error code is <see cref="Code"/> (or <c>Code:nodeId</c>), which the endpoints answer with
/// 422 and a <c>problems</c> list, as Content does.
/// </summary>
public static class ProblemErrors
{
    public const string Code = "content.validation-failed";

    public static ValidationError Error(ContentValidationProblem p) =>
        new(p.Path, p.Message, p.NodeId is null ? Code : $"{Code}:{p.NodeId}", ValidationSeverity.Error);

    /// <summary>The problems in a result's validation errors; empty for any other result.</summary>
    public static IReadOnlyList<ContentValidationProblem> Of(IEnumerable<ValidationError> errors) =>
        [.. errors.Where(e => e.ErrorCode == Code || (e.ErrorCode?.StartsWith(Code + ":", StringComparison.Ordinal) ?? false))
            .Select(e => new ContentValidationProblem(e.Identifier ?? "", e.ErrorMessage, e.ErrorCode!.Length > Code.Length ? e.ErrorCode[(Code.Length + 1)..] : null))];
}

/// <summary>Another module's API answered with an error, or could not be reached (<see cref="Status"/> null).</summary>
public sealed class ExternalApiException(string service, int? status, string message, IReadOnlyList<ContentValidationProblem>? problems = null)
    : Exception($"{service}: {(status is { } s ? $"HTTP {s}" : "unreachable")}: {message}")
{
    /// <summary>The <c>problems</c> of a 422, when the answer listed them.</summary>
    public IReadOnlyList<ContentValidationProblem> Problems { get; } = problems ?? [];

    public string Service { get; } = service;
    public int? Status { get; } = status;
    public string Detail { get; } = message;

    public Result<T> ToResult<T>() => Status switch
    {
        401 => Result<T>.Unauthorized($"{Service} did not accept the caller ({Detail})."),
        403 => Result<T>.Forbidden($"{Service} refused: {Detail}"),
        404 => Result<T>.NotFound($"{Service}: {Detail}"),
        409 => Result<T>.Conflict($"{Service}: {Detail}"),
        422 when Problems.Count > 0 => Result<T>.Invalid([.. Problems.Select(ProblemErrors.Error)]),
        400 or 422 => Result<T>.Invalid(new ValidationError(Service, Detail)),
        _ => Result<T>.Unavailable($"{Service} is not available: {Detail}")
    };
}
