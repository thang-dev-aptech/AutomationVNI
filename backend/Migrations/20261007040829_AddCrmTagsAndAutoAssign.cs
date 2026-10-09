using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmTagsAndAutoAssign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmAutoAssignSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    AssigneeUserIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    NextIndex = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CrmAutoAssignSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmTagLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmTagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CrmTagLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("PK_CrmTags", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CrmAutoAssignSettings",
                columns: new[] { "Id", "AssigneeUserIdsJson", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "ExtraJson", "IsDeleted", "NextIndex", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("c1a7e0b2-4d5f-4a8e-9b3c-1d2e3f4a5b6c"), "[]", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, false, 0, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_CrmTagLinks_CrmTagId",
                table: "CrmTagLinks",
                column: "CrmTagId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTagLinks_CrmTagId_TargetType_TargetId",
                table: "CrmTagLinks",
                columns: new[] { "CrmTagId", "TargetType", "TargetId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTagLinks_IsDeleted",
                table: "CrmTagLinks",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTagLinks_TargetType_TargetId",
                table: "CrmTagLinks",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmTags_IsDeleted",
                table: "CrmTags",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTags_Name",
                table: "CrmTags",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmAutoAssignSettings");

            migrationBuilder.DropTable(
                name: "CrmTagLinks");

            migrationBuilder.DropTable(
                name: "CrmTags");
        }
    }
}
