namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>
/// Draws a dock panel's icon. A DockPanel contribution names a Lucide icon ("layers"), while MudDock takes the icon as
/// markup, and MudTail's own icon set is a small curated map that does not use Lucide's names. So the studio carries the
/// markup of the Lucide icons its own panels name (Lucide 1.70, ISC licence). A panel from another module whose icon is
/// not here gets no icon, as before.
/// </summary>
public static class StudioIcons
{
    private static readonly Dictionary<string, string> Lucide = new(StringComparer.Ordinal)
    {
        ["layout-template"] = "<rect width='18' height='7' x='3' y='3' rx='1'/><rect width='9' height='7' x='3' y='14' rx='1'/><rect width='5' height='7' x='16' y='14' rx='1'/>",
        ["code"] = "<path d='m16 18 6-6-6-6'/><path d='m8 6-6 6 6 6'/>",
        ["layers"] = "<path d='M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z'/><path d='M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12'/><path d='M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17'/>",
        ["list-tree"] = "<path d='M8 5h13'/><path d='M13 12h8'/><path d='M13 19h8'/><path d='M3 10a2 2 0 0 0 2 2h3'/><path d='M3 5v12a2 2 0 0 0 2 2h3'/>",
        ["shapes"] = "<path d='M8.3 10a.7.7 0 0 1-.626-1.079L11.4 3a.7.7 0 0 1 1.198-.043L16.3 8.9a.7.7 0 0 1-.572 1.1Z'/><rect x='3' y='14' width='7' height='7' rx='1'/><circle cx='17.5' cy='17.5' r='3.5'/>",
        ["folder-tree"] = "<path d='M20 10a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-2.5a1 1 0 0 1-.8-.4l-.9-1.2A1 1 0 0 0 15 3h-2a1 1 0 0 0-1 1v5a1 1 0 0 0 1 1Z'/><path d='M20 21a1 1 0 0 0 1-1v-3a1 1 0 0 0-1-1h-2.9a1 1 0 0 1-.88-.55l-.42-.85a1 1 0 0 0-.92-.6H13a1 1 0 0 0-1 1v5a1 1 0 0 0 1 1Z'/><path d='M3 5a2 2 0 0 0 2 2h3'/><path d='M3 3v13a2 2 0 0 0 2 2h3'/>",
        ["sliders-horizontal"] = "<path d='M10 5H3'/><path d='M12 19H3'/><path d='M14 3v4'/><path d='M16 17v4'/><path d='M21 12h-9'/><path d='M21 19h-5'/><path d='M21 5h-7'/><path d='M8 10v4'/><path d='M8 12H3'/>",
        ["braces"] = "<path d='M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1'/><path d='M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1'/>",
        ["circle-alert"] = "<circle cx='12' cy='12' r='10'/><line x1='12' x2='12' y1='8' y2='12'/><line x1='12' x2='12.01' y1='16' y2='16'/>",
        ["activity"] = "<path d='M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2'/>",
    };

    /// <summary>The SVG markup of a Lucide icon, or "" when the name is empty or not one the studio carries.</summary>
    public static string Svg(string? name, int size = 16) =>
        name is not null && Lucide.TryGetValue(name, out var inner)
            ? $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' class='lucide lucide-{name}' aria-hidden='true'>{inner}</svg>"
            : "";
}
