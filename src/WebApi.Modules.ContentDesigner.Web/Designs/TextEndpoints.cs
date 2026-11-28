using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;

/// <summary>
/// PUT /posts/{postId}/text: one plain text value edited in place on the studio's canvas, saved through Content (which
/// decides who may edit it). Reading the post is enough here: Content refuses a caller who may not edit the value.
/// </summary>
public sealed class EditText : IEndpoint
{
    /// <summary>
    /// The part (<c>prop:{key}</c> or <c>content:{id}</c>), the value's key for a content, the new text, the text the studio
    /// showed before the edit (a different one in Content is a conflict), and the culture the workspace is answered in.
    /// </summary>
    public sealed record TextEdit(string Part, string? Key, string Text, string? Expected = null, string? Culture = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/posts/{postId:int}/text", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_EditText")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Edit one plain text value of the post or of one of its contents, through Content (409 when it changed meanwhile). Returns the workspace.");

    private static async Task<Results<Ok<WorkspaceDto>, ProblemHttpResult>> HandleAsync(int postId, TextEdit r, ISender sender, CancellationToken ct) =>
        (await sender.Send(new EditTextCommand(postId, r.Part, r.Key, r.Text, r.Expected, r.Culture), ct)).ToOkResult();
}
