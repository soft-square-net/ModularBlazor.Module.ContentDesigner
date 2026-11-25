using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>The post the studio opened, as Content has it now.</summary>
public sealed record WorkspacePost(int Id, string PublicId, string Title, string Kind, int KindVersion, int Version, string? Permalink);

/// <summary>A root value of the post's kind: its key, label, DynamicFields kind and whether the kind requires it.</summary>
public sealed record WorkspaceValue(string Key, string Label, string Kind, bool Required, string? SchemaProperty);

/// <summary>A value of a block field (its key and label): the inner parts a block template orders and hides.</summary>
public sealed record WorkspaceBlockValue(string Key, string Label);

/// <summary>
/// One content of the post for the studio's Contents tray: Content's view of it (stage, section, assignee, revisions),
/// and the label of its block field (the field's title, or a label made from its key when the snapshot has none).
/// </summary>
public sealed record WorkspaceContent(
    string ContentId,
    string NodeId,
    string FieldKey,
    int FieldVersion,
    string? FieldLabel,
    int Order,
    bool Required,
    string Stage,
    string? SectionId,
    string? AssigneeId,
    int Revision,
    int? AcceptedRevision,
    int? PinnedRevision,
    string? ReviewNote,
    bool Drift);

/// <summary>
/// Everything the studio needs to open a post, in one call: the post, its contents, the renderer's document (the canvas
/// renders it with ContentDesigner.Rendering, live), the kind's root values, the style tokens each content's field
/// allows (field key → token → values) and the design (null when the post has none yet: the studio then opens one).
/// </summary>
public sealed record WorkspaceDto(
    WorkspacePost Post,
    IReadOnlyList<WorkspaceContent> Contents,
    RenderDocument Document,
    IReadOnlyList<WorkspaceValue> KindValues,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> FieldStyles,
    PostDesignDto? Design,
    IReadOnlyDictionary<string, IReadOnlyList<WorkspaceBlockValue>>? BlockFields = null);

/// <summary>
/// The studio's workspace of a post (<see cref="WorkspaceDto"/>): Content's post and DynamicFields' field versions, through
/// the gateway, with the caller's token. The document carries no JSON-LD; the checks build it.
/// </summary>
public sealed record GetWorkspaceQuery(int PostId, string? Culture = null) : IQuery<Result<WorkspaceDto>>;

public sealed class GetWorkspaceHandler(IReadRepository<PostDesign> designs, IContentApi content, DesignWorkbench workbench)
    : IQueryHandler<GetWorkspaceQuery, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(GetWorkspaceQuery query, CancellationToken ct) =>
        await DesignResults.Calling<WorkspaceDto>(async () =>
        {
            if (await content.GetPostAsync(query.PostId, ct) is not { } post)
                return Result<WorkspaceDto>.NotFound(DesignResults.NoPost(query.PostId));

            var postFields = await workbench.FieldsAsync(post.Kind, post.KindVersion,
                DesignWorkbench.BlockFields(post.Document).Concat(post.Contents.Select(c => (c.FieldKey, c.FieldVersion))), ct);
            var design = await designs.FirstOrDefaultAsync(new DesignByPostSpec(query.PostId), ct);

            var styles = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
            var blockFields = new Dictionary<string, IReadOnlyList<WorkspaceBlockValue>>(StringComparer.Ordinal);
            foreach (var c in post.Contents)
                if (postFields.Block(c.FieldKey, c.FieldVersion) is { } field)
                {
                    styles.TryAdd(c.FieldKey, field.Styles.ToDictionary(s => s.Key, s => s.Values, StringComparer.Ordinal));
                    blockFields.TryAdd(c.FieldKey, [.. field.Values.Select(v => new WorkspaceBlockValue(v.Key, v.Label))]);
                }

            return new WorkspaceDto(
                new WorkspacePost(post.Id, post.PublicId, post.Title, post.Kind, post.KindVersion, post.Version, post.Permalink),
                [.. post.Contents.Select(c => new WorkspaceContent(c.ContentId, c.NodeId, c.FieldKey, c.FieldVersion,
                    postFields.Block(c.FieldKey, c.FieldVersion)?.Title ?? Label(c.FieldKey),
                    c.Order, c.Required, c.Stage, c.SectionId, c.AssigneeId, c.Revision, c.AcceptedRevision, c.PinnedRevision,
                    c.ReviewNote, c.Drift))],
                workbench.Document(post, postFields, query.Culture, null),
                [.. (postFields.Kind?.Values ?? []).Select(v => new WorkspaceValue(v.Key, v.Label, v.Kind, v.Required, v.SchemaProperty))],
                styles,
                design is null ? null : PostDesignDto.From(design),
                blockFields);
        });

    /// <summary>"block.video" → "Video": DynamicFields' field versions carry no title, so the key names it.</summary>
    internal static string Label(string fieldKey)
    {
        var name = fieldKey[(fieldKey.LastIndexOf('.') + 1)..].Replace('-', ' ').Replace('_', ' ');
        return name.Length == 0 ? fieldKey : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

/// <summary>The post's change feed after an event id (Content's, through the gateway): the studio's hot reload.</summary>
public sealed record GetPostEventsQuery(int PostId, long After = 0) : IQuery<Result<FeedPage>>;

public sealed class GetPostEventsHandler(IContentApi content) : IQueryHandler<GetPostEventsQuery, Result<FeedPage>>
{
    public async ValueTask<Result<FeedPage>> Handle(GetPostEventsQuery query, CancellationToken ct) =>
        query.After < 0
            ? Result<FeedPage>.Invalid(new ValidationError(nameof(query.After), "'after' is an event id, 0 or more."))
            : await DesignResults.Calling<FeedPage>(async () => await content.GetPostEventsAsync(query.PostId, query.After, ct));
}
