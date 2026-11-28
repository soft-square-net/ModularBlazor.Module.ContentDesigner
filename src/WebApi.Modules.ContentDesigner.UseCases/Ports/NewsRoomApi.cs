namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

/// <summary>A NewsRoom post project, as far as this module reads it.</summary>
public sealed record NewsRoomProject(Guid Id, string State);

/// <summary>How sure the answer to "does this post have a project?" is.</summary>
public enum NewsRoomAnswer
{
    /// <summary>The deployment has no NewsRoom (its routes answer 404).</summary>
    NotInstalled,

    /// <summary><see cref="PostProjects.Projects"/> is the truth: an empty list means the post has no project.</summary>
    Known,

    /// <summary>NewsRoom did not answer (unreachable, too slow, or failing).</summary>
    Unreachable,

    /// <summary>
    /// NewsRoom answered, but neither the module's service client nor the caller may see every project, so "none found"
    /// proves nothing.
    /// </summary>
    CannotSee
}

public sealed record PostProjects(NewsRoomAnswer Answer, IReadOnlyList<NewsRoomProject> Projects)
{
    public static PostProjects Of(NewsRoomAnswer answer) => new(answer, []);

    public static PostProjects Known(params NewsRoomProject[] projects) => new(NewsRoomAnswer.Known, projects);
}

/// <summary>The NewsRoom module, an optional neighbour. It does not throw for a failed call: the answer says what happened.</summary>
public interface INewsRoomApi
{
    Task<PostProjects> GetPostProjectsAsync(int postId, CancellationToken ct);
}
