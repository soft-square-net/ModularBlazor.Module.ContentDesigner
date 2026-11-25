using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class InitialDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contentdesigner");

            migrationBuilder.CreateTable(
                name: "block_templates",
                schema: "contentdesigner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    View = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PartOrderJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    HiddenPartsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    TokensJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_templates",
                schema: "contentdesigner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "integer", nullable: false),
                    ViewsJson = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "post_designs",
                schema: "contentdesigner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "integer", nullable: false),
                    BasedOnTemplateId = table.Column<int>(type: "integer", nullable: true),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    PublishedJson = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_designs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_block_templates_TenantId",
                schema: "contentdesigner",
                table: "block_templates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_block_templates_TenantId_FieldKey_View",
                schema: "contentdesigner",
                table: "block_templates",
                columns: new[] { "TenantId", "FieldKey", "View" });

            migrationBuilder.CreateIndex(
                name: "IX_document_templates_TenantId",
                schema: "contentdesigner",
                table: "document_templates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_document_templates_TenantId_Kind",
                schema: "contentdesigner",
                table: "document_templates",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_post_designs_TenantId",
                schema: "contentdesigner",
                table: "post_designs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_post_designs_TenantId_Kind",
                schema: "contentdesigner",
                table: "post_designs",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_post_designs_TenantId_PostId",
                schema: "contentdesigner",
                table: "post_designs",
                columns: new[] { "TenantId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_post_designs_TenantId_PublicId",
                schema: "contentdesigner",
                table: "post_designs",
                columns: new[] { "TenantId", "PublicId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "block_templates",
                schema: "contentdesigner");

            migrationBuilder.DropTable(
                name: "document_templates",
                schema: "contentdesigner");

            migrationBuilder.DropTable(
                name: "post_designs",
                schema: "contentdesigner");
        }
    }
}
