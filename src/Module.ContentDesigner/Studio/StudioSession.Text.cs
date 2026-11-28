using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// Plain text edited in place on the canvas, saved through Content when the edit is committed (Enter, or the focus
/// leaves). The canvas shows the new text at once, marked as saving; Content's answer then replaces the post. Content
/// decides who may edit a value: a refusal puts the old text back, says why, and the part stays read only until the
/// post is opened again (a 403) or shows in Problems (a 422). A text someone changed meanwhile (409) reloads the post and
/// keeps the caller's text aside (<see cref="SetAsideText"/>) to copy from. Text edits are not part of the design's
/// undo: Content keeps their history (revisions).
/// </summary>
public sealed partial class StudioSession
{
    private readonly HashSet<string> _savingText = new(StringComparer.Ordinal);
    private readonly HashSet<string> _textRefused = new(StringComparer.Ordinal);

    /// <summary>The parts whose text is being saved now.</summary>
    public IReadOnlySet<string> SavingText => _savingText;

    /// <summary>The last text Content did not take because it changed meanwhile: the part and the caller's text.</summary>
    public (string Part, string Text)? SetAsideText { get; private set; }

    /// <summary>What decides which parts are editable now; the canvas builds its page again when it changes.</summary>
    public string TextState => $"{Locked}|{string.Join(',', _savingText)}|{string.Join(',', _textRefused)}";

    public void ClearSetAsideText()
    {
        SetAsideText = null;
        Notify();
    }

    /// <summary>Whether a part may be edited in place now (the canvas offers it only when the server would take it).</summary>
    public bool CanEditText(string part) =>
        !Locked && !_textRefused.Contains(part) && !_savingText.Contains(part) && Document is { } document && TextParts.KeyOf(document, part) is not null;

    /// <summary>Saves one text edited in place; false when nothing was saved (the reason is shown).</summary>
    public async Task<bool> EditTextAsync(string part, string text)
    {
        if (Document is not { } before || TextParts.KeyOf(before, part) is not { } key) return false;
        if (!CanEditText(part))
        {
            await _toast(Locked ? "The post is read only now: you may not change it." : "This text cannot be edited here now.", "warning");
            return false;
        }
        var expected = TextParts.TextOf(before, part, key);
        var next = TextParts.OneLine(text);
        if (next == expected) return true;

        _savingText.Add(part);
        Document = TextParts.With(before, part, key, next); // shown at once, marked as saving
        Notify();
        var contentId = part.StartsWith("content:", StringComparison.Ordinal) ? part["content:".Length..] : null;
        try
        {
            return await RunAsync(async api =>
            {
                try
                {
                    Accept(await api.EditTextAsync(PostId, new TextEdit
                    {
                        Part = part, Key = contentId is null ? null : key, Text = next, Expected = expected, Culture = Culture
                    }));
                    if (contentId is not null) ChangedContents = new HashSet<string> { contentId };
                }
                catch (ContentDesignerApiException e)
                {
                    Document = before; // the old text comes back
                    switch (e.StatusCode)
                    {
                        case 409:
                            SetAsideText = (part, next);
                            Accept(await api.GetWorkspaceAsync(PostId, Culture));
                            throw new InvalidOperationException("Someone changed this text in Content meanwhile; it is reloaded. Your text is kept aside to copy from.");
                        case 422 when ContentRefusals.FromAnswer(e.Response, contentId) is { Count: > 0 } problems:
                            Refusals.Add(contentId, problems);
                            throw new InvalidOperationException("Content refused the text: see Problems.");
                        case 403:
                            _textRefused.Add(part);
                            break;
                    }
                    throw;
                }
            });
        }
        finally
        {
            _savingText.Remove(part);
            Notify();
        }
    }
}
