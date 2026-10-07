using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "Posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CampaignSlotAt",
                table: "Posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignChannelGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CampaignChannelGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CampaignChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageStrategy = table.Column<int>(type: "INTEGER", nullable: true),
                    ScheduleMode = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekdaysJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishTimesJson = table.Column<string>(type: "TEXT", nullable: false),
                    JitterMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Campaign_Channel_Slot",
                table: "Posts",
                columns: new[] { "CampaignId", "SocialChannelId", "CampaignSlotAt" },
                unique: true,
                filter: "CampaignId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CampaignId",
                table: "Posts",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannelGroups_Campaign_Group_Active",
                table: "CampaignChannelGroups",
                columns: new[] { "CampaignId", "ChannelGroupId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannelGroups_CampaignId",
                table: "CampaignChannelGroups",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannelGroups_ChannelGroupId",
                table: "CampaignChannelGroups",
                column: "ChannelGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannelGroups_IsDeleted",
                table: "CampaignChannelGroups",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannels_Campaign_Channel_Active",
                table: "CampaignChannels",
                columns: new[] { "CampaignId", "SocialChannelId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannels_CampaignId",
                table: "CampaignChannels",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannels_IsDeleted",
                table: "CampaignChannels",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignChannels_SocialChannelId",
                table: "CampaignChannels",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_IsDeleted",
                table: "Campaigns",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status",
                table: "Campaigns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignChannelGroups");

            migrationBuilder.DropTable(
                name: "CampaignChannels");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Posts_Campaign_Channel_Slot",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_CampaignId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "CampaignSlotAt",
                table: "Posts");
        }
    }
}
