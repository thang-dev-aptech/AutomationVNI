using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignCreatedByUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            // Backfill: khớp CreatedBy (UserName) → AspNetUsers.Id.
            migrationBuilder.Sql("""
                UPDATE Campaigns
                SET CreatedByUserId = (
                    SELECT u.Id
                    FROM AspNetUsers AS u
                    WHERE u.UserName = Campaigns.CreatedBy
                    LIMIT 1
                )
                WHERE CreatedByUserId IS NULL
                  AND CreatedBy IS NOT NULL
                  AND TRIM(CreatedBy) <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CreatedByUserId",
                table: "Campaigns",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Campaigns_CreatedByUserId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Campaigns");
        }
    }
}
