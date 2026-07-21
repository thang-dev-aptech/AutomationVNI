using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookPageMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalResultId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_MessageActionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalConversationId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ParticipantExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParticipantName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ParticipantAvatarUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Snippet = table.Column<string>(type: "TEXT", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCustomerMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPageMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnreadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InboxStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InternalNote = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_PageConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SenderExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecipientExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsFromPage = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEcho = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDelivered = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_PageMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageActionLogs_ActionType",
                table: "MessageActionLogs",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActionLogs_CreatedAt",
                table: "MessageActionLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActionLogs_PageConversationId",
                table: "MessageActionLogs",
                column: "PageConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_ExternalConversationId",
                table: "PageConversations",
                column: "ExternalConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_InboxStatus",
                table: "PageConversations",
                column: "InboxStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_IsDeleted",
                table: "PageConversations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_LastMessageAt",
                table: "PageConversations",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_SocialChannelId",
                table: "PageConversations",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_SocialChannelId_ParticipantExternalId",
                table: "PageConversations",
                columns: new[] { "SocialChannelId", "ParticipantExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_PageMessages_IsDeleted",
                table: "PageMessages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PageMessages_PageConversationId",
                table: "PageMessages",
                column: "PageConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PageMessages_SentAt",
                table: "PageMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_PageMessages_SocialChannelId",
                table: "PageMessages",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_PageMessages_SocialChannelId_ExternalMessageId",
                table: "PageMessages",
                columns: new[] { "SocialChannelId", "ExternalMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageActionLogs");

            migrationBuilder.DropTable(
                name: "PageConversations");

            migrationBuilder.DropTable(
                name: "PageMessages");
        }
    }
}
