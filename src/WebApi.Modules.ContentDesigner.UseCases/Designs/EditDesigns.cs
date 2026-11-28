using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>
/// Open a post in the designer: its design, made from the template named, else the kind's default template, else the
/// starter (one section with the headline and every content). 409 if the post has a design already.
/// </summary>
public sealed record OpenDesignCommand(int PostId, int? TemplateId = null) : ICommand<Result<PostDesignDto>>;

public sealed class OpenDesignHandler(IRepository<PostDesign> designs, IReadRepository<DocumentTemplate> templates, IContentApi content,
    IAccessScopes access, DesignRights rights, TimeProvider clock) : ICommandHandler<OpenDesignCommand, Result<PostDesignDto>>
{
    public async ValueTask<Result<PostDesignDto>> Handle(OpenDesignCommand command, CancellationToken ct) =>
        await DesignResults.Calling<PostDesignDto>(async () =>
        {
            if (access.UserId is not { Length: > 0 } me) return Result<PostDesignDto>.Unauthorized("Sign in first.");
            if (await designs.AnyAsync(new DesignByPostSpec(command.PostId), ct))
                return Result<PostDesignDto>.Conflict($"Post {command.PostId} has a design already.");
            if (await content.GetPostAsync(command.PostId, ct) is not { } post)
                return Result<PostDesignDto>.NotFound(DesignResults.NoPost(command.PostId));
            var allowed = await rights.RequireAsync(ContentDesignerPermissions.Arrange, command.PostId, post, ct);
            if (!allowed.IsSuccess) return allowed.As<PostDesignDto>();

            DocumentTemplate? template;
            if (command.TemplateId is { } id)
            {
                template = await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(id), ct);
                if (template is null) return Result<PostDesignDto>.NotFound($"Document template {id} does not exist.");
                if (template.Kind != post.Kind) return Result<PostDesignDto>.Conflict($"Template {id} is for '{template.Kind}', the post is a '{post.Kind}'.");
            }
            else template = (await templates.ListAsync(new DefaultTemplatesSpec(post.Kind), ct)).FirstOrDefault();

            var design = PostDesign.Create(post.Id, post.PublicId, post.Kind, post.KindVersion, template?.Views ?? DesignViews.Starter,
                template?.Id, me, clock.GetUtcNow());
            return PostDesignDto.From(await designs.AddAsync(design, ct));
        });
}

/// <summary>Replace the whole draft (the four views). <see cref="Version"/> is the version the caller read.</summary>
public sealed record SaveDesignCommand(int PostId, int Version, DesignViews Views) : ICommand<Result<PostDesignDto>>;

public sealed class SaveDesignHandler(IRepository<PostDesign> designs, IAccessScopes access, DesignRights rights, TimeProvider clock)
    : ICommandHandler<SaveDesignCommand, Result<PostDesignDto>>
{
    public ValueTask<Result<PostDesignDto>> Handle(SaveDesignCommand command, CancellationToken ct) =>
        DesignEdits.EditAsync(designs, access, rights, ContentDesignerPermissions.Arrange, command.PostId, command.Version,
            (design, me) => design.ReplaceDraft(command.Views, me, clock.GetUtcNow()), ct);
}

/// <summary>Arrange one view.</summary>
public sealed record ArrangeViewCommand(int PostId, ViewKind View, int Version, ViewDesign Design) : ICommand<Result<PostDesignDto>>;

public sealed class ArrangeViewHandler(IRepository<PostDesign> designs, IAccessScopes access, DesignRights rights, TimeProvider clock)
    : ICommandHandler<ArrangeViewCommand, Result<PostDesignDto>>
{
    public ValueTask<Result<PostDesignDto>> Handle(ArrangeViewCommand command, CancellationToken ct) =>
        DesignEdits.EditAsync(designs, access, rights, ContentDesignerPermissions.Arrange, command.PostId, command.Version,
            (design, me) => design.Arrange(command.View, command.Design, me, clock.GetUtcNow()), ct);
}

/// <summary>Take a document template's views (all, or the ones named) into the draft.</summary>
public sealed record ApplyTemplateCommand(int PostId, int Version, int TemplateId, IReadOnlyList<ViewKind>? Views = null) : ICommand<Result<PostDesignDto>>;

public sealed class ApplyTemplateHandler(IRepository<PostDesign> designs, IReadRepository<DocumentTemplate> templates, IAccessScopes access,
    DesignRights rights, TimeProvider clock)
    : ICommandHandler<ApplyTemplateCommand, Result<PostDesignDto>>
{
    public async ValueTask<Result<PostDesignDto>> Handle(ApplyTemplateCommand command, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(command.TemplateId), ct) is not { } template)
            return Result<PostDesignDto>.NotFound($"Document template {command.TemplateId} does not exist.");
        return await DesignEdits.EditAsync(designs, access, rights, ContentDesignerPermissions.Arrange, command.PostId, command.Version,
            (design, me) => design.ApplyTemplate(template.Id, template.Kind, template.Views, command.Views, me, clock.GetUtcNow()), ct);
    }
}

/// <summary>
/// Publish the draft: every check runs first against the post as it is now, and any error is a 409 that lists them.
/// The design then pins the revision of each content the checks saw and moves to the post's kind version.
/// </summary>
public sealed record PublishDesignCommand(int PostId, int Version, string? Culture = null) : ICommand<Result<PostDesignDto>>;

public sealed class PublishDesignHandler(IRepository<PostDesign> designs, IContentApi content, DesignWorkbench workbench, IAccessScopes access,
    DesignRights rights, TimeProvider clock)
    : ICommandHandler<PublishDesignCommand, Result<PostDesignDto>>
{
    public async ValueTask<Result<PostDesignDto>> Handle(PublishDesignCommand command, CancellationToken ct) =>
        await DesignResults.Calling<PostDesignDto>(async () =>
        {
            if (access.UserId is not { Length: > 0 } me) return Result<PostDesignDto>.Unauthorized("Sign in first.");
            if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(command.PostId), ct) is not { } design)
                return Result<PostDesignDto>.NotFound(DesignResults.NoDesign(command.PostId));
            var found = await content.GetPostAsync(command.PostId, ct);
            var allowed = await rights.RequireAsync(ContentDesignerPermissions.Publish, command.PostId, found, ct);
            if (!allowed.IsSuccess) return allowed.As<PostDesignDto>();
            if (design.Version != command.Version)
                return Result<PostDesignDto>.Conflict(DesignResults.Stale($"The design of post {command.PostId}", design.Version, command.Version));
            if (found is not { } post)
                return Result<PostDesignDto>.NotFound(DesignResults.NoPost(command.PostId));

            var problems = await workbench.CheckAsync(design.Draft, post.KindVersion, post, null, command.Culture, ct);
            if (problems.HasErrors())
                return Result<PostDesignDto>.Conflict([.. problems.Where(p => p.Severity == ProblemSeverity.Error)
                    .Select(p => $"{(p.View is { } v ? v.Key() + ": " : "")}{p.Message}")]);

            var now = clock.GetUtcNow();
            design.Follow(post.Kind);
            design.Rebase(post.KindVersion, me, now);
            design.Publish(
                post.Contents.ToDictionary(c => c.ContentId, c => c.Revision, StringComparer.Ordinal),
                post.Contents.ToDictionary(c => c.ContentId, c => c.NodeId, StringComparer.Ordinal),
                me, now);
            await designs.UpdateAsync(design, ct);
            return PostDesignDto.From(design);
        });
}

/// <summary>Take the published design down; the public pages answer 404 until it is published again.</summary>
public sealed record UnpublishDesignCommand(int PostId, int Version) : ICommand<Result<PostDesignDto>>;

public sealed class UnpublishDesignHandler(IRepository<PostDesign> designs, IAccessScopes access, DesignRights rights, TimeProvider clock)
    : ICommandHandler<UnpublishDesignCommand, Result<PostDesignDto>>
{
    public ValueTask<Result<PostDesignDto>> Handle(UnpublishDesignCommand command, CancellationToken ct) =>
        DesignEdits.EditAsync(designs, access, rights, ContentDesignerPermissions.Publish, command.PostId, command.Version,
            (design, me) => design.Unpublish(me, clock.GetUtcNow()), ct);
}

/// <summary>Delete a post's design (and its published state).</summary>
public sealed record DeleteDesignCommand(int PostId, int Version) : ICommand<Result>;

public sealed class DeleteDesignHandler(IRepository<PostDesign> designs, DesignRights rights) : ICommandHandler<DeleteDesignCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDesignCommand command, CancellationToken ct)
    {
        if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(command.PostId), ct) is not { } design)
            return Result.NotFound(DesignResults.NoDesign(command.PostId));
        var allowed = await rights.RequireAsync(ContentDesignerPermissions.Arrange, command.PostId, ct);
        if (!allowed.IsSuccess) return allowed;
        if (design.Version != command.Version)
            return Result.Conflict(DesignResults.Stale($"The design of post {command.PostId}", design.Version, command.Version));
        await designs.DeleteAsync(design, ct);
        return Result.NoContent();
    }
}

internal static class DesignEdits
{
    /// <summary>Load, check the caller, their right on the post and the version, change through the aggregate, save.</summary>
    public static async ValueTask<Result<PostDesignDto>> EditAsync(IRepository<PostDesign> designs, IAccessScopes access, DesignRights rights,
        string permission, int postId, int version, Action<PostDesign, string> change, CancellationToken ct)
    {
        if (access.UserId is not { Length: > 0 } me) return Result<PostDesignDto>.Unauthorized("Sign in first.");
        if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(postId), ct) is not { } design)
            return Result<PostDesignDto>.NotFound(DesignResults.NoDesign(postId));
        var (allowed, post) = await rights.RequireAndReadAsync(permission, postId, ct);
        if (!allowed.IsSuccess) return allowed.As<PostDesignDto>();
        if (design.Version != version)
            return Result<PostDesignDto>.Conflict(DesignResults.Stale($"The design of post {postId}", design.Version, version));
        if (post is not null) design.Follow(post.Kind);
        change(design, me);
        await designs.UpdateAsync(design, ct);
        return PostDesignDto.From(design);
    }
}
