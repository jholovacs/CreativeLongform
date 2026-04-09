using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimelineEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimelineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortKey = table.Column<decimal>(type: "numeric", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    WorldElementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "Scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_WorldElements_WorldElementId",
                        column: x => x.WorldElementId,
                        principalTable: "WorldElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_BookId_SortKey",
                table: "TimelineEntries",
                columns: new[] { "BookId", "SortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_SceneId",
                table: "TimelineEntries",
                column: "SceneId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_WorldElementId",
                table: "TimelineEntries",
                column: "WorldElementId");

            // Backfill one timeline row per existing scene (matches DefaultSortKeyForScene).
            migrationBuilder.Sql(
                """
                INSERT INTO "TimelineEntries" ("Id", "BookId", "Kind", "SortKey", "SceneId", "Title", "Summary", "WorldElementId")
                SELECT gen_random_uuid(), c."BookId", 0,
                       (c."Order"::decimal * 1000000) + (s."Order"::decimal * 1000),
                       s."Id", s."Title", NULL, NULL
                FROM "Scenes" s
                INNER JOIN "Chapters" c ON s."ChapterId" = c."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEntries");
        }
    }
}
