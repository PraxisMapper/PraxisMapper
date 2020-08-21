using Microsoft.EntityFrameworkCore.Migrations;

namespace OsmXmlParser.Migrations
{
    public partial class nameProcessedWays : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "ProcessedWays",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                table: "ProcessedWays");
        }
    }
}
