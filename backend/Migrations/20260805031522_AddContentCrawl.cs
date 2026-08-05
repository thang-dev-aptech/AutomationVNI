using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentCrawl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentFingerprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SimHash = table.Column<long>(type: "INTEGER", nullable: false),
                    Band0 = table.Column<int>(type: "INTEGER", nullable: false),
                    Band1 = table.Column<int>(type: "INTEGER", nullable: false),
                    Band2 = table.Column<int>(type: "INTEGER", nullable: false),
                    Band3 = table.Column<int>(type: "INTEGER", nullable: false),
                    TitleSnippet = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_ContentFingerprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawledArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrawlSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrawlRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SourceGuid = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Author = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceCategory = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SimHash = table.Column<long>(type: "INTEGER", nullable: false),
                    DuplicateOfId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DuplicateTarget = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateScore = table.Column<double>(type: "REAL", nullable: true),
                    DuplicateMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateReason = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DedupAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RewriteAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    RejectReason = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResultPostCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DraftContent = table.Column<string>(type: "TEXT", nullable: true),
                    DraftExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramChatId = table.Column<long>(type: "INTEGER", nullable: true),
                    TelegramMessageId = table.Column<int>(type: "INTEGER", nullable: true),
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
                    table.PrimaryKey("PK_CrawledArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawlRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrawlSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ItemsFetched = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsNew = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsDuplicate = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsFiltered = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    TriggerSource = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_CrawlRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawlSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SiteDomain = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxItemsPerRun = table.Column<int>(type: "INTEGER", nullable: false),
                    LookbackHours = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    IncludeKeywords = table.Column<string>(type: "TEXT", nullable: true),
                    ExcludeKeywords = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultChannelIds = table.Column<string>(type: "TEXT", nullable: true),
                    BrowserProfile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_CrawlSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_Band0",
                table: "ContentFingerprints",
                column: "Band0");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_Band1",
                table: "ContentFingerprints",
                column: "Band1");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_Band2",
                table: "ContentFingerprints",
                column: "Band2");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_Band3",
                table: "ContentFingerprints",
                column: "Band3");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_ContentAt",
                table: "ContentFingerprints",
                column: "ContentAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_ContentHash",
                table: "ContentFingerprints",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_IsDeleted",
                table: "ContentFingerprints",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFingerprints_OwnerType_OwnerId",
                table: "ContentFingerprints",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_ContentHash",
                table: "CrawledArticles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_CrawlRunId",
                table: "CrawledArticles",
                column: "CrawlRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_CrawlSourceId",
                table: "CrawledArticles",
                column: "CrawlSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_CrawlSourceId_SourceGuid",
                table: "CrawledArticles",
                columns: new[] { "CrawlSourceId", "SourceGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_DuplicateOfId",
                table: "CrawledArticles",
                column: "DuplicateOfId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_FetchedAt",
                table: "CrawledArticles",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_IsDeleted",
                table: "CrawledArticles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_NormalizedUrl",
                table: "CrawledArticles",
                column: "NormalizedUrl");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_PublishedAt",
                table: "CrawledArticles",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_ResultBatchId",
                table: "CrawledArticles",
                column: "ResultBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_SimHash",
                table: "CrawledArticles",
                column: "SimHash");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledArticles_Status",
                table: "CrawledArticles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlRuns_CrawlSourceId",
                table: "CrawlRuns",
                column: "CrawlSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlRuns_IsDeleted",
                table: "CrawlRuns",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlRuns_StartedAt",
                table: "CrawlRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlRuns_Status",
                table: "CrawlRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlSources_CategoryId",
                table: "CrawlSources",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlSources_IsActive",
                table: "CrawlSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlSources_IsDeleted",
                table: "CrawlSources",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlSources_SourceType",
                table: "CrawlSources",
                column: "SourceType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentFingerprints");

            migrationBuilder.DropTable(
                name: "CrawledArticles");

            migrationBuilder.DropTable(
                name: "CrawlRuns");

            migrationBuilder.DropTable(
                name: "CrawlSources");
        }
    }
}
