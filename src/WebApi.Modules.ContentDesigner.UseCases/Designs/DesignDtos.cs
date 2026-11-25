using soft.square.net.ContentDesigner.Rendering.Amp;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>The published state of a design: its views, the kind version and content revisions it was checked against.</summary>
public sealed record PublishedDesignDto(int DesignVersion, DesignViews Views, int KindVersion, IReadOnlyDictionary<string, int> Pins,
    string PublishedBy, DateTimeOffset PublishedAt)
{
    public static PublishedDesignDto? From(PublishedDesign? published) => published is null ? null :
        new(published.DesignVersion, published.Views, published.KindVersion, published.Pins, published.PublishedBy, published.PublishedAt);
}

/// <summary>A post's design as the API returns it. <see cref="Version"/> goes back with every change.</summary>
public sealed record PostDesignDto(
    int Id,
    int PostId,
    string PublicId,
    string Kind,
    int KindVersion,
    int? BasedOnTemplateId,
    DesignViews Draft,
    PublishedDesignDto? Published,
    int Version,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    public static PostDesignDto From(PostDesign d) =>
        new(d.Id, d.PostId, d.PublicId, d.Kind, d.KindVersion, d.BasedOnTemplateId, d.Draft, PublishedDesignDto.From(d.Published),
            d.Version, d.CreatedBy, d.CreatedAt, d.UpdatedBy, d.UpdatedAt);
}

public sealed record DesignSummary(int Id, int PostId, string PublicId, string Kind, bool Published, int? PublishedVersion,
    int Version, string UpdatedBy, DateTimeOffset UpdatedAt)
{
    public static DesignSummary From(PostDesign d) =>
        new(d.Id, d.PostId, d.PublicId, d.Kind, d.Published is not null, d.Published?.DesignVersion, d.Version, d.UpdatedBy, d.UpdatedAt);
}

public sealed record DesignPage(IReadOnlyList<DesignSummary> Items, int Page, int PageSize, int Total);

/// <summary>The checks' findings: <see cref="Valid"/> when there is no error (warnings do not block publication).</summary>
public sealed record DesignReport(bool Valid, int Errors, int Warnings, IReadOnlyList<DesignProblem> Problems)
{
    public static DesignReport From(IReadOnlyList<DesignProblem> problems)
    {
        var errors = problems.Count(p => p.Severity == ProblemSeverity.Error);
        return new(errors == 0, errors, problems.Count - errors, problems);
    }
}

/// <summary>A rendered page for the studio's Output pane: the HTML, and for AMP its CSS size and broken rules.</summary>
public sealed record PreviewDto(string Html, bool Amp, int CssBytes, IReadOnlyList<AmpIssue> AmpIssues);

/// <summary>Small helpers the design use cases share.</summary>
public static class DesignResults
{
    public static Result CheckPage(int page, int pageSize, int maxPageSize) =>
        page < 1 ? Result.Invalid(new ValidationError(nameof(page), "The page starts at 1."))
        : pageSize < 1 || pageSize > maxPageSize ? Result.Invalid(new ValidationError(nameof(pageSize), $"The page size is 1 to {maxPageSize}."))
        : Result.Success();

    public static string NoDesign(int postId) => $"Post {postId} has no design yet; open it in the designer first.";

    public static string Stale(string what, int current, int read) =>
        $"{what} is at version {current}, not {read}; reload it and try again.";

    public static string NoPost(int postId) => $"Post {postId} does not exist in Content, or you may not see it.";

    /// <summary>Runs a call to another module, turning its failure into a result.</summary>
    public static async Task<Result<T>> Calling<T>(Func<Task<Result<T>>> call)
    {
        try { return await call(); }
        catch (ExternalApiException e) { return e.ToResult<T>(); }
    }
}
