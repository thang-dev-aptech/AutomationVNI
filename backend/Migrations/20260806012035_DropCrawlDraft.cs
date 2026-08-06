using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class DropCrawlDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rewriting(3) là trạng thái "đang xào nháp" — bỏ bước đó rồi thì không còn ai đưa
            // tin ra khỏi trạng thái này nữa. Tin chỉ vào được 3 SAU khi đã chấm trùng xong,
            // nên đẩy thẳng sang Pending(4) là đúng nghĩa, không bỏ qua bước kiểm tra nào.
            migrationBuilder.Sql("UPDATE CrawledArticles SET Status = 4 WHERE Status = 3;");

            migrationBuilder.DropColumn(
                name: "DraftContent",
                table: "CrawledArticles");

            migrationBuilder.DropColumn(
                name: "DraftExtraJson",
                table: "CrawledArticles");

            migrationBuilder.DropColumn(
                name: "RewriteAttemptCount",
                table: "CrawledArticles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DraftContent",
                table: "CrawledArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftExtraJson",
                table: "CrawledArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RewriteAttemptCount",
                table: "CrawledArticles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
