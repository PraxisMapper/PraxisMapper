using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace PraxisCore.GameTools
{
    public class MeterGrid
    {
        //Handles the fairly common scenario that you want a grid based on more common, practical measurements than degrees.
        //Works out distance from -90,-180, and converts that to int-pairs for storage.
        public record MeterGridResults(int xId, int yId, int metersPerSquare, GeoArea tile);

        //QUICK MATH:
        //1 degree of latitude is 111,111 meters, so 1 meter n/s is (1m = 1 / 111,111) = .0000009
        //1 degree of longitude is 111,111 * cos(latitude), so 1 meter e/w is .0000009 * cos(latitude.ToRadians()) degrees.
        const double metersPerDegree = 111111;
        const double oneMeterLat = 1 / metersPerDegree;

        public static MeterGridResults GetMeterGrid(double lat, double lon, int metersPerSquare)
        {
            var p = new NetTopologySuite.Geometries.Point(lon, lat);
            var xDist =  (lon + 180) * Math.Cos(lat.ToRadians()) * metersPerDegree; //p.MetersDistanceTo(new NetTopologySuite.Geometries.Point(-180, lat)); 
            var yDist = p.MetersDistanceTo(new NetTopologySuite.Geometries.Point(lon, -90)); 

            int xId = (int)(xDist / metersPerSquare);
            int yId = (int)(yDist / metersPerSquare);

            GeoArea thisTile = new GeoArea(
                yId * metersPerSquare * oneMeterLat,
                xId * metersPerSquare * oneMeterLat * Math.Cos(lat.ToRadians()),
                (yId + 1) * metersPerSquare * oneMeterLat,
                (xId + 1) * metersPerSquare * oneMeterLat * Math.Cos(lat.ToRadians()));

            return new MeterGridResults(xId, yId, metersPerSquare, thisTile);
        }

        public static string GetMeterGridName(MeterGridResults data)
        {
            return data.xId.ToString() + "|" + data.yId.ToString() + "|" + data.metersPerSquare.ToString();
        }


        public static void SaveMeterGridAreaData(MeterGridResults data, string key, object value, DateTime? expiration = null)
        {
            using var db = new PraxisContext();
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
            using var db = new PraxisContext();
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
            using var db = new PraxisContext();
            string name = GetMeterGridName(data);
            var row = db.AreaData.FirstOrDefault(a => a.PlusCode == name && a.DataKey == key && (a.Expiration == null || a.Expiration > DateTime.UtcNow));
            if (row == null)
                return Array.Empty<byte>();

            return row.DataValue;
        }

        public static byte[] LoadMeterGridSecureData(MeterGridResults data, string key, string password)
        {
            using var db = new PraxisContext();
            string name = GetMeterGridName(data);
            var row = db.AreaData.FirstOrDefault(a => a.PlusCode == name && a.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();

            return GenericData.DecryptValue(row.IvData, row.DataValue, password);
        }
    }
}
