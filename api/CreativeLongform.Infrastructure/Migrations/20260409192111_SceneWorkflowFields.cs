using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SceneWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedStateTableJson",
                table: "Scenes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeginningStateJson",
                table: "Scenes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NarrativePerspective",
                table: "Scenes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NarrativeTense",
                table: "Scenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Synopsis",
                table: "Scenes",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsComplete",
                table: "Chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedStateTableJson",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "BeginningStateJson",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "NarrativePerspective",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "NarrativeTense",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "Synopsis",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "IsComplete",
                table: "Chapters");
        }
    }
}
