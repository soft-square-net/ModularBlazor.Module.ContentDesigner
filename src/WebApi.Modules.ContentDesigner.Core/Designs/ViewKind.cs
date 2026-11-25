using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

/// <summary>
/// The four views of a post. Mobile, Tablet and Desktop merge into one responsive page (Mobile is the base, Tablet the
/// <c>md</c> breakpoint, Desktop <c>lg</c>); AMP is a page of its own.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ViewKind>))]
public enum ViewKind
{
    Mobile,
    Tablet,
    Desktop,
    Amp
}

public static class ViewKinds
{
    public static IReadOnlyList<ViewKind> All { get; } = [ViewKind.Mobile, ViewKind.Tablet, ViewKind.Desktop, ViewKind.Amp];

    /// <summary>The views that merge into the responsive page, narrowest first.</summary>
    public static IReadOnlyList<ViewKind> Web { get; } = [ViewKind.Mobile, ViewKind.Tablet, ViewKind.Desktop];

    /// <summary>The view's key in routes and paths: mobile, tablet, desktop, amp.</summary>
    public static string Key(this ViewKind view) => view.ToString().ToLowerInvariant();

    public static bool TryParse(string? text, out ViewKind view) =>
        Enum.TryParse(text, ignoreCase: true, out view) && Enum.IsDefined(view);
}
