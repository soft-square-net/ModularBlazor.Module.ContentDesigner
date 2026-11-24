namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases;

/// <summary>Module settings (section <c>ContentDesigner</c>; defaults in the manifest, changed on the Host Modules page).</summary>
public sealed class ContentDesignerOptions
{
    /// <summary>The largest page a list or search returns.</summary>
    public int MaxPageSize { get; set; } = 100;
}
