using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.GameTools.MeterGrid;

namespace PraxisCore.GameTools
{
    /// <summary>
    /// A quick way to make a customized grid, using modified PlusCode logic. Can be saved directly to the AreaData table, using the integer coordinate pairs.<br />
    /// DIFFERENCES: These will use integer pairs as identifiers, and will not have layers with different rules to work out size.<br />
    /// This does support layers, which subdivide each grid square to another grid with the same count of cells.
    /// </summary>
    public sealed class CustomGrid
    {
        //This is for using PraxisMapper on a grid setup on-the-fly that doesn't use PlusCode standards.
        //(which means it'll be a little bit off even using PlusCode math, since it starts with an 18x9 grid, then subdivides into 20x20 grids after that).
        //Requirements: given a size (square) and a set of dimentions(optional, default = 1), find the XY pair (or sets of pairs) that
        //represent a location, and also a GeoArea to enable compatibility with the AreaData table. (PlusCode will be the name/ID of the area still for that grid).

        public record CustomGridResults(List<Tuple<int, int>> coordPairs, GeoArea tile);

        
        public double GetGridSize(int countX)
        {
            return 360 / countX; //360 / 20 = 18 degrees each
        }


        public double GetGridCount(double size) //size in degrees
        {
            return 360 / size; // 360 / .5 = 720 tiles on X axis. 360 / 4 = 90 tiles.
        }

        /// <summary>
        /// Returns the list of integer pairs that match up to the given lat/lon coordinates, given the number of tiles in each grid layer on the X axis and the number of layers to go down.
        /// </summary>
        /// <param name="lat">Latitude to determine the intpair for</param>
        /// <param name="lon">Longitude to determine the intpair for</param>
        /// <param name="tileCount">How many cells are in the X axis on the grid. On the first layer, there will be half as many Y tiles because latitude is -90 to 90 instead of -180 to 180 like longitude. </param>
        /// <param name="layerCount">How many times to subdivide the </param>
        /// <returns>a list of Tuples containing the X and Y values for each layer of grid tiles, and a GeoArea representing the entire tile.</returns>
        public CustomGridResults FindGridCode(double lat, double lon, int tileCount, int layerCount = 1)
        {
            //Reminder: opposite of exponent is logarithm
            var totalMultiplier = tileCount ^ layerCount;

            long latMath = (long)((lat + 90) * totalMultiplier);
            long longMath = (long)((lon + 180) * totalMultiplier);
            long southPoint = -90;
            long westPoint = -180;

            List<Tuple<int, int>> results = new List<Tuple<int, int>>();

            for (int i = 0; i < layerCount; i++)
            {
                int xPos = (int)(longMath % tileCount);
                int yPos = (int)(latMath % tileCount);

                results.Add(Tuple.Create(xPos, yPos));
                longMath /= tileCount;
                latMath /= tileCount;

                southPoint += yPos * (tileCount ^ i);
                westPoint += xPos * (tileCount ^ i);
            }

            //Create GeoArea for this tile as well. 
            var tileLength = 360 / totalMultiplier; //360 / (20 ^ 5 = 3,200,000) = 0.000 01125 degrees. Not quite. 00225 is for Cell8, also a little off. Might be because there's no round off here.
            GeoArea thisTile = new GeoArea(southPoint, westPoint, southPoint + tileLength, westPoint + tileLength);


            //return the code for the tile. This could also be passed into a name generator for a better 
            return new CustomGridResults(results, thisTile);
        }

        /// <summary>
        /// Returns a string version of the integer pairs identifying a tile. Digits are separated with hyphens, pairs with pipes (EX: 12-4|2-3|5-3);
        /// </summary>
        public static string GetCustomGridName(CustomGridResults data)
        {
            string name = "";
            foreach (var t in data.coordPairs)
            {
                name += t.Item1.ToString() + "-" + t.Item2.ToString() + "|";
            }
            return name.Substring(0, name.Length - 1);
        }

        /// <summary>
        /// Given a list of integer pairs, the count of tiles per layer in the grid, and the number of layers, returns a GeoArea representing that area.
        /// </summary>
        public GeoArea DecodeCustomGrid(List<Tuple<int, int>> values, int tileCount, int layerCount)
        {
            //the above function, but backwards.
            double lat = -90;
            double lon = -180;

            int totalMultiplier = 0;
            double tileLength = 0;
            //foreach layer, add the size of the tile to both lat and lon to get the SW corner of the tile. On the final tile, add half again if you want the center point of that tile instead.
            for (int i = 0; i < layerCount; i++)
            {
                totalMultiplier = tileCount ^ i;
                tileLength = 360 / totalMultiplier;
                lon += values[i].Item1 * tileLength;
                lat += values[i].Item2 * tileLength;
            }

            //now make the area for it.
            GeoArea thisTile = new GeoArea(lat, lon, lat + tileLength, lon + tileLength);
            return thisTile;
        }

        /// <summary>
        /// Attach data to the results of FindGridCode and save it to the AreaData table, like it was a PlusCode.
        /// </summary>
        /// <param name="data">the results from FindGridcode</param>
        /// <param name="key">the name to save the data under</param>
        /// <param name="value">the object to save</param>
        /// <param name="expiration">How many seconds this data is valid for.</param>
        public static void SaveCustomGridAreaData(CustomGridResults data, string key, object value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            string name = GetCustomGridName(data);
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

        /// <summary>
        /// Attach encrypted data to the results of FindGridCode and save it to the AreaData table, like it was a PlusCode. Required if this is storing data on a player in an area.
        /// </summary>
        public static void SaveCustomGridSecureAreaData(CustomGridResults data, string key, string password, object value, double? expiration = null) {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            byte[] encryptedValue = GenericData.EncryptValue(value.ToJsonByteArray(), password, out byte[] IVs);
            string name = GetCustomGridName(data);

            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == name && p.DataKey == key);
            if (row == null) {
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

        public static byte[] LoadCustomGridData(CustomGridResults data, string key)
        {
            var db = new PraxisContext();
            string name = GetCustomGridName(data);
            var row = db.AreaData.FirstOrDefault(a => a.PlusCode == name && a.DataKey == key);
            if (row == null)
                return Array.Empty<byte>();

            return row.DataValue;
        }
    }
}
