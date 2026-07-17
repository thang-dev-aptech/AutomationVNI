using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialConnectionAndChannelType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChannelType",
                table: "SocialChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SocialConnectionId",
                table: "SocialChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocialConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Scopes = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_SocialConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialChannels_ChannelType",
                table: "SocialChannels",
                column: "ChannelType");

            migrationBuilder.CreateIndex(
                name: "IX_SocialChannels_SocialConnectionId",
                table: "SocialChannels",
                column: "SocialConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_IsActive",
                table: "SocialConnections",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_IsDeleted",
                table: "SocialConnections",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_Provider",
                table: "SocialConnections",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_Provider_ExternalUserId",
                table: "SocialConnections",
                columns: new[] { "Provider", "ExternalUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialConnections");

            migrationBuilder.DropIndex(
                name: "IX_SocialChannels_ChannelType",
                table: "SocialChannels");

            migrationBuilder.DropIndex(
                name: "IX_SocialChannels_SocialConnectionId",
                table: "SocialChannels");

            migrationBuilder.DropColumn(
                name: "ChannelType",
                table: "SocialChannels");

            migrationBuilder.DropColumn(
                name: "SocialConnectionId",
                table: "SocialChannels");
        }
    }
}
