using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class turfwar2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Cell10",
                table: "TurfWarEntries",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cell8",
                table: "TurfWarEntries",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TurfWarConfigId",
                table: "TurfWarEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Cell10LockoutTimer",
                table: "TurfWarConfigs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TurfWarConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TurfWarScoreRecords",
                columns: table => new
                {
                    TurfWarScoreRecordId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TurfWarConfigId = table.Column<int>(type: "int", nullable: false),
                    Results = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurfWarScoreRecords", x => x.TurfWarScoreRecordId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarEntries_Cell10",
                table: "TurfWarEntries",
                column: "Cell10");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarEntries_Cell8",
                table: "TurfWarEntries",
                column: "Cell8");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarEntries_FactionId",
                table: "TurfWarEntries",
                column: "FactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarEntries_TurfWarConfigId",
                table: "TurfWarEntries",
                column: "TurfWarConfigId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurfWarScoreRecords");

            migrationBuilder.DropIndex(
                name: "IX_TurfWarEntries_Cell10",
                table: "TurfWarEntries");

            migrationBuilder.DropIndex(
                name: "IX_TurfWarEntries_Cell8",
                table: "TurfWarEntries");

            migrationBuilder.DropIndex(
                name: "IX_TurfWarEntries_FactionId",
                table: "TurfWarEntries");

            migrationBuilder.DropIndex(
                name: "IX_TurfWarEntries_TurfWarConfigId",
                table: "TurfWarEntries");

            migrationBuilder.DropColumn(
                name: "Cell8",
                table: "TurfWarEntries");

            migrationBuilder.DropColumn(
                name: "TurfWarConfigId",
                table: "TurfWarEntries");

            migrationBuilder.DropColumn(
                name: "Cell10LockoutTimer",
                table: "TurfWarConfigs");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "TurfWarConfigs");

            migrationBuilder.AlterColumn<string>(
                name: "Cell10",
                table: "TurfWarEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
