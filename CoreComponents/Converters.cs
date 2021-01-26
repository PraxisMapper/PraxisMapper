using CoreComponents.Support;
using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;
using static CoreComponents.GeometrySupport;
using NetTopologySuite.Geometries.Prepared;

namespace CoreComponents
{
    public static class Converters
    {
        public static Tuple<double, double, double> ShiftPlusCodeToFlexParams(string plusCode)
        {
            //This helper method is necessary if I want to minimize code duplication.
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

        public static Geometry GeoAreaToPolygon(GeoArea plusCodeArea) //TODO: use this in more places where I have a couple extra lines because of GeoAreaToCoordArray
        {
            return factory.CreatePolygon(GeoAreaToCoordArray(plusCodeArea));
        }

        public static IPreparedGeometry GeoAreaToPreparedPolygon(GeoArea plusCodeArea)
        {
            return pgf.Create(GeoAreaToPolygon(plusCodeArea));
        }

        public static MapData ConvertNodeToMapData(NodeReference n)
        {
            return new MapData()
            {
                name = n.name,
                type = n.type,
                place = factory.CreatePoint(new Coordinate(n.lon, n.lat)),
                NodeId = n.Id,
                AreaTypeId = areaTypeReference[n.type.StartsWith("admin") ? "admin" : n.type].First()
            };
        }

        public static MapData ConvertWayToMapData(ref WayData w)
        {
            //An entry with no name and no type is probably a relation support entry.
            //Something with a type and no name was found to be interesting but not named
            //something with a name and no type is probably an excluded entry.
            //I always want a type. Names are optional.
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

            //Adding support for LineStrings. A lot of rivers/streams/footpaths are treated this way.
            if (w.nds.First().id != w.nds.Last().id)
            {
                LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                md.place = temp2;
            }
            else
            {
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

                md.WayId = w.id;
            }
            w = null;
            return md;
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

        public static void SplitArea(GeoArea area, int divideCount, List<MapData> places, out List<MapData>[] placeArray, out GeoArea[] areaArray)
        {
            //Take area, divide it into a divideCount * divideCount grid of area. Return matching arrays of MapData and GeoArea, with indexes that correspond 1:1
            //The purpose of this function is to reduce code involved in optimizing a search, and to make it more flexible to test 
            //performance improvements on area splits.

            placeArray = new List<MapData>[1];
            areaArray = new GeoArea[1];

            if (divideCount <= 1)
            {
                placeArray[0] = places;
                areaArray[0] = area;
                return;
            }

            var latDivider = area.LatitudeHeight / divideCount;
            var lonDivider = area.LongitudeWidth / divideCount;

            List<List<MapData>> resultsPlace = new List<List<MapData>>();
            List<GeoArea> resultsArea = new List<GeoArea>();
            resultsArea.Capacity = divideCount * divideCount;
            resultsPlace.Capacity = divideCount * divideCount;

            for (var x = 0; x < divideCount; x++)
            {
                for (var y = 0; y < divideCount; y++)
                {
                    var box = new GeoArea(area.SouthLatitude + (latDivider * y),
                        area.WestLongitude + (lonDivider * x),
                        area.SouthLatitude + (latDivider * y) + latDivider,
                        area.WestLongitude + (lonDivider * x) + lonDivider);
                    resultsPlace.Add(Place.GetPlaces(box, places));
                    resultsArea.Add(box);
                }
            }

            placeArray = resultsPlace.ToArray();
            areaArray = resultsArea.ToArray();

            return;
        }

        public static SixLabors.ImageSharp.Drawing.Polygon PolygonToDrawingPolygon(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            //Remember that plus codes start at the southwest corner, so y coordinates need inverted. Or flip the image when done.
            var originalPoints = place.Coordinates;
            var typeConvertedPoints = originalPoints.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            var output = new SixLabors.ImageSharp.Drawing.Polygon(part);
            return output;
        }

        public static List<SixLabors.ImageSharp.PointF> LineToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var originalPoints = place.Coordinates;
            var typeConvertedPoints = originalPoints.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY)))).ToList();
            //SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            return typeConvertedPoints;
        }
    }
}
