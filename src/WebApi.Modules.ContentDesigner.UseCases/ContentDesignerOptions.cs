namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases;

/// <summary>Module settings (section <c>ContentDesigner</c>; defaults in the manifest, changed on the Host Modules page).</summary>
public sealed class ContentDesignerOptions
{
    /// <summary>The largest page a list returns.</summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// The gateway's internal address, where this module calls Content and DynamicFields (<c>{ApiBaseUrl}/api/content/...</c>).
    /// Always over HTTP: modules load in isolated contexts and never call each other in process.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>The public site's address, prefixed to a post's permalink for the canonical link (empty: the permalink as is).</summary>
    public string SiteBaseUrl { get; set; } = "";

    /// <summary>The AMP page's address; <c>{publicId}</c> is the post's public id.</summary>
    public string AmpUrlTemplate { get; set; } = "/api/contentdesigner/render/{publicId}/amp";

    /// <summary>The URL of an asset (a Storage file id); <c>{id}</c> is the id.</summary>
    public string AssetUrlTemplate { get; set; } = "/api/storage/items/{id}/content";

    /// <summary>The canonical page's stylesheets, comma separated (MudTail's core and its editorial pack).</summary>
    public string Stylesheets { get; set; } =
        "/_content/soft.square.net.UI.MudTail/css/mudtail.min.css,/_content/soft.square.net.UI.MudTail/css/packs/editorial.min.css";

    /// <summary>How long a rendered public page stays cached, in minutes (0: not cached).</summary>
    public int RenderCacheMinutes { get; set; } = 10;

    /// <summary>
    /// Optional service client (client credentials) for public renders: an anonymous visitor cannot read DynamicFields,
    /// so without it a public page renders without JSON-LD and with root values typed by their shape.
    /// </summary>
    public string ServiceAuthority { get; set; } = "";
    public string ServiceClientId { get; set; } = "";
    public string ServiceClientSecret { get; set; } = "";
}
