using Microsoft.JSInterop;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>What the canvas shows, as the open session has it now.</summary>
public sealed record CanvasState(string View, bool ShowHidden, string? Direction, string? Culture, string? Selected);

/// <summary>
/// The designer panel's MudCanvas: it creates the canvas and keeps it showing the session's state. Creating it takes a
/// while (the first one loads the designer's chunk), and meanwhile the panel can lose its session, get another one or be
/// disposed (the app reloads after a language change, the studio is left). So nothing read before an await is assumed to
/// hold after it: the state is asked for again, and a canvas that arrives after the panel is gone is disposed.
/// </summary>
/// <param name="create">Creates the canvas showing a state; null while UI.MudTail is not active.</param>
/// <param name="current">The state to show, or null while the panel has no open design.</param>
public sealed class DesignCanvas(Func<CanvasState, ValueTask<IJSObjectReference?>> create, Func<CanvasState?> current) : IAsyncDisposable
{
    private IJSObjectReference? _canvas;
    private CanvasState? _told;
    private bool _creating;
    private bool _disposed;

    /// <summary>Creates the canvas if there is none, then tells it what changed. Called after each render.</summary>
    public async Task SyncAsync()
    {
        if (_disposed || _creating || current() is not { } first) return;
        if (_canvas is null)
        {
            IJSObjectReference? canvas;
            _creating = true;
            try { canvas = await create(first with { Selected = null }); }
            finally { _creating = false; } // a failed creation is tried again after the next render
            if (canvas is null) return;
            if (_disposed)
            {
                await MudTailInterop.DisposeHandleAsync(canvas);
                return;
            }
            _canvas = canvas;
            _told = first with { Selected = null };
        }

        // The session may have changed, or gone, while the canvas was being created.
        if (current() is not { } state || _told is not { } told) return;
        var target = _canvas;
        try
        {
            if (told.View != state.View) await target.InvokeVoidAsync("setDevice", Views.Device(state.View));
            if (told.ShowHidden != state.ShowHidden) await target.InvokeVoidAsync("setShowHidden", state.ShowHidden);
            if (told.Direction != state.Direction || told.Culture != state.Culture)
                await target.InvokeVoidAsync("setDirection", state.Direction, state.Culture);
            if (told.Selected != state.Selected) await target.InvokeVoidAsync("select", state.Selected);
            await target.InvokeVoidAsync("refresh"); // the blocks may have moved: the selection box follows
            _told = state;
        }
        catch (Exception e) when (_disposed && e is JSException or ObjectDisposedException) { } // disposed in between
    }

    /// <summary>The canvas selected a part itself: it needs no telling.</summary>
    public void Selected(string? id)
    {
        if (_told is not null) _told = _told with { Selected = id };
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        var canvas = _canvas;
        _canvas = null;
        await MudTailInterop.DisposeHandleAsync(canvas);
    }
}
