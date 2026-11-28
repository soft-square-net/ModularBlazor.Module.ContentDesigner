using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.Module.ContentDesigner.Tests;

/// <summary>The studio's hot reload: live nudges, catch-up from the feed, polling as the fallback.</summary>
public class HotReloadTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Behaves like the platform's channel where the studio can see it: resync once joined, refusal, state.</summary>
    private sealed class FakeLive : ILiveChannel
    {
        public LiveState State { get; private set; } = LiveState.Connected;
        public event Action<LiveState>? StateChanged;
#pragma warning disable CS0067 // the studio uses the per-subscription resync
        public event Func<LiveResync, Task>? Resync;
#pragma warning restore CS0067
        public string? Refuse { get; set; }
        public string? Topic { get; private set; }
        public long? From { get; private set; }
        public Func<LiveMessage, Task>? Handler { get; private set; }
        public LiveSubscribeOptions? Options { get; private set; }
        public bool Left { get; private set; }

        public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<LiveMessage, Task> handler, long? fromSequence = null,
            LiveSubscribeOptions? options = null, CancellationToken ct = default)
        {
            if (State == LiveState.Connected && Refuse is not null) throw new LiveSubscriptionException(topic, Refuse);
            (Topic, From, Handler, Options) = (topic, fromSequence, handler, options);
            if (State == LiveState.Connected && fromSequence is not null) await RaiseAsync(LiveResyncReason.Subscribed);
            return new Leave(this);
        }

        public Task SendAsync(long? eventId = null) => Handler!(new LiveMessage(Topic!, "post.changed",
            eventId is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(new { eventId, type = "post.changed" }),
            null, DateTimeOffset.UtcNow));

        public async Task SetStateAsync(LiveState state, bool rejoin = true)
        {
            State = state;
            StateChanged?.Invoke(state);
            if (state == LiveState.Connected && rejoin) await RaiseAsync(LiveResyncReason.Reconnected);
        }

        public Task RevokeAsync(string reason) => Options!.OnRevoked!(reason);

        private Task RaiseAsync(LiveResyncReason reason) =>
            Options?.OnResync?.Invoke(new LiveResync(Topic!, From, reason)) ?? Task.CompletedTask;

        private sealed class Leave(FakeLive live) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                live.Left = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class Feed
    {
        public int Reads { get; private set; }
        public TaskCompletionSource? Hold { get; set; }

        public async Task<bool> CatchUpAsync(CancellationToken ct)
        {
            Reads++;
            if (Hold is { } hold) await hold.Task;
            return false;
        }
    }

    private static (StudioHotReload Reload, Feed Feed, Clock Clock) Make(ILiveChannel? live, bool hidden = false)
    {
        var feed = new Feed();
        var clock = new Clock();
        var reload = new StudioHotReload(42, feed.CatchUpAsync, () => 17, live, () => Task.FromResult(hidden), clock,
            (_, _) => Task.CompletedTask); // the waits between reads pass at once
        return (reload, feed, clock);
    }

    [Fact]
    public void The_topic_is_contents_post_topic() => StudioHotReload.Topic(42).ShouldBe("content:post:42");

    [Fact]
    public async Task Without_a_live_channel_it_polls_while_the_page_is_visible()
    {
        var (reload, feed, _) = Make(live: null);
        await reload.StartAsync(TestContext.Current.CancellationToken);

        reload.Mode.ShouldBe(StudioSync.Polling);
        await reload.TickAsync(TestContext.Current.CancellationToken);
        await reload.TickAsync(TestContext.Current.CancellationToken);
        feed.Reads.ShouldBe(2);

        var (hiddenReload, hiddenFeed, _) = Make(live: null, hidden: true);
        await hiddenReload.TickAsync(TestContext.Current.CancellationToken);
        hiddenFeed.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Joined_it_catches_up_once_then_follows_messages_instead_of_polling()
    {
        var live = new FakeLive();
        var (reload, feed, clock) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);
        await reload.CatchUpAsync(TestContext.Current.CancellationToken); // waits for the read the join started

        live.Topic.ShouldBe("content:post:42");
        live.From.ShouldBe(17); // from the last feed event the studio read
        reload.Mode.ShouldBe(StudioSync.Live);
        var afterJoin = feed.Reads;
        afterJoin.ShouldBeGreaterThanOrEqualTo(1);

        await reload.TickAsync(TestContext.Current.CancellationToken); // live: no polling
        feed.Reads.ShouldBe(afterJoin);

        await live.SendAsync();
        await reload.CatchUpAsync(TestContext.Current.CancellationToken);
        feed.Reads.ShouldBeGreaterThan(afterJoin);

        var beforeSafety = feed.Reads;
        clock.Now += StudioHotReload.LiveSafetyEvery;
        await reload.TickAsync(TestContext.Current.CancellationToken); // the slow safety read
        feed.Reads.ShouldBe(beforeSafety + 1);
    }

    [Fact]
    public async Task A_refused_topic_falls_back_to_polling()
    {
        var live = new FakeLive { Refuse = "unknown-topic" }; // Content does not offer the post topic
        var (reload, feed, _) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);

        reload.Mode.ShouldBe(StudioSync.Polling);
        reload.Refused.ShouldBe("unknown-topic");
        await reload.TickAsync(TestContext.Current.CancellationToken);
        feed.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task It_polls_while_the_connection_is_down_and_catches_up_when_it_is_back()
    {
        var live = new FakeLive();
        var (reload, feed, _) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);
        await reload.CatchUpAsync(TestContext.Current.CancellationToken);

        await live.SetStateAsync(LiveState.Reconnecting);
        reload.Mode.ShouldBe(StudioSync.Polling);
        var before = feed.Reads;
        await reload.TickAsync(TestContext.Current.CancellationToken);
        feed.Reads.ShouldBe(before + 1);

        // Connected, but the topic not joined again yet: still polling.
        await live.SetStateAsync(LiveState.Connected, rejoin: false);
        reload.Mode.ShouldBe(StudioSync.Polling);

        await live.SetStateAsync(LiveState.Connected); // the reconnect's resync
        await reload.CatchUpAsync(TestContext.Current.CancellationToken);
        reload.Mode.ShouldBe(StudioSync.Live);
        feed.Reads.ShouldBeGreaterThan(before + 1);
    }

    [Fact]
    public async Task A_revoked_topic_falls_back_to_polling()
    {
        var live = new FakeLive();
        var (reload, feed, _) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);
        reload.Mode.ShouldBe(StudioSync.Live);

        await live.RevokeAsync("revoked"); // the user's shift ended
        reload.Mode.ShouldBe(StudioSync.Polling);
        reload.Refused.ShouldBe("revoked");
    }

    [Fact]
    public async Task Subscribing_while_offline_polls_until_joined()
    {
        var live = new FakeLive();
        await live.SetStateAsync(LiveState.Offline);
        var (reload, _, _) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);
        reload.Mode.ShouldBe(StudioSync.Polling);

        await live.SetStateAsync(LiveState.Connected);
        reload.Mode.ShouldBe(StudioSync.Live);
    }

    [Fact]
    public async Task Catch_ups_never_overlap_and_requests_meanwhile_make_one_more_read()
    {
        var (reload, feed, _) = Make(live: null);
        feed.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = reload.CatchUpAsync(TestContext.Current.CancellationToken);
        while (feed.Reads == 0) await Task.Delay(5, TestContext.Current.CancellationToken);
        var second = reload.CatchUpAsync(TestContext.Current.CancellationToken);
        var third = reload.CatchUpAsync(TestContext.Current.CancellationToken);
        feed.Reads.ShouldBe(1); // the first still runs

        feed.Hold.SetResult();
        await Task.WhenAll(first, second, third);
        feed.Reads.ShouldBe(2); // the two asked meanwhile: one more read
    }

    /// <summary>
    /// Content's feed as the studio meets it: a row shows only once it is older than the event delay. Time passes only
    /// when the hot reload waits.
    /// </summary>
    private sealed class HeldBackFeed
    {
        private static readonly TimeSpan EventDelay = TimeSpan.FromSeconds(2);
        private readonly List<(long Id, TimeSpan At)> _rows = [];
        private TimeSpan _now;

        public long LastEvent { get; private set; } = 17;
        public List<TimeSpan> ReadAt { get; } = [];
        public List<TimeSpan> Waits { get; } = [];
        public Dictionary<long, TimeSpan> Shown { get; } = [];

        public void Commit(long id) => _rows.Add((id, _now));

        public Task<bool> CatchUpAsync(CancellationToken ct)
        {
            ReadAt.Add(_now);
            var rows = _rows.Where(r => r.Id > LastEvent && r.At <= _now - EventDelay).OrderBy(r => r.Id).ToList();
            foreach (var row in rows) Shown[row.Id] = _now - row.At;
            if (rows.Count > 0) LastEvent = rows[^1].Id;
            return Task.FromResult(rows.Count > 0);
        }

        public Task WaitAsync(TimeSpan wait, CancellationToken ct)
        {
            Waits.Add(wait);
            _now += wait;
            return Task.CompletedTask;
        }
    }

    private static async Task<(StudioHotReload Reload, HeldBackFeed Feed, FakeLive Live)> JoinedHeldBack()
    {
        var live = new FakeLive();
        var feed = new HeldBackFeed();
        var reload = new StudioHotReload(42, feed.CatchUpAsync, () => feed.LastEvent, live, () => Task.FromResult(false), new Clock(), feed.WaitAsync);
        await reload.StartAsync(TestContext.Current.CancellationToken);
        await Idle(reload);
        feed.ReadAt.Clear();
        feed.Waits.Clear();
        return (reload, feed, live);
    }

    /// <summary>Waits until no read runs (asking for one while a read runs returns that run).</summary>
    private static async Task Idle(StudioHotReload reload)
    {
        for (var i = 0; i < 5; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        await reload.CatchUpAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_nudge_ahead_of_the_feed_is_read_again_until_the_row_it_names_shows()
    {
        var (_, feed, live) = await JoinedHeldBack();

        feed.Commit(18);
        await live.SendAsync(18); // at once, as Content sends it: the feed still holds the row back
        for (var i = 0; i < 100 && feed.LastEvent < 18; i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        feed.LastEvent.ShouldBe(18);
        feed.ReadAt.Count.ShouldBe(3); // at once, 1.2 s and 2.2 s later
        feed.Shown[18].ShouldBeLessThan(TimeSpan.FromSeconds(2.5)); // not at the next change, or after 60 s
    }

    [Fact]
    public async Task A_row_the_feed_never_shows_is_given_up_after_the_last_retry()
    {
        var (reload, feed, live) = await JoinedHeldBack();

        await live.SendAsync(99);
        for (var i = 0; i < 100 && feed.Waits.Count < StudioHotReload.Retries.Count; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        feed.Waits.ShouldBe(StudioHotReload.Retries);
        feed.ReadAt.Count.ShouldBe(StudioHotReload.Retries.Count + 1);

        await reload.CatchUpAsync(TestContext.Current.CancellationToken); // a later tick reads once
        feed.ReadAt.Count.ShouldBe(StudioHotReload.Retries.Count + 2);
    }

    [Fact]
    public async Task A_newer_message_does_not_wait_out_an_older_rows_retry()
    {
        var live = new FakeLive();
        var feed = new HeldBackFeed();
        var stuck = false; // from here on a wait ends only when it is cancelled
        var waits = 0;
        var reload = new StudioHotReload(42, feed.CatchUpAsync, () => feed.LastEvent, live, () => Task.FromResult(false), new Clock(),
            (wait, ct) =>
            {
                if (!stuck) return feed.WaitAsync(wait, ct);
                Interlocked.Increment(ref waits);
                return Task.Delay(Timeout.InfiniteTimeSpan, ct);
            });
        await reload.StartAsync(TestContext.Current.CancellationToken);
        await Idle(reload);
        feed.ReadAt.Clear();
        stuck = true;

        await live.SendAsync(98); // a row the feed does not show yet: the studio waits
        for (var i = 0; i < 100 && Volatile.Read(ref waits) < 1; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        feed.ReadAt.Count.ShouldBe(1);

        await live.SendAsync(99);
        for (var i = 0; i < 100 && Volatile.Read(ref waits) < 2; i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        feed.ReadAt.Count.ShouldBe(2); // read at once, then waiting again
        waits.ShouldBe(2);
    }

    [Fact]
    public async Task A_resync_reads_once_more_for_a_row_still_held_back()
    {
        var (_, feed, live) = await JoinedHeldBack();

        feed.Commit(18);
        await live.SetStateAsync(LiveState.Reconnecting);
        await live.SetStateAsync(LiveState.Connected); // no message will name row 18
        for (var i = 0; i < 100 && feed.LastEvent < 18; i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        feed.LastEvent.ShouldBe(18);
        feed.Waits.ShouldBe([StudioHotReload.SettleDelay]);
    }

    [Theory]
    [InlineData("""{ "eventId": 21, "type": "content.changed" }""", 21L)]
    [InlineData("""{ "type": "content.changed" }""", null)]
    [InlineData("""[ 1 ]""", null)]
    public void The_row_a_message_names_is_its_event_id(string payload, long? expected) =>
        StudioHotReload.EventOf(new LiveMessage("content:post:42", "post.changed",
            System.Text.Json.JsonDocument.Parse(payload).RootElement.Clone(), null, DateTimeOffset.UtcNow)).ShouldBe(expected);

    [Fact]
    public async Task Closing_leaves_the_topic()
    {
        var live = new FakeLive();
        var (reload, _, _) = Make(live);
        await reload.StartAsync(TestContext.Current.CancellationToken);

        await reload.DisposeAsync();
        live.Left.ShouldBeTrue();
    }
}
