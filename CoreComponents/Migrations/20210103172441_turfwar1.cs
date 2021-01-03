using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

namespace CoreComponents.Migrations
{
    public partial class turfwar1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Geometry>(
                name: "place",
                table: "MapData",
                type: "geography",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geography",
                oldNullable: true);

            migrationBuilder.AlterColumn<Geometry>(
                name: "place",
                table: "GeneratedMapData",
                type: "geography",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geography",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "TurfWarConfigs",
                columns: table => new
                {
                    TurfWarConfigId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TurfWarDurationHours = table.Column<int>(type: "int", nullable: false),
                    TurfWarNextReset = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurfWarConfigs", x => x.TurfWarConfigId);
                });

            migrationBuilder.CreateTable(
                name: "TurfWarEntries",
                columns: table => new
                {
                    TurfWarEntryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Cell10 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FactionId = table.Column<int>(type: "int", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CanFlipFactionAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurfWarEntries", x => x.TurfWarEntryId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurfWarConfigs");

            migrationBuilder.DropTable(
                name: "TurfWarEntries");

            migrationBuilder.AlterColumn<Geometry>(
                name: "place",
                table: "MapData",
                type: "geography",
                nullable: true,
                oldClrType: typeof(Geometry),
                oldType: "geography");

            migrationBuilder.AlterColumn<Geometry>(
                name: "place",
                table: "GeneratedMapData",
                type: "geography",
                nullable: true,
                oldClrType: typeof(Geometry),
                oldType: "geography");
        }
    }
}
