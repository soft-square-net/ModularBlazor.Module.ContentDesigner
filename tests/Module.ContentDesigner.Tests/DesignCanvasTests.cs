using Microsoft.JSInterop;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>
/// The designer panel's canvas while the studio changes under it: creating MudCanvas takes a while, and meanwhile the panel
/// can lose its session or be disposed (this crashed with a NullReferenceException after a language change); and the
/// studio page's hand-over of the page context to the page that replaces it.
/// </summary>
public class DesignCanvasTests
{
    private sealed class Canvas : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(args is { Length: > 0 } ? $"{identifier}:{string.Join(",", args)}" : identifier);
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Studio
    {
        public CanvasState? State { get; set; } = new("mobile", true, "ltr", "en", null);
        public TaskCompletionSource<IJSObjectReference?> Creating { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Created { get; private set; }
        public DesignCanvas Host { get; }

        public Studio() => Host = new DesignCanvas(_ =>
        {
            Created++;
            return new ValueTask<IJSObjectReference?>(Creating.Task);
        }, () => State);
    }

    [Fact]
    public async Task A_session_that_goes_while_the_canvas_is_created_does_not_crash_the_panel()
    {
        var studio = new Studio();
        var canvas = new Canvas();

        var sync = studio.Host.SyncAsync();
        studio.State = null; // the studio closed its session
        studio.Creating.SetResult(canvas);
        await sync;

        canvas.Calls.ShouldBeEmpty();

        studio.State = new("desktop", false, "rtl", "ar", "prop:headline"); // the next session: the same canvas follows it
        await studio.Host.SyncAsync();

        studio.Created.ShouldBe(1);
        canvas.Calls.ShouldContain("setShowHidden:False");
        canvas.Calls.ShouldContain("setDirection:rtl,ar");
        canvas.Calls.ShouldContain("select:prop:headline");
        canvas.Calls.Last().ShouldBe("refresh");
    }

    [Fact]
    public async Task What_changed_while_the_canvas_was_created_is_told_once_it_is_there()
    {
        var studio = new Studio();
        var canvas = new Canvas();

        var sync = studio.Host.SyncAsync();
        await studio.Host.SyncAsync(); // a render meanwhile creates no second canvas
        studio.State = studio.State! with { Direction = "rtl", Culture = "ar" };
        studio.Creating.SetResult(canvas);
        await sync;

        studio.Created.ShouldBe(1);
        canvas.Calls.ShouldBe(["setDirection:rtl,ar", "refresh"]);
    }

    [Fact]
    public async Task A_canvas_that_arrives_after_the_panel_is_gone_is_disposed()
    {
        var studio = new Studio();
        var canvas = new Canvas();

        var sync = studio.Host.SyncAsync();
        await studio.Host.DisposeAsync();
        studio.Creating.SetResult(canvas);
        await sync;

        canvas.Calls.ShouldBe(["dispose"]);
        canvas.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_creation_is_tried_again_after_the_next_render()
    {
        var studio = new Studio();
        studio.Creating.SetException(new JSException("the chunk did not load"));
        await Should.ThrowAsync<JSException>(studio.Host.SyncAsync);

        studio.Creating = new();
        studio.Creating.SetResult(new Canvas());
        await studio.Host.SyncAsync();

        studio.Created.ShouldBe(2);
    }

    private static StudioSession Session(int postId) =>
        new(postId, () => new ContentDesignerClient(new HttpClient()), e => e.Message, (_, _) => ValueTask.CompletedTask);

    [Fact]
    public void A_closing_studio_leaves_the_context_of_the_one_that_replaced_it()
    {
        var replacement = Session(9041);
        StudioSessions.Add(replacement);
        try
        {
            StudioSessions.MayClearContext("9041").ShouldBeFalse(); // the new page's post: its panels need it
        }
        finally
        {
            StudioSessions.Remove(replacement);
        }

        StudioSessions.MayClearContext("9041").ShouldBeTrue();
        StudioSessions.MayClearContext(null).ShouldBeTrue();
    }
}
