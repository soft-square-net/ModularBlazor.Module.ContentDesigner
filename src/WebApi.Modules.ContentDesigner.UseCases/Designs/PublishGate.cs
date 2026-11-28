using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

/// <summary>What NewsRoom says about publishing a post's design (<see cref="PublishGateDto.Status"/>).</summary>
public static class PublishGateStatuses
{
    /// <summary>The deployment has no NewsRoom: the workflow does not gate publishing.</summary>
    public const string NoNewsRoom = "no-newsroom";

    /// <summary>The post has no project: the workflow does not gate publishing.</summary>
    public const string NoProject = "no-project";

    /// <summary>The post's project is in a state that allows publishing.</summary>
    public const string Open = "open";

    /// <summary>The post's project is in a state that does not.</summary>
    public const string Closed = "closed";

    /// <summary>Whether the post has a project, or its state, cannot be told now.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// Whether the newsroom workflow lets the post's design be published now. It says nothing about the caller's rights, and
/// nothing about taking a design down, which is never gated. <see cref="Message"/> says why when it does not
/// <see cref="Allows"/>.
/// </summary>
public sealed record PublishGateDto(bool Allows, string Status, Guid? ProjectId = null, string? ProjectState = null, string? Message = null);

/// <summary>
/// A post with a NewsRoom project may be published only while the project is InProduction or ReadyForDesk. A post without
/// a project, and a deployment without NewsRoom, are not gated. When that cannot be told (NewsRoom does not answer, or
/// nobody asked may see every project) publishing waits: letting it through would skip the workflow for the very posts it
/// controls.
/// </summary>
public sealed class PublishGate(INewsRoomApi newsRoom)
{
    // NewsRoom's state names (its Contracts package is not published, so no reference).
    private static readonly string[] PublishableStates = ["InProduction", "ReadyForDesk"];

    /// <summary>The caller's rights with the workflow's say, asked only for a caller who may publish at all.</summary>
    public async Task<PostRightsDto> AddToAsync(PostRightsDto rights, CancellationToken ct) =>
        rights.CanPublish ? rights with { Publishing = await EvaluateAsync(rights.PostId, ct) } : rights;

    public async Task<PublishGateDto> EvaluateAsync(int postId, CancellationToken ct)
    {
        var found = await newsRoom.GetPostProjectsAsync(postId, ct);
        switch (found.Answer)
        {
            case NewsRoomAnswer.NotInstalled:
                return new PublishGateDto(true, PublishGateStatuses.NoNewsRoom);
            case NewsRoomAnswer.Unreachable:
                return new PublishGateDto(false, PublishGateStatuses.Unknown, Message:
                    $"NewsRoom is not answering, so it cannot be told whether the newsroom workflow lets post {postId} be published. Try again shortly.");
            case NewsRoomAnswer.CannotSee:
                return new PublishGateDto(false, PublishGateStatuses.Unknown, Message:
                    $"It cannot be told whether post {postId} has a NewsRoom project: neither the Content Designer's service client nor your account may see every project. " +
                    "An administrator has to set ContentDesigner:ServiceAuthority, ServiceClientId and ServiceClientSecret, and grant that client newsroom.projects on the whole tenant.");
        }
        if (found.Projects.Count == 0) return new PublishGateDto(true, PublishGateStatuses.NoProject);

        var open = found.Projects.FirstOrDefault(p => PublishableStates.Contains(p.State, StringComparer.OrdinalIgnoreCase));
        if (open is not null) return new PublishGateDto(true, PublishGateStatuses.Open, open.Id, open.State);

        var project = found.Projects[0]; // NewsRoom lists the newest first
        return new PublishGateDto(false, PublishGateStatuses.Closed, project.Id, project.State,
            $"The post's NewsRoom project is {project.State}: its design can be published only while the project is InProduction or ReadyForDesk.");
    }
}
