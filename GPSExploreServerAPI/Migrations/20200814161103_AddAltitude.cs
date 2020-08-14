using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class AddAltitude : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "maxAltitude",
                table: "PlayerData",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maxAltitude",
                table: "PlayerData");
        }
    }
}
