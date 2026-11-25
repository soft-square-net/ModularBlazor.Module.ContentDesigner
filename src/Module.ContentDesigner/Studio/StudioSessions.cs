using Microsoft.AspNetCore.Components;
using soft.square.net.ModularBlazor.Abstractions.Shell;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>The studio's keys: the studio panels are for, and the page context key of the open post.</summary>
public static class StudioKeys
{
    /// <summary>The studio key dock panels name in <c>ForStudio</c>.</summary>
    public const string Studio = "contentdesigner";

    /// <summary>The page context key the studio sets to the open post's id (Content owns the entity, so its key).</summary>
    public const string Post = "content.post";
}

/// <summary>
/// The open studio sessions, by post id. The studio page adds its session and removes it when it closes; panels look
/// theirs up. Module code, so it goes away with the module (nothing in a host-wide cache).
/// </summary>
public static class StudioSessions
{
    private static readonly Dictionary<int, StudioSession> Open = [];

    /// <summary>Raised when a session opens or closes, for panels mounted before their session existed.</summary>
    public static event Action? Changed;

    public static StudioSession? Find(int postId)
    {
        lock (Open) return Open.GetValueOrDefault(postId);
    }

    public static void Add(StudioSession session)
    {
        lock (Open) Open[session.PostId] = session;
        Changed?.Invoke();
    }

    public static void Remove(StudioSession session)
    {
        lock (Open)
            if (Open.TryGetValue(session.PostId, out var current) && ReferenceEquals(current, session)) Open.Remove(session.PostId);
        Changed?.Invoke();
    }
}

/// <summary>
/// What every studio panel shares: it is a dock panel mounted by the host (mb-module-panel), finds the open post in its
/// <see cref="ContributionContext"/> (or the page context) and re-renders whenever the session changes.
/// </summary>
public abstract class StudioPanelBase : ComponentBase, IDisposable
{
    [Inject] protected IPageContext PageContext { get; set; } = default!;

    /// <summary>Given by the host: this panel's contribution and the studio's context (the open post).</summary>
    [Parameter] public ContributionContext? Context { get; set; }

    protected StudioSession? Session { get; private set; }

    protected override void OnInitialized()
    {
        StudioSessions.Changed += OnSessionsChanged;
        PageContext.Changed += OnSessionsChanged;
    }

    protected override void OnParametersSet() => Attach();

    private int? PostId =>
        int.TryParse(Context?.ContextValue ?? PageContext.Get(StudioKeys.Post), out var id) ? id : null;

    private void Attach()
    {
        var session = PostId is { } id ? StudioSessions.Find(id) : null;
        if (ReferenceEquals(session, Session)) return;
        if (Session is not null) Session.Changed -= OnChanged;
        Session = session;
        if (Session is not null) Session.Changed += OnChanged;
        OnSessionAttached();
    }

    /// <summary>Called when the panel gets (or loses) its session.</summary>
    protected virtual void OnSessionAttached() { }

    private void OnSessionsChanged() => _ = InvokeAsync(() => { Attach(); StateHasChanged(); });

    private void OnChanged() => _ = InvokeAsync(() => { OnSessionChanged(); StateHasChanged(); });

    /// <summary>Called on every change of the session, before the panel renders again.</summary>
    protected virtual void OnSessionChanged() { }

    public virtual void Dispose()
    {
        StudioSessions.Changed -= OnSessionsChanged;
        PageContext.Changed -= OnSessionsChanged;
        if (Session is not null) Session.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
