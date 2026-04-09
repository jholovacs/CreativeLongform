using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MeasurementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorldElements_BookId_Slug",
                table: "WorldElements");

            migrationBuilder.AddColumn<int>(
                name: "MeasurementPreset",
                table: "Books",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementSystemJson",
                table: "Books",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorldElements_BookId_Slug",
                table: "WorldElements",
                columns: new[] { "BookId", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorldElements_BookId_Slug",
                table: "WorldElements");

            migrationBuilder.DropColumn(
                name: "MeasurementPreset",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "MeasurementSystemJson",
                table: "Books");

            migrationBuilder.CreateIndex(
                name: "IX_WorldElements_BookId_Slug",
                table: "WorldElements",
                columns: new[] { "BookId", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL");
        }
    }
}
