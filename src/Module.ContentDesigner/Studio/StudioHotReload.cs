using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>How the studio learns that the post's contents changed.</summary>
public enum StudioSync
{
    /// <summary>Reads the post's change feed every <see cref="StudioHotReload.PollEvery"/> while the page is visible.</summary>
    Polling,

    /// <summary>Joined to the post's live topic: each message makes it read the feed at once.</summary>
    Live
}

/// <summary>
/// The studio's hot reload (phase 6a). Content owns the post, so the studio listens on Content's live topic for it
/// (<c>content:post:{id}</c>, see <see cref="Topic"/>) through the platform's <see cref="ILiveChannel"/>. A message is only
/// a nudge: the studio always reads what changed from the post's change feed (<c>?after=</c> the last event it read), which
/// also catches up after a reconnect or a gap (the channel's resync). Polling every 4 s is the fallback: while the app has
/// no live channel, while the channel is not connected or the topic not joined yet, and when the server refused or
/// revoked the topic (for example while Content does not offer it). While live, a slow safety read
/// (<see cref="LiveSafetyEvery"/>) covers a message lost on the way (messages are best effort).
/// Catch-ups never overlap: one asked for while another runs is done once more right after it.
/// </summary>
public sealed class StudioHotReload : IAsyncDisposable
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan LiveSafetyEvery = TimeSpan.FromSeconds(60);

    private readonly Func<CancellationToken, Task<bool>> _catchUp;
    private readonly Func<long> _lastEvent;
    private readonly ILiveChannel? _live;
    private readonly Func<Task<bool>> _hidden;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private IAsyncDisposable? _subscription;
    private bool _joined;
    private Task? _running;
    private bool _again;
    private DateTimeOffset _lastCatchUp;
    private CancellationToken _ct;
    private bool _disposed;

    /// <param name="catchUp">Reads the feed after the last event and reloads what changed (<see cref="StudioSession.CatchUpAsync"/>).</param>
    /// <param name="lastEvent">The last feed event read: the sequence the subscription starts from.</param>
    /// <param name="live">The app's live channel, or null when the host has none.</param>
    /// <param name="hidden">Whether the page is hidden (no polling then; live messages are still followed).</param>
    public StudioHotReload(int postId, Func<CancellationToken, Task<bool>> catchUp, Func<long> lastEvent, ILiveChannel? live,
        Func<Task<bool>> hidden, TimeProvider? time = null)
    {
        PostId = postId;
        _catchUp = catchUp;
        _lastEvent = lastEvent;
        _live = live;
        _hidden = hidden;
        _time = time ?? TimeProvider.System;
        _lastCatchUp = _time.GetUtcNow();
    }

    /// <summary>The topic Content publishes the post's changes to (the hub adds the tenant).</summary>
    public static string Topic(int postId) => $"content:post:{postId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public int PostId { get; }

    public StudioSync Mode =>
        _live is { State: LiveState.Connected } && _subscription is not null && _joined && Refused is null ? StudioSync.Live : StudioSync.Polling;

    /// <summary>Why the live topic is not used (the server's answer: <c>unknown-topic</c>, <c>forbidden</c>, <c>revoked</c>...), or null.</summary>
    public string? Refused { get; private set; }

    /// <summary>Raised when <see cref="Mode"/> or <see cref="Refused"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Joins the post's live topic, when the app has a live channel; polling covers the time until it is joined.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _ct = ct;
        if (_live is null) return;
        _live.StateChanged += OnStateChanged;
        try
        {
            var subscription = await _live.SubscribeAsync(Topic(PostId), OnMessageAsync, _lastEvent(),
                new LiveSubscribeOptions { OnResync = OnResyncAsync, OnRevoked = OnRevokedAsync }, ct);
            if (_disposed || Refused is not null)
            {
                await subscription.DisposeAsync();
                return;
            }
            _subscription = subscription;
        }
        catch (LiveSubscriptionException e)
        {
            Refused = e.Error;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Refused = "error"; // the channel failed: polling it is
        }
        Changed?.Invoke();
    }

    /// <summary>Ticks every <see cref="PollEvery"/> until cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollEvery, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) await TickAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// One timer tick: while polling, reads the feed when the page is visible; while live, only when the last read is
    /// older than <see cref="LiveSafetyEvery"/>.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (Mode == StudioSync.Live && _time.GetUtcNow() - _lastCatchUp < LiveSafetyEvery) return;
        if (await _hidden()) return;
        await CatchUpAsync(ct);
    }

    /// <summary>Reads the feed now; when a read is running, once more after it.</summary>
    public Task CatchUpAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running)
            {
                _again = true;
                return running;
            }
            _running = LoopAsync(ct);
            return _running;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        await Task.Yield(); // the caller (a hub message, a tick) returns at once; the reads run one after the other
        while (!ct.IsCancellationRequested && !_disposed)
        {
            lock (_gate) _again = false;
            _lastCatchUp = _time.GetUtcNow();
            try { await _catchUp(ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* the next nudge or tick tries again; the session shows its error */ }
            lock (_gate)
            {
                if (!_again) return;
            }
        }
    }

    private Task OnMessageAsync(LiveMessage message) => CatchUpAsync(_ct);

    private Task OnResyncAsync(LiveResync resync)
    {
        // Joined (first time or after a reconnect) or a gap: read whatever came since the last event read.
        _joined = true;
        Changed?.Invoke();
        return CatchUpAsync(_ct);
    }

    private Task OnRevokedAsync(string reason)
    {
        Refused = reason;
        _subscription = null; // the channel dropped it
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void OnStateChanged(LiveState state)
    {
        if (state != LiveState.Connected) _joined = false; // joined again on reconnect: its resync says so
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_live is not null) _live.StateChanged -= OnStateChanged;
        var subscription = _subscription;
        _subscription = null;
        if (subscription is not null)
        {
            try { await subscription.DisposeAsync(); }
            catch (Exception) { /* leaving a topic is best effort */ }
        }
    }
}
