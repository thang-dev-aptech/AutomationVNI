using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class PromptTemplateCategoryPack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageBody",
                table: "PromptTemplates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TextBody",
                table: "PromptTemplates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill legacy Body into the matching column.
            migrationBuilder.Sql("""
                UPDATE "PromptTemplates" SET "TextBody" = "Body" WHERE "TemplateType" = 1 AND ("TextBody" IS NULL OR "TextBody" = '');
                UPDATE "PromptTemplates" SET "ImageBody" = "Body" WHERE "TemplateType" = 2 AND ("ImageBody" IS NULL OR "ImageBody" = '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageBody",
                table: "PromptTemplates");

            migrationBuilder.DropColumn(
                name: "TextBody",
                table: "PromptTemplates");
        }
    }
}
