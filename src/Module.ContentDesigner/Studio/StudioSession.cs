using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;
using RenderDocument = soft.square.net.ContentDesigner.Rendering.Model.RenderDocument;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// One open post in the studio: what the page and every panel share (the panels are separate Blazor root components,
/// mounted by the host's mb-module-panel, so they find the session through <see cref="StudioSessions"/>). It holds the
/// workspace, the unsaved views with undo, the current view and selection, the last check and the change feed, and
/// raises <see cref="Changed"/> after every change. Calls to the API go through <see cref="RunAsync"/>.
/// </summary>
public sealed class StudioSession
{
    private const int UndoDepth = 50;
    private const int ActivityKept = 100;

    private readonly Func<ContentDesignerClient> _api;
    private readonly Func<Exception, string> _describe;
    private readonly Func<string, string, ValueTask> _toast;
    private readonly Stack<DViews> _undo = new();
    private readonly Stack<DViews> _redo = new();
    private int _busy;

    public StudioSession(int postId, Func<ContentDesignerClient> api, Func<Exception, string> describe, Func<string, string, ValueTask> toast)
    {
        PostId = postId;
        _api = api;
        _describe = describe;
        _toast = toast;
    }

    public int PostId { get; }

    public WorkspaceDto? Workspace { get; private set; }
    public RenderDocument? Document { get; private set; }
    public PostDesignDto? Design { get; private set; }
    public IReadOnlyDictionary<int, RenderBlockTemplate> BlockTemplates { get; private set; } = new Dictionary<int, RenderBlockTemplate>();

    /// <summary>The tenant's block templates (the Explorer, the Toolbox and Properties list them).</summary>
    public IReadOnlyList<BlockTemplateDto> BlockTemplateList { get; private set; } = [];
    public IReadOnlyList<DocumentTemplateDto> Templates { get; private set; } = [];

    /// <summary>The views as edited; <see cref="Saved"/> as last read or saved.</summary>
    public DViews? Views { get; private set; }
    public DViews? Saved { get; private set; }
    public bool Dirty => Views is not null && !ReferenceEquals(Views, Saved);

    public string View { get; private set; } = Studio.Views.Mobile;
    public string? Selected { get; private set; }
    public string? Culture { get; private set; }
    public string? Direction { get; private set; }
    public bool ShowHidden { get; private set; } = true;

    /// <summary>Whether the library picker ("add existing content") is open.</summary>
    public bool LibraryOpen { get; private set; }

    public void ShowLibrary(bool open)
    {
        if (LibraryOpen == open) return;
        LibraryOpen = open;
        Notify();
    }

    public DesignReport? Report { get; private set; }
    public bool Checking { get; private set; }

    /// <summary>
    /// True when the views or the culture changed since <see cref="Report"/> was made: the report is shown as out of
    /// date until the next Check (an undo back to the checked views makes it current again).
    /// </summary>
    public bool ReportStale => Report is not null && (!ReferenceEquals(_checkedViews, Views) || _checkedCulture != Culture);

    private DViews? _checkedViews;
    private string? _checkedCulture;

    /// <summary>The post's change feed, newest first, and the last event id read.</summary>
    public List<FeedEvent> Activity { get; } = [];
    public long LastEvent { get; private set; }

    /// <summary>Content ids that changed in the last reload, for the canvas and the tray to flash.</summary>
    public IReadOnlySet<string> ChangedContents { get; private set; } = new HashSet<string>();

    public bool Busy => _busy > 0;
    public string? Error { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The current view's design as it shows (its own, or the one it inherits).</summary>
    public DView? Current => Views is null ? null : DesignEditor.Resolve(Views, View);

    public string? InheritsFrom => Views?[View].InheritsFrom;

    public event Action? Changed;

    public void Notify() => Changed?.Invoke();

    // ───────────── loading ─────────────

    /// <summary>Opens the post: its workspace, a design (made from the kind's default when it has none), the templates.</summary>
    public Task<bool> OpenAsync() => RunAsync(async api =>
    {
        var workspace = await api.GetWorkspaceAsync(PostId, Culture);
        var design = workspace.Design ?? await api.OpenDesignAsync(PostId, new OpenRequest());
        Accept(workspace);
        TakeDesign(design, clearHistory: true);

        await LoadBlockTemplatesAsync(api);
        Templates = await api.ListDocumentTemplatesAsync(workspace.Post.Kind);

        // Start the feed at its end: the studio shows what happens from now on.
        var page = await api.GetPostEventsAsync(PostId, 0);
        while (page.More) page = await api.GetPostEventsAsync(PostId, page.Last);
        LastEvent = page.Last;
        Activity.Clear();
        Activity.AddRange(page.Events.Reverse().Take(20));
    });

    private async Task LoadBlockTemplatesAsync(ContentDesignerClient api)
    {
        var templates = await api.ListBlockTemplatesAsync();
        BlockTemplateList = [.. templates.OrderBy(t => t.FieldKey, StringComparer.Ordinal).ThenBy(t => t.View).ThenBy(t => t.Name, StringComparer.CurrentCulture)];
        BlockTemplates = templates.ToDictionary(t => t.Id, t => new RenderBlockTemplate(t.PartOrder, t.HiddenParts,
            t.Tokens is null ? null : new Dictionary<string, string>(t.Tokens)));
    }

    /// <summary>The post again (after a change in Content), keeping the design, the edits and the selection.</summary>
    public Task<bool> ReloadPostAsync(IReadOnlySet<string>? changed = null) => RunAsync(async api =>
    {
        Accept(await api.GetWorkspaceAsync(PostId, Culture));
        ChangedContents = changed ?? new HashSet<string>();
    }, toast: false);

    private void Accept(WorkspaceDto workspace)
    {
        Workspace = workspace;
        Document = StudioJson.Convert<RenderDocument>(workspace.Document);
        Direction ??= Document.Direction;
        Culture ??= Document.Culture;
    }

    internal void TakeDesign(PostDesignDto design, bool clearHistory)
    {
        if (Views is not null && !clearHistory) Push(Views);
        Design = design;
        Views = Saved = StudioJson.From(design.Draft);
        if (clearHistory) { _undo.Clear(); _redo.Clear(); }
        if (Selected is not null && Current is { } current && !DesignEditor.Exists(current, Selected)) Selected = null;
    }

    // ───────────── editing ─────────────

    /// <summary>Applies an edit to the views (undoable); a refused edit shows its reason and changes nothing.</summary>
    public bool Edit(Func<DViews, DViews> edit, string? select = null)
    {
        if (Views is null) return false;
        DViews next;
        try { next = edit(Views); }
        catch (EditException e)
        {
            _ = _toast(e.Message, "warning");
            return false;
        }
        if (ReferenceEquals(next, Views)) return false;
        Push(Views);
        _redo.Clear();
        Views = next;
        if (select is not null) Selected = select;
        Notify();
        return true;
    }

    /// <summary><see cref="Edit"/> for an edit that also says what to select (a region it added).</summary>
    public bool EditWith(Func<DViews, (DViews Views, string? Select)> edit)
    {
        string? select = null;
        if (!Edit(v =>
            {
                var (next, selected) = edit(v);
                select = selected;
                return next;
            })) return false;
        if (select is not null && Selected != select)
        {
            Selected = select;
            Notify();
        }
        return true;
    }

    private void Push(DViews views)
    {
        _undo.Push(views);
        if (_undo.Count > UndoDepth)
        {
            var keep = _undo.Take(UndoDepth).Reverse().ToList();
            _undo.Clear();
            foreach (var v in keep) _undo.Push(v);
        }
    }

    public void Undo()
    {
        if (Views is null || _undo.Count == 0) return;
        _redo.Push(Views);
        Views = _undo.Pop();
        Notify();
    }

    public void Redo()
    {
        if (Views is null || _redo.Count == 0) return;
        _undo.Push(Views);
        Views = _redo.Pop();
        Notify();
    }

    /// <summary>Drops the unsaved edits.</summary>
    public void Revert()
    {
        if (!Dirty || Saved is null) return;
        Push(Views!);
        Views = Saved;
        Notify();
    }

    public void SelectView(string view)
    {
        if (View == view) return;
        View = view;
        Notify();
    }

    public void Select(string? id)
    {
        if (Selected == id) return;
        Selected = id;
        Notify();
    }

    public void SetShowHidden(bool show)
    {
        ShowHidden = show;
        Notify();
    }

    public void SetDirection(string? direction)
    {
        Direction = direction;
        Notify();
    }

    /// <summary>Reads the post in another language (its translation overlay, when Content serves one).</summary>
    public async Task SetCultureAsync(string? culture)
    {
        Culture = string.IsNullOrWhiteSpace(culture) ? null : culture.Trim();
        Direction = null;
        await ReloadPostAsync();
        Direction ??= Document?.Direction;
        Notify();
    }

    // ───────────── drag and drop ─────────────

    /// <summary>The block field of a content of the post.</summary>
    public string? FieldOf(string contentId) => Workspace?.Contents.FirstOrDefault(c => c.ContentId == contentId)?.FieldKey;

    public BlockTemplateDto? BlockTemplate(int id) => BlockTemplateList.FirstOrDefault(t => t.Id == id);

    /// <summary>A drop on the canvas: one edit (undoable), or a library content added to the post and then placed.</summary>
    public async Task<bool> DropAsync(CanvasDrop drop)
    {
        if (Views is null) return false;
        DropPlan plan;
        try
        {
            plan = DropPlanner.Plan(Views, View, drop, FieldOf, id => BlockTemplate(id) is { } t
                ? new TemplateInfo(t.Id, t.FieldKey, t.View?.ToString() ?? "", t.Tokens is null ? null : new Dictionary<string, string>(t.Tokens))
                : null);
        }
        catch (EditException e)
        {
            await _toast(e.Message, "warning");
            return false;
        }
        if (plan.Attach is { } attach) return await AttachAsync(attach.ContentId, required: false, attach.RegionId, attach.Index);
        return plan.Edit is { } edit && EditWith(edit);
    }

    // ───────────── the library ─────────────

    public async Task<LibraryPage?> LibraryAsync(string? fieldKey, string? stage, string? sectionId, int skip, int take)
    {
        LibraryPage? page = null;
        await RunAsync(async api => page = await api.ListLibraryAsync(fieldKey, stage, sectionId, skip, take), toast: false);
        return page;
    }

    public async Task<LibraryItemDto?> LibraryItemAsync(string contentId)
    {
        LibraryItemDto? item = null;
        await RunAsync(async api => item = await api.GetLibraryItemAsync(contentId), toast: false);
        return item;
    }

    /// <summary>
    /// Adds a library content to the post (at the post's version), then places it in a region at a position when one is
    /// given (otherwise it shows through <c>items</c>). A post that changed meanwhile is reloaded, and nothing is added.
    /// </summary>
    public Task<bool> AttachAsync(string contentId, bool required, string? regionId = null, int index = int.MaxValue) => RunAsync(async api =>
    {
        if (Workspace is null) return;
        if (Workspace.Contents.Any(c => c.ContentId == contentId))
        {
            await _toast("That content is in the post already.", "info");
            return;
        }
        try
        {
            Accept(await api.AttachContentAsync(PostId, new AttachRequest
            {
                PostVersion = Workspace.Post.Version, ContentId = contentId, Required = required, Culture = Culture
            }));
        }
        catch (ContentDesignerApiException e) when (e.StatusCode == 409)
        {
            Accept(await api.GetWorkspaceAsync(PostId, Culture));
            throw new InvalidOperationException("The post changed in Content meanwhile; it is reloaded. Add the content again.");
        }
        ChangedContents = new HashSet<string> { contentId };
        var part = "content:" + contentId;
        if (regionId is not null) Edit(v => DesignEditor.PlaceAt(v, View, part, regionId, index), part);
        else Select(part);
        await _toast("The content is added to the post.", "success");
    });

    // ───────────── block templates ─────────────

    /// <summary>
    /// Saves how a content shows as a block template of its field: in this view, or one template per view (named
    /// "{name} · {view}"), from the views as edited (unsaved ones too).
    /// </summary>
    public Task<bool> SaveBlockAsTemplateAsync(string part, string name, bool allViews, bool withContent) => RunAsync(async api =>
    {
        if (Views is null) return;
        var design = StudioJson.ToApi(Views);
        var views = allViews ? Studio.Views.All : [View];
        foreach (var view in views)
            await api.SaveBlockAsTemplateAsync(PostId, Studio.Views.Device(view), new SaveBlockRequest
            {
                Part = part, Name = allViews ? $"{name} · {Studio.Views.Label(view)}" : name, WithContent = withContent, Design = design
            });
        await LoadBlockTemplatesAsync(api);
        await _toast(allViews ? $"Saved as {views.Count} block templates." : $"Saved as the block template \"{name}\".", "success");
    });

    /// <summary>Renames a block template, or changes its inner parts (their order and the hidden ones).</summary>
    public Task<bool> EditBlockTemplateAsync(BlockTemplateDto template, string name, IReadOnlyList<string> partOrder, IReadOnlyList<string> hiddenParts) =>
        RunAsync(async api =>
        {
            await api.EditBlockTemplateAsync(template.Id, new BlockTemplateEdit
            {
                Version = template.Version, Name = name, PartOrder = partOrder, HiddenParts = hiddenParts, Tokens = template.Tokens, Content = template.Content
            });
            await LoadBlockTemplatesAsync(api);
        });

    public Task<bool> DeleteBlockTemplateAsync(BlockTemplateDto template) => RunAsync(async api =>
    {
        await api.DeleteBlockTemplateAsync(template.Id, template.Version);
        await LoadBlockTemplatesAsync(api);
        await _toast($"The block template \"{template.Name}\" is deleted.", "success");
    });

    // ───────────── saving and the API ─────────────

    public Task<bool> SaveAsync() => RunAsync(async api =>
    {
        if (!Dirty || Design is null || Views is null) return;
        var edited = Views;
        var saved = await api.SaveDesignAsync(PostId, new SaveRequest { Version = Design.Version, Views = StudioJson.ToApi(edited) });
        Design = saved;
        Views = Saved = edited; // keep the instance: the canvas and the undo stack refer to it
        await _toast("Design saved.", "success");
    });

    /// <summary>Saves first when there are unsaved edits (template and publish actions work on the stored draft).</summary>
    private async Task<bool> SavedFirstAsync() => !Dirty || await SaveAsync();

    /// <summary>The checks (and the JSON-LD) of the views as edited, without saving them.</summary>
    public async Task<bool> CheckAsync()
    {
        if (Views is not { } views) return false;
        var culture = Culture;
        Checking = true;
        Notify();
        try
        {
            return await RunAsync(async api =>
            {
                Report = await api.CheckDesignAsync(PostId, new CheckRequest { Design = StudioJson.ToApi(views), Culture = culture });
                (_checkedViews, _checkedCulture) = (views, culture);
            }, toast: false);
        }
        finally
        {
            Checking = false;
            Notify();
        }
    }

    public async Task<PreviewDto?> PreviewAsync(bool amp)
    {
        PreviewDto? result = null;
        if (Views is null) return null;
        await RunAsync(async api => result = await api.PreviewDesignAsync(PostId,
            new PreviewRequest { Amp = amp, Culture = Culture, Views = StudioJson.ToApi(Views) }), toast: false);
        return result;
    }

    /// <summary>Takes a document template's views: all of them, or only the current view's.</summary>
    public async Task<bool> ApplyTemplateAsync(int templateId, bool currentViewOnly)
    {
        if (!await SavedFirstAsync()) return false;
        return await RunAsync(async api =>
        {
            var design = await api.ApplyTemplateAsync(PostId, new ApplyRequest
            {
                Version = Design!.Version,
                TemplateId = templateId,
                Views = currentViewOnly ? [Studio.Views.Kind(View)] : null
            });
            TakeDesign(design, clearHistory: false);
            await _toast("Template applied.", "success");
        });
    }

    public async Task<bool> SaveAsTemplateAsync(string name, string? description, bool makeDefault)
    {
        if (!await SavedFirstAsync()) return false;
        return await RunAsync(async api =>
        {
            await api.SaveDesignAsTemplateAsync(PostId, new SaveAsTemplateRequest { Name = name, Description = description, MakeDefault = makeDefault });
            Templates = await api.ListDocumentTemplatesAsync(Workspace!.Post.Kind);
            await _toast($"Saved as the template \"{name}\".", "success");
        });
    }

    public Task<bool> SetDefaultAsync(int templateId, bool isDefault) => RunAsync(async api =>
    {
        await api.SetDefaultTemplateAsync(templateId, new DefaultRequest { IsDefault = isDefault });
        Templates = await api.ListDocumentTemplatesAsync(Workspace!.Post.Kind);
    });

    public async Task<bool> PublishAsync()
    {
        if (!await SavedFirstAsync()) return false;
        return await RunAsync(async api =>
        {
            Design = await api.PublishDesignAsync(PostId, new PublishRequest { Version = Design!.Version, Culture = Culture });
            await _toast("Design published.", "success");
        });
    }

    public async Task<bool> UnpublishAsync()
    {
        if (!await SavedFirstAsync()) return false;
        return await RunAsync(async api =>
        {
            Design = await api.UnpublishDesignAsync(PostId, new UnpublishRequest { Version = Design!.Version });
            await _toast("The published design is down.", "success");
        });
    }

    // ───────────── hot reload ─────────────

    /// <summary>
    /// Reads the post's change feed since the last event. When contents or the post changed, reloads the post (the design,
    /// the edits and the selection stay) and checks again. Returns whether anything changed.
    /// </summary>
    public async Task<bool> PollAsync(CancellationToken ct)
    {
        if (Workspace is null) return false;
        FeedPage page;
        var events = new List<FeedEvent>();
        try
        {
            var after = LastEvent;
            do
            {
                page = await _api().GetPostEventsAsync(PostId, after, ct);
                events.AddRange(page.Events);
                after = page.Last;
            } while (page.More && !ct.IsCancellationRequested);
            LastEvent = after;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false; // the next tick tries again; the toolbar shows nothing for a missed poll
        }
        if (events.Count == 0) return false;

        Activity.InsertRange(0, events.AsEnumerable().Reverse());
        if (Activity.Count > ActivityKept) Activity.RemoveRange(ActivityKept, Activity.Count - ActivityKept);
        var changed = events.Select(e => e.ContentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        await ReloadPostAsync(changed);
        Notify();
        if (Report is not null) await CheckAsync();
        return true;
    }

    // ───────────── calls ─────────────

    /// <summary>Runs API calls: busy state, a readable error (toast unless asked not to), and <see cref="Changed"/>.</summary>
    public async Task<bool> RunAsync(Func<ContentDesignerClient, Task> call, bool toast = true)
    {
        Interlocked.Increment(ref _busy);
        Error = null;
        Notify();
        try
        {
            await call(_api());
            return true;
        }
        catch (Exception e)
        {
            Error = _describe(e);
            if (toast) await _toast(Error, "error");
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            Notify();
        }
    }
}
