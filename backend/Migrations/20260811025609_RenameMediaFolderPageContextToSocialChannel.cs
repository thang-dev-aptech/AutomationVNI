using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class RenameMediaFolderPageContextToSocialChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cột "SocialChannelId" đã tồn tại sẵn trong DB (mồ côi từ migration cũ đã bị xoá khỏi
            // disk trước khi PageContextId ra đời — xem AddMediaFolderSocialChannelId 2026-08-07) và
            // ĐÃ có dữ liệu thật (folder gắn page qua cột này). "PageContextId" (2026-08-11) chưa hề
            // có dữ liệu. Vì vậy chỉ cần bỏ cột PageContextId, giữ nguyên SocialChannelId + dữ liệu.
            migrationBuilder.DropColumn(
                name: "PageContextId",
                table: "MediaFolders");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_SocialChannelId",
                table: "MediaFolders",
                column: "SocialChannelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFolders_SocialChannelId",
                table: "MediaFolders");

            migrationBuilder.AddColumn<Guid>(
                name: "PageContextId",
                table: "MediaFolders",
                type: "TEXT",
                nullable: true);
        }
    }
}
