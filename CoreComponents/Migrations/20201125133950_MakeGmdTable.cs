using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

namespace CoreComponents.Migrations
{
    public partial class MakeGmdTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneratedMapData",
                columns: table => new
                {
                    GeneratedMapDataId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    place = table.Column<Geometry>(type: "geography", nullable: true),
                    type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AreaTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedMapData", x => x.GeneratedMapDataId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedMapData");
        }
    }
}
