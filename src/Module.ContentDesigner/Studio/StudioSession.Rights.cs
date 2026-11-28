using System.Text.Json;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// The caller's rights on the post's design, which a shift opens and closes. Without the right to arrange, the studio is
/// read only. When the right goes while the studio is open (the live topic is revoked, or the server answers 403 after it
/// allowed), the shift ended: unsaved work is kept aside in the browser, and when the right returns the studio opens up
/// again and does the save it still owes.
/// </summary>
public sealed partial class StudioSession
{
    private bool _suspended;
    private bool _hadRights;
    private bool _lost;

    public PostRightsDto? Rights { get; private set; }

    /// <summary>True while the design may not be changed (or, after a revoke, until the server says it may).</summary>
    public bool Locked => _suspended || Rights is { CanArrange: false };

    /// <summary>The right went while the studio was open: "your shift ended".</summary>
    public bool ShiftEnded => Locked && (_suspended || _lost);

    /// <summary>The caller's right to publish or take down the design now.</summary>
    public bool CanPublish => !Locked && Rights?.CanPublish != false;

    /// <summary>The newsroom workflow's say on publishing; null when nobody was asked (or an older server).</summary>
    public PublishGateDto? Publishing => Rights?.Publishing;

    /// <summary>Publishing needs the right and the workflow's leave; taking a design down needs only the right.</summary>
    public bool CanPublishNow => CanPublish && Publishing?.Allows != false;

    /// <summary>A save was asked for and refused: it is done when the right returns.</summary>
    public bool SavePending { get; private set; }

    /// <summary>True when the unsaved work is also in the browser's storage, so closing the page keeps it.</summary>
    public bool DraftKept { get; private set; }

    /// <summary>A kept copy that was not applied by itself, because the saved design changed since: the user decides.</summary>
    public StoredDraft? OfferedCopy { get; private set; }

    /// <summary>Raised when the right returns, so the studio joins the live topic again.</summary>
    public event Action? RightsReturned;

    private string DraftKey => $"contentdesigner.draft:{_owner}:{Workspace?.Post.PublicId ?? PostId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>The server revoked something live: lock at once, then ask it what the rights are.</summary>
    public async Task SuspendAsync(CancellationToken ct = default)
    {
        if (!Locked)
        {
            _suspended = true;
            await KeepAsync();
            Notify();
        }
        await VerifyRightsAsync(ct);
    }

    /// <summary>Asks for the rights now. A 403 is an answer (no rights); any other failure leaves things as they are.</summary>
    public async Task VerifyRightsAsync(CancellationToken ct = default)
    {
        PostRightsDto rights;
        try { rights = await _api().GetPostRightsAsync(PostId, ct); }
        catch (ContentDesignerApiException e) when (e.StatusCode == 403)
        {
            rights = new PostRightsDto { PostId = PostId, CanArrange = false, CanPublish = false, Reason = "no-right" };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return;
        }
        await ApplyRightsAsync(rights);
    }

    private async Task ApplyRightsAsync(PostRightsDto rights)
    {
        var wasLocked = Locked;
        Rights = rights;
        _suspended = false;
        if (rights.CanArrange)
        {
            _hadRights = true;
            _lost = false;
            Notify();
            if (wasLocked) await UnlockedAsync();
        }
        else
        {
            _lost |= _hadRights;
            if (!wasLocked) await KeepAsync();
            LibraryOpen = false;
            Notify();
        }
    }

    private async Task UnlockedAsync()
    {
        RightsReturned?.Invoke();
        if (Design is null)
        {
            await OpenAsync(); // opened without rights on a post that had no design yet
            return;
        }
        if (SavePending && Dirty) await RetrySaveAsync();
        else
        {
            SavePending = false;
            await _toast(Dirty ? "Your rights are back. Your unsaved work is here: save it when ready." : "Your rights are back.", "success");
        }
    }

    private async Task RetrySaveAsync()
    {
        if (Design is null || Views is null) return;
        var edited = Views;
        try
        {
            Design = await _api().SaveDesignAsync(PostId, new SaveRequest { Version = Design.Version, Views = StudioJson.ToApi(edited) });
            Saved = edited;
            SavePending = false;
            await DropKeptAsync();
            await _toast("Your rights are back, and your unsaved work is saved.", "success");
        }
        catch (ContentDesignerApiException e) when (e.StatusCode == 409)
        {
            // Someone saved meanwhile: their design shows, and the kept copy waits for the user's choice.
            var copy = Copy(edited, savePending: false);
            try
            {
                var current = await _api().GetDesignAsync(PostId);
                SavePending = false;
                if (_drafts is not null) DraftKept = await _drafts.SaveAsync(DraftKey, copy);
                OfferedCopy = copy;
                TakeDesign(current, clearHistory: true);
                await _toast("The design changed while you were away, so your work is not saved over it. Your copy is kept.", "warning");
            }
            catch (Exception again)
            {
                Error = _describe(again);
                await _toast(Error, "error");
            }
        }
        catch (ContentDesignerApiException e) when (e.StatusCode == 403)
        {
            await VerifyRightsAsync();
        }
        catch (Exception e)
        {
            Error = _describe(e);
            await _toast(Error, "error");
        }
        Notify();
    }

    private StoredDraft Copy(DViews views, bool savePending) =>
        new(Design?.Version ?? 0, JsonSerializer.Serialize(views, StudioJson.Options), savePending, DateTimeOffset.UtcNow);

    private async Task KeepAsync()
    {
        if (_drafts is null || Design is null || Views is null || !Dirty) return;
        DraftKept = await _drafts.SaveAsync(DraftKey, Copy(Views, SavePending));
    }

    private async Task DropKeptAsync()
    {
        if (_drafts is null || OfferedCopy is not null) return;
        await _drafts.RemoveAsync(DraftKey);
        DraftKept = false;
    }

    /// <summary>
    /// Work kept from an earlier visit. It comes back as unsaved edits only when the saved design is still the one it was
    /// made on; otherwise it is offered.
    /// </summary>
    private async Task RestoreKeptAsync()
    {
        if (_drafts is null || Design is null || Saved is null) return;
        if (await _drafts.LoadAsync(DraftKey) is not { } kept) return;
        if (Read(kept) is not { } views || JsonSerializer.Serialize(views, StudioJson.Options) == JsonSerializer.Serialize(Saved, StudioJson.Options))
        {
            await _drafts.RemoveAsync(DraftKey);
            return;
        }
        DraftKept = true;
        if (kept.DesignVersion != Design.Version)
        {
            OfferedCopy = kept;
            Notify();
            return;
        }
        Push(Views!);
        Views = views;
        SavePending = kept.SavePending;
        Notify();
        if (SavePending && !Locked) await RetrySaveAsync();
        else await _toast("Your unsaved work from earlier is back.", "info");
    }

    private static DViews? Read(StoredDraft draft)
    {
        try { return JsonSerializer.Deserialize<DViews>(draft.Views, StudioJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return null; }
    }

    /// <summary>Puts the offered copy in the editor as unsaved edits (undoable): saving it then replaces the saved design.</summary>
    public void RestoreCopy()
    {
        if (Locked || OfferedCopy is not { } copy || Read(copy) is not { } views) return;
        OfferedCopy = null;
        if (!Edit(_ => views)) Notify();
    }

    public async Task DiscardCopyAsync()
    {
        if (OfferedCopy is null) return;
        OfferedCopy = null;
        await DropKeptAsync();
        await KeepAsync();
        Notify();
    }
}
