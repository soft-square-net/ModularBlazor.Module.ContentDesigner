using System.Text.Json.Nodes;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Templates;

public sealed record BlockTemplateDto(int Id, string Name, string FieldKey, ViewKind View, IReadOnlyList<string> PartOrder,
    IReadOnlyList<string> HiddenParts, IReadOnlyDictionary<string, string> Tokens, JsonElement? Content, int Version,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static BlockTemplateDto From(BlockTemplate t) =>
        new(t.Id, t.Name, t.FieldKey, t.View, t.PartOrder, t.HiddenParts, t.Tokens,
            t.ContentJson is null ? null : JsonDocument.Parse(t.ContentJson).RootElement.Clone(),
            t.Version, t.CreatedBy, t.CreatedAt, t.UpdatedAt);
}

/// <summary>The tenant's block templates; optionally of one block field and one view (the studio's toolbox).</summary>
public sealed record ListBlockTemplatesQuery(string? FieldKey = null, ViewKind? View = null) : IQuery<Result<IReadOnlyList<BlockTemplateDto>>>;

public sealed class ListBlockTemplatesHandler(IReadRepository<BlockTemplate> templates)
    : IQueryHandler<ListBlockTemplatesQuery, Result<IReadOnlyList<BlockTemplateDto>>>
{
    public async ValueTask<Result<IReadOnlyList<BlockTemplateDto>>> Handle(ListBlockTemplatesQuery query, CancellationToken ct) =>
        Result<IReadOnlyList<BlockTemplateDto>>.Success([.. (await templates.ListAsync(new BlockTemplatesSpec(query.FieldKey, query.View), ct)).Select(BlockTemplateDto.From)]);
}

public sealed record GetBlockTemplateQuery(int Id) : IQuery<Result<BlockTemplateDto>>;

public sealed class GetBlockTemplateHandler(IReadRepository<BlockTemplate> templates) : IQueryHandler<GetBlockTemplateQuery, Result<BlockTemplateDto>>
{
    public async ValueTask<Result<BlockTemplateDto>> Handle(GetBlockTemplateQuery query, CancellationToken ct) =>
        await templates.FirstOrDefaultAsync(new BlockTemplateByIdSpec(query.Id), ct) is { } template
            ? BlockTemplateDto.From(template)
            : Result<BlockTemplateDto>.NotFound(TemplateResults.NoBlockTemplate(query.Id));
}

public sealed record CreateBlockTemplateCommand(string Name, string FieldKey, ViewKind View, IReadOnlyList<string>? PartOrder = null,
    IReadOnlyList<string>? HiddenParts = null, IReadOnlyDictionary<string, string>? Tokens = null, JsonElement? Content = null)
    : ICommand<Result<BlockTemplateDto>>;

public sealed class CreateBlockTemplateHandler(IRepository<BlockTemplate> templates, IAccessScopes access, TimeProvider clock)
    : ICommandHandler<CreateBlockTemplateCommand, Result<BlockTemplateDto>>
{
    public async ValueTask<Result<BlockTemplateDto>> Handle(CreateBlockTemplateCommand c, CancellationToken ct)
    {
        if (access.UserId is not { Length: > 0 } me) return Result<BlockTemplateDto>.Unauthorized("Sign in first.");
        var template = BlockTemplate.Create(c.Name, c.FieldKey, c.View, c.PartOrder, c.HiddenParts, c.Tokens, Json(c.Content), me, clock.GetUtcNow());
        return BlockTemplateDto.From(await templates.AddAsync(template, ct));
    }

    internal static string? Json(JsonElement? content) =>
        content is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } json ? json.GetRawText() : null;
}

public sealed record EditBlockTemplateCommand(int Id, int Version, string Name, IReadOnlyList<string>? PartOrder = null,
    IReadOnlyList<string>? HiddenParts = null, IReadOnlyDictionary<string, string>? Tokens = null, JsonElement? Content = null)
    : ICommand<Result<BlockTemplateDto>>;

public sealed class EditBlockTemplateHandler(IRepository<BlockTemplate> templates, TimeProvider clock)
    : ICommandHandler<EditBlockTemplateCommand, Result<BlockTemplateDto>>
{
    public async ValueTask<Result<BlockTemplateDto>> Handle(EditBlockTemplateCommand c, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new BlockTemplateByIdSpec(c.Id), ct) is not { } template)
            return Result<BlockTemplateDto>.NotFound(TemplateResults.NoBlockTemplate(c.Id));
        if (template.Version != c.Version)
            return Result<BlockTemplateDto>.Conflict(DesignResults.Stale($"Block template {c.Id}", template.Version, c.Version));
        template.Edit(c.Name, c.PartOrder, c.HiddenParts, c.Tokens, CreateBlockTemplateHandler.Json(c.Content), clock.GetUtcNow());
        await templates.UpdateAsync(template, ct);
        return BlockTemplateDto.From(template);
    }
}

/// <summary>Delete a block template. A placement that still names it is reported by the checks.</summary>
public sealed record DeleteBlockTemplateCommand(int Id, int Version) : ICommand<Result>;

public sealed class DeleteBlockTemplateHandler(IRepository<BlockTemplate> templates) : ICommandHandler<DeleteBlockTemplateCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteBlockTemplateCommand c, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new BlockTemplateByIdSpec(c.Id), ct) is not { } template)
            return Result.NotFound(TemplateResults.NoBlockTemplate(c.Id));
        if (template.Version != c.Version)
            return Result.Conflict(DesignResults.Stale($"Block template {c.Id}", template.Version, c.Version));
        await templates.DeleteAsync(template, ct);
        return Result.NoContent();
    }
}

/// <summary>
/// Save how one content is placed in a view as a block template of its field: the placement's tokens, the inner
/// order and hidden parts of the block template it uses (if any), and with <see cref="WithContent"/> its values too.
/// <see cref="Design"/>: the views as the studio has them, unsaved (null: the stored draft).
/// </summary>
public sealed record SaveBlockAsTemplateCommand(int PostId, ViewKind View, string Part, string Name, bool WithContent = false, DesignViews? Design = null)
    : ICommand<Result<BlockTemplateDto>>;

public sealed class SaveBlockAsTemplateHandler(IReadRepository<PostDesign> designs, IRepository<BlockTemplate> templates, IContentApi content,
    IAccessScopes access, TimeProvider clock) : ICommandHandler<SaveBlockAsTemplateCommand, Result<BlockTemplateDto>>
{
    public async ValueTask<Result<BlockTemplateDto>> Handle(SaveBlockAsTemplateCommand c, CancellationToken ct) =>
        await DesignResults.Calling<BlockTemplateDto>(async () =>
        {
            if (access.UserId is not { Length: > 0 } me) return Result<BlockTemplateDto>.Unauthorized("Sign in first.");
            if (!PartRef.TryParse(c.Part, out var part) || part.Kind != PartKind.Content)
                return Result<BlockTemplateDto>.Invalid(new ValidationError(nameof(c.Part), "A block template is saved from a content (content:{id})."));
            if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(c.PostId), ct) is not { } design)
                return Result<BlockTemplateDto>.NotFound(DesignResults.NoDesign(c.PostId));
            if (await content.GetPostAsync(c.PostId, ct) is not { } post)
                return Result<BlockTemplateDto>.NotFound(DesignResults.NoPost(c.PostId));
            if (post.Contents.FirstOrDefault(x => x.ContentId == part.Key) is not { } info)
                return Result<BlockTemplateDto>.NotFound($"Content {part.Key} is not attached to post {c.PostId}.");

            var placement = (c.Design ?? design.Draft).Resolve(c.View).PlacementOf(part);
            BlockTemplate? used = placement?.BlockTemplateId is { } id ? await templates.FirstOrDefaultAsync(new BlockTemplateByIdSpec(id), ct) : null;
            var tokens = new Dictionary<string, string>(used?.Tokens ?? StyleTokens.Empty, StringComparer.Ordinal);
            foreach (var (token, value) in placement?.Tokens ?? StyleTokens.Empty) tokens[token] = value;

            string? json = null;
            if (c.WithContent)
            {
                var node = PostDocuments.Children(post.Document).FirstOrDefault(n => PostDocuments.Str(n, "nodeId") == info.NodeId);
                if (node.ValueKind != JsonValueKind.Object)
                    return Result<BlockTemplateDto>.NotFound($"Content {part.Key} is not in the post's document.");
                var tree = (JsonObject)PostDocuments.ToFieldsTree(new JsonObject { ["children"] = new JsonArray(JsonNode.Parse(node.GetRawText())) }
                    .Deserialize<JsonElement>())["children"]![0]!;
                tree.Remove("nodeId");
                json = tree.ToJsonString();
            }

            var template = BlockTemplate.Create(c.Name, info.FieldKey, c.View, used?.PartOrder, used?.HiddenParts, tokens, json, me, clock.GetUtcNow());
            return BlockTemplateDto.From(await templates.AddAsync(template, ct));
        });
}
