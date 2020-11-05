using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class omsdata2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MinimumNodes_MinimumWays_MinimumWayId",
                table: "MinimumNodes");

            migrationBuilder.DropIndex(
                name: "IX_MinimumNodes_MinimumWayId",
                table: "MinimumNodes");

            migrationBuilder.DropColumn(
                name: "MinimumWayId",
                table: "MinimumNodes");

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinimumNodeMinimumWay");

            migrationBuilder.AddColumn<long>(
                name: "MinimumWayId",
                table: "MinimumNodes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MinimumNodes_MinimumWayId",
                table: "MinimumNodes",
                column: "MinimumWayId");

            migrationBuilder.AddForeignKey(
                name: "FK_MinimumNodes_MinimumWays_MinimumWayId",
                table: "MinimumNodes",
                column: "MinimumWayId",
                principalTable: "MinimumWays",
                principalColumn: "MinimumWayId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
