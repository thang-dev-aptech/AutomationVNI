using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmScheduledMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmScheduledMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SentMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_CrmScheduledMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmScheduledMessages_IsDeleted",
                table: "CrmScheduledMessages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmScheduledMessages_PageConversationId_Status",
                table: "CrmScheduledMessages",
                columns: new[] { "PageConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmScheduledMessages_Status_ScheduledAtUtc",
                table: "CrmScheduledMessages",
                columns: new[] { "Status", "ScheduledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmScheduledMessages");
        }
    }
}
