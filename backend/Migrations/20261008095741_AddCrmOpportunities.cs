using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmOpportunities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmOpportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    StageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PageConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SocialCommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LostReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_CrmOpportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmOpportunityStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CrmOpportunityStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmOpportunityWatchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CrmOpportunityWatchers", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CrmOpportunityStages",
                columns: new[] { "Id", "Color", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "ExtraJson", "IsDeleted", "Kind", "Name", "SortOrder", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), "#3B82F6", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 1, "Mới", 1, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111102"), "#8B5CF6", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 1, "Đủ điều kiện", 2, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111103"), "#F59E0B", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 1, "Bám đuổi", 3, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111104"), "#10B981", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 1, "Đàm phán chốt", 4, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111105"), "#059669", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 2, "Đã mua", 5, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111106"), "#EF4444", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null, null, false, 3, "Thất bại", 6, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_AssigneeUserId",
                table: "CrmOpportunities",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_CrmCustomerId",
                table: "CrmOpportunities",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_IsDeleted",
                table: "CrmOpportunities",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_LastActivityAtUtc",
                table: "CrmOpportunities",
                column: "LastActivityAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_PageConversationId",
                table: "CrmOpportunities",
                column: "PageConversationId",
                unique: true,
                filter: "IsDeleted = 0 AND IsArchived = 0 AND Status = 1 AND PageConversationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_SocialCommentId",
                table: "CrmOpportunities",
                column: "SocialCommentId",
                unique: true,
                filter: "IsDeleted = 0 AND IsArchived = 0 AND Status = 1 AND SocialCommentId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_StageId_IsDeleted",
                table: "CrmOpportunities",
                columns: new[] { "StageId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityStages_IsDeleted",
                table: "CrmOpportunityStages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityStages_SortOrder",
                table: "CrmOpportunityStages",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityWatchers_IsDeleted",
                table: "CrmOpportunityWatchers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityWatchers_OpportunityId",
                table: "CrmOpportunityWatchers",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityWatchers_OpportunityId_UserId",
                table: "CrmOpportunityWatchers",
                columns: new[] { "OpportunityId", "UserId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunityWatchers_UserId",
                table: "CrmOpportunityWatchers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmOpportunities");

            migrationBuilder.DropTable(
                name: "CrmOpportunityStages");

            migrationBuilder.DropTable(
                name: "CrmOpportunityWatchers");
        }
    }
}
