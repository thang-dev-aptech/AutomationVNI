using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsSubscribersAndNewsletter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NewsletterSentAt",
                table: "NewsArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewsSubscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnsubscribeToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_NewsSubscribers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSubscribers_Email",
                table: "NewsSubscribers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSubscribers_IsActive",
                table: "NewsSubscribers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSubscribers_UnsubscribeToken",
                table: "NewsSubscribers",
                column: "UnsubscribeToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsSubscribers");

            migrationBuilder.DropColumn(
                name: "NewsletterSentAt",
                table: "NewsArticles");
        }
    }
}
