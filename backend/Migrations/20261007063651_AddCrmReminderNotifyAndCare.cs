using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmReminderNotifyAndCare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "CrmCustomerReminders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAtUtc",
                table: "CrmCustomerReminders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_NotifiedAtUtc",
                table: "CrmCustomerReminders",
                column: "NotifiedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmCustomerReminders_NotifiedAtUtc",
                table: "CrmCustomerReminders");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "CrmCustomerReminders");

            migrationBuilder.DropColumn(
                name: "NotifiedAtUtc",
                table: "CrmCustomerReminders");
        }
    }
}
