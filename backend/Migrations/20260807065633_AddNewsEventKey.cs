using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsEventKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfNewsId",
                table: "NewsArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "NewsArticles",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_EventKey",
                table: "NewsArticles",
                column: "EventKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_EventKey",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "DuplicateOfNewsId",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "NewsArticles");
        }
    }
}
