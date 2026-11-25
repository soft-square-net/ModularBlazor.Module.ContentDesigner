using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contentdesigner_block_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    FieldKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    View = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    PartOrderJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    HiddenPartsJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    TokensJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_block_templates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contentdesigner_document_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Kind = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "int", nullable: false),
                    ViewsJson = table.Column<string>(type: "longtext", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_document_templates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contentdesigner_post_designs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    PublicId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "int", nullable: false),
                    BasedOnTemplateId = table.Column<int>(type: "int", nullable: true),
                    DraftJson = table.Column<string>(type: "longtext", nullable: false),
                    PublishedJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PublishedVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_post_designs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_block_templates_TenantId",
                table: "contentdesigner_block_templates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_block_templates_TenantId_FieldKey_View",
                table: "contentdesigner_block_templates",
                columns: new[] { "TenantId", "FieldKey", "View" });

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_document_templates_TenantId",
                table: "contentdesigner_document_templates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_document_templates_TenantId_Kind",
                table: "contentdesigner_document_templates",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_post_designs_TenantId",
                table: "contentdesigner_post_designs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_post_designs_TenantId_Kind",
                table: "contentdesigner_post_designs",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_post_designs_TenantId_PostId",
                table: "contentdesigner_post_designs",
                columns: new[] { "TenantId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contentdesigner_post_designs_TenantId_PublicId",
                table: "contentdesigner_post_designs",
                columns: new[] { "TenantId", "PublicId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contentdesigner_block_templates");

            migrationBuilder.DropTable(
                name: "contentdesigner_document_templates");

            migrationBuilder.DropTable(
                name: "contentdesigner_post_designs");
        }
    }
}
