using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_ChannelGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_ChannelGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelGroupMembers_ChannelGroupId",
                table: "ChannelGroupMembers",
                column: "ChannelGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelGroupMembers_Group_Channel_Active",
                table: "ChannelGroupMembers",
                columns: new[] { "ChannelGroupId", "SocialChannelId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelGroupMembers_IsDeleted",
                table: "ChannelGroupMembers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelGroupMembers_SocialChannelId",
                table: "ChannelGroupMembers",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelGroups_IsDeleted",
                table: "ChannelGroups",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelGroupMembers");

            migrationBuilder.DropTable(
                name: "ChannelGroups");
        }
    }
}
