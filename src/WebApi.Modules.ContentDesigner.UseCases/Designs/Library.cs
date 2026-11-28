using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>A page of the shared library; <see cref="More"/> when another page may follow.</summary>
public sealed record LibraryPage(IReadOnlyList<LibraryContent> Items, bool More);

/// <summary>One library content for the studio's preview: Content's row and the content as the renderer's block.</summary>
public sealed record LibraryItemDto(LibraryContent Content, RenderBlock Block);

/// <summary>
/// Content's shared library, for the studio's "add existing content" (through the gateway, with the caller's token, so
/// Content decides what the caller may see). Archived contents are left out.
/// </summary>
public sealed record ListLibraryQuery(string? FieldKey = null, string? Stage = null, string? SectionId = null, int Skip = 0, int Take = 20,
    Guid? UnitId = null)
    : IQuery<Result<LibraryPage>>;

public sealed class ListLibraryHandler(IContentApi content) : IQueryHandler<ListLibraryQuery, Result<LibraryPage>>
{
    public const int MaxTake = 100;

    public async ValueTask<Result<LibraryPage>> Handle(ListLibraryQuery query, CancellationToken ct)
    {
        if (query.Skip < 0) return Result<LibraryPage>.Invalid(new ValidationError(nameof(query.Skip), "'skip' is 0 or more."));
        if (query.Take is < 1 or > MaxTake) return Result<LibraryPage>.Invalid(new ValidationError(nameof(query.Take), $"'take' is 1 to {MaxTake}."));
        return await DesignResults.Calling<LibraryPage>(async () =>
        {
            // One more than asked tells whether another page follows.
            var items = await content.ListContentsAsync(new LibraryFilter(query.FieldKey, query.Stage, query.SectionId, query.Skip, query.Take + 1, query.UnitId), ct);
            return new LibraryPage([.. items.Where(i => !i.Archived).Take(query.Take)], items.Count > query.Take);
        });
    }
}

public sealed record GetLibraryItemQuery(string ContentId) : IQuery<Result<LibraryItemDto>>;

public sealed class GetLibraryItemHandler(IContentApi content) : IQueryHandler<GetLibraryItemQuery, Result<LibraryItemDto>>
{
    public async ValueTask<Result<LibraryItemDto>> Handle(GetLibraryItemQuery query, CancellationToken ct) =>
        await DesignResults.Calling<LibraryItemDto>(async () =>
        {
            if (await content.GetContentAsync(query.ContentId, ct) is not { } item)
                return Result<LibraryItemDto>.NotFound($"Content {query.ContentId} does not exist, or you may not see it.");
            return new LibraryItemDto(item.Content, Block(item));
        });

    /// <summary>The content's node as the renderer's block (its id is the content id, as in a post).</summary>
    internal static RenderBlock Block(LibraryContentDetail item)
    {
        var document = JsonSerializer.SerializeToElement(new { children = new[] { item.Node } });
        var nodeId = PostDocuments.Str(item.Node, "nodeId") ?? item.Content.NodeId;
        var blocks = PostDocuments.Blocks(document, new Dictionary<string, string>(StringComparer.Ordinal) { [nodeId] = item.Content.ContentId });
        return blocks.Count > 0
            ? blocks[0]
            : new RenderBlock(item.Content.ContentId, nodeId, item.Content.FieldKey, item.Content.FieldVersion, Empty, Empty);
    }

    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
}

/// <summary>
/// Uses a library content in the post (Content's attach, at the post's version: a stale version is a conflict) and
/// returns the post's workspace again, so the studio shows the new content in one round trip. The design does not
/// change: the content shows through <c>items</c> until the studio places it.
/// </summary>
public sealed record AttachContentCommand(int PostId, int PostVersion, string ContentId, int? Index = null, bool Required = false, string? Culture = null)
    : ICommand<Result<WorkspaceDto>>;

public sealed class AttachContentHandler(IContentApi content, ISender sender, DesignRights rights) : ICommandHandler<AttachContentCommand, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(AttachContentCommand c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.ContentId)) return Result<WorkspaceDto>.Invalid(new ValidationError(nameof(c.ContentId), "Name the content to add."));
        if (c.Index is < 0) return Result<WorkspaceDto>.Invalid(new ValidationError(nameof(c.Index), "'index' is 0 or more."));
        return await DesignResults.Calling<WorkspaceDto>(async () =>
        {
            var allowed = await rights.RequireAsync(ContentDesignerPermissions.Arrange, c.PostId, await content.GetPostAsync(c.PostId, ct), ct);
            if (!allowed.IsSuccess) return allowed.As<WorkspaceDto>();
            await content.AttachContentAsync(c.PostId, c.PostVersion, c.ContentId, c.Index, c.Required, ct);
            return await sender.Send(new GetWorkspaceQuery(c.PostId, c.Culture), ct);
        });
    }
}
