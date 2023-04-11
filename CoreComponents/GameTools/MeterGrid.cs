using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace PraxisCore.GameTools
{
    public class MeterGrid
    {
        //Handles the fairly common scenario that you want a grid based on more common, practical measurements than degrees.
        //Works out distance from 0,0, and converts that to int-pairs for storage.
        public record MeterGridResults(int xId, int yId, int metersPerSquare, GeoArea tile);

        const double oneMeterLat = .0000009d;
        const double degreesToRadians = Math.PI / 180;

        public static MeterGridResults GetMeterGrid(double lat, double lon, int metersPerSquare)
        {
            var p = new NetTopologySuite.Geometries.Point(lon, lat);
            var xDist = p.MetersDistanceTo(new NetTopologySuite.Geometries.Point(-180, lat)); 
            var yDist = p.MetersDistanceTo(new NetTopologySuite.Geometries.Point(lon, -90)); 

            if (lon > 0)
                xDist += new NetTopologySuite.Geometries.Point(-180, lat).MetersDistanceTo(new NetTopologySuite.Geometries.Point(0, lat));

            //var latSign = lat > 0 ? 1 : -1;
            //var lonSign = lat > 0 ? 1 : -1;

            int xId = (int)(xDist / metersPerSquare); // * lonSign;
            int yId = (int)(yDist / metersPerSquare); // * latSign;

            //QUICK MATH:
            //1 degree of latitude is 111,111 meters. (111,111m = 1d)
            //SO 1 meter n/s is (1m = 1 / 111,111) = .0000009
            //1 degree of longitude is 111,111 * cos(latitude) .
            //SO 1 meter e/w is .0000009 * cos(latitude) degrees.

            //To figure out the radius at a certain latitude, we need this math below:
            //var xRadius = 111111 * Math.Cos(lat * (Math.PI / 180));
            //Then we can use that radius to determine the size of a degree of longitude.

            GeoArea thisTile = new GeoArea(
                yId * metersPerSquare * oneMeterLat,
                xId * metersPerSquare * oneMeterLat * Math.Cos(lat * degreesToRadians),
                (yId + 1) * metersPerSquare * oneMeterLat,
                (xId + 1) * metersPerSquare * oneMeterLat * Math.Cos(lat * degreesToRadians));

            return new MeterGridResults(xId, yId, metersPerSquare, thisTile);
        }

        public static string GetMeterGridName(MeterGridResults data)
        {
            return data.xId.ToString() + "|" + data.yId.ToString() + "|" + data.metersPerSquare.ToString();
        }


        public static void SaveMeterGridAreaData(MeterGridResults data, string key, object value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            string name = GetMeterGridName(data);
            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == name && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaData();
                row.DataKey = key;
                row.PlusCode = name;
                row.AreaCovered = data.tile.ToPolygon();
                db.AreaData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            row.DataValue = value.ToJsonByteArray();
            row.Expiration = expiration;
            db.SaveChanges();
        }

        public static void SaveMeterGridSecureAreaData(MeterGridResults data, string key, string password, object value, double? expiration = null)
        {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            byte[] encryptedValue = GenericData.EncryptValue(value.ToJsonByteArray(), password, out byte[] IVs);
            string name = GetMeterGridName(data);

            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == name && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaData();
                row.DataKey = key;
                row.PlusCode = name;
                row.AreaCovered = data.tile.ToPolygon();
                db.AreaData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;

            row.DataValue = encryptedValue;
            row.IvData = IVs;
            db.SaveChanges();
        }

        public static byte[] LoadMeterGridData(MeterGridResults data, string key)
        {
            var db = new PraxisContext();
            string name = GetMeterGridName(data);
            var row = db.AreaData.FirstOrDefault(a => a.PlusCode == name && a.DataKey == key && (a.Expiration == null || a.Expiration > DateTime.UtcNow));
            if (row == null)
                return Array.Empty<byte>();

            return row.DataValue;
        }

        public static byte[] LoadMeterGridSecureData(MeterGridResults data, string key, string password)
        {
            var db = new PraxisContext();
            string name = GetMeterGridName(data);
            var row = db.AreaData.FirstOrDefault(a => a.PlusCode == name && a.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();

            return GenericData.DecryptValue(row.IvData, row.DataValue, password);
        }
    }
}
