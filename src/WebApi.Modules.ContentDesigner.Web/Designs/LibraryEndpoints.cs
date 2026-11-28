using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;

// Content's shared library, for the studio's "add existing content": thin proxies through the gateway, with the caller's
// token, so Content's own permissions decide what may be listed and attached.

/// <summary>GET /library?fieldKey=&amp;stage=&amp;sectionId=&amp;unitId=&amp;skip=&amp;take=: the library, newest change first.</summary>
public sealed class Library : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/library", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListLibrary")
            .WithSummary("Content's shared library (the contents the caller may see, archived ones left out), for adding an existing content to a post.");

    private static async Task<Results<Ok<LibraryPage>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct,
        string? fieldKey = null, string? stage = null, string? sectionId = null, int skip = 0, int take = 20, Guid? unitId = null) =>
        (await sender.Send(new ListLibraryQuery(fieldKey, stage, sectionId, skip, take, unitId), ct)).ToOkResult();
}

/// <summary>GET /library/{contentId}: one library content with its values, as the renderer's block (the studio's preview).</summary>
public sealed class LibraryItem : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/library/{contentId}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetLibraryItem")
            .WithSummary("One content of the library with its values, as the renderer's block.");

    private static async Task<Results<Ok<LibraryItemDto>, ProblemHttpResult>> HandleAsync(string contentId, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetLibraryItemQuery(contentId), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/contents: use a library content in the post; answers the post's workspace again.</summary>
public sealed class AttachContent : IEndpoint
{
    /// <summary>The post's version (Content's), the content, where in the reading order (null: at the end), whether the post needs it.</summary>
    public sealed record AttachRequest(int PostVersion, string ContentId, int? Index = null, bool Required = false, string? Culture = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/contents", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_AttachContent")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Add an existing content of the library to the post (Content's attach, at the post's version; 409 when the post changed). Returns the workspace.");

    private static async Task<Results<Ok<WorkspaceDto>, ProblemHttpResult>> HandleAsync(int postId, AttachRequest r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new AttachContentCommand(postId, r.PostVersion, r.ContentId, r.Index, r.Required, r.Culture), ct)).ToOkResult();
}
