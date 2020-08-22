using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class SPOIs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SinglePointsOfInterests",
                columns: table => new
                {
                    SinglePointsOfInterestId = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NodeID = table.Column<long>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    lat = table.Column<double>(nullable: false),
                    lon = table.Column<double>(nullable: false),
                    NodeType = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SinglePointsOfInterests", x => x.SinglePointsOfInterestId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SinglePointsOfInterests");
        }
    }
}
