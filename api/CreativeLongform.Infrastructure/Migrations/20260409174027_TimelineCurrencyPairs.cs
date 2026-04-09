using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeLongform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimelineCurrencyPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyPairAuthority",
                table: "TimelineEntries",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyPairBase",
                table: "TimelineEntries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyPairExchangeNote",
                table: "TimelineEntries",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyPairQuote",
                table: "TimelineEntries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyPairAuthority",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyPairBase",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyPairExchangeNote",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyPairQuote",
                table: "TimelineEntries");
        }
    }
}
