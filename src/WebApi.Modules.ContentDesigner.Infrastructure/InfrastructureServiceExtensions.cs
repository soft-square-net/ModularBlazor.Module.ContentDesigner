using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// The module's infrastructure, in the module's own container (it unloads with the module):
    /// <list type="bullet">
    /// <item>the tenant DbContext, and through it <c>IRepository&lt;T&gt;</c> / <c>IReadRepository&lt;T&gt;</c> for every
    /// aggregate root (PostDesign, DocumentTemplate, BlockTemplate);</item>
    /// <item>the typed HTTP clients of Content and DynamicFields, through the gateway;</item>
    /// <item>the module's memory cache (field versions, rendered pages), size-limited.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddTenantDbContext<ContentDesignerDbContext>();

        services.AddHttpContextAccessor();
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 }));
        services.AddHttpClient(nameof(ServiceTokenSource), client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ServiceTokenSource>();
        services.AddHttpClient<Gateway>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IContentApi, ContentApi>();
        services.AddScoped<IFieldsApi, FieldsApi>();
        return services;
    }
}
