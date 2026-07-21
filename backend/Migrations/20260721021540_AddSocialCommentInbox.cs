using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialCommentInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialCommentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalResultId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_CommentActionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalCommentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentExternalCommentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ParentCommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthorExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    AuthorUsername = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    PermalinkUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFromPage = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPending = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeletedOnPlatform = table.Column<bool>(type: "INTEGER", nullable: false),
                    LikeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InboxStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InternalNote = table.Column<string>(type: "TEXT", nullable: true),
                    RepliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_SocialComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalPostId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LocalPostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    PermalinkUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CommentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCommentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncCursor = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ObjectId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Verb = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Item = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentActionLogs_ActionType",
                table: "CommentActionLogs",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_CommentActionLogs_CreatedAt",
                table: "CommentActionLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommentActionLogs_SocialCommentId",
                table: "CommentActionLogs",
                column: "SocialCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_InboxStatus",
                table: "SocialComments",
                column: "InboxStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_IsDeleted",
                table: "SocialComments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_ParentCommentId",
                table: "SocialComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_Platform",
                table: "SocialComments",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_SocialChannelId",
                table: "SocialComments",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_SocialChannelId_ExternalCommentId",
                table: "SocialComments",
                columns: new[] { "SocialChannelId", "ExternalCommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_SocialPostId",
                table: "SocialComments",
                column: "SocialPostId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_IsDeleted",
                table: "SocialPosts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_LocalPostId",
                table: "SocialPosts",
                column: "LocalPostId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_Platform",
                table: "SocialPosts",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_SocialChannelId",
                table: "SocialPosts",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_SocialChannelId_ExternalPostId",
                table: "SocialPosts",
                columns: new[] { "SocialChannelId", "ExternalPostId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_CreatedAt",
                table: "WebhookEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_EventKey",
                table: "WebhookEvents",
                column: "EventKey");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_Platform",
                table: "WebhookEvents",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_Status",
                table: "WebhookEvents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentActionLogs");

            migrationBuilder.DropTable(
                name: "SocialComments");

            migrationBuilder.DropTable(
                name: "SocialPosts");

            migrationBuilder.DropTable(
                name: "WebhookEvents");
        }
    }
}
