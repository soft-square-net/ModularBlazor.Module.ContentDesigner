using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>
/// The post kinds a template or a design can be for: DynamicFields' published root post fields, each with its static URL
/// prefix, through the gateway with the caller's token.
/// </summary>
public sealed record ListPostKindsQuery : IQuery<Result<IReadOnlyList<PostKindInfo>>>;

public sealed class ListPostKindsHandler(IFieldsApi fields) : IQueryHandler<ListPostKindsQuery, Result<IReadOnlyList<PostKindInfo>>>
{
    public async ValueTask<Result<IReadOnlyList<PostKindInfo>>> Handle(ListPostKindsQuery query, CancellationToken ct) =>
        await DesignResults.Calling<IReadOnlyList<PostKindInfo>>(async () =>
            Result<IReadOnlyList<PostKindInfo>>.Success([.. (await fields.ListPostKindsAsync(ct)).OrderBy(k => k.Title, StringComparer.OrdinalIgnoreCase)]));
}

/// <summary>
/// The Organization units' names, for the studio's unit filters. Empty when Organization is not installed or does not
/// answer the caller.
/// </summary>
public sealed record ListUnitNamesQuery : IQuery<Result<IReadOnlyList<UnitName>>>;

public sealed class ListUnitNamesHandler(IOrganizationApi organization) : IQueryHandler<ListUnitNamesQuery, Result<IReadOnlyList<UnitName>>>
{
    public async ValueTask<Result<IReadOnlyList<UnitName>>> Handle(ListUnitNamesQuery query, CancellationToken ct) =>
        Result<IReadOnlyList<UnitName>>.Success([.. (await organization.ListUnitsAsync(ct)).OrderBy(u => u.NameEn, StringComparer.OrdinalIgnoreCase)]);
}
