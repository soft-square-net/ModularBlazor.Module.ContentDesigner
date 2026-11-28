using DotNet.Testcontainers.Containers;
using soft.square.net.WebApi.Data;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;

namespace soft.square.net.WebApi.Modules.ContentDesigner.MigrationTests;

/// <summary>PostgreSQL: the module's tables in the schema <c>contentdesigner</c>.</summary>
public sealed class PostgreSqlMigrations : MigrationHarness
{
    protected override DatabaseProvider Provider => DatabaseProvider.PostgreSql;
    protected override string OfflineConnection => "Host=localhost;Database=contentdesigner_tests";
    protected override IDatabaseContainer CreateContainer() => new PostgreSqlBuilder("postgres:17").Build();
    protected override string Table(string name) => $"contentdesigner.\"{name}\"";
    protected override string Column(string name) => $"\"{name}\"";
}

/// <summary>SQL Server: the module's tables in the schema <c>contentdesigner</c>.</summary>
public sealed class SqlServerMigrations : MigrationHarness
{
    protected override DatabaseProvider Provider => DatabaseProvider.SqlServer;
    protected override string OfflineConnection => "Server=localhost;Database=contentdesigner_tests;TrustServerCertificate=true";
    protected override IDatabaseContainer CreateContainer() => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    protected override string Table(string name) => $"[contentdesigner].[{name}]";
    protected override string Column(string name) => $"[{name}]";
}

/// <summary>MySQL: no schemas, so the module's tables are prefixed <c>contentdesigner_</c>.</summary>
public sealed class MySqlMigrations : MigrationHarness
{
    protected override DatabaseProvider Provider => DatabaseProvider.MySql;
    protected override string OfflineConnection => "Server=localhost;Database=contentdesigner_tests;User ID=test;Password=test";
    protected override IDatabaseContainer CreateContainer() => new MySqlBuilder("mysql:8.4").Build();
    protected override string Table(string name) => $"`contentdesigner_{name}`";
    protected override string Column(string name) => $"`{name}`";
}

/// <summary>Oracle (Free 23ai): the module's tables in the user's schema, prefixed <c>contentdesigner_</c>.</summary>
public sealed class OracleMigrations : MigrationHarness
{
    protected override DatabaseProvider Provider => DatabaseProvider.Oracle;
    protected override string OfflineConnection => "User Id=test;Password=test;Data Source=localhost:1521/FREEPDB1";
    protected override IDatabaseContainer CreateContainer() => new OracleBuilder("gvenzl/oracle-free:23-slim").Build();
    protected override string Table(string name) => $"\"contentdesigner_{name}\"";
    protected override string Column(string name) => $"\"{name}\"";
}
