using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations.Oracle
{
    /// <inheritdoc />
    public partial class InitialDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contentdesigner_block_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "NUMBER(10)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    TenantId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    FieldKey = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    View = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    PartOrderJson = table.Column<string>(type: "NCLOB", maxLength: 4000, nullable: false),
                    HiddenPartsJson = table.Column<string>(type: "NCLOB", maxLength: 4000, nullable: false),
                    TokensJson = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: false),
                    ContentJson = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    CreatedBy = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    Version = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_block_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contentdesigner_document_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "NUMBER(10)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    TenantId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    Kind = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    ViewsJson = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    IsDefault = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    CreatedBy = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    Version = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_document_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contentdesigner_post_designs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "NUMBER(10)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    TenantId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    PublicId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    KindVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    BasedOnTemplateId = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    DraftJson = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    PublishedJson = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    CreatedBy = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    UpdatedBy = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    Version = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    PublishedVersion = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contentdesigner_post_designs", x => x.Id);
                });

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
