using System.Net;
using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;
using RenderBlock = soft.square.net.ContentDesigner.Rendering.Model.RenderBlock;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;
using RenderValue = soft.square.net.ContentDesigner.Rendering.Model.RenderValue;

namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>Plain text edited in place on the canvas (phase E1): what may be edited, and saving it through Content.</summary>
public class TextEditingTests
{
    private const string Content1 = "11111111-1111-1111-1111-111111111111";

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RenderDocument Document() => new("post.news", "en", "A post",
        Json("""{ "headline": "A post", "kicker": "Breaking", "tags": ["a", "b"], "datePublished": "2026-11-28" }"""),
        [
            new RenderBlock("h1", "n-h1", "block.heading", 1, Json("""{ "text": "Hello" }"""), Json("{}")),
            new RenderBlock("t1", "n-t1", "block.text", 1, Json("""{ "text": "Line" }"""), Json("{}")),
            new RenderBlock("p1", "n-p1", "block.paragraph", 1, Json("""{ "text": "World" }"""), Json("{}")),
            new RenderBlock("x1", "n-x1", "block.text", 1, Json("""{ "text": ["not", "one"] }"""), Json("{}"))
        ],
        Values: new Dictionary<string, RenderValue>
        {
            ["headline"] = new("Text", "Headline"),
            ["kicker"] = new("Text", "Kicker"),
            ["tags"] = new("Text", "Tags", Multiple: true),
            ["datePublished"] = new("Date", "Published")
        });

    [Theory]
    [InlineData("prop:headline", "headline")]
    [InlineData("prop:kicker", "kicker")]
    [InlineData("content:h1", "text")]
    [InlineData("content:t1", "text")]
    [InlineData("prop:tags", null)]
    [InlineData("prop:datePublished", null)]
    [InlineData("content:p1", null)]
    [InlineData("content:x1", null)]
    [InlineData("region:body", null)]
    public void Only_single_plain_text_values_are_edited_in_place(string part, string? key) =>
        TextParts.KeyOf(Document(), part).ShouldBe(key);

    [Fact]
    public void The_canvas_marks_the_editable_parts_only_when_editing_is_allowed()
    {
        var views = new DViews(new DView([new DRegion("body", "Section")],
            [new DPlacement("prop:headline", "body", 0), new DPlacement("content:h1", "body", 1), new DPlacement("content:p1", "body", 2)]),
            DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile));
        static IEnumerable<PageNode> All(IEnumerable<PageNode> nodes) => nodes.SelectMany(n => n is RegionNode r ? new[] { n }.Concat(All(r.Children)) : [n]);
        IReadOnlyDictionary<string, string> Editable(Func<string, bool>? editable) =>
            All(RenderMapping.Page(Document(), RenderMapping.Design(views), Views.Mobile, editable).Children)
                .Where(n => n.Attributes.ContainsKey("data-has-text"))
                .ToDictionary(n => (string)n.Attributes["data-block-id"], n => (string)n.Attributes["data-edit-key"]);

        Editable(null).ShouldBeEmpty();
        Editable(_ => true).ShouldBe(new Dictionary<string, string> { ["prop:headline"] = "headline", ["content:h1"] = "text" });
        Editable(p => p != "content:h1").Keys.ShouldBe(["prop:headline"]);
    }

    [Fact]
    public void An_edit_shows_in_the_document_at_once_on_one_line()
    {
        var edited = TextParts.With(Document(), "content:h1", "text", TextParts.OneLine("  Good\nmorning "));
        TextParts.TextOf(edited, "content:h1", "text").ShouldBe("Good morning");
        TextParts.TextOf(TextParts.With(Document(), "prop:headline", "headline", "New"), "prop:headline", "headline").ShouldBe("New");
        TextParts.TextOf(Document(), "content:t1", "text").ShouldBe("Line");
    }

    [Fact]
    public void The_saving_parts_are_dimmed_with_escaped_ids()
    {
        TextSaving.Css([]).ShouldBeEmpty();
        TextSaving.Css(["content:a\"b"]).ShouldContain("[data-block-id=\"content:a\\\"b\"]");
    }

    // ───────────── saving through the module's API ─────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The module's API with one heading content; the text edit answers as a test sets it.</summary>
    private sealed class Api : HttpMessageHandler
    {
        public string Heading { get; set; } = "Hello";
        public HttpStatusCode EditAnswer { get; set; } = HttpStatusCode.OK;
        public List<JsonElement> Edits { get; } = [];
        public bool CanArrange { get; set; } = true;

        private object Workspace() => new
        {
            post = new { id = 7, publicId = "pub-7", title = "A post", kind = "post.news", kindVersion = 1, version = 1 },
            contents = new[] { new { contentId = Content1, nodeId = "n-1", fieldKey = "block.heading", stage = "draft" } },
            document = new
            {
                kind = "post.news", title = "A post", culture = "en", direction = "ltr", root = new { headline = "A post" },
                contents = new[] { new { id = Content1, nodeId = "n-1", fieldKey = "block.heading", fieldVersion = 1, content = new { text = Heading }, style = new { } } }
            },
            kindValues = Array.Empty<object>(),
            fieldStyles = new { },
            design = new
            {
                postId = 7, publicId = "pub-7", kind = "post.news", version = 1,
                draft = StudioJson.ToApi(new DViews(new DView([new DRegion("body", "Section")], [new DPlacement("content:" + Content1, "body", 0)]),
                    DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile), DView.Inherit(Views.Mobile)))
            },
            rights = new { postId = 7, canArrange = CanArrange, canPublish = CanArrange }
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/text", StringComparison.Ordinal))
            {
                Edits.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone());
                if (EditAnswer != HttpStatusCode.OK) return Json(new { status = (int)EditAnswer, detail = "Refused by Content." }, EditAnswer);
                Heading = Edits[^1].GetProperty("text").GetString()!;
                return Json(Workspace());
            }
            if (path.EndsWith("/workspace", StringComparison.Ordinal)) return Json(Workspace());
            if (path.EndsWith("/rights", StringComparison.Ordinal)) return Json(new { postId = 7, canArrange = CanArrange, canPublish = CanArrange });
            if (path.EndsWith("/events", StringComparison.Ordinal)) return Json(new { events = Array.Empty<object>(), last = 0, more = false });
            return Json(Array.Empty<object>());
        }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value, StudioJson.Options), System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class NoDrafts : IDraftStore
    {
        public Task<bool> SaveAsync(string key, StoredDraft draft) => Task.FromResult(true);
        public Task<StoredDraft?> LoadAsync(string key) => Task.FromResult<StoredDraft?>(null);
        public Task RemoveAsync(string key) => Task.CompletedTask;
    }

    private static async Task<StudioSession> Opened(Api api)
    {
        var client = new ContentDesignerClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost/") });
        var session = new StudioSession(7, () => client, e => e.Message, (_, _) => ValueTask.CompletedTask, new NoDrafts(), "editor-1");
        (await session.OpenAsync()).ShouldBeTrue(session.Error);
        return session;
    }

    private const string Part = "content:" + Content1;

    [Fact]
    public async Task A_committed_edit_is_saved_through_the_module_with_the_text_it_replaced()
    {
        var api = new Api();
        var session = await Opened(api);
        session.CanEditText(Part).ShouldBeTrue();

        (await session.EditTextAsync(Part, "Good\nmorning")).ShouldBeTrue(session.Error);

        var edit = api.Edits.ShouldHaveSingleItem();
        edit.GetProperty("part").GetString().ShouldBe(Part);
        edit.GetProperty("key").GetString().ShouldBe("text");
        edit.GetProperty("text").GetString().ShouldBe("Good morning");
        edit.GetProperty("expected").GetString().ShouldBe("Hello");
        TextParts.TextOf(session.Document!, Part, "text").ShouldBe("Good morning");
        session.SavingText.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unchanged_text_is_not_sent()
    {
        var api = new Api();
        var session = await Opened(api);
        (await session.EditTextAsync(Part, " Hello ")).ShouldBeTrue();
        api.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_text_changed_meanwhile_is_reloaded_and_the_callers_text_kept_aside()
    {
        var api = new Api { EditAnswer = HttpStatusCode.Conflict };
        var session = await Opened(api);
        api.Heading = "Changed by someone";

        (await session.EditTextAsync(Part, "Mine")).ShouldBeFalse();

        session.SetAsideText.ShouldBe((Part, "Mine"));
        TextParts.TextOf(session.Document!, Part, "text").ShouldBe("Changed by someone");
    }

    [Fact]
    public async Task A_refused_edit_puts_the_old_text_back_and_the_part_stays_read_only()
    {
        var api = new Api { EditAnswer = HttpStatusCode.Forbidden };
        var session = await Opened(api);

        (await session.EditTextAsync(Part, "Mine")).ShouldBeFalse();

        TextParts.TextOf(session.Document!, Part, "text").ShouldBe("Hello");
        session.CanEditText(Part).ShouldBeFalse();
        (await session.EditTextAsync(Part, "Again")).ShouldBeFalse();
        api.Edits.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_is_editable_while_the_post_is_read_only()
    {
        var session = await Opened(new Api { CanArrange = false });
        session.Locked.ShouldBeTrue();
        session.CanEditText(Part).ShouldBeFalse();
    }
}
