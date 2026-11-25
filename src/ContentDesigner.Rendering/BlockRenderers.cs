using Microsoft.AspNetCore.Components;
using soft.square.net.ContentDesigner.Rendering.Blocks;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>
/// The <c>BlockRenderer</c> contract: the Razor component that renders a block field on the web page and, optionally,
/// on the AMP page. Both take one parameter, <c>Context</c> (a <see cref="BlockContext"/>); derive from
/// <see cref="BlockComponent"/>. An AMP component writes only AMP markup (amp-img, amp-video, ...), no script and no
/// style attribute.
/// </summary>
public sealed record BlockRenderer(string FieldKey, Type Component, Type? AmpComponent)
{
    public BlockRenderer(string fieldKey, Type component, bool ampSafe) : this(fieldKey, component, ampSafe ? component : null) { }
}

/// <summary>
/// The renderers by field key. <see cref="Default"/> holds the built-in ones, one per DynamicFields starter block;
/// any other field renders with <see cref="FallbackBlock"/> (its values as a list), on the web and in AMP.
/// </summary>
public sealed class BlockRendererRegistry
{
    private readonly Dictionary<string, BlockRenderer> _renderers = new(StringComparer.Ordinal);

    public static BlockRendererRegistry Default { get; } = new BlockRendererRegistry()
        .Add(new("block.heading", typeof(HeadingBlock), ampSafe: true))
        .Add(new("block.text", typeof(TextBlock), ampSafe: true))
        .Add(new("block.paragraph", typeof(ParagraphBlock), ampSafe: true))
        .Add(new("block.quote", typeof(QuoteBlock), ampSafe: true))
        .Add(new("block.image", typeof(ImageBlock), typeof(AmpImageBlock)))
        .Add(new("block.video", typeof(VideoBlock), typeof(AmpVideoBlock)))
        .Add(new("block.audio", typeof(AudioBlock), typeof(AmpAudioBlock)))
        .Add(new("block.gallery", typeof(GalleryBlock), typeof(AmpGalleryBlock)));

    public BlockRendererRegistry Add(BlockRenderer renderer)
    {
        if (!typeof(IComponent).IsAssignableFrom(renderer.Component) || renderer.AmpComponent is { } amp && !typeof(IComponent).IsAssignableFrom(amp))
            throw new ArgumentException($"The renderer of '{renderer.FieldKey}' is not a component.");
        _renderers[renderer.FieldKey] = renderer;
        return this;
    }

    /// <summary>A copy to add renderers to.</summary>
    public BlockRendererRegistry With(params BlockRenderer[] renderers)
    {
        var copy = new BlockRendererRegistry();
        foreach (var renderer in _renderers.Values.Concat(renderers)) copy.Add(renderer);
        return copy;
    }

    /// <summary>The component for a field; null in AMP for a renderer without an AMP variant (the block is left out).</summary>
    public Type? Resolve(string fieldKey, bool amp) =>
        _renderers.TryGetValue(fieldKey, out var renderer) ? amp ? renderer.AmpComponent : renderer.Component : typeof(FallbackBlock);

    public (bool HasRenderer, bool HasAmp) Support(string fieldKey) =>
        _renderers.TryGetValue(fieldKey, out var renderer) ? (true, renderer.AmpComponent is not null) : (false, true);
}

/// <summary>The base of a block renderer.</summary>
public abstract class BlockComponent : ComponentBase
{
    [Parameter, EditorRequired] public BlockContext Context { get; set; } = default!;
}
