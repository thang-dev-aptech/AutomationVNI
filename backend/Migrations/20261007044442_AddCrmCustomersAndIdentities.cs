using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmCustomersAndIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmCustomerActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_CrmCustomerActionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_CrmCustomerIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerMergeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeptCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MergedCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsUndone = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CrmCustomerMergeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CrmCustomerNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerPhoneSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RawMatched = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SourceMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceCommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDismissed = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CrmCustomerPhoneSuggestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CrmCustomerReminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_CrmCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomerTagLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrmTagId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_CrmCustomerTagLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerActionLogs_ActionType",
                table: "CrmCustomerActionLogs",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerActionLogs_CrmCustomerId",
                table: "CrmCustomerActionLogs",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerActionLogs_IsDeleted",
                table: "CrmCustomerActionLogs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerIdentities_CrmCustomerId",
                table: "CrmCustomerIdentities",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerIdentities_IsDeleted",
                table: "CrmCustomerIdentities",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerIdentities_Platform_SocialChannelId_ExternalId",
                table: "CrmCustomerIdentities",
                columns: new[] { "Platform", "SocialChannelId", "ExternalId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerIdentities_SocialChannelId",
                table: "CrmCustomerIdentities",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerMergeRecords_IsDeleted",
                table: "CrmCustomerMergeRecords",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerMergeRecords_KeptCustomerId",
                table: "CrmCustomerMergeRecords",
                column: "KeptCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerMergeRecords_MergedCustomerId",
                table: "CrmCustomerMergeRecords",
                column: "MergedCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerNotes_CrmCustomerId",
                table: "CrmCustomerNotes",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerNotes_IsDeleted",
                table: "CrmCustomerNotes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerPhoneSuggestions_CrmCustomerId",
                table: "CrmCustomerPhoneSuggestions",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerPhoneSuggestions_IsDeleted",
                table: "CrmCustomerPhoneSuggestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerPhoneSuggestions_PhoneE164",
                table: "CrmCustomerPhoneSuggestions",
                column: "PhoneE164");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_AssigneeUserId",
                table: "CrmCustomerReminders",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_CrmCustomerId",
                table: "CrmCustomerReminders",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_DueAtUtc",
                table: "CrmCustomerReminders",
                column: "DueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_IsDeleted",
                table: "CrmCustomerReminders",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomers_DisplayName",
                table: "CrmCustomers",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomers_IsDeleted",
                table: "CrmCustomers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomers_PhoneE164",
                table: "CrmCustomers",
                column: "PhoneE164");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerTagLinks_CrmCustomerId",
                table: "CrmCustomerTagLinks",
                column: "CrmCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerTagLinks_CrmCustomerId_CrmTagId",
                table: "CrmCustomerTagLinks",
                columns: new[] { "CrmCustomerId", "CrmTagId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerTagLinks_CrmTagId",
                table: "CrmCustomerTagLinks",
                column: "CrmTagId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerTagLinks_IsDeleted",
                table: "CrmCustomerTagLinks",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmCustomerActionLogs");

            migrationBuilder.DropTable(
                name: "CrmCustomerIdentities");

            migrationBuilder.DropTable(
                name: "CrmCustomerMergeRecords");

            migrationBuilder.DropTable(
                name: "CrmCustomerNotes");

            migrationBuilder.DropTable(
                name: "CrmCustomerPhoneSuggestions");

            migrationBuilder.DropTable(
                name: "CrmCustomerReminders");

            migrationBuilder.DropTable(
                name: "CrmCustomers");

            migrationBuilder.DropTable(
                name: "CrmCustomerTagLinks");
        }
    }
}
