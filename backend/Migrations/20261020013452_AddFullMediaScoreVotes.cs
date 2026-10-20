using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFullMediaScoreVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VotesCast",
                table: "CampaignMediaScores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VotesTotal",
                table: "CampaignMediaScores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VotesYes",
                table: "CampaignMediaScores",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VotesCast",
                table: "CampaignMediaScores");

            migrationBuilder.DropColumn(
                name: "VotesTotal",
                table: "CampaignMediaScores");

            migrationBuilder.DropColumn(
                name: "VotesYes",
                table: "CampaignMediaScores");
        }
    }
}
