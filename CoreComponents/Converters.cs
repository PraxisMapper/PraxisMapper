using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public static Coordinate[] CompleteWayToCoordArray(OsmSharp.Complete.CompleteWay w)
        {
            if (w == null)
                return null;

            List<Coordinate> results = new List<Coordinate>();
            results.Capacity = w.Nodes.Count();

            foreach (var node in w.Nodes)
                results.Add(new Coordinate(node.Longitude.Value, node.Latitude.Value));

            return results.ToArray();
        }

        public static SkiaSharp.SKPoint[] PolygonToSKPoints(Geometry place, GeoArea drawingArea, double degreesPerPixelX, double degreesPerPixelY)
        {
            SkiaSharp.SKPoint[] points = place.Coordinates.Select(o => new SkiaSharp.SKPoint((float)((o.X - drawingArea.WestLongitude) * (1 / degreesPerPixelX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / degreesPerPixelY)))).ToArray();
            return points;
        }

        public static GeoArea GeometryToGeoArea(Geometry g)
        {
            GeoArea results = new GeoArea(g.EnvelopeInternal.MinY, g.EnvelopeInternal.MinX, g.EnvelopeInternal.MaxY, g.EnvelopeInternal.MaxX);
            return results;
        }
    }
}
