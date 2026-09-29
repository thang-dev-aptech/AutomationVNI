using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveKnownFolderMappingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriveParentId",
                table: "GoogleDriveKnownFolders",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaFolderId",
                table: "GoogleDriveKnownFolders",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "GoogleDriveKnownFolders",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_GoogleDriveKnownFolders_MediaFolderId",
                table: "GoogleDriveKnownFolders",
                column: "MediaFolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoogleDriveKnownFolders_MediaFolderId",
                table: "GoogleDriveKnownFolders");

            migrationBuilder.DropColumn(
                name: "DriveParentId",
                table: "GoogleDriveKnownFolders");

            migrationBuilder.DropColumn(
                name: "MediaFolderId",
                table: "GoogleDriveKnownFolders");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "GoogleDriveKnownFolders");
        }
    }
}
