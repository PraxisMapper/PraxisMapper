using Microsoft.EntityFrameworkCore.Migrations;

namespace DatabaseAccess.Migrations
{
    public partial class CreateGeoSearchSproc : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            string sprocCode = "create PROCEDURE lookupMapData " +
                "@lat as decimal(10, 6), " +
                "@lon as decimal(10, 6) " +
                "AS " +
                "BEGIN " +
                "SET NOCOUNT ON; " +
                    "DECLARE @p1 geography " +
                    "SET @p1 = geography::STGeomFromText('POINT (' + CAST(@lon as varchar) + ' ' + CAST(@lat as varchar) + ')', 4326) " +
                    "SELECT @lat, @lon, 'POINT (' + CAST(@lon as varchar) + ' ' + CAST(@lat as varchar) + ')' " +
                    "SELECT distinct name, type FROM MapData " +
                    "WHERE @p1.STWithin(place) = 1 " +
                "END ";

            migrationBuilder.Sql(sprocCode);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
