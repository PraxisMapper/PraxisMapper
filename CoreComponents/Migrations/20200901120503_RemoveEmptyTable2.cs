using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class RemoveEmptyTable2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterestingPoint");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterestingPoint",
                columns: table => new
                {
                    InterestingPointId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OsmWayId = table.Column<long>(type: "bigint", nullable: false),
                    PlusCode2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlusCode8 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ProcessedWayID = table.Column<long>(type: "bigint", nullable: false),
                    areaType = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestingPoint", x => x.InterestingPointId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterestingPoint_PlusCode8",
                table: "InterestingPoint",
                column: "PlusCode8");
        }
    }
}
