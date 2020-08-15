using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class PerformanceInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceInfo");
        }
    }
}
