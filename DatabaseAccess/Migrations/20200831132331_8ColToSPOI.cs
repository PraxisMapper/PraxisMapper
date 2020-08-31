using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class _8ColToSPOI : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PlusCode",
                table: "SinglePointsOfInterests",
                maxLength: 15,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlusCode8",
                table: "SinglePointsOfInterests",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SinglePointsOfInterests_PlusCode8",
                table: "SinglePointsOfInterests",
                column: "PlusCode8");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SinglePointsOfInterests_PlusCode8",
                table: "SinglePointsOfInterests");

            migrationBuilder.DropColumn(
                name: "PlusCode8",
                table: "SinglePointsOfInterests");

            migrationBuilder.AlterColumn<string>(
                name: "PlusCode",
                table: "SinglePointsOfInterests",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldMaxLength: 15,
                oldNullable: true);
        }
    }
}
