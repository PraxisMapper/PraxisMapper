using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace PraxisCore
{
    public class CustomGrid
    {
        //This is for using PraxisMapper on a grid setup on-the-fly that doesn't use PlusCode standards.
        //(which means it'll be a little bit off even using PlusCode math, since it starts with an 18x9 grid, then subdivides into 20x20 grids after that).
        //Requirements: given a size (square) and a set of dimentions(optional, default = 1), find the XY pair (or sets of pairs) that
        //represent a location, and also a GeoArea to enable compatibility with the AreaData table. (PlusCode will be the name/ID of the area still for that grid).

        public record CustomGridResults(List<Tuple<int, int>> coordPairs, GeoArea tile);

        //count is how many tiles are in this grid total.
        public void GetGridSize(int countX)
        {
            var gridX = 360 / countX; //360 / 20 = 18 degrees each
        }

        public void GetGridCount(double size) //size in degrees
        {
            var gridX = 360 / size; // 360 / .5 = 720 tiles on X axis. 360 / 4 = 90 tiles.
        }

        public CustomGridResults FindGridCode(double lat, double lon, int tileCount, int layerCount =1)
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

                southPoint += (yPos * (tileCount ^ i));
                westPoint += (xPos * (tileCount ^ i));
            }

            //Create GeoArea for this tile as well. 
            var tileLength = 360 /  totalMultiplier; //360 / (20 ^ 5 = 3,200,000) = 0.000 01125 degrees. Not quite. 00225 is for Cell8, also a little off. Might be because there's no round off here.
            GeoArea thisTile = new GeoArea(southPoint, westPoint, southPoint + tileLength, westPoint + tileLength);


            //return the code for the tile. This could also be passed into a name generator for a better 
            return new CustomGridResults(results, thisTile);
        }

        public static string GetCustomGridName(CustomGridResults data)
        {
            string name = "";
            foreach(var t in data.coordPairs)
            {
                name += t.Item1.ToString() + "-" + t.Item2.ToString() + "|";
            }
            return name.Substring(0, name.Length -1);
        }

        public GeoArea DecodeCustomGrid(List<Tuple<int, int>> values, int tileCount, int layerCount)
        {
            //the above function, but backwards.
            double lat = -90;
            double lon = -180;

            int totalMultiplier = 0;
            double tileLength  = 0;
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

        public void SaveCustomGridAreaData(CustomGridResults data, string key, string value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            var saveData = new DbTables.AreaData() { DataKey = key, DataValue = value.ToByteArrayUTF8(), Expiration = expiration, PlusCode = GetCustomGridName(data), GeoAreaIndex = data.tile.ToPolygon() };
            db.AreaData.Add(saveData);
            db.SaveChanges();
        }
    }
}
