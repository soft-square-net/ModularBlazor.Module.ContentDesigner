using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace soft.square.net.ContentDesigner.Rendering.Blocks;

/// <summary>An h1 to h6 element chosen by a level token (h1 ... h6; anything else is h2).</summary>
public sealed class DynamicHeading : ComponentBase
{
    [Parameter] public string Level { get; set; } = "h2";
    [Parameter] public IReadOnlyDictionary<string, object>? Attributes { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Level is ['h', >= '1' and <= '6'] ? Level : "h2");
        builder.AddMultipleAttributes(1, Attributes);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
