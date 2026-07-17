using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationJobPipelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "GenerationJobs",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "GenerationJobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationJobs_IdempotencyKey",
                table: "GenerationJobs",
                column: "IdempotencyKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GenerationJobs_IdempotencyKey",
                table: "GenerationJobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "GenerationJobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "GenerationJobs");
        }
    }
}
