using CoreComponents.Support;
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

        public static SkiaSharp.SKPoint PlaceInfoToSKPoint(CoreComponents.StandaloneDbTables.PlaceInfo2 pi, ImageStats imgstats)
        {
            SkiaSharp.SKPoint point = new SkiaSharp.SKPoint();
            point.X = (float)((pi.lonCenter - imgstats.area.WestLongitude) * (1 / imgstats.degreesPerPixelX));
            point.Y =(float)((pi.latCenter - imgstats.area.SouthLatitude) * (1 / imgstats.degreesPerPixelY));
            return point;
        }

        public static SkiaSharp.SKPoint[] PlaceInfoToSKPoints(CoreComponents.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            var points = new SkiaSharp.SKPoint[5];
            points[0] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter + heightMod)); //upper right corner
            points[1] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter - heightMod)); //lower right
            points[2] = new SkiaSharp.SKPoint((float)(pi.lonCenter - widthMod), (float)(pi.latCenter - heightMod)); //lower left
            points[3] = new SkiaSharp.SKPoint((float)(pi.lonCenter - widthMod), (float)(pi.latCenter + heightMod)); //upper left
            points[4] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter + heightMod)); //upper right corner again for a closed shape.

            //points is now a geometric area. Convert to image area
            points = points.Select(p => new SkiaSharp.SKPoint((float)((p.X - info.area.WestLongitude) * (1 / info.degreesPerPixelX)), (float)((p.Y - info.area.SouthLatitude) * (1 / info.degreesPerPixelY)))).ToArray();

            return points;
        }

        public static SkiaSharp.SKRect PlaceInfoToRect(CoreComponents.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            SkiaSharp.SKRect r = new SkiaSharp.SKRect();
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            r.Left = (float)pi.lonCenter - widthMod;
            r.Left = (float)(r.Left - info.area.WestLongitude) * (float)(1/info.degreesPerPixelX);
            r.Right =(float) pi.lonCenter + widthMod;
            r.Right = (float)(r.Right - info.area.WestLongitude) * (float)(1 / info.degreesPerPixelX);
            r.Top = (float)pi.latCenter + heightMod;
            r.Top = (float)(r.Top - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);
            r.Bottom = (float)pi.latCenter - heightMod;
            r.Bottom = (float)(r.Bottom - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);


            return r;
        }

        public static GeoArea GeometryToGeoArea(Geometry g)
        {
            try
            {
                GeoArea results = new GeoArea(g.EnvelopeInternal.MinY, g.EnvelopeInternal.MinX, g.EnvelopeInternal.MaxY, g.EnvelopeInternal.MaxX);
                return results;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static int GetSlippyXFromLon(double lon, int z)
        {
            return (int)(Math.Floor((lon + 180.0) / 360.0 * (1 << z)));
        }

        public static int GetSlippyYFromLat(double lat, int z)
        {
            return (int)Math.Floor((1 - Math.Log(Math.Tan(lat.ToRadians()) + 1 / Math.Cos(lat.ToRadians())) / Math.PI) / 2 * (1 << z));
        }

        public static double SlippyXToLon(int x, int z)
        {
            return x / (double)(1 << z) * 360.0 - 180;
        }

        public static double SlippyYToLat(int y, int z)
        {
            double n = Math.PI - 2.0 * Math.PI * y / (double)(1 << z);
            return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        }

        //Incomplete, slippy tiles require some compromises for this to work
        //public static void GetSlippyTileForPoint(GeoArea buffered, int zoomLevel)
        //{
        //    var intersectCheck = Converters.GeoAreaToPolygon(buffered);

        //    //start drawing maptiles and sorting out data.
        //    var swCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MinX, zoomLevel);
        //    var neCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MaxX, zoomLevel);
        //    var swCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MinY, zoomLevel);
        //    var neCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MaxY, zoomLevel);

        //}
    }
}
