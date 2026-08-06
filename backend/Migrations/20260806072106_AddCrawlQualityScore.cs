using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlQualityScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "CrawledArticles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreenReason",
                table: "CrawledArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreenSummary",
                table: "CrawledArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreenTopic",
                table: "CrawledArticles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "CrawledArticles");

            migrationBuilder.DropColumn(
                name: "ScreenReason",
                table: "CrawledArticles");

            migrationBuilder.DropColumn(
                name: "ScreenSummary",
                table: "CrawledArticles");

            migrationBuilder.DropColumn(
                name: "ScreenTopic",
                table: "CrawledArticles");
        }
    }
}
