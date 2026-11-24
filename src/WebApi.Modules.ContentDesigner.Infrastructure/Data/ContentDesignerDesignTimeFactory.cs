using Microsoft.EntityFrameworkCore.Design;
using soft.square.net.WebApi.Data;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data;

/// <summary>
/// Design time, one migration set per engine (Data/Migrations/&lt;Provider&gt;): add migrations with the platform's
/// src/WebApi/scripts/Add-Migration.ps1 -Project &lt;this project&gt; -Name &lt;Name&gt; -MigrationsDir Data/Migrations
/// -Namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations (see the README).
/// </summary>
internal sealed class ContentDesignerDesignTimeFactory : IDesignTimeDbContextFactory<ContentDesignerDbContext>
{
    public ContentDesignerDbContext CreateDbContext(string[] args) =>
        new(TenantDbDesignTime.Options<ContentDesignerDbContext>(args), TenantDbDesignTime.Tenant);
}
