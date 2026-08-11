using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPageMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LikeCount",
                table: "SocialPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetricsSyncedAt",
                table: "SocialPosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformCommentCount",
                table: "SocialPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShareCount",
                table: "SocialPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChannelMetricDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Followers = table.Column<int>(type: "INTEGER", nullable: false),
                    PostsPublished = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalLikes = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalComments = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalShares = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_ChannelMetricDaily", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_PostedAt",
                table: "SocialPosts",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMetricDaily_Date",
                table: "ChannelMetricDaily",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMetricDaily_SocialChannelId_Date",
                table: "ChannelMetricDaily",
                columns: new[] { "SocialChannelId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelMetricDaily");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_PostedAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "MetricsSyncedAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "PlatformCommentCount",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "ShareCount",
                table: "SocialPosts");
        }
    }
}
