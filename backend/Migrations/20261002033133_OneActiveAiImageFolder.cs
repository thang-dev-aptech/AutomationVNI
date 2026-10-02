using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class OneActiveAiImageFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_OneActiveAiFolder",
                table: "MediaFolders",
                columns: new[] { "SocialChannelId", "ParentFolderId" },
                unique: true,
                filter: "IsDeleted = 0 AND ParentFolderId IS NOT NULL AND Name = 'Ảnh AI'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFolders_OneActiveAiFolder",
                table: "MediaFolders");
        }
    }
}
