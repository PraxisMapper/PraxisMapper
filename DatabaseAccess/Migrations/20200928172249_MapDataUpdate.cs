using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class MapDataUpdate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SinglePointsOfInterests");

            migrationBuilder.AlterColumn<long>(
                name: "WayId",
                table: "MapData",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "NodeId",
                table: "MapData",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RelationId",
                table: "MapData",
                type: "bigint",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "MapData");

            migrationBuilder.DropColumn(
                name: "RelationId",
                table: "MapData");

            migrationBuilder.AlterColumn<long>(
                name: "WayId",
                table: "MapData",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "SinglePointsOfInterests",
                columns: table => new
                {
                    SinglePointsOfInterestId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NodeID = table.Column<long>(type: "bigint", nullable: false),
                    NodeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlusCode = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    PlusCode6 = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    PlusCode8 = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    lat = table.Column<double>(type: "float", nullable: false),
                    lon = table.Column<double>(type: "float", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SinglePointsOfInterests", x => x.SinglePointsOfInterestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_NodeID",
                table: "SinglePointsOfInterests",
                column: "NodeID");

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode",
                table: "SinglePointsOfInterests",
                column: "PlusCode");

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode6",
                table: "SinglePointsOfInterests",
                column: "PlusCode6");

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode8",
                table: "SinglePointsOfInterests",
                column: "PlusCode8");
        }
    }
}
