using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class SpoiPlusCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlusCode",
                table: "SinglePointsOfInterests",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode",
                table: "SinglePointsOfInterests",
                column: "PlusCode");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SinglePointsOfInterests_PlusCode",
                table: "SinglePointsOfInterests");

            migrationBuilder.DropColumn(
                name: "PlusCode",
                table: "SinglePointsOfInterests");
        }
    }
}
