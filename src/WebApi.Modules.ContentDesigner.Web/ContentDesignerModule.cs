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
    public ModuleManifest Manifest { get; } = new("contentdesigner", "ContentDesigner", "0.1.0", "contentdesigner")
    {
        Description = "ContentDesigner: notes per tenant (the template's example; replace with the module's own features).",
        DefaultSettings = new Dictionary<string, string?> { ["ContentDesigner:MaxPageSize"] = "100" },
        Permissions = ContentDesignerPermissions.All
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddServiceConfigs(settings);
}
