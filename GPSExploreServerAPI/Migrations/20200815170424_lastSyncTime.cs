using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class lastSyncTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "lastSyncTime",
                table: "PlayerData",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lastSyncTime",
                table: "PlayerData");
        }
    }
}
