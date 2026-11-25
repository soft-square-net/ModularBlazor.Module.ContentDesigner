using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soft.square.net.ContentDesigner.Rendering;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Configurations;

public static class ServiceConfigs
{
    /// <summary>
    /// Everything the module registers, in the module's own container (never the host's): settings, data access, any
    /// services the use cases share, and the mediator with every handler.
    /// </summary>
    public static IServiceCollection AddServiceConfigs(this IServiceCollection services, IConfiguration settings)
    {
        services.Configure<ContentDesignerOptions>(settings.GetSection("ContentDesigner"));
        services.AddInfrastructureServices();

        // Shared by the design use cases: the page renderer (HtmlRenderer over the Rendering components) and the workbench.
        services.AddScoped(sp => new PageRenderer(sp, sp.GetRequiredService<ILoggerFactory>()));
        services.AddScoped<DesignWorkbench>();

        services.AddMediatorSourceGen();
        return services;
    }
}
