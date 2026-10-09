using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadAtUtc",
                table: "SocialComments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadAtUtc",
                table: "PageConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_LastReadAtUtc",
                table: "SocialComments",
                column: "LastReadAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_LastReadAtUtc",
                table: "PageConversations",
                column: "LastReadAtUtc");

            // Backfill: đã đọc (UnreadCount=0) → LastReadAtUtc = LastMessageAt, tránh bùng badge.
            migrationBuilder.Sql("""
                UPDATE PageConversations
                SET LastReadAtUtc = COALESCE(LastMessageAt, UpdatedAt, CreatedAt)
                WHERE UnreadCount = 0
                  AND LastReadAtUtc IS NULL
                  AND IsDeleted = 0;
                """);

            // Backfill: bình luận gốc Replied/Ignored → coi như đã đọc.
            migrationBuilder.Sql("""
                UPDATE SocialComments
                SET LastReadAtUtc = COALESCE(UpdatedAt, RepliedAt, CommentedAt, CreatedAt)
                WHERE ParentCommentId IS NULL
                  AND InboxStatus IN (3, 4)
                  AND LastReadAtUtc IS NULL
                  AND IsDeleted = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialComments_LastReadAtUtc",
                table: "SocialComments");

            migrationBuilder.DropIndex(
                name: "IX_PageConversations_LastReadAtUtc",
                table: "PageConversations");

            migrationBuilder.DropColumn(
                name: "LastReadAtUtc",
                table: "SocialComments");

            migrationBuilder.DropColumn(
                name: "LastReadAtUtc",
                table: "PageConversations");
        }
    }
}
