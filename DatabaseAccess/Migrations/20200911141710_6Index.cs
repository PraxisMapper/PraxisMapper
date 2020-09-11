using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class _6Index : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlusCode6",
                table: "SinglePointsOfInterests",
                maxLength: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode6",
                table: "SinglePointsOfInterests",
                column: "PlusCode6");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SinglePointsOfInterests_PlusCode6",
                table: "SinglePointsOfInterests");

            migrationBuilder.DropColumn(
                name: "PlusCode6",
                table: "SinglePointsOfInterests");
        }
    }
}
