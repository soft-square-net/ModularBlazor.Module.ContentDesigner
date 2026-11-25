using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Rendering;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Rendering;

/// <summary>
/// GET /render/{publicId}?culture= and GET /render/{publicId}/amp?culture=: a published post with its published design,
/// as a whole HTML page (the canonical page, or the AMP page). Anonymous, like Content's public API; the tenant comes
/// from the host name or the X-Tenant header. The public routes of the site (/amp/{publicId}/{slug}) come with the Site
/// module and call these.
/// </summary>
public sealed class RenderPage : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/render/{publicId}", (string publicId, string? culture, ISender sender, CancellationToken ct) => HandleAsync(publicId, culture, false, sender, ct))
            .AllowAnonymous()
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .WithName("ContentDesigner_RenderPage")
            .WithSummary("The published post as its designed responsive HTML page.");

    internal static async Task<Results<ContentHttpResult, ProblemHttpResult>> HandleAsync(string publicId, string? culture, bool amp, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RenderPublishedQuery(publicId, amp, culture), ct);
        return result.IsSuccess ? TypedResults.Text(result.Value.Html, "text/html; charset=utf-8") : result.ToProblem();
    }
}

public sealed class RenderAmpPage : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapGet("/render/{publicId}/amp", (string publicId, string? culture, ISender sender, CancellationToken ct) => RenderPage.HandleAsync(publicId, culture, true, sender, ct))
            .AllowAnonymous()
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .WithName("ContentDesigner_RenderAmpPage")
            .WithSummary("The published post as its designed AMP page.");
}
