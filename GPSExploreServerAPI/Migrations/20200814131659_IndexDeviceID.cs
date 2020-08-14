using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class IndexDeviceID : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "deviceID",
                table: "PlayerData",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerData_deviceID",
                table: "PlayerData",
                column: "deviceID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerData_deviceID",
                table: "PlayerData");

            migrationBuilder.AlterColumn<string>(
                name: "deviceID",
                table: "PlayerData",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);
        }
    }
}
