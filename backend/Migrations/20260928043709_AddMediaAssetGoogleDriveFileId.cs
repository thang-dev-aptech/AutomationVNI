using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssetGoogleDriveFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveFileId",
                table: "MediaAssets",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_GoogleDriveFileId",
                table: "MediaAssets",
                column: "GoogleDriveFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_GoogleDriveFileId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "GoogleDriveFileId",
                table: "MediaAssets");
        }
    }
}
