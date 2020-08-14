using Microsoft.EntityFrameworkCore.Migrations;

namespace GPSExploreServerAPI.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerData",
                columns: table => new
                {
                    PlayerDataID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    deviceID = table.Column<string>(nullable: true),
                    t10Cells = table.Column<int>(nullable: false),
                    t8Cells = table.Column<int>(nullable: false),
                    cellVisits = table.Column<int>(nullable: false),
                    distance = table.Column<int>(nullable: false),
                    score = table.Column<int>(nullable: false),
                    DateLastTrophyBought = table.Column<int>(nullable: false),
                    timePlayed = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerData", x => x.PlayerDataID);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerData");
        }
    }
}
