using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class TestPremade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedWays");

            migrationBuilder.CreateTable(
                name: "PremadeResults",
                columns: table => new
                {
                    PremadeResultsId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlusCode6 = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PremadeResults", x => x.PremadeResultsId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PremadeResults_PlusCode6",
                table: "PremadeResults",
                column: "PlusCode6");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PremadeResults");

            migrationBuilder.CreateTable(
                name: "ProcessedWays",
                columns: table => new
                {
                    ProcessedWayId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AreaTypeId = table.Column<int>(type: "int", nullable: false),
                    OsmWayId = table.Column<long>(type: "bigint", nullable: false),
                    distanceE = table.Column<double>(type: "float", nullable: false),
                    distanceN = table.Column<double>(type: "float", nullable: false),
                    lastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    latitudeS = table.Column<double>(type: "float", nullable: false),
                    longitudeW = table.Column<double>(type: "float", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWays", x => x.ProcessedWayId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWays_OsmWayId",
                table: "ProcessedWays",
                column: "OsmWayId");
        }
    }
}
