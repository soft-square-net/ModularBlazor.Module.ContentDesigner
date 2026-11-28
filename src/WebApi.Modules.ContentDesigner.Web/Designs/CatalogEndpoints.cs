using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;

/// <summary>GET /kinds: the post kinds (DynamicFields' published root post fields) with their URL prefixes.</summary>
public sealed class Kinds : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/kinds", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListPostKinds")
            .WithSummary("The post kinds a template or design can be for: DynamicFields' root post fields, with their URL prefixes.");

    private static async Task<Results<Ok<IReadOnlyList<PostKindInfo>>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct) =>
        (await sender.Send(new ListPostKindsQuery(), ct)).ToOkResult();
}

/// <summary>GET /units: the Organization units' names, for the studio's unit filters.</summary>
public sealed class Units : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/units", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_ListUnits")
            .WithSummary("The Organization units' names, for filters; empty when Organization is not there or does not answer the caller.");

    private static async Task<Results<Ok<IReadOnlyList<UnitName>>, ProblemHttpResult>> HandleAsync(ISender sender, CancellationToken ct) =>
        (await sender.Send(new ListUnitNamesQuery(), ct)).ToOkResult();
}

/// <summary>GET /posts/{postId}/rights: what the caller may do with the post's design now.</summary>
public sealed class Rights : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/posts/{postId:int}/rights", HandleAsync)
            .RequirePermission(ContentDesignerPermissions.Read)
            .WithName("ContentDesigner_GetPostRights")
            .WithSummary("What the caller may do with the post's design now: arrange and publish on its unit, press or categories (a shift's rights come and go).");

    private static async Task<Results<Ok<PostRightsDto>, ProblemHttpResult>> HandleAsync(int postId, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetPostRightsQuery(postId), ct)).ToOkResult();
}
