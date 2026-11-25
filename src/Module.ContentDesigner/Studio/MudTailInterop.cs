using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using soft.square.net.ModularBlazor.Abstractions.MudTail;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>MudTail V2 components the studio creates (dock, canvas, monaco) through UI.MudTail's runtime.</summary>
public static class MudTailInterop
{
    /// <summary>
    /// <c>create(name, host, options, dotNetRef)</c>: the handle, or null when UI.MudTail is not active. The first call
    /// of a large component loads its chunk and stylesheet pack (designer, monaco, dock) on demand.
    /// </summary>
    public static async ValueTask<IJSObjectReference?> CreateAsync(this MudTailRuntime mudTail, string name, ElementReference host,
        object options, object? dotNetRef = null) =>
        await mudTail.GetAsync() is { } runtime
            ? await runtime.InvokeAsync<IJSObjectReference>("create", name, host, options, dotNetRef)
            : null;

    /// <summary>Disposes a handle, ignoring a page that is already gone.</summary>
    public static async ValueTask DisposeHandleAsync(IJSObjectReference? handle)
    {
        if (handle is null) return;
        try
        {
            await handle.InvokeVoidAsync("dispose");
            await handle.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
    }
}
