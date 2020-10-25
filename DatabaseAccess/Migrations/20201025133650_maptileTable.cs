using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class maptileTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MapTiles",
                columns: table => new
                {
                    MapTileId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlusCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tileData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    regenerate = table.Column<bool>(type: "bit", nullable: false),
                    resolutionScale = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapTiles", x => x.MapTileId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapTiles");
        }
    }
}
