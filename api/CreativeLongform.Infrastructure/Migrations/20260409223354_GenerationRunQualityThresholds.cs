using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenerationRunQualityThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "QualityAcceptMinScore",
                table: "GenerationRuns",
                type: "double precision",
                nullable: false,
                defaultValue: 75.0);

            migrationBuilder.AddColumn<double>(
                name: "QualityReviewOnlyMinScore",
                table: "GenerationRuns",
                type: "double precision",
                nullable: false,
                defaultValue: 55.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualityAcceptMinScore",
                table: "GenerationRuns");

            migrationBuilder.DropColumn(
                name: "QualityReviewOnlyMinScore",
                table: "GenerationRuns");
        }
    }
}
