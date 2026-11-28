using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Templates;

public sealed record DocumentTemplateDto(int Id, string Name, string? Description, string Kind, int KindVersion, DesignViews Views,
    bool IsDefault, int Version, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static DocumentTemplateDto From(DocumentTemplate t) =>
        new(t.Id, t.Name, t.Description, t.Kind, t.KindVersion, t.Views, t.IsDefault, t.Version, t.CreatedBy, t.CreatedAt, t.UpdatedAt);
}

/// <summary>The tenant's document templates, by kind and name; optionally of one kind.</summary>
public sealed record ListDocumentTemplatesQuery(string? Kind = null) : IQuery<Result<IReadOnlyList<DocumentTemplateDto>>>;

public sealed class ListDocumentTemplatesHandler(IReadRepository<DocumentTemplate> templates)
    : IQueryHandler<ListDocumentTemplatesQuery, Result<IReadOnlyList<DocumentTemplateDto>>>
{
    public async ValueTask<Result<IReadOnlyList<DocumentTemplateDto>>> Handle(ListDocumentTemplatesQuery query, CancellationToken ct) =>
        Result<IReadOnlyList<DocumentTemplateDto>>.Success([.. (await templates.ListAsync(new DocumentTemplatesSpec(PostKindKeys.Normalize(query.Kind)), ct)).Select(DocumentTemplateDto.From)]);
}

public sealed record GetDocumentTemplateQuery(int Id) : IQuery<Result<DocumentTemplateDto>>;

public sealed class GetDocumentTemplateHandler(IReadRepository<DocumentTemplate> templates) : IQueryHandler<GetDocumentTemplateQuery, Result<DocumentTemplateDto>>
{
    public async ValueTask<Result<DocumentTemplateDto>> Handle(GetDocumentTemplateQuery query, CancellationToken ct) =>
        await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(query.Id), ct) is { } template
            ? DocumentTemplateDto.From(template)
            : Result<DocumentTemplateDto>.NotFound(TemplateResults.NoDocumentTemplate(query.Id));
}

/// <summary>A new document template for a kind (a root field key and the version it is made on).</summary>
public sealed record CreateDocumentTemplateCommand(string Name, string? Description, string Kind, int KindVersion, DesignViews Views, bool MakeDefault = false)
    : ICommand<Result<DocumentTemplateDto>>;

public sealed class CreateDocumentTemplateHandler(IRepository<DocumentTemplate> templates, IAccessScopes access, TimeProvider clock)
    : ICommandHandler<CreateDocumentTemplateCommand, Result<DocumentTemplateDto>>
{
    public async ValueTask<Result<DocumentTemplateDto>> Handle(CreateDocumentTemplateCommand command, CancellationToken ct)
    {
        if (access.UserId is not { Length: > 0 } me) return Result<DocumentTemplateDto>.Unauthorized("Sign in first.");
        var now = clock.GetUtcNow();
        var template = DocumentTemplate.Create(command.Name, command.Description, command.Kind, command.KindVersion, command.Views, me, now);
        return await TemplateResults.AddAsync(templates, template, command.MakeDefault, now, ct);
    }
}

/// <summary>Save a post's draft as a new document template for its kind.</summary>
public sealed record SaveDesignAsTemplateCommand(int PostId, string Name, string? Description = null, bool MakeDefault = false)
    : ICommand<Result<DocumentTemplateDto>>;

public sealed class SaveDesignAsTemplateHandler(IReadRepository<PostDesign> designs, IRepository<DocumentTemplate> templates, IAccessScopes access, TimeProvider clock)
    : ICommandHandler<SaveDesignAsTemplateCommand, Result<DocumentTemplateDto>>
{
    public async ValueTask<Result<DocumentTemplateDto>> Handle(SaveDesignAsTemplateCommand command, CancellationToken ct)
    {
        if (access.UserId is not { Length: > 0 } me) return Result<DocumentTemplateDto>.Unauthorized("Sign in first.");
        if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(command.PostId), ct) is not { } design)
            return Result<DocumentTemplateDto>.NotFound(DesignResults.NoDesign(command.PostId));
        var now = clock.GetUtcNow();
        var template = DocumentTemplate.Create(command.Name, command.Description, design.Kind, design.KindVersion,
            WithoutContents(design.Draft), me, now);
        return await TemplateResults.AddAsync(templates, template, command.MakeDefault, now, ct);
    }

    /// <summary>
    /// A template serves every post of the kind, so it keeps no placement of one post's content (those contents fall
    /// into items). The root values, regions and items stay.
    /// </summary>
    internal static DesignViews WithoutContents(DesignViews views)
    {
        static ViewDesign Strip(ViewDesign view) => view.InheritsFrom is not null ? view
            : view with { Placements = [.. view.AllPlacements.Where(p => PartRef.Parse(p.Part).Kind != PartKind.Content)] };
        return new DesignViews(Strip(views.Mobile), Strip(views.Tablet), Strip(views.Desktop), Strip(views.Amp));
    }
}

public sealed record EditDocumentTemplateCommand(int Id, int Version, string Name, string? Description, DesignViews Views) : ICommand<Result<DocumentTemplateDto>>;

public sealed class EditDocumentTemplateHandler(IRepository<DocumentTemplate> templates, TimeProvider clock)
    : ICommandHandler<EditDocumentTemplateCommand, Result<DocumentTemplateDto>>
{
    public async ValueTask<Result<DocumentTemplateDto>> Handle(EditDocumentTemplateCommand command, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(command.Id), ct) is not { } template)
            return Result<DocumentTemplateDto>.NotFound(TemplateResults.NoDocumentTemplate(command.Id));
        if (template.Version != command.Version)
            return Result<DocumentTemplateDto>.Conflict(DesignResults.Stale($"Document template {command.Id}", template.Version, command.Version));
        template.Edit(command.Name, command.Description, command.Views, clock.GetUtcNow());
        await templates.UpdateAsync(template, ct);
        return DocumentTemplateDto.From(template);
    }
}

/// <summary>Delete a template. Designs made from it keep their views.</summary>
public sealed record DeleteDocumentTemplateCommand(int Id, int Version) : ICommand<Result>;

public sealed class DeleteDocumentTemplateHandler(IRepository<DocumentTemplate> templates) : ICommandHandler<DeleteDocumentTemplateCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDocumentTemplateCommand command, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(command.Id), ct) is not { } template)
            return Result.NotFound(TemplateResults.NoDocumentTemplate(command.Id));
        if (template.Version != command.Version)
            return Result.Conflict(DesignResults.Stale($"Document template {command.Id}", template.Version, command.Version));
        await templates.DeleteAsync(template, ct);
        return Result.NoContent();
    }
}

/// <summary>Make a template its kind's default (the one a post with no design starts from), or stop it being one.</summary>
public sealed record SetDefaultTemplateCommand(int Id, bool IsDefault = true) : ICommand<Result<DocumentTemplateDto>>;

public sealed class SetDefaultTemplateHandler(IRepository<DocumentTemplate> templates, TimeProvider clock)
    : ICommandHandler<SetDefaultTemplateCommand, Result<DocumentTemplateDto>>
{
    public async ValueTask<Result<DocumentTemplateDto>> Handle(SetDefaultTemplateCommand command, CancellationToken ct)
    {
        if (await templates.FirstOrDefaultAsync(new DocumentTemplateByIdSpec(command.Id), ct) is not { } template)
            return Result<DocumentTemplateDto>.NotFound(TemplateResults.NoDocumentTemplate(command.Id));
        var now = clock.GetUtcNow();
        if (command.IsDefault) await TemplateResults.ClearDefaultsAsync(templates, template.Kind, template.Id, now, ct);
        template.MakeDefault(command.IsDefault, now);
        await templates.UpdateAsync(template, ct);
        return DocumentTemplateDto.From(template);
    }
}

internal static class TemplateResults
{
    public static string NoDocumentTemplate(int id) => $"Document template {id} does not exist.";

    public static string NoBlockTemplate(int id) => $"Block template {id} does not exist.";

    /// <summary>Clears the kind's other defaults; the caller's save writes them with its own change.</summary>
    public static async Task ClearDefaultsAsync(IRepository<DocumentTemplate> templates, string kind, int keep, DateTimeOffset now, CancellationToken ct)
    {
        var others = (await templates.ListAsync(new DefaultTemplatesSpec(kind), ct)).Where(t => t.Id != keep).ToList();
        foreach (var other in others) other.MakeDefault(false, now);
        if (others.Count > 0) await templates.UpdateRangeAsync(others, ct);
    }

    public static async Task<Result<DocumentTemplateDto>> AddAsync(IRepository<DocumentTemplate> templates, DocumentTemplate template, bool makeDefault,
        DateTimeOffset now, CancellationToken ct)
    {
        if (makeDefault)
        {
            await TemplateResults.ClearDefaultsAsync(templates, template.Kind, 0, now, ct);
            template.MakeDefault(true, now);
        }
        return DocumentTemplateDto.From(await templates.AddAsync(template, ct));
    }
}
