using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class InitialDLLSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AreaTypes",
                columns: table => new
                {
                    AreaTypeId = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaName = table.Column<string>(nullable: true),
                    OsmTags = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AreaTypes", x => x.AreaTypeId);
                });

            migrationBuilder.CreateTable(
                name: "InterestingPoints",
                columns: table => new
                {
                    InterestingPointId = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OsmWayId = table.Column<long>(nullable: false),
                    PlusCode8 = table.Column<string>(maxLength: 8, nullable: true),
                    PlusCode2 = table.Column<string>(maxLength: 2, nullable: true),
                    ProcessedWayID = table.Column<long>(nullable: false),
                    areaType = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestingPoints", x => x.InterestingPointId);
                });

            migrationBuilder.CreateTable(
                name: "PerformanceInfo",
                columns: table => new
                {
                    PerformanceInfoID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    functionName = table.Column<string>(nullable: true),
                    runTime = table.Column<long>(nullable: false),
                    calledAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceInfo", x => x.PerformanceInfoID);
                });

            migrationBuilder.CreateTable(
                name: "PlayerData",
                columns: table => new
                {
                    PlayerDataID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    deviceID = table.Column<string>(nullable: true),
                    t10Cells = table.Column<int>(nullable: false),
                    t8Cells = table.Column<int>(nullable: false),
                    cellVisits = table.Column<int>(nullable: false),
                    distance = table.Column<int>(nullable: false),
                    score = table.Column<int>(nullable: false),
                    DateLastTrophyBought = table.Column<int>(nullable: false),
                    timePlayed = table.Column<int>(nullable: false),
                    maxSpeed = table.Column<int>(nullable: false),
                    totalSpeed = table.Column<int>(nullable: false),
                    maxAltitude = table.Column<int>(nullable: false),
                    lastSyncTime = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerData", x => x.PlayerDataID);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedWays",
                columns: table => new
                {
                    ProcessedWayId = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OsmWayId = table.Column<long>(nullable: false),
                    latitudeS = table.Column<double>(nullable: false),
                    longitudeW = table.Column<double>(nullable: false),
                    distanceE = table.Column<double>(nullable: false),
                    distanceN = table.Column<double>(nullable: false),
                    lastUpdated = table.Column<DateTime>(nullable: false),
                    AreaTypeId = table.Column<int>(nullable: false),
                    AreaType = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWays", x => x.ProcessedWayId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterestingPoints_PlusCode8",
                table: "InterestingPoints",
                column: "PlusCode8");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerData_deviceID",
                table: "PlayerData",
                column: "deviceID");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWays_OsmWayId",
                table: "ProcessedWays",
                column: "OsmWayId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AreaTypes");

            migrationBuilder.DropTable(
                name: "InterestingPoints");

            migrationBuilder.DropTable(
                name: "PerformanceInfo");

            migrationBuilder.DropTable(
                name: "PlayerData");

            migrationBuilder.DropTable(
                name: "ProcessedWays");
        }
    }
}
