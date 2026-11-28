using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations.MySql
{
    /// <summary>Data only: document templates and post designs name their kind by its field key (see <see cref="PostKindKeysSql"/>).</summary>
    public partial class PostKindsAsFieldKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in PostKindKeysSql.Up(t => $"`contentdesigner_{t}`", c => $"`{c}`"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in PostKindKeysSql.Down(t => $"`contentdesigner_{t}`", c => $"`{c}`"))
                migrationBuilder.Sql(sql);
        }
    }
}
