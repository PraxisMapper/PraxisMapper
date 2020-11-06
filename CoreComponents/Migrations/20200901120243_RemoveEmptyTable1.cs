using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class RemoveEmptyTable1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_InterestingPoints",
                table: "InterestingPoints");

            migrationBuilder.RenameTable(
                name: "InterestingPoints",
                newName: "InterestingPoint");

            migrationBuilder.RenameIndex(
                name: "IX_InterestingPoints_PlusCode8",
                table: "InterestingPoint",
                newName: "IX_InterestingPoint_PlusCode8");

            migrationBuilder.AlterColumn<string>(
                name: "PlusCode8",
                table: "InterestingPoint",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PlusCode2",
                table: "InterestingPoint",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_InterestingPoint",
                table: "InterestingPoint",
                column: "InterestingPointId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_InterestingPoint",
                table: "InterestingPoint");

            migrationBuilder.RenameTable(
                name: "InterestingPoint",
                newName: "InterestingPoints");

            migrationBuilder.RenameIndex(
                name: "IX_InterestingPoint_PlusCode8",
                table: "InterestingPoints",
                newName: "IX_InterestingPoints_PlusCode8");

            migrationBuilder.AlterColumn<string>(
                name: "PlusCode8",
                table: "InterestingPoints",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PlusCode2",
                table: "InterestingPoints",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_InterestingPoints",
                table: "InterestingPoints",
                column: "InterestingPointId");
        }
    }
}
