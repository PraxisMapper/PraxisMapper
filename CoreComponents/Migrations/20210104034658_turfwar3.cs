using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class turfwar3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WinningFactionID",
                table: "TurfWarScoreRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WinningScore",
                table: "TurfWarScoreRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WinningFactionID",
                table: "TurfWarScoreRecords");

            migrationBuilder.DropColumn(
                name: "WinningScore",
                table: "TurfWarScoreRecords");
        }
    }
}
