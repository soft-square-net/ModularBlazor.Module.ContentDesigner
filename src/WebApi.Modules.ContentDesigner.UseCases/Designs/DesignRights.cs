using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>Why the caller may not arrange a post (<see cref="PostRightsDto.Reason"/>).</summary>
public static class PostRightsReasons
{
    /// <summary>The post is in no unit yet: only the moderator and the Desk may arrange or publish it.</summary>
    public const string NoUnit = "no-unit";

    /// <summary>The caller's rights cover neither the post's unit (or one above it) nor its press or categories.</summary>
    public const string NoRight = "no-right";

    /// <summary>Content does not show the caller the post (any more).</summary>
    public const string PostHidden = "post-hidden";
}

/// <summary>
/// What the caller may do with one post's design right now (grants count only while they run). <see cref="Publishing"/>
/// is the newsroom workflow's say on publishing, asked only for a caller who <see cref="CanPublish"/> (else null);
/// publishing needs both.
/// </summary>
public sealed record PostRightsDto(int PostId, bool CanArrange, bool CanPublish, string? Reason, Guid? UnitId, PublishGateDto? Publishing = null);

/// <summary>
/// The fine-grained rights on one post's design. The endpoints check that the caller holds the permission somewhere; this
/// checks it for <em>this</em> post, with the caller's grants as they are now: <c>contentdesigner.arrange</c> and
/// <c>contentdesigner.publish</c> must cover one of the post's scopes: its unit or a unit above it
/// (<c>unit:{id}</c>, from the unit path Content sends), or, for compatibility until the newsroom workflow ships, its
/// press or one of its categories. A grant on the whole tenant covers every post. So a shift's rights, which start at
/// check-in and end at check-out, open and close the studio with the shift.
/// <para>A post in no unit yet is the moderator's and the Desk's alone: the caller also needs one of
/// <see cref="ContentDesignerOptions.UnitlessEditorPermissions"/> on the post's press or categories.</para>
/// </summary>
public sealed class DesignRights(IAccessScopes access, IContentApi content, IOptions<ContentDesignerOptions> options)
{
    /// <summary>The post's scopes: its press, its categories, and its unit with every unit above it.</summary>
    public static IReadOnlyList<string> ScopesOf(ContentPost post)
    {
        var scopes = new List<string>();
        if (post.PressId is { Length: > 0 } press) scopes.Add(AccessScope.Press(press));
        scopes.AddRange((post.Categories ?? []).Distinct(StringComparer.Ordinal).Select(AccessScope.Category));
        var units = UnitScopes.For(post.UnitPath);
        if (units.Count > 0) scopes.AddRange(units);
        else if (post.UnitId is { } unit) scopes.Add(UnitScopes.Of(unit));
        return scopes;
    }

    /// <summary>
    /// The rights on a post as Content showed it to the caller; <paramref name="post"/> null when it showed none, which
    /// leaves only rights on the whole tenant.
    /// </summary>
    public async Task<PostRightsDto> EvaluateAsync(int postId, ContentPost? post, CancellationToken ct)
    {
        IReadOnlyList<string> scopes = post is null ? [] : ScopesOf(post);
        var arrange = await access.HasPermissionAsync(ContentDesignerPermissions.Arrange, scopes, ct);
        var publish = await access.HasPermissionAsync(ContentDesignerPermissions.Publish, scopes, ct);
        if (post is { UnitId: null } && (arrange || publish) && !await IsUnitlessEditorAsync(scopes, ct))
            return new PostRightsDto(postId, false, false, PostRightsReasons.NoUnit, null);
        var reason = arrange ? null : post is null ? PostRightsReasons.PostHidden : PostRightsReasons.NoRight;
        return new PostRightsDto(postId, arrange, publish, reason, post?.UnitId);
    }

    /// <summary>Success when the caller holds <paramref name="permission"/> (arrange or publish) on the post now; else 403.</summary>
    public async Task<Result> RequireAsync(string permission, int postId, ContentPost? post, CancellationToken ct)
    {
        var rights = await EvaluateAsync(postId, post, ct);
        var allowed = permission == ContentDesignerPermissions.Publish ? rights.CanPublish : rights.CanArrange;
        return allowed ? Result.Success() : Result.Forbidden(Refusal(permission, rights));
    }

    /// <summary><see cref="RequireAsync(string, int, ContentPost?, CancellationToken)"/>, reading the post from Content.</summary>
    public async Task<Result> RequireAsync(string permission, int postId, CancellationToken ct) =>
        (await RequireAndReadAsync(permission, postId, ct)).Allowed;

    /// <summary>As <see cref="RequireAsync(string, int, CancellationToken)"/>, also handing back the post it read.</summary>
    public async Task<(Result Allowed, ContentPost? Post)> RequireAndReadAsync(string permission, int postId, CancellationToken ct)
    {
        ContentPost? post;
        try
        {
            post = await content.GetPostAsync(postId, ct);
        }
        catch (ExternalApiException e)
        {
            return (e.ToResult<PostRightsDto>().ToFailure(), null);
        }
        return (await RequireAsync(permission, postId, post, ct), post);
    }

    private async Task<bool> IsUnitlessEditorAsync(IReadOnlyList<string> scopes, CancellationToken ct)
    {
        foreach (var permission in options.Value.UnitlessEditorPermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (await access.HasPermissionAsync(permission, scopes, ct)) return true;
        return false;
    }

    private static string Refusal(string permission, PostRightsDto rights) => rights.Reason switch
    {
        PostRightsReasons.NoUnit =>
            $"Post {rights.PostId} is in no unit yet: only the moderator and the Desk may change its design ({permission}). The workflow assigns the unit.",
        PostRightsReasons.PostHidden => $"You may not see post {rights.PostId} in Content now, so you may not change its design ({permission}).",
        _ => $"You may not change the design of post {rights.PostId} now ({permission}): your rights cover neither its unit nor its press or categories."
    };
}

/// <summary>The caller's rights on one post's design: the studio asks again after a refusal, until they come back.</summary>
public sealed record GetPostRightsQuery(int PostId) : IQuery<Result<PostRightsDto>>;

public sealed class GetPostRightsHandler(IContentApi content, DesignRights rights, PublishGate gate)
    : IQueryHandler<GetPostRightsQuery, Result<PostRightsDto>>
{
    public async ValueTask<Result<PostRightsDto>> Handle(GetPostRightsQuery query, CancellationToken ct) =>
        await DesignResults.Calling<PostRightsDto>(async () =>
            await gate.AddToAsync(await rights.EvaluateAsync(query.PostId, await content.GetPostAsync(query.PostId, ct), ct), ct));
}
