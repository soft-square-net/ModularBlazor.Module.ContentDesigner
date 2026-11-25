using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data;

namespace soft.square.net.WebApi.Modules.ContentDesigner.IntegrationTests.Data;

/// <summary>
/// One in-memory SQLite database with the module's model (EnsureCreated, not the per-provider migrations: those are
/// checked with <c>dotnet ef migrations has-pending-model-changes</c>). Each <see cref="For"/> call is a new context,
/// as a new request would get, for the given tenant.
/// </summary>
public sealed class ContentDesignerDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ContentDesignerDbContext> _options;

    public ContentDesignerDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ContentDesignerDbContext>().UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .Options;
        using var context = For("acme");
        context.Database.EnsureCreated();
    }

    public ContentDesignerDbContext For(string? tenantId) => new(_options, new TestTenant(tenantId));

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// SQLite cannot order by a DateTimeOffset (the module's providers can): store them as sortable binary numbers here.
    /// </summary>
    private sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }

    private sealed class TestTenant(string? id) : ITenantContext
    {
        public TenantInfo? Tenant { get; } = id is null ? null : new TenantInfo(id, id, TenantStatus.Active, [], "", "");
        public string? ResolvedBy => "test";
    }
}
