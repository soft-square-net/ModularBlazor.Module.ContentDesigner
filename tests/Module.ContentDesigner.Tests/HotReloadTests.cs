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

        public Task SendAsync() => Handler!(new LiveMessage(Topic!, "post.changed", null, null, DateTimeOffset.UtcNow));

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
        var reload = new StudioHotReload(42, feed.CatchUpAsync, () => 17, live, () => Task.FromResult(hidden), clock);
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
