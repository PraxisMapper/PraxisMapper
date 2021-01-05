using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreComponents.Migrations
{
    public partial class turfwar5 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TurfWarTeamAssignments",
                columns: table => new
                {
                    TurfWarTeamAssignmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    deviceID = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TurfWarConfigId = table.Column<int>(type: "int", nullable: false),
                    FactionId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurfWarTeamAssignments", x => x.TurfWarTeamAssignmentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarTeamAssignments_deviceID",
                table: "TurfWarTeamAssignments",
                column: "deviceID");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarTeamAssignments_FactionId",
                table: "TurfWarTeamAssignments",
                column: "FactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TurfWarTeamAssignments_TurfWarConfigId",
                table: "TurfWarTeamAssignments",
                column: "TurfWarConfigId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurfWarTeamAssignments");
        }
    }
}
