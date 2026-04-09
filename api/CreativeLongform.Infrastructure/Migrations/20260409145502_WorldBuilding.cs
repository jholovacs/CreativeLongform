using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorldBuilding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "GenerationRunId",
                table: "LlmCalls",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "BookId",
                table: "LlmCalls",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentStyleNotes",
                table: "Books",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryToneAndStyle",
                table: "Books",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WorldElements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldElements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorldElements_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneWorldElements",
                columns: table => new
                {
                    SceneId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldElementId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneWorldElements", x => new { x.SceneId, x.WorldElementId });
                    table.ForeignKey(
                        name: "FK_SceneWorldElements_Scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "Scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SceneWorldElements_WorldElements_WorldElementId",
                        column: x => x.WorldElementId,
                        principalTable: "WorldElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorldElementLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromWorldElementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToWorldElementId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldElementLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorldElementLinks_WorldElements_FromWorldElementId",
                        column: x => x.FromWorldElementId,
                        principalTable: "WorldElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorldElementLinks_WorldElements_ToWorldElementId",
                        column: x => x.ToWorldElementId,
                        principalTable: "WorldElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_BookId",
                table: "LlmCalls",
                column: "BookId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmCall_GenerationOrBook",
                table: "LlmCalls",
                sql: "\"GenerationRunId\" IS NOT NULL OR \"BookId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SceneWorldElements_WorldElementId",
                table: "SceneWorldElements",
                column: "WorldElementId");

            migrationBuilder.CreateIndex(
                name: "IX_WorldElementLinks_FromWorldElementId_ToWorldElementId_Relat~",
                table: "WorldElementLinks",
                columns: new[] { "FromWorldElementId", "ToWorldElementId", "RelationLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorldElementLinks_ToWorldElementId",
                table: "WorldElementLinks",
                column: "ToWorldElementId");

            migrationBuilder.CreateIndex(
                name: "IX_WorldElements_BookId_Slug",
                table: "WorldElements",
                columns: new[] { "BookId", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LlmCalls_Books_BookId",
                table: "LlmCalls",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LlmCalls_Books_BookId",
                table: "LlmCalls");

            migrationBuilder.DropTable(
                name: "SceneWorldElements");

            migrationBuilder.DropTable(
                name: "WorldElementLinks");

            migrationBuilder.DropTable(
                name: "WorldElements");

            migrationBuilder.DropIndex(
                name: "IX_LlmCalls_BookId",
                table: "LlmCalls");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmCall_GenerationOrBook",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "BookId",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "ContentStyleNotes",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "StoryToneAndStyle",
                table: "Books");

            migrationBuilder.AlterColumn<Guid>(
                name: "GenerationRunId",
                table: "LlmCalls",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
