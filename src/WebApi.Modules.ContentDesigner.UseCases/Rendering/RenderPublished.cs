using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Rendering;

/// <summary>A public page: its HTML.</summary>
public sealed record RenderedHtml(string Html);

/// <summary>
/// The published design of a published post, as the canonical page or the AMP page, in a culture (the post's source
/// language when none is asked). Anonymous: the post comes from Content's public API. Cached per tenant, post,
/// publication of the post (its publishedAt), publication of the design and culture.
/// </summary>
public sealed record RenderPublishedQuery(string PublicId, bool Amp = false, string? Culture = null) : IQuery<Result<RenderedHtml>>;

public sealed class RenderPublishedHandler(
    IReadRepository<PostDesign> designs,
    IContentApi content,
    DesignWorkbench workbench,
    IMemoryCache cache,
    ITenantContext tenant,
    IOptions<ContentDesignerOptions> options) : IQueryHandler<RenderPublishedQuery, Result<RenderedHtml>>
{
    public async ValueTask<Result<RenderedHtml>> Handle(RenderPublishedQuery query, CancellationToken ct) =>
        await DesignResults.Calling<RenderedHtml>(async () =>
        {
            if (await designs.FirstOrDefaultAsync(new DesignByPublicIdSpec(query.PublicId), ct) is not { Published: { } published })
                return Result<RenderedHtml>.NotFound($"Post {query.PublicId} has no published design.");
            if (await content.GetPublishedAsync(query.PublicId, query.Culture, ct) is not { } post)
                return Result<RenderedHtml>.NotFound($"Post {query.PublicId} is not published.");

            var culture = query.Culture ?? post.ServedLanguages.FirstOrDefault() ?? "en";
            var key = string.Join('|', "contentdesigner-render", tenant.TenantId, query.PublicId, post.PublishedAt.UtcTicks,
                published.DesignVersion, culture, query.Amp ? "amp" : "web");
            if (cache.TryGetValue(key, out RenderedHtml? hit) && hit is not null) return hit;

            var page = await workbench.RenderPublishedAsync(published, post, query.Amp, culture, ct);
            var html = new RenderedHtml(page.Html);
            if (options.Value.RenderCacheMinutes > 0)
                cache.Set(key, html, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.RenderCacheMinutes),
                    Size = page.Html.Length
                });
            return html;
        });
}
