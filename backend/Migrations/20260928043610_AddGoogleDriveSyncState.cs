using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoogleDriveSyncState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    UpdatedByUserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PageToken = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_GoogleDriveSyncState", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "GoogleDriveSyncState",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "ExtraJson", "IsDeleted", "IsEnabled", "LastImportedCount", "LastSyncAt", "PageToken", "UpdatedAt", "UpdatedBy", "UpdatedByUserName" },
                values: new object[] { new Guid("b3e6c8a1-6d1e-4f5c-9a34-6d2f1e8b9c02"), new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, false, true, 0, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleDriveSyncState");
        }
    }
}
