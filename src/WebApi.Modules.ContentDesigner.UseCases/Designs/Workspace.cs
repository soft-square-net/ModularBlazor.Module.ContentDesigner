using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>A category of the post, with its names (null when Content did not list it for the caller).</summary>
public sealed record WorkspaceCategory(string Id, string? NameEn, string? NameAr);

/// <summary>
/// The post the studio opened, as Content has it now. Its unit and categories are the newsroom workflow's: the studio
/// only shows them. <see cref="UnitId"/> null: the post is in no unit yet.
/// </summary>
public sealed record WorkspacePost(int Id, string PublicId, string Title, string Kind, int KindVersion, int Version, string? Permalink,
    string? UrlPrefix = null, Guid? UnitId = null, string? UnitNameEn = null, string? UnitNameAr = null,
    IReadOnlyList<WorkspaceCategory>? Categories = null);

/// <summary>A root value of the post's kind: its key, label, DynamicFields kind and whether the kind requires it.</summary>
public sealed record WorkspaceValue(string Key, string Label, string Kind, bool Required, string? SchemaProperty);

/// <summary>A value of a block field (its key and label): the inner parts a block template orders and hides.</summary>
public sealed record WorkspaceBlockValue(string Key, string Label);

/// <summary>
/// One content of the post for the studio's Contents tray: Content's view of it (unit, stage, section, assignee,
/// revisions; unit and member names from Organization's directory, null when it does not know them),
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
    bool Drift,
    Guid? UnitId = null,
    string? UnitNameEn = null,
    string? UnitNameAr = null,
    string? AssigneeName = null);

/// <summary>
/// Everything the studio needs to open a post, in one call: the post, its contents, the renderer's document (the canvas
/// renders it with ContentDesigner.Rendering, live), the kind's root values, the style tokens each content's field
/// allows (field key → token → values), the design (null when the post has none yet: the studio then opens one) and what
/// the caller may do with it now (<see cref="Rights"/>).
/// </summary>
public sealed record WorkspaceDto(
    WorkspacePost Post,
    IReadOnlyList<WorkspaceContent> Contents,
    RenderDocument Document,
    IReadOnlyList<WorkspaceValue> KindValues,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> FieldStyles,
    PostDesignDto? Design,
    IReadOnlyDictionary<string, IReadOnlyList<WorkspaceBlockValue>>? BlockFields = null,
    PostRightsDto? Rights = null);

/// <summary>
/// The studio's workspace of a post (<see cref="WorkspaceDto"/>): Content's post and DynamicFields' field versions, through
/// the gateway, with the caller's token. The document carries no JSON-LD; the checks build it.
/// </summary>
public sealed record GetWorkspaceQuery(int PostId, string? Culture = null) : IQuery<Result<WorkspaceDto>>;

public sealed class GetWorkspaceHandler(IReadRepository<PostDesign> designs, IContentApi content, DesignWorkbench workbench,
    IOrganizationApi organization, DesignRights rights, PublishGate gate)
    : IQueryHandler<GetWorkspaceQuery, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(GetWorkspaceQuery query, CancellationToken ct) =>
        await DesignResults.Calling<WorkspaceDto>(async () =>
        {
            if (await content.GetPostAsync(query.PostId, ct) is not { } post)
                return Result<WorkspaceDto>.NotFound(DesignResults.NoPost(query.PostId));

            var postFields = await workbench.FieldsAsync(post.DocumentField, post.KindVersion,
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

            var names = await NamesAsync(post, ct);
            var categories = await CategoriesAsync(post, ct);
            var postUnit = names.Unit(post.UnitId);

            return new WorkspaceDto(
                new WorkspacePost(post.Id, post.PublicId, post.Title, post.Kind, post.KindVersion, post.Version, post.Permalink,
                    post.UrlPrefix, post.UnitId, postUnit?.NameEn, postUnit?.NameAr, categories),
                [.. post.Contents.Select(c => new WorkspaceContent(c.ContentId, c.NodeId, c.FieldKey, c.FieldVersion,
                    postFields.Block(c.FieldKey, c.FieldVersion)?.Title ?? Label(c.FieldKey),
                    c.Order, c.Required, c.Stage, c.SectionId, c.AssigneeId, c.Revision, c.AcceptedRevision, c.PinnedRevision,
                    c.ReviewNote, c.Drift, c.UnitId, names.Unit(c.UnitId)?.NameEn, names.Unit(c.UnitId)?.NameAr, names.Member(c.AssigneeId)))],
                workbench.Document(post, postFields, query.Culture, null),
                [.. (postFields.Kind?.Values ?? []).Select(v => new WorkspaceValue(v.Key, v.Label, v.Kind, v.Required, v.SchemaProperty))],
                styles,
                design is null ? null : PostDesignDto.From(design),
                blockFields,
                await gate.AddToAsync(await rights.EvaluateAsync(post.Id, post, ct), ct));
        });

    /// <summary>The unit and member names the post and its contents need; whatever Organization does not give stays an id.</summary>
    private async Task<DirectoryNames> NamesAsync(ContentPost post, CancellationToken ct)
    {
        var unitIds = post.Contents.Select(c => c.UnitId).Append(post.UnitId).OfType<Guid>().Distinct().ToList();
        if (unitIds.Count == 0) return new DirectoryNames(new Dictionary<Guid, UnitName>(), new Dictionary<string, string>());

        var units = (await organization.ListUnitsAsync(ct)).GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        if (post.Contents.Any(c => c.AssigneeId is not null))
            foreach (var unit in unitIds.Where(units.ContainsKey))
                foreach (var member in await organization.MembersAsync(unit, ct))
                    members.TryAdd(member.UserId, member.DisplayName);
        return new DirectoryNames(units, members);
    }

    private async Task<IReadOnlyList<WorkspaceCategory>> CategoriesAsync(ContentPost post, CancellationToken ct)
    {
        var ids = (post.Categories ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return [];
        var known = new Dictionary<string, CategoryName>(StringComparer.Ordinal);
        try
        {
            foreach (var category in await content.ListCategoriesAsync(post.PressId, ct)) known.TryAdd(category.Id, category);
        }
        catch (ExternalApiException)
        {
            // Names only: the header shows the ids.
        }
        return [.. ids.Select(id => known.TryGetValue(id, out var c) ? new WorkspaceCategory(id, c.NameEn, c.NameAr) : new WorkspaceCategory(id, null, null))];
    }

    private sealed record DirectoryNames(IReadOnlyDictionary<Guid, UnitName> Units, IReadOnlyDictionary<string, string> Members)
    {
        public UnitName? Unit(Guid? id) => id is { } value && Units.TryGetValue(value, out var unit) ? unit : null;

        public string? Member(string? userId) => userId is not null && Members.TryGetValue(userId, out var name) ? name : null;
    }

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
