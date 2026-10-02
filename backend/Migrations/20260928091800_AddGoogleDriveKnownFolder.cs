using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveKnownFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoogleDriveKnownFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_GoogleDriveKnownFolders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleDriveKnownFolders_FolderId",
                table: "GoogleDriveKnownFolders",
                column: "FolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoogleDriveKnownFolders_IsDeleted",
                table: "GoogleDriveKnownFolders",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleDriveKnownFolders");
        }
    }
}
