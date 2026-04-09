using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenerationRunWorkflowOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinWordsOverride",
                table: "GenerationRuns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopAfterDraft",
                table: "GenerationRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinWordsOverride",
                table: "GenerationRuns");

            migrationBuilder.DropColumn(
                name: "StopAfterDraft",
                table: "GenerationRuns");
        }
    }
}
