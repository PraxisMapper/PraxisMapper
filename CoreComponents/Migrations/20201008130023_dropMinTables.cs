using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class dropMinTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinimumNodeMinimumWay");

            migrationBuilder.DropTable(
                name: "minimumRelations");

            migrationBuilder.DropTable(
                name: "MinimumNodes");

            migrationBuilder.DropTable(
                name: "MinimumWays");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MinimumNodes",
                columns: table => new
                {
                    MinimumNodeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Lat = table.Column<double>(type: "float", nullable: true),
                    Lon = table.Column<double>(type: "float", nullable: true),
                    NodeId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumNodes", x => x.MinimumNodeId);
                });

            migrationBuilder.CreateTable(
                name: "minimumRelations",
                columns: table => new
                {
                    MinimumRelationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelationId = table.Column<long>(type: "bigint", nullable: false)
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WayId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumWays", x => x.MinimumWayId);
                });

            migrationBuilder.CreateTable(
                name: "MinimumNodeMinimumWay",
                columns: table => new
                {
                    NodesMinimumNodeId = table.Column<long>(type: "bigint", nullable: false),
                    WaysMinimumWayId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinimumNodeMinimumWay", x => new { x.NodesMinimumNodeId, x.WaysMinimumWayId });
                    table.ForeignKey(
                        name: "FK_MinimumNodeMinimumWay_MinimumNodes_NodesMinimumNodeId",
                        column: x => x.NodesMinimumNodeId,
                        principalTable: "MinimumNodes",
                        principalColumn: "MinimumNodeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinimumNodeMinimumWay_MinimumWays_WaysMinimumWayId",
                        column: x => x.WaysMinimumWayId,
                        principalTable: "MinimumWays",
                        principalColumn: "MinimumWayId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinimumNodeMinimumWay_WaysMinimumWayId",
                table: "MinimumNodeMinimumWay",
                column: "WaysMinimumWayId");
        }
    }
}
