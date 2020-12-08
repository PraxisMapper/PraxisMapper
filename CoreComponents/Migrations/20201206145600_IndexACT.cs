using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class IndexACT : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AreaControlTeams_FactionId",
                table: "AreaControlTeams",
                column: "FactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AreaControlTeams_MapDataId",
                table: "AreaControlTeams",
                column: "MapDataId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AreaControlTeams_FactionId",
                table: "AreaControlTeams");

            migrationBuilder.DropIndex(
                name: "IX_AreaControlTeams_MapDataId",
                table: "AreaControlTeams");
        }
    }
}
