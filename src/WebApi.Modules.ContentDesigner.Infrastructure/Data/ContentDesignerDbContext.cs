using System.Reflection;
using Microsoft.EntityFrameworkCore;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data;

/// <summary>
/// The module's tenant data: schema "contentdesigner" in each tenant's database (table prefix on engines without schemas):
/// post designs, document templates and block templates. Views are stored as JSON, one column per state, so the
/// aggregates' rules guard them and every engine stores them alike.
/// The platform's <see cref="TenantDbContext"/> adds the tenant filter on every <c>ITenantOwned</c> entity, stamps the
/// tenant on save and dispatches domain events. The entity mappings are the <c>IEntityTypeConfiguration</c> classes in
/// <c>Data/Config</c>; the migrations, one set per engine, are in <c>Data/Migrations/&lt;Provider&gt;</c>.
/// </summary>
public sealed class ContentDesignerDbContext(DbContextOptions<ContentDesignerDbContext> options, ITenantContext tenant) : TenantDbContext(options, tenant)
{
    protected override string Schema => "contentdesigner";

    public DbSet<PostDesign> PostDesigns => Set<PostDesign>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<BlockTemplate> BlockTemplates => Set<BlockTemplate>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
}
