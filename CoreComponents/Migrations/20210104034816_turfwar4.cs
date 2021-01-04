using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class turfwar4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TurfWarScoreRecords_TurfWarConfigId",
                table: "TurfWarScoreRecords",
                column: "TurfWarConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarScoreRecords_WinningFactionID",
                table: "TurfWarScoreRecords",
                column: "WinningFactionID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TurfWarScoreRecords_TurfWarConfigId",
                table: "TurfWarScoreRecords");

            migrationBuilder.DropIndex(
                name: "IX_TurfWarScoreRecords_WinningFactionID",
                table: "TurfWarScoreRecords");
        }
    }
}
