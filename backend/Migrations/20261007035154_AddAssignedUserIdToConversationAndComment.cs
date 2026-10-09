using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedUserIdToConversationAndComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedUserId",
                table: "SocialComments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedUserId",
                table: "PageConversations",
                type: "TEXT",
                nullable: true);

            // Backfill: khớp AssignedTo (UserName) → AspNetUsers.Id khi trùng chính xác.
            migrationBuilder.Sql("""
                UPDATE PageConversations
                SET AssignedUserId = (
                    SELECT u.Id
                    FROM AspNetUsers AS u
                    WHERE u.UserName = PageConversations.AssignedTo
                    LIMIT 1
                )
                WHERE AssignedUserId IS NULL
                  AND AssignedTo IS NOT NULL
                  AND TRIM(AssignedTo) <> '';
                """);

            migrationBuilder.Sql("""
                UPDATE SocialComments
                SET AssignedUserId = (
                    SELECT u.Id
                    FROM AspNetUsers AS u
                    WHERE u.UserName = SocialComments.AssignedTo
                    LIMIT 1
                )
                WHERE AssignedUserId IS NULL
                  AND AssignedTo IS NOT NULL
                  AND TRIM(AssignedTo) <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SocialComments_AssignedUserId",
                table: "SocialComments",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PageConversations_AssignedUserId",
                table: "PageConversations",
                column: "AssignedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialComments_AssignedUserId",
                table: "SocialComments");

            migrationBuilder.DropIndex(
                name: "IX_PageConversations_AssignedUserId",
                table: "PageConversations");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "SocialComments");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "PageConversations");
        }
    }
}
