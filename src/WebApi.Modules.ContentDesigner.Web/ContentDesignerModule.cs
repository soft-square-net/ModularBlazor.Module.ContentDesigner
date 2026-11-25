using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Modules.ContentDesigner.Web.Configurations;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web;

/// <summary>
/// The module: its manifest (id, name, version, route prefix, default settings, permissions) and its services.
/// This is the composition root (the Ardalis template's Program.cs): the runtime creates it, reads the manifest and
/// calls <see cref="ConfigureServices"/> on the module's own container; the endpoints are in
/// <see cref="ContentDesignerRoutes"/>, mapped under <c>/api/contentdesigner</c>. Tenant data, schema "contentdesigner".
/// </summary>
public sealed class ContentDesignerModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("contentdesigner", "ContentDesigner", "0.2.0", "contentdesigner")
    {
        Description = "Content Designer: the four-view arrangement of posts (Mobile, Tablet, Desktop, AMP), document and block templates, the design checks, and the server HTML and AMP pages.",
        DefaultSettings = new Dictionary<string, string?>
        {
            ["ContentDesigner:MaxPageSize"] = "100",
            ["ContentDesigner:ApiBaseUrl"] = "http://localhost:5080",
            ["ContentDesigner:SiteBaseUrl"] = "",
            ["ContentDesigner:AmpUrlTemplate"] = "/api/contentdesigner/render/{publicId}/amp",
            ["ContentDesigner:AssetUrlTemplate"] = "/api/storage/items/{id}/content",
            ["ContentDesigner:Stylesheets"] = "/_content/soft.square.net.UI.MudTail/css/mudtail.min.css,/_content/soft.square.net.UI.MudTail/css/packs/editorial.min.css",
            ["ContentDesigner:RenderCacheMinutes"] = "10",
            ["ContentDesigner:ServiceAuthority"] = "",
            ["ContentDesigner:ServiceClientId"] = "",
            ["ContentDesigner:ServiceClientSecret"] = ""
        },
        Permissions = ContentDesignerPermissions.All
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddServiceConfigs(settings);
}
