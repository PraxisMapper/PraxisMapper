using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class IncludeSpeed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "maxSpeed",
                table: "PlayerData",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "totalSpeed",
                table: "PlayerData",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maxSpeed",
                table: "PlayerData");

            migrationBuilder.DropColumn(
                name: "totalSpeed",
                table: "PlayerData");
        }
    }
}
