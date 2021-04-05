using CoreComponents.Support;
using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;
using static CoreComponents.GeometrySupport;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class Converters
    {
        public static Tuple<double, double, double> ShiftPlusCodeToFlexParams(string plusCode)
        {
            //This helper method is necessary if I want to allow arbitrary-sized areas while using PlusCodes at the code, perhaps.
            //Take a plus code, convert it to the parameters i need for my Flex calls (center lat, center lon, size)
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            return new Tuple<double, double, double>(box.CenterLatitude, box.CenterLongitude, box.LatitudeHeight); //Plus codes aren't square, so this over-shoots the width.
        }

        public static Coordinate[] GeoAreaToCoordArray(GeoArea plusCodeArea)
        {
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        public static Geometry GeoAreaToPolygon(GeoArea plusCodeArea)
        {
            return factory.CreatePolygon(GeoAreaToCoordArray(plusCodeArea));
        }

        public static IPreparedGeometry GeoAreaToPreparedPolygon(GeoArea plusCodeArea)
        {
            return pgf.Create(GeoAreaToPolygon(plusCodeArea));
        }

        public static MapData ConvertNodeToMapData(NodeReference n)
        {
            //Admin boundaries and buildings should not be points.
            if (!n.type.StartsWith("admin") && n.type != "building")
                return new MapData()
                {
                    name = n.name,
                    type = n.type,
                    place = factory.CreatePoint(new Coordinate(n.lon, n.lat)),
                    NodeId = n.Id,
                    AreaTypeId = areaTypeReference[n.type.StartsWith("admin") ? "admin" : n.type].First(),
                    AreaSize = .000125 //We treat points as being 1 cell10 in size for filtering purposes. don't draw them if you can't see them.
                };
            return null; //These should get filtered out before writing to file.
        }

        public static MapData ConvertWayToMapData(ref WayData w)
        {
            //An entry with no name and no type is probably a relation support entry.
            //Something with a type and no name was found to be interesting but not named
            //something with a name and no type is probably an intentionally excluded entry.
            //I always want a type. Names are optional.
            try
            {
                Log.WriteLog("Processing Way " + w.id + " " + w.name + " to MapData at " + DateTime.Now, Log.VerbosityLevels.High);
                if (w.AreaType == "")
                {
                    w = null;
                    return null;
                }

                //Take a single tagged Way, and make it a usable MapData entry for the app.
                MapData md = new MapData();
                md.name = w.name;
                md.WayId = w.id;
                md.type = w.AreaType;
                md.AreaTypeId = areaTypeReference[w.AreaType.StartsWith("admin") ? "admin" : w.AreaType].First();

                //Normally, we want to make sure looping roads/trails are linestrings. They can be marked with the tag area=yes to indicate that they should be drawn as a polygon.
                if (w.nds.First().id != w.nds.Last().id || (w.AreaType == "trail" && w.forceArea == false) || (w.AreaType == "road" && w.forceArea == false))
                {
                    //this is a linestring.
                    if (w.nds.Count() < 2)
                    {
                        Log.WriteLog("Way " + w.id + " has 1 or 0 nodes to parse into a line. This is a point, not processing.");
                        w = null;
                        return null;
                    }
                    LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                    md.place = SimplifyArea(temp2); //Linestrings should get the same rounding effect as polygons, with the same maptile quality consequences.
                    md.AreaSize = md.place.Length;
                }
                else
                { //Polygon
                    if (w.nds.Count <= 3)
                    {
                        Log.WriteLog("Way " + w.id + " doesn't have enough nodes to parse into a Polygon and first/last points are the same. This entry is an awkward line, not processing.");
                        w = null;
                        return null;
                    }

                    Polygon temp = factory.CreatePolygon(WayToCoordArray(w));
                    md.place = SimplifyArea(temp);
                    if (md.place == null)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not counter-clockwise forward or reversed.");
                        w = null;
                        return null;
                    }
                    if (!md.place.IsValid)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not valid according to its own internal check.");
                        w = null;
                        return null;
                    }
                    md.AreaSize = md.place.Length; //md.place.Area; //Area is square degrees, which make them way way smaller than I want to consider. I want to use external ring lenght, I think, for filter purposes.
                    md.WayId = w.id;
                }
                w = null;
                return md;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Exception converting Way to MapData:" + ex.Message + ex.StackTrace);
                return null;
            }
        }

        public static Coordinate[] WayToCoordArray(Support.WayData w)
        {
            if (w == null)
                return null;

            List<Coordinate> results = new List<Coordinate>();
            results.Capacity = w.nds.Count();

            foreach (var node in w.nds)
                results.Add(new Coordinate(node.lon, node.lat));

            return results.ToArray();
        }

        public static SkiaSharp.SKPoint[] PolygonToSKPoints(Geometry place, GeoArea drawingArea, double degreesPerPixelX, double degreesPerPixelY)
        {
            SkiaSharp.SKPoint[] points = place.Coordinates.Select(o => new SkiaSharp.SKPoint((float)((o.X - drawingArea.WestLongitude) * (1 / degreesPerPixelX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / degreesPerPixelY)))).ToArray();
            return points;
        }
    }
}
