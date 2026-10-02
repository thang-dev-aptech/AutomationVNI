using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaCaptionJobInitialSkipped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InitialSkipped",
                table: "MediaCaptionJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Job tạo trước cột này: ước lượng số ảnh bỏ qua ban đầu = Skipped trừ item Skipped
            // (Status 4). Bảng chỉ mới có trên DB dev nên sai số (nếu có) không ảnh hưởng production.
            migrationBuilder.Sql("""
                UPDATE "MediaCaptionJobs"
                SET "InitialSkipped" = MAX(0, "Skipped" - (
                    SELECT COUNT(*) FROM "MediaCaptionJobItems" i
                    WHERE i."JobId" = "MediaCaptionJobs"."Id" AND i."IsDeleted" = 0 AND i."Status" = 4));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialSkipped",
                table: "MediaCaptionJobs");
        }
    }
}
