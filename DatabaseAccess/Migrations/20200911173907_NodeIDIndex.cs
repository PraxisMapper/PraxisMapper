using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class NodeIDIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_NodeID",
                table: "SinglePointsOfInterests",
                column: "NodeID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SinglePointsOfInterests_NodeID",
                table: "SinglePointsOfInterests");
        }
    }
}
