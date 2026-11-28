using System.Text.Json;
using System.Text.RegularExpressions;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;
using RenderBlock = soft.square.net.ContentDesigner.Rendering.Model.RenderBlock;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// One problem Content refused a step with. <see cref="ContentId"/> is the content it checked (null: it checked the
/// post), <see cref="Path"/> points into what it checked, <see cref="NodeId"/> is known only from a 422 answer, and
/// <see cref="Action"/> is the step refused (null when the answer did not say).
/// </summary>
public sealed record ContentRefusal(string? ContentId, string? NodeId, string? Action, string Path, string Message);

/// <summary>A row of the Problems panel: a finding of the designer's checks, of Content's, or of both.</summary>
public sealed record StudioProblem(DesignProblem? Design, ContentRefusal? Refusal, string? NodeId, string Message)
{
    /// <summary>Whether Content refused a step because of it.</summary>
    public bool Refused => Refusal is not null;

    /// <summary>A problem only Content reports is an error: it blocks the step in Content.</summary>
    public ProblemSeverity Severity => Design?.Severity ?? ProblemSeverity.Error;

    /// <summary>The view it is in; null for the whole design.</summary>
    public string? View => Design?.View?.Value.ToString();

    public string Code => Design?.Code ?? ContentRefusals.Code;

    public string Path => Design?.Path ?? Refusal!.Path;
}

/// <summary>
/// What Content refused and has not accepted since, from the post's change feed (<c>content.validation-failed</c> rows,
/// read in order) and from 422 answers, merged with the designer's own checks. A later change of a content (or a step
/// it passes) takes its refusals away; the same goes for the post.
/// </summary>
public sealed partial class ContentRefusals
{
    public const string Code = "content-refused";
    private const string FailedType = "content.validation-failed";
    private const string PostKey = "";

    private readonly Dictionary<string, List<ContentRefusal>> _open = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ContentRefusal> All => [.. _open.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value)];

    public void Clear() => _open.Clear();

    /// <summary>
    /// Takes one feed row, in the feed's order. True when it is a new refusal. <paramref name="nodeOf"/> gives a
    /// content's node id (for the refusals of the post's check that a change of that content answers).
    /// </summary>
    public bool Apply(FeedEvent e, Func<string, string?> nodeOf)
    {
        var contentId = e.ContentId?.ToString();
        switch (e.Type)
        {
            case FailedType:
                var refusals = Parse(contentId, e.Detail);
                if (refusals.Count == 0) return false;
                _open[contentId ?? PostKey] = refusals;
                return true;
            case "content.changed" or "content.state-changed" or "content.deleted" or "content.detached" when contentId is not null:
                _open.Remove(contentId);
                if (nodeOf(contentId) is { } node && _open.TryGetValue(PostKey, out var post))
                {
                    post.RemoveAll(r => r.NodeId == node);
                    if (post.Count == 0) _open.Remove(PostKey);
                }
                return false;
            case "post.changed" or "post.state-changed":
                _open.Remove(PostKey);
                return false;
            default:
                return false;
        }
    }

    /// <summary>Takes the problems of a 422 answer; <paramref name="contentId"/> null when it was the post that was refused.</summary>
    public void Add(string? contentId, IReadOnlyList<ContentRefusal> problems)
    {
        if (problems.Count > 0) _open[contentId ?? PostKey] = [.. problems];
    }

    /// <summary>The <c>problems</c> of a 422 body (<c>[{ path, message, nodeId }]</c>); empty for any other body.</summary>
    public static IReadOnlyList<ContentRefusal> FromAnswer(string? body, string? contentId = null)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{')) return [];
        try
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("problems", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            List<ContentRefusal> found = [];
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string? Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (Text("message") is { Length: > 0 } message)
                    found.Add(new ContentRefusal(contentId, Text("nodeId"), null, Text("path") ?? "", message));
            }
            return found;
        }
        catch (JsonException) { return []; }
    }

    /// <summary>A feed row's detail: "{action}: {path} {message}; {path} {message}" (Content writes the first few).</summary>
    internal static List<ContentRefusal> Parse(string? contentId, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return [];
        var colon = detail.IndexOf(": ", StringComparison.Ordinal);
        var action = colon > 0 && !detail[..colon].Contains('/') ? detail[..colon] : null;
        var rest = action is null ? detail : detail[(colon + 2)..];
        // A new problem starts at a "; " followed by a path, so a message may hold semicolons.
        return [.. NextProblem().Split(rest).Where(s => !string.IsNullOrWhiteSpace(s)).Select(segment =>
        {
            var space = segment.IndexOf(' ');
            var hasPath = segment.StartsWith('/') && space > 0;
            var path = hasPath ? segment[..space] : "";
            return new ContentRefusal(contentId, null, action, path, (hasPath ? segment[(space + 1)..] : segment).Trim());
        })];
    }

    /// <summary>
    /// The designer's findings with Content's joined in: a finding both report (same node, same place in the node, same
    /// message) is one row that says Content refused it; what only Content reports follows, once each.
    /// </summary>
    public static IReadOnlyList<StudioProblem> Merge(IReadOnlyList<DesignProblem> design, IReadOnlyList<ContentRefusal> refusals,
        RenderDocument? document)
    {
        var refused = new Dictionary<(string, string, string), ContentRefusal>();
        foreach (var refusal in refusals)
        {
            var key = Key(refusal.NodeId ?? NodeOf(document, refusal), refusal.Path, refusal.Message);
            // The same problem from the content's check and the post's check, or from the feed and a 422: once.
            if (!refused.TryGetValue(key, out var kept) || kept.Action is null) refused[key] = refusal;
        }

        List<StudioProblem> merged = [];
        var joined = new HashSet<(string, string, string)>();
        foreach (var problem in design)
        {
            var key = Key(problem.NodeId, problem.Path, problem.Message);
            var refusal = refused.GetValueOrDefault(key);
            if (refusal is not null) joined.Add(key);
            merged.Add(new StudioProblem(problem, refusal, problem.NodeId, problem.Message));
        }
        merged.AddRange(refused.Where(p => !joined.Contains(p.Key))
            .Select(p => new StudioProblem(null, p.Value, p.Value.NodeId ?? NodeOf(document, p.Value), p.Value.Message)));
        return merged;
    }

    /// <summary>The node a refusal is in: the content's (or the post's) node its path's /children/{i} steps lead to.</summary>
    public static string? NodeOf(RenderDocument? document, ContentRefusal refusal)
    {
        if (document is null) return null;
        RenderBlock? block = refusal.ContentId is { } id
            ? document.Contents.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;
        if (refusal.ContentId is not null && block is null) return null;
        IReadOnlyList<RenderBlock> children = block is null ? document.Contents : block.Children ?? [];
        var segments = refusal.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length && segments[i] == "children"; i += 2)
        {
            if (!int.TryParse(segments[i + 1], out var index) || index < 0 || index >= children.Count) break;
            block = children[index];
            children = block.Children ?? [];
        }
        return block?.NodeId;
    }

    /// <summary>One problem's identity: its node, its path inside the node (the way to the node left off) and its message.</summary>
    private static (string, string, string) Key(string? nodeId, string? path, string message) =>
        (nodeId ?? "", WayToNode().Replace(path ?? "", ""), message.Trim().TrimEnd('.'));

    [GeneratedRegex(@"; (?=/)")]
    private static partial Regex NextProblem();

    [GeneratedRegex(@"^(/children/\d+)+")]
    private static partial Regex WayToNode();
}
