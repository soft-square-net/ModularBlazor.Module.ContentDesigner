using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;

// The endpoints of a post's design, under /posts/{postId}/design (postId: Content's post id), and the list of designs.

/// <summary>GET /designs?page=&amp;pageSize=&amp;kind=: one page of designs, the latest change first.</summary>
public sealed class List : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/designs", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListDesigns")
            .WithSummary("One page of post designs, the latest change first; optionally of one kind.");

    private static async Task<Results<Ok<DesignPage>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, string? kind = null) =>
        (await sender.Send(new ListDesignsQuery(page, pageSize, kind), ct)).ToOkResult();
}

/// <summary>GET /posts/{postId}/design: the post's design; 404 when it has none.</summary>
public sealed class Get : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/posts/{postId:int}/design", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetDesign")
            .WithSummary("A post's design: its four-view draft and its published state.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetDesignQuery(postId), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design: open the post in the designer.</summary>
public sealed class Open : IEndpoint
{
    public sealed record OpenRequest(int? TemplateId = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_OpenDesign")
            .WithSummary("Make the post's design from a template, else the kind's default template, else the starter; 409 if it has one.");

    private static async Task<Results<Created<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, OpenRequest? request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new OpenDesignCommand(postId, request?.TemplateId), ct)).ToCreatedResult(d => $"posts/{d.PostId}/design");
}

/// <summary>PUT /posts/{postId}/design: replace the draft.</summary>
public sealed class Save : IEndpoint
{
    public sealed record SaveRequest(int Version, DesignViews Views);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/posts/{postId:int}/design", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_SaveDesign")
            .WithSummary("Replace the draft's four views; 409 if the design changed since 'version' or a view breaks a structural rule.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, SaveRequest request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new SaveDesignCommand(postId, request.Version, request.Views), ct)).ToOkResult();
}

/// <summary>PUT /posts/{postId}/design/views/{view}: arrange one view.</summary>
public sealed class Arrange : IEndpoint
{
    public sealed record ArrangeRequest(int Version, ViewDesign Design);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPut("/posts/{postId:int}/design/views/{view}", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_ArrangeView")
            .WithSummary("Arrange one view (mobile, tablet, desktop, amp): its regions and placements, or inherit another view.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, string view, ArrangeRequest request, ISender sender, CancellationToken ct) =>
        !ViewKinds.TryParse(view, out var kind)
            ? Problems.UnknownView(view)
            : (await sender.Send(new ArrangeViewCommand(postId, kind, request.Version, request.Design), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design/apply-template.</summary>
public sealed class ApplyTemplate : IEndpoint
{
    public sealed record ApplyRequest(int Version, int TemplateId, IReadOnlyList<ViewKind>? Views = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/apply-template", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_ApplyTemplate")
            .WithSummary("Take a document template's views (all, or the ones named) into the draft.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, ApplyRequest request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new ApplyTemplateCommand(postId, request.Version, request.TemplateId, request.Views), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design/check: the Problems panel.</summary>
public sealed class Check : IEndpoint
{
    /// <param name="Views">Only these views (all by default).</param>
    /// <param name="Design">Unsaved views to check instead of the stored draft (the studio, while editing).</param>
    public sealed record CheckRequest(IReadOnlyList<ViewKind>? Views = null, string? Culture = null, DesignViews? Design = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/check", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_CheckDesign")
            .WithSummary("Check the draft (or the unsaved views sent): design rules, DynamicFields validation, JSON-LD essentials, the AMP page; with the JSON-LD.");

    private static async Task<Results<Ok<DesignReport>, ProblemHttpResult>> HandleAsync(int postId, CheckRequest? request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new CheckDesignQuery(postId, request?.Views, request?.Culture, request?.Design), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design/preview: the studio's Output pane.</summary>
public sealed class Preview : IEndpoint
{
    public sealed record PreviewRequest(bool Amp = false, string? Culture = null, DesignViews? Views = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/preview", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_PreviewDesign")
            .WithSummary("The draft (or the unsaved views sent) rendered as the responsive page or the AMP page.");

    private static async Task<Results<Ok<PreviewDto>, ProblemHttpResult>> HandleAsync(int postId, PreviewRequest? request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new PreviewDesignQuery(postId, request?.Amp ?? false, request?.Culture, request?.Views), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design/publish.</summary>
public sealed class Publish : IEndpoint
{
    public sealed record PublishRequest(int Version, string? Culture = null);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/publish", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Publish)
            .WithName("ContentDesigner_PublishDesign")
            .WithSummary("Publish the draft once every check passes (409 lists the errors); 409 while the post's NewsRoom project is not InProduction, ReadyForDesk or Published, 503 when that cannot be told; pins the content revisions it was checked against.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, PublishRequest request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new PublishDesignCommand(postId, request.Version, request.Culture), ct)).ToOkResult();
}

/// <summary>POST /posts/{postId}/design/unpublish.</summary>
public sealed class Unpublish : IEndpoint
{
    public sealed record UnpublishRequest(int Version);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:int}/design/unpublish", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Publish)
            .WithName("ContentDesigner_UnpublishDesign")
            .WithSummary("Take the published design down; the public pages answer 404.");

    private static async Task<Results<Ok<PostDesignDto>, ProblemHttpResult>> HandleAsync(int postId, UnpublishRequest request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new UnpublishDesignCommand(postId, request.Version), ct)).ToOkResult();
}

/// <summary>DELETE /posts/{postId}/design?version=.</summary>
public sealed class Delete : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/posts/{postId:int}/design", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Arrange)
            .WithName("ContentDesigner_DeleteDesign")
            .WithSummary("Delete the post's design and its published state.");

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(int postId, int version, ISender sender, CancellationToken ct) =>
        (await sender.Send(new DeleteDesignCommand(postId, version), ct)).ToNoContentResult();
}

/// <summary>GET /posts/{postId}/workspace?culture=: what the studio opens a post with.</summary>
public sealed class Workspace : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/posts/{postId:int}/workspace", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetWorkspace")
            .WithSummary("The studio's workspace of a post: the post and its contents (Content), the renderer's document, the kind's values, the fields' style tokens and the design (null when none).");

    private static async Task<Results<Ok<WorkspaceDto>, ProblemHttpResult>> HandleAsync(int postId, ISender sender, CancellationToken ct, string? culture = null) =>
        (await sender.Send(new GetWorkspaceQuery(postId, culture), ct)).ToOkResult();
}

/// <summary>GET /posts/{postId}/events?after=: the post's change feed, for the studio's hot reload.</summary>
public sealed class Events : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/posts/{postId:int}/events", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetPostEvents")
            .WithSummary("The post's change feed after an event id (Content's): read the next page with after = last.");

    private static async Task<Results<Ok<FeedPage>, ProblemHttpResult>> HandleAsync(int postId, ISender sender, CancellationToken ct, long after = 0) =>
        (await sender.Send(new GetPostEventsQuery(postId, after), ct)).ToOkResult();
}

internal static class Problems
{
    public static ProblemHttpResult UnknownView(string view) =>
        TypedResults.Problem($"'{view}' is not a view (mobile, tablet, desktop, amp).", statusCode: StatusCodes.Status400BadRequest);
}
