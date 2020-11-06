using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class omsdata1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "minimumRelations",
                columns: table => new
                {
                    MinimumRelationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_minimumRelations", x => x.MinimumRelationId);
                });

            migrationBuilder.CreateTable(
                name: "MinimumWays",
                columns: table => new
                {
                    MinimumWayId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumWays", x => x.MinimumWayId);
                });

            migrationBuilder.CreateTable(
                name: "MinimumNodes",
                columns: table => new
                {
                    MinimumNodeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Lat = table.Column<double>(type: "float", nullable: true),
                    Lon = table.Column<double>(type: "float", nullable: true),
                    MinimumWayId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumNodes", x => x.MinimumNodeId);
                    table.ForeignKey(
                        name: "FK_MinimumNodes_MinimumWays_MinimumWayId",
                        column: x => x.MinimumWayId,
                        principalTable: "MinimumWays",
                        principalColumn: "MinimumWayId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinimumNodes_MinimumWayId",
                table: "MinimumNodes",
                column: "MinimumWayId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinimumNodes");

            migrationBuilder.DropTable(
                name: "minimumRelations");

            migrationBuilder.DropTable(
                name: "MinimumWays");
        }
    }
}
