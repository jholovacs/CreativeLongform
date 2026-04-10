using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SceneManuscriptAndFinalizeNextScene : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManuscriptText",
                table: "Scenes",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Scenes"
                SET "ManuscriptText" = "LatestDraftText"
                WHERE "ApprovedStateTableJson" IS NOT NULL
                  AND "ManuscriptText" IS NULL
                  AND "LatestDraftText" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManuscriptText",
                table: "Scenes");
        }
    }
}
