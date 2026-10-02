using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveFullTreeReconciledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FullTreeReconciledAt",
                table: "GoogleDriveSyncState",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "GoogleDriveSyncState",
                keyColumn: "Id",
                keyValue: new Guid("b3e6c8a1-6d1e-4f5c-9a34-6d2f1e8b9c02"),
                column: "FullTreeReconciledAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullTreeReconciledAt",
                table: "GoogleDriveSyncState");
        }
    }
}
