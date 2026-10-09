using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmOpportunityActivityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CrmOpportunityId",
                table: "CrmCustomerReminders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmOpportunityId",
                table: "CrmCustomerNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerReminders_CrmOpportunityId",
                table: "CrmCustomerReminders",
                column: "CrmOpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomerNotes_CrmOpportunityId",
                table: "CrmCustomerNotes",
                column: "CrmOpportunityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmCustomerReminders_CrmOpportunityId",
                table: "CrmCustomerReminders");

            migrationBuilder.DropIndex(
                name: "IX_CrmCustomerNotes_CrmOpportunityId",
                table: "CrmCustomerNotes");

            migrationBuilder.DropColumn(
                name: "CrmOpportunityId",
                table: "CrmCustomerReminders");

            migrationBuilder.DropColumn(
                name: "CrmOpportunityId",
                table: "CrmCustomerNotes");
        }
    }
}
