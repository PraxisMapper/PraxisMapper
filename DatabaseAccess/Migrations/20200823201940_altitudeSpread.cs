using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class altitudeSpread : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maxAltitude",
                table: "PlayerData");

            migrationBuilder.AddColumn<int>(
                name: "altitudeSpread",
                table: "PlayerData",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "altitudeSpread",
                table: "PlayerData");

            migrationBuilder.AddColumn<int>(
                name: "maxAltitude",
                table: "PlayerData",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
