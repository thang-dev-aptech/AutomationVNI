using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImageTemplateId",
                table: "Posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TextTemplateId",
                table: "Posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultImageTemplateId",
                table: "PageContexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultTextTemplateId",
                table: "PageContexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TemplateType = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_IsActive",
                table: "PromptTemplates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_IsDefault",
                table: "PromptTemplates",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_IsDeleted",
                table: "PromptTemplates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_TemplateType",
                table: "PromptTemplates",
                column: "TemplateType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropColumn(
                name: "ImageTemplateId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "TextTemplateId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DefaultImageTemplateId",
                table: "PageContexts");

            migrationBuilder.DropColumn(
                name: "DefaultTextTemplateId",
                table: "PageContexts");
        }
    }
}
