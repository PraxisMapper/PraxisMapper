using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Classes
{
    public static class MapSupport
    {
        public static List<MapData> GetPlaces(GeoArea area, List<MapData> source = null)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var coordSeq = MakeBox(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static Coordinate[] MakeBox(GeoArea plusCodeArea)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        public static void SplitArea(GeoArea area, int divideCount, List<MapData> places, out List<MapData>[] placeArray, out GeoArea[] areaArray)
        {
            //Take area, divide it into a divideCount * divideCount grid of area. Return matching arrays of MapData and GeoArea, with indexes that correspond 1:1
            //The purpose of this function is to reduce code involved in optimizing a search, and to make it more flexible to test 
            //performance improvements on area splits.

            placeArray = new List<MapData>[1];
            areaArray = new GeoArea[1];

            //Logic note 1: if divideCount is 20, this is just reducing a PlusCode to the next smaller set of PlusCodes ranges.
            if (divideCount == 0 || divideCount == 1)
                return;

            var latDivider = area.LatitudeHeight / divideCount;
            var lonDivider = area.LongitudeWidth / divideCount;

            List<List<MapData>> resultsPlace = new List<List<MapData>>();
            List<GeoArea> resultsArea = new List<GeoArea>();

            for (var x = 0; x < divideCount; x++)
            {
                for (var y = 0; y < divideCount; y++)
                {
                    var box = new GeoArea(area.SouthLatitude + (latDivider * y), area.WestLongitude + (lonDivider * x), area.SouthLatitude + (latDivider * y) + latDivider, area.WestLongitude + (lonDivider * x) + lonDivider);
                    resultsPlace.Add(GetPlaces(box, places));
                    resultsArea.Add(box);
                }
            }

            placeArray = resultsPlace.ToArray();
            areaArray = resultsArea.ToArray();

            return;
        }
    }
}
