using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using soft.square.net.ContentDesigner.Rendering.Model;

namespace soft.square.net.ContentDesigner.Rendering.Blocks;

/// <summary>
/// An AMP media element (amp-img, amp-video, amp-iframe) with a valid layout: <c>fill</c> inside an aspect box,
/// otherwise <c>responsive</c> with the asset's size (16 by 9 when it is unknown).
/// </summary>
public sealed class AmpMedia : ComponentBase
{
    [Parameter, EditorRequired] public string Element { get; set; } = "amp-img";
    [Parameter, EditorRequired] public RenderAsset Asset { get; set; } = default!;
    [Parameter] public bool Boxed { get; set; }
    [Parameter] public IReadOnlyDictionary<string, object>? Extra { get; set; }

    public static bool IsBoxed(IReadOnlyDictionary<string, object> media) =>
        media.Any(a => a.Key.StartsWith("data-mb-aspect", StringComparison.Ordinal) && !Equals(a.Value, "auto"));

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Element);
        builder.AddAttribute(1, "src", Asset.Url);
        if (Boxed)
        {
            builder.AddAttribute(2, "layout", "fill");
        }
        else
        {
            var sized = Asset.Width is > 0 && Asset.Height is > 0;
            builder.AddAttribute(3, "width", (sized ? Asset.Width!.Value : 16).ToString());
            builder.AddAttribute(4, "height", (sized ? Asset.Height!.Value : 9).ToString());
            builder.AddAttribute(5, "layout", "responsive");
        }
        builder.AddMultipleAttributes(6, Extra);
        builder.CloseElement();
    }
}
