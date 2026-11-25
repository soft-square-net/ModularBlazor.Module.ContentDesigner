using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>One page of the tenant's post designs, the latest change first; optionally of one kind.</summary>
public sealed record ListDesignsQuery(int Page = 1, int PageSize = 20, string? Kind = null) : IQuery<Result<DesignPage>>;

public sealed class ListDesignsHandler(IReadRepository<PostDesign> designs, IOptions<ContentDesignerOptions> options)
    : IQueryHandler<ListDesignsQuery, Result<DesignPage>>
{
    public async ValueTask<Result<DesignPage>> Handle(ListDesignsQuery query, CancellationToken ct)
    {
        var paging = DesignResults.CheckPage(query.Page, query.PageSize, options.Value.MaxPageSize);
        if (!paging.IsSuccess) return paging.As<DesignPage>();
        var spec = new DesignsPageSpec(query.Page, query.PageSize, query.Kind);
        var items = await designs.ListAsync(spec, ct);
        return new DesignPage([.. items.Select(DesignSummary.From)], query.Page, query.PageSize, await designs.CountAsync(spec, ct));
    }
}

/// <summary>A post's design.</summary>
public sealed record GetDesignQuery(int PostId) : IQuery<Result<PostDesignDto>>;

public sealed class GetDesignHandler(IReadRepository<PostDesign> designs) : IQueryHandler<GetDesignQuery, Result<PostDesignDto>>
{
    public async ValueTask<Result<PostDesignDto>> Handle(GetDesignQuery query, CancellationToken ct) =>
        await designs.FirstOrDefaultAsync(new DesignByPostSpec(query.PostId), ct) is { } design
            ? PostDesignDto.From(design)
            : Result<PostDesignDto>.NotFound(DesignResults.NoDesign(query.PostId));
}

/// <summary>
/// Run the checks on the draft (all views, or the ones named) against the post as it is now in Content: the design
/// rules, DynamicFields' validation, the JSON-LD essentials and the AMP page. With <see cref="Design"/>, those unsaved
/// views instead of the stored draft (the studio checks while editing).
/// </summary>
public sealed record CheckDesignQuery(int PostId, IReadOnlyList<ViewKind>? Views = null, string? Culture = null, DesignViews? Design = null)
    : IQuery<Result<DesignReport>>;

public sealed class CheckDesignHandler(IReadRepository<PostDesign> designs, IContentApi content, DesignWorkbench workbench)
    : IQueryHandler<CheckDesignQuery, Result<DesignReport>>
{
    public async ValueTask<Result<DesignReport>> Handle(CheckDesignQuery query, CancellationToken ct) =>
        await DesignResults.Calling<DesignReport>(async () =>
        {
            if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(query.PostId), ct) is not { } design)
                return Result<DesignReport>.NotFound(DesignResults.NoDesign(query.PostId));
            if (await content.GetPostAsync(query.PostId, ct) is not { } post)
                return Result<DesignReport>.NotFound(DesignResults.NoPost(query.PostId));
            var views = query.Design?.Checked() ?? design.Draft;
            var (problems, jsonLd) = await workbench.CheckWithJsonLdAsync(views, design.KindVersion, post, query.Views, query.Culture, ct);
            return DesignReport.From(problems, jsonLd);
        });
}

/// <summary>
/// The draft rendered as the responsive page or the AMP page, over the post's working document. With
/// <see cref="Views"/>, those unsaved views instead of the stored draft (the studio's Output pane while editing).
/// </summary>
public sealed record PreviewDesignQuery(int PostId, bool Amp = false, string? Culture = null, DesignViews? Views = null) : IQuery<Result<PreviewDto>>;

public sealed class PreviewDesignHandler(IReadRepository<PostDesign> designs, IContentApi content, DesignWorkbench workbench)
    : IQueryHandler<PreviewDesignQuery, Result<PreviewDto>>
{
    public async ValueTask<Result<PreviewDto>> Handle(PreviewDesignQuery query, CancellationToken ct) =>
        await DesignResults.Calling<PreviewDto>(async () =>
        {
            DesignViews views;
            if (query.Views is { } unsaved) views = unsaved.Checked();
            else if (await designs.FirstOrDefaultAsync(new DesignByPostSpec(query.PostId), ct) is { } design) views = design.Draft;
            else return Result<PreviewDto>.NotFound(DesignResults.NoDesign(query.PostId));

            if (await content.GetPostAsync(query.PostId, ct) is not { } post)
                return Result<PreviewDto>.NotFound(DesignResults.NoPost(query.PostId));
            var page = await workbench.RenderDraftAsync(views, post, query.Amp, query.Culture, ct);
            return new PreviewDto(page.Html, page.Amp, page.CssBytes, page.AmpIssues);
        });
}
