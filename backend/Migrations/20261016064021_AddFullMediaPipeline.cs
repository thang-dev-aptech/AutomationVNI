using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFullMediaPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // CampaignMode.Recycle — chiến dịch hiện có giữ Recycle

            migrationBuilder.AddColumn<string>(
                name: "QualityPauseReason",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualityPausedAt",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceFolderId",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignMediaEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BestScore = table.Column<double>(type: "REAL", nullable: true),
                    BestChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InputHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
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
                    table.PrimaryKey("PK_CampaignMediaEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignMediaScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pass = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScoredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CampaignMediaScores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMediaEvaluations_Campaign_Media_Active",
                table: "CampaignMediaEvaluations",
                columns: new[] { "CampaignId", "MediaAssetId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMediaEvaluations_IsDeleted",
                table: "CampaignMediaEvaluations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMediaEvaluations_Status_NextAttemptAt",
                table: "CampaignMediaEvaluations",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMediaScores_Campaign_Media_Channel_Pass_Active",
                table: "CampaignMediaScores",
                columns: new[] { "CampaignId", "MediaAssetId", "SocialChannelId", "Pass" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMediaScores_IsDeleted",
                table: "CampaignMediaScores",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignMediaEvaluations");

            migrationBuilder.DropTable(
                name: "CampaignMediaScores");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "QualityPauseReason",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "QualityPausedAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SourceFolderId",
                table: "Campaigns");
        }
    }
}
