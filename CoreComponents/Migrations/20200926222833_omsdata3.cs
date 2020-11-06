using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class omsdata3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WayId",
                table: "MinimumWays",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RelationId",
                table: "minimumRelations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "NodeId",
                table: "MinimumNodes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WayId",
                table: "MinimumWays");

            migrationBuilder.DropColumn(
                name: "RelationId",
                table: "minimumRelations");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "MinimumNodes");
        }
    }
}
