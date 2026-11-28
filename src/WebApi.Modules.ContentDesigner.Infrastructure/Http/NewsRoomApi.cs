using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;

/// <summary>
/// <see cref="INewsRoomApi"/> over NewsRoom's HTTP API (<c>/api/newsroom</c>), through the gateway; no package of NewsRoom
/// is referenced. NewsRoom lists only the projects the asker may see, so it is asked as the module's service client first
/// (when one is configured), then as the caller; an empty list counts only from someone who may see every project
/// (<c>GET /access/mine</c>: moderate, projects or workflow on the whole tenant). The list itself answers 404 only when
/// NewsRoom is not there.
/// </summary>
public sealed class NewsRoomApi(Gateway gateway, ILogger<NewsRoomApi> logger) : INewsRoomApi
{
    private const string Service = "NewsRoom";

    /// <summary>All the asking together: the studio waits for it when it opens a post and reads its rights.</summary>
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(5);

    private static readonly string[] SeesProjects = ["newsroom.moderate", "newsroom.projects", "newsroom.workflow"];

    private sealed record ProjectWire(Guid Id, string? State);

    private sealed record ProjectPageWire(List<ProjectWire>? Items);

    private sealed record ReachWire(string? Permission, bool TenantWide);

    private sealed record AccessWire(List<ReachWire>? Permissions);

    public async Task<PostProjects> GetPostProjectsAsync(int postId, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeLimit);
        try
        {
            if (await gateway.ServiceTokenAsync(limit.Token) is { } service &&
                await AskAsync(postId, service, limit.Token) is { Answer: not NewsRoomAnswer.CannotSee } answer)
                return answer;
            return await AskAsync(postId, null, limit.Token);
        }
        catch (Exception e) when (e is ExternalApiException or JsonException or NotSupportedException or HttpRequestException
                                      || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(e, "NewsRoom did not answer about post {PostId}.", postId);
            return PostProjects.Of(NewsRoomAnswer.Unreachable);
        }
    }

    /// <summary>One identity's answer: <paramref name="bearer"/> is the service's token, null the caller's.</summary>
    private async Task<PostProjects> AskAsync(int postId, string? bearer, CancellationToken ct)
    {
        using var list = await gateway.SendAsync(Service, HttpMethod.Get, $"/api/newsroom/projects?postId={postId}&pageSize=20", null, ct, bearer);
        if (list.StatusCode == HttpStatusCode.NotFound) return PostProjects.Of(NewsRoomAnswer.NotInstalled);
        if (Refused(list)) return PostProjects.Of(NewsRoomAnswer.CannotSee);
        if (!list.IsSuccessStatusCode) throw await Gateway.FailureAsync(Service, list, ct);
        var page = await list.Content.ReadFromJsonAsync<ProjectPageWire>(Gateway.Json, ct);
        if (page?.Items is not { } items) throw new JsonException("NewsRoom's project list has no items.");
        if (items.Count > 0) return PostProjects.Known([.. items.Select(p => new NewsRoomProject(p.Id, p.State ?? ""))]);

        using var mine = await gateway.SendAsync(Service, HttpMethod.Get, "/api/newsroom/access/mine", null, ct, bearer);
        if (Refused(mine)) return PostProjects.Of(NewsRoomAnswer.CannotSee);
        if (!mine.IsSuccessStatusCode) throw await Gateway.FailureAsync(Service, mine, ct);
        var access = await mine.Content.ReadFromJsonAsync<AccessWire>(Gateway.Json, ct);
        var seesAll = access?.Permissions?.Any(p => p.TenantWide && SeesProjects.Contains(p.Permission, StringComparer.OrdinalIgnoreCase)) == true;
        return seesAll ? PostProjects.Known() : PostProjects.Of(NewsRoomAnswer.CannotSee);
    }

    private static bool Refused(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
