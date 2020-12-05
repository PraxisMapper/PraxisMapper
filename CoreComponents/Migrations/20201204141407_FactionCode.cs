using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class FactionCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "regenerate",
                table: "MapTiles");

            migrationBuilder.RenameColumn(
                name: "factionId",
                table: "AreaControlTeams",
                newName: "FactionId");

            migrationBuilder.AddColumn<int>(
                name: "FactionID",
                table: "PlayerData",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "MapTiles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpireOn",
                table: "MapTiles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "mode",
                table: "MapTiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HtmlColor",
                table: "Factions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGeneratedArea",
                table: "AreaControlTeams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "claimedAt",
                table: "AreaControlTeams",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FactionID",
                table: "PlayerData");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "MapTiles");

            migrationBuilder.DropColumn(
                name: "ExpireOn",
                table: "MapTiles");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "MapTiles");

            migrationBuilder.DropColumn(
                name: "HtmlColor",
                table: "Factions");

            migrationBuilder.DropColumn(
                name: "IsGeneratedArea",
                table: "AreaControlTeams");

            migrationBuilder.DropColumn(
                name: "claimedAt",
                table: "AreaControlTeams");

            migrationBuilder.RenameColumn(
                name: "FactionId",
                table: "AreaControlTeams",
                newName: "factionId");

            migrationBuilder.AddColumn<bool>(
                name: "regenerate",
                table: "MapTiles",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
