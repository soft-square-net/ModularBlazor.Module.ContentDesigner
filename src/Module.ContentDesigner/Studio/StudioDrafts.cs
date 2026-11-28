using System.Text.Json;
using Microsoft.JSInterop;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// Unsaved work kept aside: the views as edited, the design version they were edited from, and whether a save was
/// asked for and is still owed.
/// </summary>
public sealed record StoredDraft(int DesignVersion, string Views, bool SavePending, DateTimeOffset At);

/// <summary>Where the studio keeps unsaved work while it may not save it. Every call is best effort and never throws.</summary>
public interface IDraftStore
{
    /// <summary>True when the draft was kept.</summary>
    Task<bool> SaveAsync(string key, StoredDraft draft);

    Task<StoredDraft?> LoadAsync(string key);

    Task RemoveAsync(string key);
}

/// <summary>
/// The browser's local storage. It may be off, full or blocked (private windows, policies), so every call is wrapped:
/// the studio then keeps the work only as long as the page is open, and says so.
/// </summary>
public sealed class BrowserDraftStore(IJSRuntime js) : IDraftStore
{
    public async Task<bool> SaveAsync(string key, StoredDraft draft)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, JsonSerializer.Serialize(draft, StudioJson.Options));
            return true;
        }
        catch (Exception e) when (e is JSException or InvalidOperationException or TaskCanceledException) { return false; }
    }

    public async Task<StoredDraft?> LoadAsync(string key)
    {
        try
        {
            var text = await js.InvokeAsync<string?>("localStorage.getItem", key);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<StoredDraft>(text, StudioJson.Options);
        }
        catch (Exception e) when (e is JSException or JsonException or InvalidOperationException or TaskCanceledException) { return null; }
    }

    public async Task RemoveAsync(string key)
    {
        try { await js.InvokeVoidAsync("localStorage.removeItem", key); }
        catch (Exception e) when (e is JSException or InvalidOperationException or TaskCanceledException) { }
    }
}
