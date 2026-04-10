using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OllamaModelPreferencesAndChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OllamaModelChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    PreviousModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NewModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OllamaModelChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OllamaModelPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WriterModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CriticModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AgentModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WorldBuildingModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OllamaModelPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OllamaModelChangeLogs_OccurredAt",
                table: "OllamaModelChangeLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OllamaModelChangeLogs");

            migrationBuilder.DropTable(
                name: "OllamaModelPreferences");
        }
    }
}
