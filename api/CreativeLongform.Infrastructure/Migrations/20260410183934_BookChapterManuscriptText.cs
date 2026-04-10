using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookChapterManuscriptText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManuscriptText",
                table: "Chapters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManuscriptText",
                table: "Books",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManuscriptText",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "ManuscriptText",
                table: "Books");
        }
    }
}
