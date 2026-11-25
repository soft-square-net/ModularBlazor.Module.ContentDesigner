using System.Text.Json;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Templates;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Templates;

// Document templates (/document-templates), block templates (/block-templates), and saving a design or a block as one.

public sealed class ListDocumentTemplates : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/document-templates", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListDocumentTemplates")
            .WithSummary("The document templates, by kind and name; optionally of one kind.");

    private static async Task<Results<Ok<IReadOnlyList<DocumentTemplateDto>>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct, string? kind = null) =>
        (await sender.Send(new ListDocumentTemplatesQuery(kind), ct)).ToOkResult();
}

public sealed class GetDocumentTemplate : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/document-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetDocumentTemplate");

    private static async Task<Results<Ok<DocumentTemplateDto>, ProblemHttpResult>> HandleAsync(int id, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetDocumentTemplateQuery(id), ct)).ToOkResult();
}

public sealed class CreateDocumentTemplate : IEndpoint
{
    public sealed record NewDocumentTemplate(string Name, string? Description, string Kind, int KindVersion, DesignViews Views, bool MakeDefault = false);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/document-templates", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_CreateDocumentTemplate")
            .WithSummary("A new document template for a kind; makeDefault makes it the kind's default.");

    private static async Task<Results<Created<DocumentTemplateDto>, ProblemHttpResult>> HandleAsync(NewDocumentTemplate r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new CreateDocumentTemplateCommand(r.Name, r.Description, r.Kind, r.KindVersion, r.Views, r.MakeDefault), ct))
            .ToCreatedResult(t => $"document-templates/{t.Id}");
}

public sealed class EditDocumentTemplate : IEndpoint
{
    public sealed record DocumentTemplateEdit(int Version, string Name, string? Description, DesignViews Views);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/document-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_EditDocumentTemplate");

    private static async Task<Results<Ok<DocumentTemplateDto>, ProblemHttpResult>> HandleAsync(int id, DocumentTemplateEdit r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new EditDocumentTemplateCommand(id, r.Version, r.Name, r.Description, r.Views), ct)).ToOkResult();
}

public sealed class DeleteDocumentTemplate : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/document-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_DeleteDocumentTemplate")
            .WithSummary("Delete a document template; designs made from it keep their views.");

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(int id, int version, ISender sender, CancellationToken ct) =>
        (await sender.Send(new DeleteDocumentTemplateCommand(id, version), ct)).ToNoContentResult();
}

public sealed class SetDefaultTemplate : IEndpoint
{
    public sealed record DefaultRequest(bool IsDefault = true);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/document-templates/{id:int}/default", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_SetDefaultTemplate")
            .WithSummary("Make a template its kind's default (the kind's other default stops being one), or not.");

    private static async Task<Results<Ok<DocumentTemplateDto>, ProblemHttpResult>> HandleAsync(int id, DefaultRequest? r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new SetDefaultTemplateCommand(id, r?.IsDefault ?? true), ct)).ToOkResult();
}

public sealed class SaveDesignAsTemplate : IEndpoint
{
    public sealed record SaveAsTemplateRequest(string Name, string? Description = null, bool MakeDefault = false);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/save-as-template", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_SaveDesignAsTemplate")
            .WithSummary("Save the post's draft as a document template of its kind (the post's own contents fall into items).");

    private static async Task<Results<Created<DocumentTemplateDto>, ProblemHttpResult>> HandleAsync(int postId, SaveAsTemplateRequest r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new SaveDesignAsTemplateCommand(postId, r.Name, r.Description, r.MakeDefault), ct)).ToCreatedResult(t => $"document-templates/{t.Id}");
}

public sealed class ListBlockTemplates : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/block-templates", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListBlockTemplates")
            .WithSummary("The block templates; optionally of one block field and one view.");

    private static async Task<Results<Ok<IReadOnlyList<BlockTemplateDto>>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct,
        string? field = null, string? view = null)
    {
        ViewKind? kind = null;
        if (view is not null)
        {
            if (!ViewKinds.TryParse(view, out var parsed)) return Problems.UnknownView(view);
            kind = parsed;
        }
        return (await sender.Send(new ListBlockTemplatesQuery(field, kind), ct)).ToOkResult();
    }
}

public sealed class GetBlockTemplate : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/block-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetBlockTemplate");

    private static async Task<Results<Ok<BlockTemplateDto>, ProblemHttpResult>> HandleAsync(int id, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetBlockTemplateQuery(id), ct)).ToOkResult();
}

public sealed class CreateBlockTemplate : IEndpoint
{
    public sealed record NewBlockTemplate(string Name, string FieldKey, ViewKind View, IReadOnlyList<string>? PartOrder = null,
        IReadOnlyList<string>? HiddenParts = null, IReadOnlyDictionary<string, string>? Tokens = null, JsonElement? Content = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/block-templates", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_CreateBlockTemplate");

    private static async Task<Results<Created<BlockTemplateDto>, ProblemHttpResult>> HandleAsync(NewBlockTemplate r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new CreateBlockTemplateCommand(r.Name, r.FieldKey, r.View, r.PartOrder, r.HiddenParts, r.Tokens, r.Content), ct))
            .ToCreatedResult(t => $"block-templates/{t.Id}");
}

public sealed class EditBlockTemplate : IEndpoint
{
    public sealed record BlockTemplateEdit(int Version, string Name, IReadOnlyList<string>? PartOrder = null,
        IReadOnlyList<string>? HiddenParts = null, IReadOnlyDictionary<string, string>? Tokens = null, JsonElement? Content = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/block-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_EditBlockTemplate");

    private static async Task<Results<Ok<BlockTemplateDto>, ProblemHttpResult>> HandleAsync(int id, BlockTemplateEdit r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new EditBlockTemplateCommand(id, r.Version, r.Name, r.PartOrder, r.HiddenParts, r.Tokens, r.Content), ct)).ToOkResult();
}

public sealed class DeleteBlockTemplate : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/block-templates/{id:int}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_DeleteBlockTemplate")
            .WithSummary("Delete a block template; the checks report placements that still name it.");

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(int id, int version, ISender sender, CancellationToken ct) =>
        (await sender.Send(new DeleteBlockTemplateCommand(id, version), ct)).ToNoContentResult();
}

public sealed class SaveBlockAsTemplate : IEndpoint
{
    public sealed record SaveBlockRequest(string Part, string Name, bool WithContent = false);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/views/{view}/save-block-as-template", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Templates)
            .WithName("ContentDesigner_SaveBlockAsTemplate")
            .WithSummary("Save how a content (content:{id}) is placed in a view as a block template of its field; withContent keeps its values.");

    private static async Task<Results<Created<BlockTemplateDto>, ProblemHttpResult>> HandleAsync(int postId, string view, SaveBlockRequest r, ISender sender, CancellationToken ct) =>
        !ViewKinds.TryParse(view, out var kind)
            ? Problems.UnknownView(view)
            : (await sender.Send(new SaveBlockAsTemplateCommand(postId, kind, r.Part, r.Name, r.WithContent), ct)).ToCreatedResult(t => $"block-templates/{t.Id}");
}
