using System.Net;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>The studio when a shift opens and closes its rights, and when Content refuses a content.</summary>
public class ShiftRightsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DViews Starting { get; } = new(
        new DView([new DRegion("body", "Section")], [new DPlacement("content:a", "body", 0)]),
        DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile));

    /// <summary>The module's API as the studio sees it: rights that a test turns on and off, one design, a feed.</summary>
    private sealed class Api : HttpMessageHandler
    {
        public bool CanArrange { get; set; } = true;
        public bool HasDesign { get; set; } = true;
        public int Version { get; set; } = 3;
        public DViews Stored { get; set; } = Starting;
        public List<int> Saves { get; } = [];
        public int Opens { get; private set; }
        public List<object> Feed { get; } = [];

        private object Design() => new { postId = 7, publicId = "pub-7", kind = "post.news", version = Version, draft = StudioJson.ToApi(Stored) };

        /// <summary>The NewsRoom project's state; null: the post has none.</summary>
        public string? ProjectState { get; set; }
        public int PublishCalls { get; private set; }
        public int Unpublishes { get; private set; }

        private static bool Allowed(string state) => state is "InProduction" or "ReadyForDesk" or "Published";

        private object? Publishing() => ProjectState is not { } state ? null : new
        {
            allows = Allowed(state),
            status = Allowed(state) ? "open" : "closed",
            projectId = "0190b5a1-0000-7000-8000-0000000000aa",
            projectState = state,
            message = Allowed(state) ? null : $"The post's NewsRoom project is {state}."
        };

        private object Rights() => new
        {
            postId = 7, canArrange = CanArrange, canPublish = CanArrange, reason = CanArrange ? null : "no-right",
            publishing = CanArrange ? Publishing() : null
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/rights", StringComparison.Ordinal)) return Json(Rights());
            if (path.EndsWith("/workspace", StringComparison.Ordinal))
                return Json(new
                {
                    post = new { id = 7, publicId = "pub-7", title = "A post", kind = "post.news", kindVersion = 1, version = 1 },
                    contents = new[] { new { contentId = "11111111-1111-1111-1111-111111111111", nodeId = "n-1", fieldKey = "block.paragraph", stage = "accepted" } },
                    document = new { kind = "post.news", title = "A post", culture = "en", direction = "ltr", root = new { }, contents = Array.Empty<object>() },
                    kindValues = Array.Empty<object>(),
                    fieldStyles = new { },
                    design = HasDesign ? Design() : null,
                    rights = Rights()
                });
            if (path.EndsWith("/events", StringComparison.Ordinal))
            {
                var after = long.Parse(request.RequestUri.Query.Split("after=")[1].Split('&')[0], System.Globalization.CultureInfo.InvariantCulture);
                var events = Feed.Skip((int)after).ToList();
                return Json(new { events, last = Feed.Count, more = false });
            }
            if (path.EndsWith("/check", StringComparison.Ordinal)) return Json(new { valid = true, errors = 0, warnings = 0, problems = Array.Empty<object>() });
            if (path.EndsWith("/design/publish", StringComparison.Ordinal))
            {
                PublishCalls++;
                return ProjectState is null || Allowed(ProjectState)
                    ? Json(Design())
                    : Problem(HttpStatusCode.Conflict, $"The post's NewsRoom project is {ProjectState}.");
            }
            if (path.EndsWith("/design/unpublish", StringComparison.Ordinal))
            {
                Unpublishes++;
                return Json(Design());
            }
            if (path.EndsWith("/design", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get) return Json(Design());
                if (!CanArrange) return Problem(HttpStatusCode.Forbidden, "You may not change the design of post 7 now.");
                if (request.Method == HttpMethod.Post)
                {
                    Opens++;
                    HasDesign = true;
                    return Json(Design(), HttpStatusCode.Created);
                }
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var version = body.RootElement.GetProperty("version").GetInt32();
                if (version != Version) return Problem(HttpStatusCode.Conflict, "The design changed.");
                Stored = JsonSerializer.Deserialize<DViews>(body.RootElement.GetProperty("views").GetRawText(), StudioJson.Options)!;
                Saves.Add(++Version);
                return Json(Design());
            }
            return Json(Array.Empty<object>()); // templates and block templates
        }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value, StudioJson.Options), System.Text.Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(HttpStatusCode status, string detail) => Json(new { status = (int)status, detail }, status);
    }

    private sealed class Drafts : IDraftStore
    {
        public Dictionary<string, StoredDraft> Kept { get; } = [];
        public bool Broken { get; set; }

        public Task<bool> SaveAsync(string key, StoredDraft draft)
        {
            if (!Broken) Kept[key] = draft;
            return Task.FromResult(!Broken);
        }

        public Task<StoredDraft?> LoadAsync(string key) => Task.FromResult(Kept.GetValueOrDefault(key));

        public Task RemoveAsync(string key)
        {
            Kept.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static StudioSession Session(Api api, Drafts drafts, string owner = "editor-1")
    {
        var client = new ContentDesignerClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost/") });
        return new StudioSession(7, () => client, e => e.Message, (_, _) => ValueTask.CompletedTask, drafts, owner);
    }

    private static async Task<StudioSession> Opened(Api api, Drafts drafts)
    {
        var session = Session(api, drafts);
        (await session.OpenAsync()).ShouldBeTrue(session.Error);
        return session;
    }

    private static void Hide(StudioSession session) =>
        session.Edit(v => DesignEditor.SetHidden(v, Views.Mobile, "content:a", true)).ShouldBeTrue();

    [Theory]
    [InlineData(null, true)]
    [InlineData("InProduction", true)]
    [InlineData("ReadyForDesk", true)]
    [InlineData("Published", true)]
    [InlineData("Scheduled", false)]
    [InlineData("NeedsModeration", false)]
    public async Task Publish_follows_the_newsroom_project(string? state, bool open)
    {
        var api = new Api { ProjectState = state };
        var session = await Opened(api, new Drafts());

        session.CanPublishNow.ShouldBe(open);
        session.CanPublish.ShouldBeTrue(); // the right itself stays: taking down needs no leave

        (await session.PublishAsync()).ShouldBe(open);
        api.PublishCalls.ShouldBe(open ? 1 : 0);
        (await session.UnpublishAsync()).ShouldBeTrue(session.Error);
        api.Unpublishes.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_publish_asks_again_so_the_button_follows_the_workflow()
    {
        var api = new Api { ProjectState = "InProduction" };
        var session = await Opened(api, new Drafts());

        api.ProjectState = "Killed"; // the workflow moved since the studio last asked
        (await session.PublishAsync()).ShouldBeFalse();

        session.CanPublishNow.ShouldBeFalse();
        session.Publishing.ShouldNotBeNull().ProjectState.ShouldBe("Killed");
        session.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_next_rights_check_opens_publishing_again()
    {
        var api = new Api { ProjectState = "Approved" };
        var session = await Opened(api, new Drafts());

        api.ProjectState = "InProduction";
        await session.VerifyRightsAsync(Ct);

        session.CanPublishNow.ShouldBeTrue();
    }

    [Theory]
    [InlineData("InProduction", "In production")]
    [InlineData("ReadyForDesk", "Ready for desk")]
    [InlineData("Killed", "Killed")]
    public void A_project_state_reads_as_words(string state, string words) => StudioText.ProjectState(state).ShouldBe(words);

    [Fact]
    public async Task A_revoke_the_server_confirms_ends_the_shift_and_keeps_the_unsaved_work()
    {
        var (api, drafts) = (new Api(), new Drafts());
        var session = await Opened(api, drafts);
        Hide(session);

        api.CanArrange = false;
        await session.SuspendAsync(Ct);

        session.Locked.ShouldBeTrue();
        session.ShiftEnded.ShouldBeTrue();
        session.DraftKept.ShouldBeTrue();
        drafts.Kept.Keys.Single().ShouldBe("contentdesigner.draft:editor-1:pub-7");
        session.Dirty.ShouldBeTrue();
        session.CanPublish.ShouldBeFalse();
        session.Edit(v => DesignEditor.SetHidden(v, Views.Mobile, "content:a", false)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_revoke_the_server_does_not_confirm_changes_nothing()
    {
        var (api, drafts) = (new Api(), new Drafts());
        var session = await Opened(api, drafts);

        await session.SuspendAsync(Ct);

        session.Locked.ShouldBeFalse();
        session.ShiftEnded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_save_refused_with_403_is_done_when_the_rights_return()
    {
        var (api, drafts) = (new Api(), new Drafts());
        var session = await Opened(api, drafts);
        Hide(session);
        var returned = 0;
        session.RightsReturned += () => returned++;

        api.CanArrange = false; // the shift ended and no signal came
        (await session.SaveAsync()).ShouldBeFalse();

        session.ShiftEnded.ShouldBeTrue();
        session.SavePending.ShouldBeTrue();
        drafts.Kept.Values.Single().SavePending.ShouldBeTrue();
        api.Saves.ShouldBeEmpty();

        api.CanArrange = true;
        await session.VerifyRightsAsync(Ct);

        session.Locked.ShouldBeFalse();
        session.Dirty.ShouldBeFalse();
        session.SavePending.ShouldBeFalse();
        api.Saves.ShouldBe([4]);
        api.Stored.Mobile.AllPlacements.Single().Hidden.ShouldBeTrue();
        drafts.Kept.ShouldBeEmpty();
        returned.ShouldBe(1);
    }

    [Fact]
    public async Task A_design_that_changed_meanwhile_is_not_written_over()
    {
        var (api, drafts) = (new Api(), new Drafts());
        var session = await Opened(api, drafts);
        Hide(session);
        api.CanArrange = false;
        await session.SaveAsync();

        api.Version = 9; // someone else saved
        api.CanArrange = true;
        await session.VerifyRightsAsync(Ct);

        api.Saves.ShouldBeEmpty();
        session.Design!.Version.ShouldBe(9);
        session.Dirty.ShouldBeFalse();
        session.OfferedCopy.ShouldNotBeNull();
        drafts.Kept.Count.ShouldBe(1);

        session.RestoreCopy();
        session.Dirty.ShouldBeTrue();
        session.Current!.AllPlacements.Single().Hidden.ShouldBeTrue();
    }

    [Fact]
    public async Task Work_kept_from_an_earlier_visit_comes_back_when_the_design_is_the_same()
    {
        var (api, drafts) = (new Api(), new Drafts());
        var first = await Opened(api, drafts);
        Hide(first);
        api.CanArrange = false;
        await first.SuspendAsync(Ct);

        api.CanArrange = true;
        var second = await Opened(api, drafts);

        second.Dirty.ShouldBeTrue();
        second.Current!.AllPlacements.Single().Hidden.ShouldBeTrue();
        api.Saves.ShouldBeEmpty(); // no save was asked for
    }

    [Fact]
    public async Task Storage_that_fails_is_said_and_the_work_stays_on_the_page()
    {
        var (api, drafts) = (new Api(), new Drafts { Broken = true });
        var session = await Opened(api, drafts);
        Hide(session);

        api.CanArrange = false;
        await session.SuspendAsync(Ct);

        session.DraftKept.ShouldBeFalse();
        session.Dirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_rights_a_post_with_no_design_opens_read_only_and_gets_one_when_they_come()
    {
        var api = new Api { CanArrange = false, HasDesign = false };
        var session = await Opened(api, new Drafts());

        session.Locked.ShouldBeTrue();
        session.ShiftEnded.ShouldBeFalse(); // it never had them here
        session.Design.ShouldBeNull();
        api.Opens.ShouldBe(0);

        api.CanArrange = true;
        await session.VerifyRightsAsync(Ct);

        session.Locked.ShouldBeFalse();
        session.Design.ShouldNotBeNull();
        api.Opens.ShouldBe(1);
    }

    [Fact]
    public async Task Contents_refusals_come_from_the_feed_and_leave_with_the_next_change()
    {
        var api = new Api();
        var content = "11111111-1111-1111-1111-111111111111";
        api.Feed.Add(new { id = 1, type = "content.validation-failed", contentId = content, detail = "submit: /content/text Too long.", by = "w", at = DateTimeOffset.UtcNow });
        var session = await Opened(api, new Drafts());

        var problem = session.Problems.ShouldHaveSingleItem();
        problem.Refused.ShouldBeTrue();
        problem.Message.ShouldBe("Too long.");
        problem.Refusal!.Action.ShouldBe("submit");

        api.Feed.Add(new { id = 2, type = "content.changed", contentId = content, by = "w", at = DateTimeOffset.UtcNow });
        (await session.CatchUpAsync(Ct)).ShouldBeTrue();

        session.Problems.ShouldBeEmpty();
    }
}

public class ContentRefusalTests
{
    private static DesignProblem Own(string nodeId, string path, string message) =>
        new() { Severity = ProblemSeverity.Error, Code = "content", Path = path, Message = message, NodeId = nodeId };

    private static ContentRefusal Refused(string? nodeId, string path, string message, string? action = "submit") =>
        new(null, nodeId, action, path, message);

    [Fact]
    public void The_same_problem_of_the_same_node_is_one_row_even_when_the_paths_start_elsewhere()
    {
        var merged = ContentRefusals.Merge(
            [Own("n-1", "/children/1/content/caption", "Caption is required.")],
            [Refused("n-1", "/content/caption", "Caption is required"), Refused("n-1", "/children/1/content/caption", "Caption is required.", null)],
            null);

        var row = merged.ShouldHaveSingleItem();
        row.Design.ShouldNotBeNull();
        row.Refused.ShouldBeTrue();
        row.Refusal!.Action.ShouldBe("submit");
    }

    [Fact]
    public void The_same_message_on_two_nodes_stays_two_rows()
    {
        var merged = ContentRefusals.Merge([Own("n-1", "/content/alt", "Alt is required.")], [Refused("n-2", "/content/alt", "Alt is required.")], null);

        merged.Count.ShouldBe(2);
        merged.Count(p => p.Refused).ShouldBe(1);
    }

    [Fact]
    public void A_feed_detail_is_split_where_a_path_starts()
    {
        var problems = ContentRefusals.Parse("c-1", "accept: /content/text Too long; shorten it.; /children/0/content/alt Alt is required.");

        problems.Select(p => (p.Action, p.Path, p.Message)).ShouldBe(
        [
            ("accept", "/content/text", "Too long; shorten it."),
            ("accept", "/children/0/content/alt", "Alt is required.")
        ]);
    }

    [Fact]
    public void A_422_answer_names_its_nodes()
    {
        var problems = ContentRefusals.FromAnswer("""{"status":422,"problems":[{"path":"/content/text","message":"Needed.","nodeId":"n-3"}]}""");

        problems.ShouldHaveSingleItem().NodeId.ShouldBe("n-3");
        ContentRefusals.FromAnswer("Bad gateway").ShouldBeEmpty();
        ContentRefusals.FromAnswer("""{"detail":"no"}""").ShouldBeEmpty();
    }
}
